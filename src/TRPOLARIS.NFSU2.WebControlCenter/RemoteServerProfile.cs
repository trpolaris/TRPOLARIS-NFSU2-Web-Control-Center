using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TRPOLARIS.NFSU2.WebControlCenter;

/// <summary>Remote connection metadata. Secret fields contain Windows DPAPI ciphertext, never plaintext.</summary>
internal sealed class RemoteServerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public string AuthenticationMode { get; set; } = "password";
    public string PrivateKeyPath { get; set; } = "";
    // Encrypted with CurrentUser DPAPI so only this Windows account can decrypt saved credentials.
    public string ProtectedPassword { get; set; } = "";
    public string ProtectedKeyPassphrase { get; set; } = "";
    public string HostKeyFingerprint { get; set; } = "";
    public string RemoteProjectDirectory { get; set; } = "/srv/nfsu2";
    // Independent SFTP browser root; empty keeps compatibility with RemoteProjectDirectory.
    public string RemoteFileManagerRoot { get; set; } = "";
    // "domain", "lan", or "public-ip". Older profiles default to domain.
    public string AccessMode { get; set; } = "domain";
    public string Domain { get; set; } = "";
    public int HttpPort { get; set; } = 8080;
    public string ProxyMode { get; set; } = "caddy";
    public DateTimeOffset? LastConnectedUtc { get; set; }

    // WPF ComboBox fallback display when SelectionBoxItemTemplate is unavailable.
    public override string ToString() => Name;
}

internal static class RemoteSecretProtector
{
    private const int CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob input, string? description, IntPtr optionalEntropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input, IntPtr description, IntPtr optionalEntropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("SSH kimlik bilgileri yalnızca Windows DPAPI ile saklanabilir.");

        byte[] plain = Encoding.UTF8.GetBytes(value);
        IntPtr inputMemory = Marshal.AllocHGlobal(plain.Length);
        DataBlob input = new() { Length = plain.Length, Data = inputMemory };
        DataBlob output = default;
        try
        {
            Marshal.Copy(plain, 0, inputMemory, plain.Length);
            if (!CryptProtectData(ref input, "TRPOLARIS NFSU2 SSH profile secret",
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output))
                throw new CryptographicException("Windows DPAPI parolayı şifreleyemedi: " +
                    new Win32Exception(Marshal.GetLastWin32Error()).Message);

            byte[] encrypted = new byte[output.Length];
            Marshal.Copy(output.Data, encrypted, 0, output.Length);
            return Convert.ToBase64String(encrypted);
        }
        finally
        {
            Marshal.FreeHGlobal(inputMemory);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public static string Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue)) return "";
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("SSH kimlik bilgileri yalnızca Windows DPAPI ile açılabilir.");

        byte[] encrypted;
        try { encrypted = Convert.FromBase64String(protectedValue); }
        catch (FormatException ex) { throw new CryptographicException("Kayıtlı SSH parolası geçerli DPAPI verisi değil.", ex); }

        IntPtr inputMemory = Marshal.AllocHGlobal(encrypted.Length);
        DataBlob input = new() { Length = encrypted.Length, Data = inputMemory };
        DataBlob output = default;
        try
        {
            Marshal.Copy(encrypted, 0, inputMemory, encrypted.Length);
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, CryptProtectUiForbidden, out output))
                throw new CryptographicException("Kaydedilmiş SSH parolası bu Windows hesabında açılamadı: " +
                    new Win32Exception(Marshal.GetLastWin32Error()).Message);

            byte[] plain = new byte[output.Length];
            Marshal.Copy(output.Data, plain, 0, output.Length);
            try { return Encoding.UTF8.GetString(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        finally
        {
            Marshal.FreeHGlobal(inputMemory);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            CryptographicOperations.ZeroMemory(encrypted);
        }
    }
}

internal static class RemoteServerProfileStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private static readonly string DataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TRPOLARIS", "ControlCenter");
    private static readonly string ProfilesPath = Path.Combine(DataRoot, "remote-servers.json");

    public static IReadOnlyList<RemoteServerProfile> LoadAll()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(ProfilesPath)) return Array.Empty<RemoteServerProfile>();
                var profiles = JsonSerializer.Deserialize<List<RemoteServerProfile>>(File.ReadAllText(ProfilesPath), JsonOptions);
                return (profiles ?? new List<RemoteServerProfile>())
                    .Where(IsValidStoredProfile)
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (JsonException) { return Array.Empty<RemoteServerProfile>(); }
            catch (IOException) { return Array.Empty<RemoteServerProfile>(); }
            catch (UnauthorizedAccessException) { return Array.Empty<RemoteServerProfile>(); }
        }
    }

    public static void SaveOrUpdate(RemoteServerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.Name) ||
            string.IsNullOrWhiteSpace(profile.Host) || string.IsNullOrWhiteSpace(profile.Username) ||
            profile.Port is < 1 or > 65535)
            throw new InvalidDataException("Uzak sunucu profili geçersiz.");

        lock (Gate)
        {
            Directory.CreateDirectory(DataRoot);
            var profiles = LoadAll().ToList();
            int index = profiles.FindIndex(x => string.Equals(x.Id, profile.Id, StringComparison.Ordinal));
            if (index >= 0) profiles[index] = profile;
            else profiles.Add(profile);

            string temporaryPath = ProfilesPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(profiles, JsonOptions));
            File.Move(temporaryPath, ProfilesPath, overwrite: true);
        }
    }

    public static void Delete(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId)) return;
        lock (Gate)
        {
            var profiles = LoadAll().Where(x => !string.Equals(x.Id, profileId, StringComparison.Ordinal)).ToList();
            if (profiles.Count == 0)
            {
                try { if (File.Exists(ProfilesPath)) File.Delete(ProfilesPath); } catch { }
                return;
            }
            Directory.CreateDirectory(DataRoot);
            string temporaryPath = ProfilesPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(profiles, JsonOptions));
            File.Move(temporaryPath, ProfilesPath, overwrite: true);
        }
    }

    private static bool IsValidStoredProfile(RemoteServerProfile? profile) =>
        profile is not null && !string.IsNullOrWhiteSpace(profile.Id) &&
        !string.IsNullOrWhiteSpace(profile.Name) && !string.IsNullOrWhiteSpace(profile.Host) &&
        !string.IsNullOrWhiteSpace(profile.Username) && profile.Port is >= 1 and <= 65535;
}
