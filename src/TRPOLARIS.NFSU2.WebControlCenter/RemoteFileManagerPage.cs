using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace TRPOLARIS.NFSU2.WebControlCenter;

public partial class MainWindow
{
    private async Task RefreshRemoteFileListAsync()
    {
        if (_remoteServerSession?.IsConnected != true)
        {
            SetRemoteFileStatus("Önce SSH bağlantısı kur.", false);
            return;
        }

        try
        {
            string root = ValidateRemoteInstallRoot(_remoteInstallRootBox?.Text ?? "");
            string path = ValidateRemoteBrowserPath(_remoteFilePathBox?.Text ?? root, root);
            _remoteFilePathBox!.Text = path;
            SetRemoteFileStatus("Uzak klasör listeleniyor...", true);
            var entries = await _remoteServerSession.ListRemoteDirectoryAsync(path);
            if (_remoteFileList is not null)
            {
                _remoteFileList.ItemsSource = entries;
                _remoteFileList.SelectedItem = null;
            }
            SetRemoteFileStatus($"{path} · {entries.Count} öğe", true);
        }
        catch (Exception ex)
        {
            SetRemoteFileStatus("Klasör listelenemedi: " + ex.Message, false);
        }
    }

    private async void RemoteFileList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_remoteFileList?.SelectedItem is not RemoteFileEntry entry || !entry.IsDirectory) return;
        try
        {
            string root = ValidateRemoteInstallRoot(_remoteInstallRootBox?.Text ?? "");
            string next = ValidateRemoteBrowserPath(entry.FullPath, root);
            _remoteFilePathBox!.Text = next;
            await RefreshRemoteFileListAsync();
        }
        catch (Exception ex) { SetRemoteFileStatus(ex.Message, false); }
    }

    private async Task NavigateRemoteParentAsync()
    {
        try
        {
            string root = ValidateRemoteInstallRoot(_remoteInstallRootBox?.Text ?? "");
            string current = ValidateRemoteBrowserPath(_remoteFilePathBox?.Text ?? root, root);
            if (string.Equals(current, root, StringComparison.Ordinal))
            {
                SetRemoteFileStatus("Kurulum kök dizininin üstüne çıkılamaz.", false);
                return;
            }

            int slash = current.LastIndexOf('/');
            string parent = slash <= 0 ? root : current[..slash];
            if (parent.Length < root.Length || !parent.StartsWith(root, StringComparison.Ordinal))
                parent = root;
            _remoteFilePathBox!.Text = parent;
            await RefreshRemoteFileListAsync();
        }
        catch (Exception ex) { SetRemoteFileStatus(ex.Message, false); }
    }

    private async Task DownloadSelectedRemoteFileAsync()
    {
        if (_remoteServerSession?.IsConnected != true) { SetRemoteFileStatus("Önce SSH bağlantısı kur.", false); return; }
        if (_remoteFileList?.SelectedItem is not RemoteFileEntry entry || entry.IsDirectory)
        {
            SetRemoteFileStatus("İndirmek için bir dosya seç.", false);
            return;
        }

        string root = ValidateRemoteInstallRoot(_remoteInstallRootBox?.Text ?? "");
        string remotePath = ValidateRemoteBrowserPath(entry.FullPath, root);
        var dialog = new SaveFileDialog
        {
            Title = "Uzak dosyayı bilgisayara indir",
            FileName = Path.GetFileName(entry.Name),
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            SetRemoteFileStatus("Dosya indiriliyor: " + entry.Name, true);
            await _remoteServerSession.DownloadRemoteFileAsync(remotePath, dialog.FileName);
            SetRemoteFileStatus("Dosya indirildi: " + dialog.FileName, true);
        }
        catch (Exception ex) { SetRemoteFileStatus("İndirme başarısız: " + ex.Message, false); }
    }

    private async Task UploadRemoteFileAsync()
    {
        if (_remoteServerSession?.IsConnected != true) { SetRemoteFileStatus("Önce SSH bağlantısı kur.", false); return; }
        string root = ValidateRemoteInstallRoot(_remoteInstallRootBox?.Text ?? "");
        string current = ValidateRemoteBrowserPath(_remoteFilePathBox?.Text ?? root, root);
        var dialog = new OpenFileDialog
        {
            Title = "Uzak sunucuya dosya yükle",
            CheckFileExists = true,
            Multiselect = false,
            Filter = "All files|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        string name = Path.GetFileName(dialog.FileName);
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            SetRemoteFileStatus("Dosya adı geçersiz.", false);
            return;
        }

        try
        {
            SetRemoteFileStatus("Dosya yükleniyor: " + name, true);
            var item = new RemoteUploadItem(dialog.FileName, current.TrimEnd('/') + "/" + name, name);
            await _remoteServerSession.UploadFilesAsync(new[] { item });
            await RefreshRemoteFileListAsync();
            SetRemoteFileStatus("Dosya yüklendi: " + name, true);
        }
        catch (Exception ex) { SetRemoteFileStatus("Yükleme başarısız: " + ex.Message, false); }
    }

    private async Task CreateRemoteFolderAsync()
    {
        if (_remoteServerSession?.IsConnected != true) { SetRemoteFileStatus("Önce SSH bağlantısı kur.", false); return; }
        string name = (_remoteNewFolderBox?.Text ?? "").Trim();
        if (name.Length is < 1 or > 80 || name is "." or ".." ||
            name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
        {
            SetRemoteFileStatus("Klasör adı yalnızca harf, rakam, nokta, tire ve alt çizgi içerebilir.", false);
            return;
        }

        try
        {
            string root = ValidateRemoteInstallRoot(_remoteInstallRootBox?.Text ?? "");
            string current = ValidateRemoteBrowserPath(_remoteFilePathBox?.Text ?? root, root);
            string newPath = current.TrimEnd('/') + "/" + name;
            ValidateRemoteBrowserPath(newPath, root);
            await _remoteServerSession.CreateRemoteDirectoryAsync(newPath);
            if (_remoteNewFolderBox is not null) _remoteNewFolderBox.Text = "";
            await RefreshRemoteFileListAsync();
            SetRemoteFileStatus("Klasör oluşturuldu: " + name, true);
        }
        catch (Exception ex) { SetRemoteFileStatus("Klasör oluşturulamadı: " + ex.Message, false); }
    }

    private async Task DeleteSelectedRemoteEntryAsync()
    {
        if (_remoteServerSession?.IsConnected != true) { SetRemoteFileStatus("Önce SSH bağlantısı kur.", false); return; }
        if (_remoteFileList?.SelectedItem is not RemoteFileEntry entry)
        {
            SetRemoteFileStatus("Silmek için dosya veya klasör seç.", false);
            return;
        }

        try
        {
            string root = ValidateRemoteInstallRoot(_remoteInstallRootBox?.Text ?? "");
            string path = ValidateRemoteBrowserPath(entry.FullPath, root);
            if (string.Equals(path, root, StringComparison.Ordinal))
                throw new InvalidOperationException("Kurulum kök dizini dosya yöneticisinden silinemez.");

            string description = entry.IsDirectory
                ? "Seçilen boş klasör silinecek. Dolu klasörleri silmek için önce içeriğini ayrı ayrı kaldır."
                : "Seçilen dosya kalıcı olarak silinecek.";
            if (MessageBox.Show($"'{entry.Name}'\n\n{description}\n\nDevam edilsin mi?",
                "Uzak dosyayı sil", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            await _remoteServerSession.DeleteRemoteFileOrEmptyDirectoryAsync(path);
            await RefreshRemoteFileListAsync();
            SetRemoteFileStatus("Silme işlemi tamamlandı: " + entry.Name, true);
        }
        catch (Exception ex) { SetRemoteFileStatus("Silme başarısız: " + ex.Message, false); }
    }

    private static string ValidateRemoteBrowserPath(string path, string installRoot)
    {
        string normalized = (path ?? "").Trim().Replace('\\', '/').TrimEnd('/');
        if (normalized.Length == 0) normalized = installRoot;
        if (!normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Split('/').Any(part => part is "." or "..") ||
            !(string.Equals(normalized, installRoot, StringComparison.Ordinal) ||
              normalized.StartsWith(installRoot + "/", StringComparison.Ordinal)))
            throw new InvalidOperationException("Dosya yöneticisi yalnızca NFSU2 kurulum dizininin içinde çalışabilir.");
        return normalized;
    }

    private void SetRemoteFileStatus(string message, bool success)
    {
        if (_remoteFileStatus is null) return;
        _remoteFileStatus.Text = message;
        _remoteFileStatus.Foreground = BrushHex(success ? "#65D6B0" : "#E5B65B");
    }
}
