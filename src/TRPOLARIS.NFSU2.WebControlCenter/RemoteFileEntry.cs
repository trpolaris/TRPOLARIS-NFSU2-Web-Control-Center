namespace TRPOLARIS.NFSU2.WebControlCenter;

internal sealed record RemoteFileEntry(
    string Name, string FullPath, bool IsDirectory, long Length, DateTime LastWriteTimeUtc)
{
    public override string ToString()
    {
        string kind = IsDirectory ? "[KLASÖR]" : "[DOSYA]";
        string size = IsDirectory ? "" : " · " + FormatSize(Length);
        return kind + " " + Name + size + " · " + LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " " + units[unit];
    }
}
