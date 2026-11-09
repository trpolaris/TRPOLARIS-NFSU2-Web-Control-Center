using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TRPOLARIS.NFSU2.WebControlCenter;

internal enum UpdateChannel { Stable, Beta }
internal enum UpdateCheckFrequency { EveryLaunch, Daily, Manual }

internal sealed class UpdateSettings
{
    public UpdateChannel Channel { get; set; } = UpdateChannel.Stable;
    public UpdateCheckFrequency Frequency { get; set; } = UpdateCheckFrequency.EveryLaunch;
    public DateTimeOffset? LastCheckUtc { get; set; }
    public string? StableETag { get; set; }
    public string? StableReleaseJson { get; set; }
    public string? BetaETag { get; set; }
    public string? BetaReleaseJson { get; set; }
}

internal sealed record UpdateHistoryEntry(
    DateTimeOffset DateUtc,
    string FromVersion,
    string ToVersion,
    string Channel,
    string Result,
    string Sha256);

internal sealed record ControlCenterRelease(
    string TagName,
    string Name,
    string Body,
    string HtmlUrl,
    string Version,
    string Channel,
    string MinimumSupportedVersion,
    string AssetUrl,
    string AssetName,
    string Sha256,
    long AssetSize,
    DateTimeOffset PublishedAt);

internal static class UpdateEngine
{
    public const string Repository = "trpolaris/TRPOLARIS-NFSU2-Web-Control-Center";
    public const string Product = "NFSU2 Server control";
    public const string ExecutableName = "NFSU2 Server control.exe";
    public const string LegacyExecutableName = "TRPOLARIS.ControlCenter.exe";
    public const string PreviousLegacyExecutableName = "TRPOLARIS.NFSU2.WebControlCenter.exe";
    private const string KeyId = "f58f0a93a19ca147";
    private const string SignatureAlgorithm = "RSA-SHA256";
    private const string PublicKeyPem = @"-----BEGIN PUBLIC KEY-----
MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAwANbRZubgnirzattUViK
4UhJN9VETrYLIoGfCN8NATHvpDtda4By2ARZpd6UOwp9xiCozyI0saPKZ7+R389I
ek35b+UdMbLjmBlOeRZq6MrY9DGvl8ZisRrD64H2872Cvf6QEcGXII00PjUoo+Ib
QWgWrBeAoUdxfOnCVjzQ5TMWxdxxPHJwMZsKzqVmm3NGkPLdb/AUSHRi4yK29051
rBZ9cB75tHeqXB7Ue2IOjwKyHjaFSjV5KqqJH94acLWn65KUe94wpCAMzeQo88jn
kR5HEj9exPZtywWMVc6+QL1htOUDILwtSPjZj/+f9KR1NbfgnosOy4oSlvgImgZt
eImO9FYznercrb8Z6q+vioD9f6qu9ypxorJBz0cbUqyty/4z/9rtaLJ+UZS5LRYs
DhnCzQ4rseIuApKJw3re+HryArXgyXR3dDRvDNHfYM0oHKiOoDhxQSIUjOA8Yja/
LX1AkM31UisbXzpJBQ/htrmaUMpCwUM2U04l+q+6f24XAgMBAAE=
-----END PUBLIC KEY-----";
    private static readonly HttpClient Api = CreateClient(TimeSpan.FromSeconds(35));
    private static readonly HttpClient Download = CreateClient(Timeout.InfiniteTimeSpan);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly string DataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TRPOLARIS", "ControlCenter");
    private static readonly string SettingsPath = Path.Combine(DataRoot, "update-settings.json");
    private static readonly string HistoryPath = Path.Combine(DataRoot, "update-history.json");
    private static readonly string UpdateRoot = Path.Combine(DataRoot, "updates");
    private static readonly Regex SemVerPattern = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static UpdateSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new UpdateSettings();
            var settings = JsonSerializer.Deserialize<UpdateSettings>(File.ReadAllText(SettingsPath), JsonOptions);
            return settings ?? new UpdateSettings();
        }
        catch { return new UpdateSettings(); }
    }

    public static void SaveSettings(UpdateSettings settings)
    {
        Directory.CreateDirectory(DataRoot);
        AtomicWrite(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public static IReadOnlyList<UpdateHistoryEntry> LoadHistory()
    {
        try
        {
            if (!File.Exists(HistoryPath)) return Array.Empty<UpdateHistoryEntry>();
            return JsonSerializer.Deserialize<List<UpdateHistoryEntry>>(File.ReadAllText(HistoryPath), JsonOptions)
                ?.OrderByDescending(x => x.DateUtc).Take(30).ToArray() ?? Array.Empty<UpdateHistoryEntry>();
        }
        catch { return Array.Empty<UpdateHistoryEntry>(); }
    }

    public static void RecordHistory(string fromVersion, string toVersion, string channel, string result, string sha256)
    {
        try
        {
            Directory.CreateDirectory(DataRoot);
            var items = LoadHistory().ToList();
            items.Add(new UpdateHistoryEntry(DateTimeOffset.UtcNow, fromVersion, toVersion, channel, result, sha256));
            AtomicWrite(HistoryPath, JsonSerializer.Serialize(items.OrderByDescending(x => x.DateUtc).Take(30), JsonOptions));
        }
        catch { }
    }

    public static bool ShouldCheckAtStartup(UpdateSettings? settings = null)
    {
        settings ??= LoadSettings();
        return settings.Frequency switch
        {
            UpdateCheckFrequency.Manual => false,
            UpdateCheckFrequency.Daily => !settings.LastCheckUtc.HasValue ||
                DateTimeOffset.UtcNow - settings.LastCheckUtc.Value >= TimeSpan.FromHours(24),
            _ => true
        };
    }

    public static async Task<ControlCenterRelease?> GetLatestReleaseAsync(
        CancellationToken cancellationToken = default,
        bool force = false)
    {
        var settings = LoadSettings();
        if (!force && !ShouldCheckAtStartup(settings)) return null;

        string raw = await GetReleaseFeedAsync(settings, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        using var feed = JsonDocument.Parse(raw);
        JsonElement selected;
        if (settings.Channel == UpdateChannel.Stable)
        {
            selected = feed.RootElement;
            if (selected.ValueKind != JsonValueKind.Object) return null;
        }
        else
        {
            if (feed.RootElement.ValueKind != JsonValueKind.Array) return null;
            var candidates = new List<(SemanticVersion Version, JsonElement Json)>();
            foreach (var item in feed.RootElement.EnumerateArray())
            {
                if (GetBool(item, "draft")) continue;
                var tag = GetString(item, "tag_name");
                if (!SemanticVersion.TryParse(tag, out var parsed)) continue;
                candidates.Add((parsed, item.Clone()));
            }
            if (candidates.Count == 0) return null;
            selected = candidates.OrderByDescending(x => x.Version).First().Json;
        }

        if (GetBool(selected, "draft")) return null;
        string tagName = GetString(selected, "tag_name");
        if (!SemanticVersion.TryParse(tagName, out var tagVersion))
            throw new InvalidDataException("GitHub Release etiketi geçerli SemVer biçiminde değil: " + tagName);

        string releaseChannel = GetBool(selected, "prerelease") ? "beta" : "stable";
        if (settings.Channel == UpdateChannel.Stable && releaseChannel != "stable") return null;

        var assets = selected.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array
            ? assetsElement.EnumerateArray().Select(x => x.Clone()).ToArray()
            : Array.Empty<JsonElement>();
        var manifestAsset = assets.FirstOrDefault(x => GetString(x, "name").Equals("update-manifest.json", StringComparison.OrdinalIgnoreCase));
        if (manifestAsset.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("Release, imzalı update-manifest.json dosyasını içermiyor.");

        string manifestUrl = GetString(manifestAsset, "browser_download_url");
        EnsureTrustedGitHubUrl(manifestUrl);
        using var manifestResponse = await Download.GetAsync(manifestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        manifestResponse.EnsureSuccessStatusCode();
        await using var manifestStream = await manifestResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var manifestDoc = await JsonDocument.ParseAsync(manifestStream, cancellationToken: cancellationToken);
        var manifest = manifestDoc.RootElement;

        int schemaVersion = GetInt(manifest, "schemaVersion");
        string product = GetString(manifest, "product");
        string channel = GetString(manifest, "channel");
        string manifestVersionText = GetString(manifest, "version");
        string manifestTag = GetString(manifest, "tag");
        string platform = GetString(manifest, "platform");
        string assetName = GetString(manifest, "assetName");
        long assetSize = GetLong(manifest, "assetSize");
        string sha256 = GetString(manifest, "sha256").ToLowerInvariant();
        string minimumSupportedVersion = GetString(manifest, "minimumSupportedVersion");
        string publishedAtText = GetString(manifest, "publishedAt");
        string signatureAlgorithm = GetString(manifest, "signatureAlgorithm");
        string keyId = GetString(manifest, "keyId");
        string signatureText = GetString(manifest, "signature");

        if (schemaVersion != 2) throw new InvalidDataException("Desteklenmeyen update manifest şeması.");
        if (!product.Equals(Product, StringComparison.Ordinal)) throw new InvalidDataException("Manifest başka bir ürüne ait.");
        if (!channel.Equals(releaseChannel, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Release kanalı manifest ile eşleşmiyor.");
        if (!platform.Equals("win-x64", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Bu uygulama paketi Windows x64 için değil.");
        if (!manifestTag.Equals(tagName, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Manifest etiketi GitHub Release etiketiyle eşleşmiyor.");
        if (!SemanticVersion.TryParse(manifestVersionText, out var manifestVersion) || manifestVersion.CompareTo(tagVersion) != 0)
            throw new InvalidDataException("Manifest SemVer değeri GitHub Release etiketiyle eşleşmiyor.");
        if (!SemanticVersion.TryParse(minimumSupportedVersion, out _))
            throw new InvalidDataException("minimumSupportedVersion geçerli SemVer değil.");
        if (assetSize <= 0 || assetSize > 500L * 1024 * 1024)
            throw new InvalidDataException("Güncelleme paketi boyutu geçersiz.");
        if (!Regex.IsMatch(sha256, "^[a-f0-9]{64}$"))
            throw new InvalidDataException("Manifest SHA-256 değeri geçersiz.");
        if (!signatureAlgorithm.Equals(SignatureAlgorithm, StringComparison.Ordinal))
            throw new InvalidDataException("Desteklenmeyen manifest imza algoritması.");
        if (!keyId.Equals(KeyId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Manifest güvenilir bir imza anahtarıyla imzalanmamış.");

        var executableAsset = assets.FirstOrDefault(x => GetString(x, "name").Equals(assetName, StringComparison.OrdinalIgnoreCase));
        if (executableAsset.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("Release imzalı manifestte belirtilen EXE dosyasını içermiyor.");
        long releaseAssetSize = GetLong(executableAsset, "size");
        if (releaseAssetSize != assetSize)
            throw new InvalidDataException("GitHub asset boyutu imzalı manifest ile eşleşmiyor.");
        string assetUrl = GetString(executableAsset, "browser_download_url");
        EnsureTrustedGitHubUrl(assetUrl);

        if (!DateTimeOffset.TryParse(publishedAtText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var publishedAt))
            throw new InvalidDataException("Manifest yayın tarihi geçersiz.");

        string payload = BuildSignaturePayload(schemaVersion, product, channel, manifestVersionText, manifestTag,
            platform, assetName, assetSize, sha256, minimumSupportedVersion, publishedAtText, signatureAlgorithm, keyId);
        byte[] signature;
        try { signature = Convert.FromBase64String(signatureText); }
        catch (FormatException ex) { throw new InvalidDataException("Manifest imzası Base64 değil.", ex); }
        using (var rsa = RSA.Create())
        {
            rsa.ImportFromPem(PublicKeyPem);
            if (!rsa.VerifyData(Encoding.UTF8.GetBytes(payload), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                throw new CryptographicException("Güncelleme manifesti dijital imza doğrulamasından geçmedi.");
        }

        settings.LastCheckUtc = DateTimeOffset.UtcNow;
        SaveSettings(settings);

        return new ControlCenterRelease(
            tagName,
            GetString(selected, "name"),
            GetString(selected, "body"),
            GetString(selected, "html_url"),
            manifestVersionText,
            channel,
            minimumSupportedVersion,
            assetUrl,
            assetName,
            sha256,
            assetSize,
            publishedAt);
    }

    private static async Task<string> GetReleaseFeedAsync(UpdateSettings settings, CancellationToken cancellationToken)
    {
        bool beta = settings.Channel == UpdateChannel.Beta;
        string endpoint = beta
            ? $"https://api.github.com/repos/{Repository}/releases?per_page=50"
            : $"https://api.github.com/repos/{Repository}/releases/latest";
        string? etag = beta ? settings.BetaETag : settings.StableETag;
        string? cached = beta ? settings.BetaReleaseJson : settings.StableReleaseJson;

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        if (!string.IsNullOrWhiteSpace(etag) && EntityTagHeaderValue.TryParse(etag, out var parsedTag))
            request.Headers.IfNoneMatch.Add(parsedTag);

        using var response = await Api.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            if (!string.IsNullOrWhiteSpace(cached)) return cached;
            using var retry = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var retryResponse = await Api.SendAsync(retry, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            retryResponse.EnsureSuccessStatusCode();
            string retryBody = await retryResponse.Content.ReadAsStringAsync(cancellationToken);
            SetCache(settings, beta, retryResponse.Headers.ETag?.ToString(), retryBody);
            settings.LastCheckUtc = DateTimeOffset.UtcNow;
            SaveSettings(settings);
            return retryBody;
        }

        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        SetCache(settings, beta, response.Headers.ETag?.ToString(), body);
        settings.LastCheckUtc = DateTimeOffset.UtcNow;
        SaveSettings(settings);
        return body;
    }

    private static void SetCache(UpdateSettings settings, bool beta, string? etag, string json)
    {
        if (beta) { settings.BetaETag = etag; settings.BetaReleaseJson = json; }
        else { settings.StableETag = etag; settings.StableReleaseJson = json; }
    }

    public static async Task<string> DownloadPackageAsync(
        ControlCenterRelease release,
        IProgress<(long Received, long Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(UpdateRoot);
        string finalPath = Path.Combine(UpdateRoot, release.AssetName + ".verified");
        string partialPath = Path.Combine(UpdateRoot, release.AssetName + ".partial");
        string statePath = partialPath + ".json";
        string? priorUrl = null;
        string? priorEtag = null;
        try
        {
            if (File.Exists(statePath))
            {
                using var state = JsonDocument.Parse(await File.ReadAllTextAsync(statePath, cancellationToken));
                priorUrl = GetString(state.RootElement, "url");
                priorEtag = GetString(state.RootElement, "etag");
            }
        }
        catch { priorUrl = null; priorEtag = null; }

        if (!string.Equals(priorUrl, release.AssetUrl, StringComparison.Ordinal))
        {
            TryDelete(partialPath);
            TryDelete(statePath);
            priorEtag = null;
        }

        long existing = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        if (existing > release.AssetSize)
        {
            TryDelete(partialPath);
            existing = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, release.AssetUrl);
        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
            if (EntityTagHeaderValue.TryParse(priorEtag, out var parsedEtag))
                request.Headers.IfRange = new RangeConditionHeaderValue(parsedEtag);
        }

        using var response = await Download.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existing == release.AssetSize)
        {
            string completedHash = await HashFileAsync(partialPath, cancellationToken);
            if (completedHash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(partialPath, finalPath, true);
                TryDelete(statePath);
                return finalPath;
            }
            TryDelete(partialPath);
            TryDelete(statePath);
            throw new InvalidDataException("Devam eden indirme tamamlanmış görünüyor ancak SHA-256 doğrulanamadı.");
        }

        bool append = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (append)
        {
            long? rangeStart = response.Content.Headers.ContentRange?.From;
            if (rangeStart != existing)
                throw new InvalidDataException("Devam ettirilen indirme sunucusunun Range başlangıcı beklenen konumla eşleşmiyor.");
        }
        else if (response.StatusCode == HttpStatusCode.OK)
        {
            existing = 0;
        }
        else
        {
            response.EnsureSuccessStatusCode();
        }

        var stateObject = new { url = release.AssetUrl, etag = response.Headers.ETag?.ToString() };
        await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(stateObject), new UTF8Encoding(false), cancellationToken);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(partialPath, append ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[128 * 1024];
        long total = existing;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            total += read;
            if (total > release.AssetSize) throw new InvalidDataException("İndirilen güncelleme paketi manifest boyutunu aştı.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            progress?.Report((total, release.AssetSize));
        }
        await output.FlushAsync(cancellationToken);

        if (total != release.AssetSize)
            throw new IOException($"İndirme tamamlanmadı. {total} / {release.AssetSize} bayt alındı; kısmi dosya sonraki denetim için korundu.");

        string actualHash = await HashFileAsync(partialPath, cancellationToken);
        if (!actualHash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(partialPath);
            TryDelete(statePath);
            throw new CryptographicException("İndirilen EXE SHA-256 doğrulamasından geçmedi; dosya silindi.");
        }

        File.Move(partialPath, finalPath, true);
        TryDelete(statePath);
        return finalPath;
    }

    public static bool IsUpdateAvailable(string currentVersion, string publishedVersion) =>
        SemanticVersion.TryParse(publishedVersion, out var latest) &&
        SemanticVersion.TryParse(currentVersion, out var current) &&
        latest.CompareTo(current) > 0;

    public static bool IsMandatoryUpdate(string currentVersion, string minimumSupportedVersion) =>
        SemanticVersion.TryParse(currentVersion, out var current) &&
        SemanticVersion.TryParse(minimumSupportedVersion, out var minimum) &&
        current.CompareTo(minimum) < 0;

    public static string FormatChannel(UpdateChannel channel) => channel == UpdateChannel.Beta ? "beta" : "stable";

    private static string BuildSignaturePayload(int schemaVersion, string product, string channel, string version, string tag,
        string platform, string assetName, long assetSize, string sha256, string minimumSupportedVersion,
        string publishedAt, string signatureAlgorithm, string keyId)
    {
        return string.Join("\n", new[]
        {
            schemaVersion.ToString(CultureInfo.InvariantCulture),
            product, channel, version, tag, platform, assetName,
            assetSize.ToString(CultureInfo.InvariantCulture),
            sha256.ToLowerInvariant(), minimumSupportedVersion, publishedAt, signatureAlgorithm, keyId
        });
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;

    private static long GetLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : 0;

    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : 0;

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static void EnsureTrustedGitHubUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Güncelleme bağlantısı güvenilir bir GitHub HTTPS adresi değil.");
    }

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 8 })
        {
            Timeout = timeout
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TRPOLARIS-ControlCenter/" + AppVersion.Current);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            256 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void AtomicWrite(string path, string content)
    {
        string directory = Path.GetDirectoryName(path) ?? DataRoot;
        Directory.CreateDirectory(directory);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(false));
        File.Move(temp, path, true);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

internal readonly record struct SemanticVersion(int Major, int Minor, int Patch, string[] Prerelease) : IComparable<SemanticVersion>
{
    private static readonly Regex Pattern = new(
        @"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool TryParse(string? input, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var match = Pattern.Match(input.Trim());
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
            !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minor) ||
            !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
            return false;
        string[] pre = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : Array.Empty<string>();
        if (pre.Any(x => x.Length == 0 || (x.Length > 1 && x[0] == '0' && x.All(char.IsDigit)))) return false;
        version = new SemanticVersion(major, minor, patch, pre);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        int c = Major.CompareTo(other.Major); if (c != 0) return c;
        c = Minor.CompareTo(other.Minor); if (c != 0) return c;
        c = Patch.CompareTo(other.Patch); if (c != 0) return c;
        if (Prerelease.Length == 0 && other.Prerelease.Length == 0) return 0;
        if (Prerelease.Length == 0) return 1;
        if (other.Prerelease.Length == 0) return -1;
        int count = Math.Min(Prerelease.Length, other.Prerelease.Length);
        for (int i = 0; i < count; i++)
        {
            bool leftNumber = long.TryParse(Prerelease[i], NumberStyles.None, CultureInfo.InvariantCulture, out long left);
            bool rightNumber = long.TryParse(other.Prerelease[i], NumberStyles.None, CultureInfo.InvariantCulture, out long right);
            if (leftNumber && rightNumber) c = left.CompareTo(right);
            else if (leftNumber != rightNumber) c = leftNumber ? -1 : 1;
            else c = string.Compare(Prerelease[i], other.Prerelease[i], StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        return Prerelease.Length.CompareTo(other.Prerelease.Length);
    }

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (Prerelease.Length > 0 ? "-" + string.Join(".", Prerelease) : string.Empty);
}
