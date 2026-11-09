namespace TRPOLARIS.NFSU2.WebControlCenter;

/// <summary>
/// Single source of truth for the version displayed throughout the application.
/// The value comes from the assembly metadata generated from the project Version.
/// </summary>
internal static class AppVersion
{
    public static string Current { get; } =
        typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static string Display => $"v{Current}";
}
