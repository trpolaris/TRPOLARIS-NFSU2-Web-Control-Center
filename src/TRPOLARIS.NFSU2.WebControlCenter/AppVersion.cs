using System.Reflection;

namespace TRPOLARIS.NFSU2.WebControlCenter;

/// <summary>
/// Single source of truth for the version displayed throughout the application.
/// Uses informational SemVer so beta/preview labels remain visible.
/// </summary>
internal static class AppVersion
{
    public static string Current { get; } = Resolve();

    public static string Display => $"v{Current}";

    private static string Resolve()
    {
        var assembly = typeof(App).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            int metadataSeparator = informational.IndexOf('+');
            return metadataSeparator >= 0 ? informational[..metadataSeparator] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
