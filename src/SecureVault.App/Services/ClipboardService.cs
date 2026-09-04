using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using SecureVault.Core;
using SecureVault.Core.Format;

namespace SecureVault.App.Services;

/// <summary>
/// Provides clipboard ingestion directly in-memory without creating temporary files on disk (C24).
/// Supports image pasting, file list ingestion, and text clipping.
/// </summary>
public static class ClipboardService
{
    public static bool CanPaste()
    {
        try
        {
            var content = Clipboard.GetContent();
            return content.Contains(StandardDataFormats.Bitmap) ||
                   content.Contains(StandardDataFormats.StorageItems) ||
                   content.Contains(StandardDataFormats.Text);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ingests clipboard contents directly into the open vault in-memory.
    /// </summary>
    public static async Task<List<IndexEntry>> PasteToVaultAsync(
        VaultManager vault,
        string targetVirtualFolder = "/",
        ProtectionMode mode = ProtectionMode.SecureMode,
        IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        var added = new List<IndexEntry>();

        var package = Clipboard.GetContent();
        if (package == null) return added;

        // 1. Ingest Bitmap Image directly from clipboard stream
        if (package.Contains(StandardDataFormats.Bitmap))
        {
            var streamRef = await package.GetBitmapAsync();
            using var raStream = await streamRef.OpenReadAsync();
            using var managedStream = raStream.AsStreamForRead();

            string fileName = $"Pasted_Image_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var entry = await vault.AddFileAsync(managedStream, fileName, targetVirtualFolder, mode, progress);
            added.Add(entry);
            return added;
        }

        // 2. Ingest StorageItems (Copied files from File Explorer)
        if (package.Contains(StandardDataFormats.StorageItems))
        {
            var items = await package.GetStorageItemsAsync();
            int total = items.Count;
            int current = 0;

            foreach (var item in items)
            {
                if (item is StorageFile file)
                {
                    using var fileStream = await file.OpenStreamForReadAsync();
                    var entry = await vault.AddFileAsync(fileStream, file.Name, targetVirtualFolder, mode, null);
                    added.Add(entry);
                }

                current++;
                progress?.Report((double)current / total);
            }

            return added;
        }

        // 3. Ingest Text snippet
        if (package.Contains(StandardDataFormats.Text))
        {
            string text = await package.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
            {
                byte[] textBytes = System.Text.Encoding.UTF8.GetBytes(text);
                using var ms = new MemoryStream(textBytes);
                string fileName = $"Pasted_Note_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                var entry = await vault.AddFileAsync(ms, fileName, targetVirtualFolder, mode, progress);
                added.Add(entry);
            }
        }

        return added;
    }
}
