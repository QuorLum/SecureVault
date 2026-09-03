namespace SecureVault.Core.IO;

/// <summary>
/// Provides atomic file write and flush-to-disk guarantees for crash resilience (F07 / F08).
/// </summary>
public static class AtomicWriter
{
    /// <summary>
    /// Writes content to a temporary file, flushes completely to physical disk,
    /// and performs an atomic replace of the target file.
    /// </summary>
    public static void WriteAtomic(string destinationPath, Action<Stream> writeAction)
    {
        ArgumentNullException.ThrowIfNull(destinationPath);
        ArgumentNullException.ThrowIfNull(writeAction);

        string tempPath = destinationPath + ".tmp." + Guid.NewGuid().ToString("N");

        try
        {
            using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 65536,
                FileOptions.WriteThrough))
            {
                writeAction(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Ignore deletion of temp file on failure
                }
            }
        }
    }

    /// <summary>
    /// Flushes the specified stream down to physical disk storage.
    /// </summary>
    public static void FlushToDisk(Stream stream)
    {
        if (stream is FileStream fs)
        {
            fs.Flush(flushToDisk: true);
        }
        else
        {
            stream.Flush();
        }
    }
}
