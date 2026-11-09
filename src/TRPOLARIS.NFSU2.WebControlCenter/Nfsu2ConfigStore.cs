using System.IO;
using System.Text;
using System.Text.Json;

namespace TRPOLARIS.NFSU2.WebControlCenter;

internal sealed class Nfsu2ConfigDocument
{
    public int SchemaVersion { get; set; } = 1;
    public Nfsu2InstallConfig Install { get; set; } = new();
    public Nfsu2AccessConfig Access { get; set; } = new();
    public Nfsu2UpdateConfig Update { get; set; } = new();
}

internal sealed class Nfsu2InstallConfig
{
    public string WslDistribution { get; set; } = "";
    public string LinuxUser { get; set; } = "";
    public uint LinuxUid { get; set; }
    public string LinuxHome { get; set; } = "";
    public string ProjectDirectory { get; set; } = "";
    public string GitHub { get; set; } = "https://github.com/trpolaris/NFSU2-Web.git";
    public string SourceCommit { get; set; } = "";
    public string WasmModuleRevision { get; set; } = "";
    public string WindowsGameDirectory { get; set; } = "";
    public string WslGameDirectory { get; set; } = "";
    public int ServerPort { get; set; } = 9093;
    public string ModuleDirectory { get; set; } = "";
    public string ComposeFile { get; set; } = "";
    public string ComposeEnvironmentFile { get; set; } = "";
    public string ContainerImage { get; set; } = "nfsu2-web-rs:local";
    public string GameVolume { get; set; } = "nfsu2-web-local_game-data";
    public string StateVolume { get; set; } = "nfsu2-web-local_state";
    public string SetupLog { get; set; } = "";
    public string InstalledAt { get; set; } = "";
}

internal sealed class Nfsu2AccessConfig
{
    public string Mode { get; set; } = "local";
    public string Url { get; set; } = "";
    public string PublicUrl { get; set; } = "";
    public int LanPort { get; set; } = 8443;
    public string UpdatedAt { get; set; } = "";
}

internal sealed class Nfsu2UpdateConfig
{
    public string UpdatedAt { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public string WasmModuleRevision { get; set; } = "";
    public string DockerImage { get; set; } = "nfsu2-web-rs:local";
    public string RollbackImage { get; set; } = "";
    public string Health { get; set; } = "";
    public int HttpPort { get; set; }
}

internal static class Nfsu2ConfigStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string GetPath(string root) => Path.Combine(root, "nfsu2-config.json");

    public static Nfsu2ConfigDocument Read(string root)
    {
        lock (Gate) return ReadUnlocked(root);
    }

    public static void Update(string root, Action<Nfsu2ConfigDocument> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (Gate)
        {
            var config = ReadUnlocked(root);
            update(config);
            config.SchemaVersion = 1;
            WriteUnlocked(root, config);
        }
    }

    public static void MigrateLegacyFiles(string root)
    {
        Directory.CreateDirectory(root);
        string runtime = Path.Combine(root, "runtime");
        string logs = Path.Combine(root, "logs");
        Directory.CreateDirectory(runtime);
        Directory.CreateDirectory(logs);

        string setupPath = Path.Combine(root, "NFSU2-SETUP-REPORT.txt");
        string projectPath = Path.Combine(root, "NFSU2-PROJECT-PATH.txt");
        string updatePath = Path.Combine(root, "NFSU2-UPDATE-REPORT.txt");
        string accessModePath = Path.Combine(root, "nfsu2-access-mode.txt");
        string accessUrlPath = Path.Combine(root, "nfsu2-access-url.txt");
        string publicUrlPath = Path.Combine(root, "NFSU2-PUBLIC-URL.txt");
        string lanPortPath = Path.Combine(root, "NFSU2-LAN-PORT.txt");

        string[] legacyStateFiles =
        {
            setupPath, projectPath, updatePath, accessModePath,
            accessUrlPath, publicUrlPath, lanPortPath
        };

        if (legacyStateFiles.Any(File.Exists))
        {
            string[] setup = ReadLines(setupPath);
            string[] project = ReadLines(projectPath);
            string[] update = ReadLines(updatePath);
            string accessMode = ReadText(accessModePath).Trim().ToLowerInvariant();
            string accessUrl = ReadText(accessUrlPath).Trim();
            string publicUrl = ReadText(publicUrlPath).Trim();
            string lanPortText = ReadText(lanPortPath).Trim();

            Update(root, config =>
            {
                static string Value(IEnumerable<string> lines, string key)
                {
                    string prefix = key + ":";
                    string? line = lines.FirstOrDefault(x =>
                        x.TrimStart('\uFEFF').StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                    return line is null ? "" : line.TrimStart('\uFEFF')[prefix.Length..].Trim();
                }

                static string Pick(params string[] values) =>
                    values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";

                var install = config.Install;
                install.WslDistribution = Pick(Value(project, "WSL distribution:"), Value(setup, "WSL distribution:"), install.WslDistribution);
                install.LinuxUser = Pick(Value(project, "Linux user:"), install.LinuxUser);
                if (uint.TryParse(Value(project, "Linux uid:"), out uint legacyUid))
                    install.LinuxUid = legacyUid;
                install.LinuxHome = Pick(Value(project, "Linux home:"), install.LinuxHome);
                install.ProjectDirectory = Pick(Value(project, "Source folder (WSL):"), Value(setup, "Source folder (WSL):"), install.ProjectDirectory);
                install.GitHub = Pick(Value(project, "GitHub:"), Value(setup, "GitHub:"), install.GitHub);
                install.SourceCommit = Pick(Value(setup, "Source commit:"), install.SourceCommit);
                install.WasmModuleRevision = Pick(Value(setup, "WASM module revision:"), install.WasmModuleRevision);
                install.WindowsGameDirectory = Pick(Value(setup, "Windows game folder:"), install.WindowsGameDirectory);
                install.WslGameDirectory = Pick(Value(setup, "WSL game folder:"), install.WslGameDirectory);
                install.ModuleDirectory = Pick(Value(setup, "Module folder (Windows):"), install.ModuleDirectory);
                install.ComposeFile = Pick(Value(setup, "Compose file:"), install.ComposeFile);
                install.ComposeEnvironmentFile = Pick(Value(setup, "Compose env:"), install.ComposeEnvironmentFile);
                install.SetupLog = Pick(Value(setup, "Setup log:"), install.SetupLog);
                if (int.TryParse(Value(setup, "Server port:"), out int port) && port is >= 1024 and <= 65535)
                    install.ServerPort = port;
                if (string.IsNullOrWhiteSpace(install.InstalledAt) && !string.IsNullOrWhiteSpace(install.ProjectDirectory))
                    install.InstalledAt = DateTimeOffset.UtcNow.ToString("O");

                var access = config.Access;
                if (accessMode is "local" or "lan" or "public") access.Mode = accessMode;
                if (!string.IsNullOrWhiteSpace(accessUrl)) access.Url = accessUrl;
                if (!string.IsNullOrWhiteSpace(publicUrl)) access.PublicUrl = publicUrl;
                if (int.TryParse(lanPortText, out int lanPort) && lanPort is >= 1024 and <= 65535)
                    access.LanPort = lanPort;

                var lastUpdate = config.Update;
                lastUpdate.UpdatedAt = Pick(Value(update, "Updated:"), lastUpdate.UpdatedAt);
                lastUpdate.SourceCommit = Pick(Value(update, "Source commit:"), lastUpdate.SourceCommit);
                lastUpdate.WasmModuleRevision = Pick(Value(update, "WASM module revision:"), lastUpdate.WasmModuleRevision);
                lastUpdate.DockerImage = Pick(Value(update, "Docker image:"), lastUpdate.DockerImage);
                lastUpdate.RollbackImage = Pick(Value(update, "Rollback image:"), lastUpdate.RollbackImage);
                lastUpdate.Health = Pick(Value(update, "Health:"), lastUpdate.Health);
                if (int.TryParse(Value(update, "HTTP port:"), out int updatePort) && updatePort is >= 1024 and <= 65535)
                    lastUpdate.HttpPort = updatePort;
            });

            // Delete legacy state files only after the JSON file was successfully written.
            if (File.Exists(GetPath(root)))
            {
                foreach (string file in legacyStateFiles) TryDelete(file);
            }
        }

        // All helper scripts are embedded or generated from embedded templates.
        // Remove stale copies from previous versions; the app extracts fresh copies
        // into runtime/ only for the duration of the operation that needs them.
        foreach (string name in new[]
        {
            "nfsu2-access.sh", "nfsu2-git-update.sh", "nfsu2-project-lookup.sh",
            "nfsu2-one-click-wsl.sh", "nfsu2-stop-owned-tunnel.sh", "nfsu2-remove-verified-project.sh"
        })
        {
            TryDelete(Path.Combine(root, name));
            TryDelete(Path.Combine(runtime, name));
        }

        try
        {
            foreach (string oldLog in Directory.EnumerateFiles(root, "nfsu2-one-click-wsl-*.log", SearchOption.TopDirectoryOnly))
            {
                string destination = Path.Combine(logs, Path.GetFileName(oldLog));
                try
                {
                    if (!File.Exists(destination)) File.Move(oldLog, destination);
                    else TryDelete(oldLog);
                }
                catch { }
            }
        }
        catch { }
    }

    private static Nfsu2ConfigDocument ReadUnlocked(string root)
    {
        try
        {
            string path = GetPath(root);
            if (File.Exists(path))
            {
                Nfsu2ConfigDocument? config = JsonSerializer.Deserialize<Nfsu2ConfigDocument>(
                    File.ReadAllText(path, Encoding.UTF8), JsonOptions);
                if (config is not null)
                {
                    config.Install ??= new Nfsu2InstallConfig();
                    config.Access ??= new Nfsu2AccessConfig();
                    config.Update ??= new Nfsu2UpdateConfig();
                    return config;
                }
            }
        }
        catch { }
        return new Nfsu2ConfigDocument();
    }

    private static void WriteUnlocked(string root, Nfsu2ConfigDocument config)
    {
        Directory.CreateDirectory(root);
        string path = GetPath(root);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(config, JsonOptions), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    private static string[] ReadLines(string path)
    {
        try { return File.Exists(path) ? File.ReadAllLines(path, Encoding.UTF8) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static string ReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : ""; }
        catch { return ""; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
