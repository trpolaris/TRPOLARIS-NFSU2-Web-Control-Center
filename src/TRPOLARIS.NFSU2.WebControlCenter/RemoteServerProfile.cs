using System.IO;
using System.Text.Json;

namespace TRPOLARIS.NFSU2.WebControlCenter;

/// <summary>Remote connection metadata. Secrets are intentionally never persisted here.</summary>
internal sealed class RemoteServerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public string AuthenticationMode { get; set; } = "password";
    public string PrivateKeyPath { get; set; } = "";
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
