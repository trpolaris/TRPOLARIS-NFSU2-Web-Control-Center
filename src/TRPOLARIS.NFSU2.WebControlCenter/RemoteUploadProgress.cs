namespace TRPOLARIS.NFSU2.WebControlCenter;

internal sealed record RemoteUploadItem(string LocalPath, string RemotePath, string RelativePath);
internal sealed record RemoteUploadProgress(string CurrentFile, long BytesTransferred, long TotalBytes, int CompletedFiles, int TotalFiles);
