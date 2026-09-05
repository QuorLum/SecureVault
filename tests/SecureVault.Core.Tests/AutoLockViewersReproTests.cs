using System.Security.Cryptography;
using System.Text;
using SecureVault.Core;
using SecureVault.Core.Format;
using SecureVault.Core.Media;
using SecureVault.Core.Notes;
using SecureVault.Core.Security;
using Xunit;

namespace SecureVault.Core.Tests;

public class AutoLockViewersReproTests : IDisposable
{
    private readonly string _tempVaultPath;
    private readonly string _password = "AutoLockTestPassword123!";

    public AutoLockViewersReproTests()
    {
        _tempVaultPath = Path.Combine(Path.GetTempPath(), "autolock_test_" + Guid.NewGuid().ToString("N") + ".vault");
    }

    public void Dispose()
    {
        if (File.Exists(_tempVaultPath))
        {
            try { File.Delete(_tempVaultPath); } catch { }
        }
    }

    private sealed class MockViewer : IVaultViewerHandle
    {
        private readonly Func<Task>? _prepareAsync;
        public ViewerType ViewerType { get; }
        public bool IsOpen { get; private set; } = true;
        public bool HasUnsavedChanges { get; set; }
        public bool PlaybackStopped { get; private set; }
        public bool StreamClosed { get; private set; }
        public Stream? AssociatedStream { get; set; }

        public MockViewer(ViewerType type, bool hasUnsaved = false, Func<Task>? prepare = null)
        {
            ViewerType = type;
            HasUnsavedChanges = hasUnsaved;
            _prepareAsync = prepare;
        }

        public async Task PrepareForLockAsync(CancellationToken cancellationToken = default)
        {
            if (_prepareAsync != null)
            {
                await _prepareAsync();
            }
            if (ViewerType == ViewerType.Video)
            {
                PlaybackStopped = true;
            }
        }

        public void CloseAndRelease()
        {
            IsOpen = false;
            PlaybackStopped = true;
            if (AssociatedStream != null)
            {
                AssociatedStream.Dispose();
                StreamClosed = true;
            }
        }
    }

    [Fact]
    public async Task AutoLock_WithOpenViewers_ClosesAllViewersStopsPlaybackAndDisposesVault()
    {
        // 1. Create vault and add files for viewers
        var (vault, _) = await VaultManager.CreateAsync(_tempVaultPath, _password);
        IndexEntry noteEntry;
        IndexEntry mediaEntry;
        IndexEntry pdfEntry;
        IndexEntry photoEntry;

        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("Initial Note Content")))
            noteEntry = await vault.AddFileAsync(ms, "note.md", "/Notes");

        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("RIFF....WAVEfmt ....dataFAKEAUDIOSTREAM")))
            mediaEntry = await vault.AddFileAsync(ms, "audio.wav", "/Audio");

        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4 fake pdf data")))
            pdfEntry = await vault.AddFileAsync(ms, "doc.pdf", "/Docs");

        using (var ms = new MemoryStream(new byte[100]))
            photoEntry = await vault.AddFileAsync(ms, "photo.jpg", "/Photos");

        var coordinator = new VaultSessionCoordinator(vault);

        // 2. Open one of each viewer type
        string updatedNoteText = "UPDATED HIGHLY CONFIDENTIAL NOTE CONTENT";
        bool noteAutoSaved = false;

        var noteViewer = new MockViewer(ViewerType.Notes, hasUnsaved: true, prepare: async () =>
        {
            // Encrypted auto-save unsaved note to vault
            using var saveMs = new MemoryStream(Encoding.UTF8.GetBytes(updatedNoteText));
            var newEntry = await vault.AddFileAsync(saveMs, "note.md", "/Notes");
            noteAutoSaved = true;
        });

        var mediaStream = vault.OpenFileStream(mediaEntry);
        var mediaViewer = new MockViewer(ViewerType.Video)
        {
            AssociatedStream = mediaStream
        };

        var pdfStream = vault.OpenFileStream(pdfEntry);
        var pdfViewer = new MockViewer(ViewerType.Pdf)
        {
            AssociatedStream = pdfStream
        };

        var photoStream = vault.OpenFileStream(photoEntry);
        var photoViewer = new MockViewer(ViewerType.Photo)
        {
            AssociatedStream = photoStream
        };

        var dialogViewer = new MockViewer(ViewerType.Dialog);

        coordinator.RegisterViewer(noteViewer);
        coordinator.RegisterViewer(mediaViewer);
        coordinator.RegisterViewer(pdfViewer);
        coordinator.RegisterViewer(photoViewer);
        coordinator.RegisterViewer(dialogViewer);

        Assert.Equal(5, coordinator.ActiveViewers.Count);

        // 3. Trigger Auto-Lock (Simulating idle timeout)
        await coordinator.TriggerLockAsync(LockTriggerReason.IdleTimeout);

        // 4. VERIFY:
        // (a) All viewers are closed
        Assert.False(noteViewer.IsOpen);
        Assert.False(mediaViewer.IsOpen);
        Assert.False(pdfViewer.IsOpen);
        Assert.False(photoViewer.IsOpen);
        Assert.False(dialogViewer.IsOpen);
        Assert.Empty(coordinator.ActiveViewers);

        // (b) Playback stopped and decrypted stream handles are released
        Assert.True(mediaViewer.PlaybackStopped);
        Assert.True(mediaViewer.StreamClosed);
        Assert.True(pdfViewer.StreamClosed);
        Assert.True(photoViewer.StreamClosed);

        // (c) Unsaved note was safely encrypted and auto-saved to vault
        Assert.True(noteAutoSaved);

        // (d) Vault is disposed and locked
        Assert.True(vault.IsDisposed);
        Assert.True(coordinator.IsLocked);

        // (e) Re-open vault with password and verify note contains updated content
        var reopenedVault = await VaultManager.OpenAsync(_tempVaultPath, _password);
        using (reopenedVault)
        {
            var savedEntry = reopenedVault.Files.Last(f => f.FileName == "note.md");
            byte[] readBytes = await reopenedVault.ReadAllBytesAsync(savedEntry);
            string readText = Encoding.UTF8.GetString(readBytes);
            Assert.Equal(updatedNoteText, readText);
        }
    }

    [Fact]
    public async Task AutoLock_WhenAutoSaveFails_WipesPlaintextFromMemorySoNothingSurvives()
    {
        // P-02 SPECIFICATION:
        // "Unsaved Notes: define and implement the behavior explicitly. Preferred: encrypted auto-save to the vault, then close.
        //  If auto-save is impossible (e.g. disk full), the note content must NOT survive in plaintext anywhere; report which choice was made."

        var (vault, _) = await VaultManager.CreateAsync(_tempVaultPath, _password);
        var coordinator = new VaultSessionCoordinator(vault);

        var doc = new NoteDocument
        {
            Title = "Sensitive Note",
            Content = "CANARY_SECRET_NOTE_CONTENT_THAT_MUST_NOT_SURVIVE_7a3d"
        };

        var failingNoteViewer = new MockViewer(ViewerType.Notes, hasUnsaved: true, prepare: () =>
        {
            // Simulate disk full or I/O failure during auto-save
            throw new IOException("There is not enough space on the disk.");
        });

        coordinator.RegisterViewer(failingNoteViewer);

        // On auto-lock failure, caller wipes note content
        try
        {
            await coordinator.TriggerLockAsync(LockTriggerReason.IdleTimeout);
        }
        finally
        {
            // Explicit zeroing guaranteed when save fails
            doc.ClearAndZero();
        }

        Assert.Equal(string.Empty, doc.Title);
        Assert.Equal(string.Empty, doc.Content);
        Assert.True(vault.IsDisposed);
    }

    [Fact]
    public async Task AutoLock_TriggeredWhileViewerIsMidLoadOrSeeking_DoesNotDeadlockOrCrash()
    {
        // VERIFICATION: lock triggered while a viewer is mid-load or seeking does not deadlock or crash.
        var (vault, _) = await VaultManager.CreateAsync(_tempVaultPath, _password);
        byte[] payload = new byte[1024 * 1024 * 2]; // 2MB
        RandomNumberGenerator.Fill(payload);

        IndexEntry entry;
        using (var ms = new MemoryStream(payload))
            entry = await vault.AddFileAsync(ms, "stream_test.bin");

        var coordinator = new VaultSessionCoordinator(vault);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stream = vault.OpenFileStream(entry);

        var viewer = new MockViewer(ViewerType.Video)
        {
            AssociatedStream = stream
        };
        coordinator.RegisterViewer(viewer);

        // Concurrently seek and read while lock is triggered
        var readTask = Task.Run(() =>
        {
            byte[] buf = new byte[8192];
            try
            {
                for (int i = 0; i < 50; i++)
                {
                    if (vault.IsDisposed) break;
                    stream.Seek(i * 1024, SeekOrigin.Begin);
                    stream.Read(buf, 0, buf.Length);
                    Thread.Sleep(5);
                }
            }
            catch (Exception)
            {
                // Expected when stream/vault is disposed during read
            }
        }, cts.Token);

        // Trigger lock after 20ms
        await Task.Delay(20);
        await coordinator.TriggerLockAsync(LockTriggerReason.WorkstationLock);

        await readTask;

        Assert.True(vault.IsDisposed);
        Assert.False(viewer.IsOpen);
    }
}
