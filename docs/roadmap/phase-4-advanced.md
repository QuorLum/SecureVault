# Phase 4: Advanced Features — Implementation Roadmap

> **Branch:** `phase-4/advanced-features`
>
> **Scope:** Password hint, recovery key UI, auto-lock, thumbnails, prefetch,
> parallel processing, advanced gallery/player/notes/PDF, file manager + archives.
>
> **Feature IDs:** A05, A06–A09, A17–A18, E08–E20, H07–H18, I10–I25, J09–J20,
> L07–L11, K01–K13
>
> **Prior Phases:** Phase 1 + 2 + 3 must be complete.

---

## Build Order & Dependency Graph

```
Level 0 (independent, depends only on Phase 1–3):
  A05  Password hint
  A08  Auto-lock after idle
  A09  Failed attempt delay (already started in Phase 2 M11, finalize here)
  A17  "Encrypt Everything" button
  A18  Toggle protection mode per file
  E08-E14  Thumbnail generation system
  K01-K08  File manager (basic)

Level 1 (depends on Level 0):
  A07  Recovery key unlock (UI integration — crypto done in Phase 1)
  E15-E17  LRU chunk cache, pre-render, playback positions
  E18-E20  UI state cache, streaming decryption, parallel chunk processing
  K09-K13  Archive support
  H07-H09  Gallery: crop, flip, save edits
  J09-J10  Notes: version history, restore

Level 2 (depends on Level 1):
  H10-H18  Gallery: slideshow, albums, timeline, formats, large image handling
  I10-I25  Player: PiP, subtitles, chapters, playlists, waveform
  J11-J20  Notes: full-text search, attachments, export
  L07-L11  PDF: text search, bookmarks, copy text, remember page
```

---

## A05 — Password Hint

### Module & File Placement

- **File:** `src/SecureVault.Core/VaultManager.cs` (extend)
- **File:** `src/SecureVault.App/ViewModels/LoginViewModel.cs` (extend)
- **Dependencies:** VaultHeader (Phase 1 — hint field already in header at offset 0x00FC)

### Function Signatures

```csharp
// VaultManager additions:
    void SetPasswordHint(string hint)
    // 1. Validate: max 255 UTF-8 bytes
    // 2. Update header.PasswordHint
    // 3. Rewrite header atomically
    // 4. Recompute header HMAC

    string? GetPasswordHint()
    // Read from header (available without unlock — hint is in the unencrypted header section)
```

⚠️ **OPEN QUESTION: Password hint visibility**
The hint is stored in the header, which is readable without unlocking. This is by design (you need the hint when you've forgotten the password), but it means the hint must not contain the password or anything too revealing.

**Recommendation:** Show a warning when setting the hint: "Your hint is NOT encrypted. Anyone with access to the vault file can see it. Do not put your password in the hint."

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Set hint | "My cat's name" | Stored in header, readable without unlock |
| Hint too long | 300-char hint | Truncated to 255 UTF-8 bytes or rejected |
| No hint | Don't set | GetPasswordHint returns null, login shows nothing |

### Verification Checklist

1. ✅ Hint is stored in the unencrypted header section — verify by reading raw bytes without password
2. ✅ Setting a hint rewrites the header HMAC

---

## A08, A09 — Auto-Lock & Failed Attempt Delay

### Module & File Placement

- **File:** `src/SecureVault.App/Services/IdleLockService.cs`
- **File:** `src/SecureVault.App/Services/SystemLockDetector.cs`
- **Dependencies:** VaultManager.Lock(), Settings

### Function Signatures

```csharp
public sealed class IdleLockService : IDisposable
    IdleLockService(VaultManager vault, TimeSpan timeout)

    void Start()
    // 1. Hook keyboard/mouse input events (GetLastInputInfo Win32 API)
    // 2. Start timer that checks idle time every 30 seconds
    // 3. If idle time > timeout → VaultManager.Lock()

    void ResetTimer()
    // Called on any user interaction

public sealed class SystemLockDetector : IDisposable
    SystemLockDetector(VaultManager vault)

    void Start()
    // 1. Subscribe to SystemEvents.SessionSwitch
    // 2. On SessionSwitchReason.SessionLock → VaultManager.Lock() (M08)
```

### Exact Library Calls

- `Microsoft.Win32.SystemEvents.SessionSwitch` — detect Windows lock screen
- `GetLastInputInfo()` via P/Invoke — detect idle time
- `DispatcherTimer` — periodic check

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Auto-lock on idle | Set timeout=5s, wait 6s | Vault locked, navigate to login |
| Reset on input | Set timeout=5s, type at 3s | Timer reset, vault stays open |
| Lock on system lock | Lock Windows | Vault locked |
| Configurable timeout | Set 1 min, 5 min, 15 min, 30 min | All work |

### Verification Checklist

1. ✅ Keys are zeroed from memory on auto-lock (same as manual lock)
2. ✅ Auto-lock timer is stopped when vault is already locked (no double-lock crash)

---

## A17, A18 — Encrypt Everything & Toggle Protection Mode

### Module & File Placement

- **File:** `src/SecureVault.Core/Operations/ProtectionModeOperation.cs`
- **Dependencies:** ChunkReader, ChunkWriter, VaultIndex

### Function Signatures

```csharp
public sealed class ProtectionModeOperation
    ProtectionModeOperation(VaultManager vault)

    async Task ChangeProtectionMode(Guid fileGuid, ProtectionMode newMode, IProgress<long>? progress, CancellationToken ct)
    // 1. Read all chunks of file (in current mode)
    // 2. Re-write each chunk in new mode (re-encrypt or de-encrypt+obfuscate)
    // 3. Update index entry with new ProtectionMode
    // 4. Mark old chunks as free space
    // This is a data-intensive operation — full file re-read + re-write

    async Task EncryptAll(IProgress<FileAddProgress>? progress, CancellationToken ct)
    // 1. Find all files with ProtectionMode = FastObfuscation
    // 2. For each: ChangeProtectionMode(guid, SecureMode, progress, ct)
    // 3. Report progress per file
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Fast → Secure | File in Fast mode, change to Secure | File readable, auth tags present |
| Secure → Fast | File in Secure mode, change to Fast | File readable, no auth tags |
| Encrypt all | 3 Fast files, 2 Secure | All 5 now Secure |
| Cancel | Start encrypt-all, cancel after 1 file | 1 file converted, 2 remain Fast |
| Data integrity | Change mode, verify SHA-256 | Plaintext hash unchanged |

### Verification Checklist

1. ✅ After mode change, the plaintext SHA-256 is identical (data not corrupted)
2. ✅ Old chunks are not immediately deleted — space is reclaimed by compaction (C23, Phase 6)

---

## E08–E14 — Thumbnail Generation System

### Module & File Placement

- **File:** `src/SecureVault.Core/Media/ThumbnailGenerator.cs`
- **File:** `src/SecureVault.Core/Media/ThumbnailService.cs`
- **Dependencies:** ImageDecoder (Phase 3), VaultCache, SkiaSharp, TagLibSharp

### Function Signatures

```csharp
public sealed class ThumbnailGenerator
    static byte[] GenerateImageThumbnail(byte[] imageData, int maxDimension = 200)
    // 1. Decode image via SkiaSharp (E10)
    // 2. Resize to fit within maxDimension x maxDimension
    // 3. Encode as WebP (E09) at 80% quality
    // 4. Return WebP bytes

    static byte[] GenerateVideoThumbnail(VaultFileStream stream, int maxDimension = 200)
    // 1. Use libVLC to seek to 10% of video duration (E11)
    // 2. Capture frame
    // 3. Resize to maxDimension x maxDimension
    // 4. Encode as WebP
    // 5. Return WebP bytes

    static byte[]? GenerateAudioThumbnail(byte[] audioData, int maxDimension = 200)
    // 1. Use TagLibSharp to extract album art (E12)
    // 2. If found: resize + WebP encode
    // 3. If not found: return null (use default audio icon)

    static byte[] GeneratePdfThumbnail(byte[] pdfData, int maxDimension = 200)
    // 1. Render first page via PdfiumCore at thumbnail DPI (E13)
    // 2. Resize to maxDimension x maxDimension
    // 3. Encode as WebP
    // 4. Return WebP bytes

public sealed class ThumbnailService
    ThumbnailService(VaultManager vault, VaultCache cache)

    async Task GenerateAllThumbnails(IProgress<int>? progress, CancellationToken ct)
    // 1. Get all files without thumbnails from cache
    // 2. Use Task.WhenAll with SemaphoreSlim(Environment.ProcessorCount) for parallelism (E14)
    // 3. For each file: determine type, generate thumbnail, store in cache
    // 4. Report progress

    byte[]? GetThumbnail(Guid fileGuid)
    // Look up in cache, return WebP bytes or null
```

### Exact Library Calls

- `SkiaSharp.SKBitmap.Resize()` — resize
- `SkiaSharp.SKImage.Encode(SKEncodedImageFormat.Webp, 80)` — WebP encode (E09)
- `TagLib.File.Create(stream)` → `.Tag.Pictures[0].Data.Data` — album art
- `PdfiumCore` — render first page (E13)

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Image thumbnail | 4000x3000 JPEG | WebP ≤ 200x200, < 50KB |
| Video thumbnail | 30s MP4 | WebP frame from ~3s mark |
| Audio thumbnail | MP3 with album art | WebP of album art |
| Audio no art | MP3 without album art | Returns null |
| PDF thumbnail | 10-page PDF | WebP of first page |
| Parallel generation | 20 images | Processes using multiple cores |
| Cache storage | Generate, then GetThumbnail | Returns same WebP bytes |

### Verification Checklist

1. ✅ Thumbnails are stored in the encrypted cache, not in the vault file
2. ✅ WebP format — search for `Encode(` calls, format must be Webp
3. ✅ Max dimension 200px — no thumbnail larger than 200x200

---

## E15–E20 — LRU Cache, Prefetch, Parallel Processing

### Module & File Placement

- **File:** `src/SecureVault.Core/Cache/ChunkLruCache.cs` (E15)
- **File:** `src/SecureVault.Core/Media/ImagePrefetcher.cs` (E16)
- **File:** `src/SecureVault.Core/Cache/PlaybackPositionCache.cs` (E17)
- **File:** `src/SecureVault.Core/IO/ParallelChunkPipeline.cs` (E20)
- **Dependencies:** ChunkReader, VaultCache

### Function Signatures

```csharp
public sealed class ChunkLruCache
    ChunkLruCache(int maxChunks = 16)   // 16 chunks = 16MB max

    byte[]? Get(ulong chunkOffset)
    void Put(ulong chunkOffset, byte[] data)
    // LRU eviction when maxChunks exceeded

public sealed class ImagePrefetcher
    ImagePrefetcher(VaultManager vault, ImageDecoder decoder)

    void PrefetchAdjacent(int currentIndex, IReadOnlyList<Guid> photoGuids)
    // 1. Background decode photos at currentIndex-1 and currentIndex+1 (E16)
    // 2. Cache decoded SKBitmaps (max 3 in memory)

public sealed class ParallelChunkPipeline
    ParallelChunkPipeline(ChunkWriter writer, int parallelism)

    async Task ProcessFileParallel(Stream source, Guid fileGuid, ProtectionMode mode)
    // 1. Read chunks from source (producer)
    // 2. Compress + encrypt in parallel (workers, E20)
    // 3. Write to vault in order (consumer — maintain chunk sequence)
    // Uses System.Threading.Channels for producer-consumer pattern
```

### Verification Checklist

1. ✅ LRU cache evicts least-recently-used chunks, not random ones
2. ✅ Prefetcher loads at most 3 images (current + 2 adjacent) to avoid memory bloat
3. ✅ Parallel pipeline writes chunks in correct order despite parallel processing

---

## H07–H18 — Gallery Advanced Features

### Module & File Placement

- **File:** `src/SecureVault.App/Views/ImageEditorOverlay.xaml` (H07, H08)
- **File:** `src/SecureVault.App/ViewModels/ImageEditorViewModel.cs`
- **File:** `src/SecureVault.App/Views/SlideshowWindow.xaml` (H10)
- **File:** `src/SecureVault.App/Views/AlbumsPage.xaml` (H11)
- **File:** `src/SecureVault.App/Views/TimelinePage.xaml` (H12)

### Function Signatures

```csharp
// Image editing (H07, H08, H09):
public sealed class ImageEditorViewModel
    IRelayCommand CropCommand
    // 1. Show crop overlay (drag handles to select region)
    // 2. On confirm: SkiaSharp SKBitmap.ExtractSubset(cropRect)

    IRelayCommand FlipHorizontalCommand   // SKCanvas.Scale(-1, 1)
    IRelayCommand FlipVerticalCommand     // SKCanvas.Scale(1, -1)

    IAsyncRelayCommand SaveEditsCommand   // H09
    // 1. Encode edited bitmap to original format
    // 2. Replace file data in vault (C20 — Phase 6, or write as new file)

// Slideshow (H10):
public sealed class SlideshowViewModel
    int IntervalSeconds { get; set; }      // configurable, default 5s
    bool IsRunning { get; }
    IRelayCommand StartCommand
    IRelayCommand StopCommand
    // Timer: advance to next photo every IntervalSeconds

// Albums (H11):
// Albums are virtual collections — stored as metadata in the index
// An album is a list of fileGuids (not a folder — files can be in multiple albums)

// Timeline (H12):
// Group photos by date (year → month → day)
// Query index, group by DateAdded, display as sections
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Crop | Select 100x100 region of 400x400 image | Result is 100x100 |
| Flip horizontal | Flip image | Left and right sides swapped |
| Save edits | Edit and save | Vault file updated, old version gone (or versioned) |
| Slideshow | Start with 5 photos, 2s interval | Cycles through all 5 every 10s |
| Albums | Create album, add 3 photos | Album shows 3 photos |
| Timeline | 10 photos across 3 dates | 3 date groups |

### Verification Checklist

1. ✅ Crop/flip operations work on the in-memory bitmap, not on disk
2. ✅ Save edits writes back to vault (not to disk)
3. ✅ Slideshow stops on user interaction (click, keypress)

---

## I10–I25 — Player Advanced Features

### Module & File Placement

- **File:** `src/SecureVault.App/Views/PictureInPictureWindow.xaml` (I10)
- **File:** `src/SecureVault.App/Views/PlaylistPage.xaml` (I18)
- **File:** `src/SecureVault.App/ViewModels/PlaylistViewModel.cs`
- **File:** `src/SecureVault.App/Views/MiniPlayerControl.xaml` (I20)
- **File:** `src/SecureVault.App/Controls/WaveformDisplay.xaml` (I25)

### Function Signatures (selected key features)

```csharp
// Picture-in-Picture (I10):
// Create a new small Window with the video player
// Set TopMost = true (always on top)
// Allow resize and drag

// Subtitles (I11):
// libVLC: MediaPlayer.SetSpu(trackIndex) for embedded
// External: read .srt from vault, use Media.AddOption(":sub-file=...")
// NOTE: External subtitles from vault need a temp approach since libVLC
//       expects a file path — consider using a named pipe or memory mapping

// Playlists (I18):
public sealed class PlaylistViewModel
    ObservableCollection<PlaylistItem> Items { get; }
    int CurrentIndex { get; }
    IRelayCommand PlayNextCommand    // I19
    IRelayCommand PlayPreviousCommand
    IRelayCommand ShuffleCommand
    RepeatMode Repeat { get; set; }  // I15 — None, Single, All

// Background audio (I22):
// Audio continues playing when navigating to other pages
// MiniPlayerControl shows at bottom of library view

// Waveform (I25):
// Extract audio samples via libVLC or NAudio
// Draw waveform using SkiaSharp
```

⚠️ **OPEN QUESTION: External subtitles from vault**
libVLC expects subtitle files as file paths. Options:
1. **Named pipe** — create a named pipe, serve subtitle data through it
2. **Temporary file** — write .srt to temp, use it, delete after playback
3. **Parse SRT ourselves** — render subtitle overlay in WinUI, bypass libVLC

**Recommendation:** Option 3 for control and security (no temp files per M06), but this requires implementing an SRT parser and overlay renderer. If too complex, fall back to option 2 with secure temp file handling (M07 — overwrite + delete).

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| PiP window | Enable PiP during video | Small always-on-top window with video |
| Subtitles embedded | MKV with embedded subs | Subtitles display correctly |
| Playlist play queue | Add 3 videos to playlist | Play in order, next/previous works |
| Repeat all | Playlist with 2 items, repeat all | Loops back to first after last |
| Background audio | Play audio, navigate to files | Audio continues playing |
| Resume position | Play video to 50%, close, reopen | Resumes at 50% (I17) |

### Verification Checklist

1. ✅ Background audio doesn't stop on page navigation
2. ✅ Playback position is saved per-file in cache (not vault — it's UI state)
3. ✅ Keyboard shortcuts work: Space (play/pause), Arrows (seek), F (fullscreen), M (mute)

---

## J09–J20 — Notes Advanced Features

### Module & File Placement

- **File:** `src/SecureVault.Core/Notes/NoteVersionHistory.cs` (J09, J10)
- **File:** `src/SecureVault.App/Views/NoteSearchResults.xaml` (J11)
- **File:** `src/SecureVault.Core/Notes/NotebookService.cs` (J07 extended)

### Function Signatures

```csharp
// Version history (J09, J10):
public sealed class NoteVersionHistory
    void SaveVersion(Guid noteGuid, NoteDocument snapshot)
    // 1. Store snapshot with timestamp
    // 2. Keep last 10 versions (FIFO eviction)
    // 3. Versions stored as additional vault files linked to the note

    IReadOnlyList<NoteVersion> GetHistory(Guid noteGuid)
    void RestoreVersion(Guid noteGuid, int versionIndex)

// Full-text search (J11):
    IReadOnlyList<SearchResult> SearchNotes(string query)
    // Search across all note Contents, return matching notes with highlighted excerpts

// Attachments (J13, J14):
    void AttachFile(Guid noteGuid, Guid fileGuid)    // link by reference
    void EmbedImage(Guid noteGuid, Guid imageGuid)   // inline in markdown as vault:// URI

// Export (J15, J16):
    byte[] ExportAsPdf(Guid noteGuid)    // Markdown → HTML → PDF
    string ExportAsText(Guid noteGuid)   // Raw content
    string ExportAsMarkdown(Guid noteGuid)
```

### Verification Checklist

1. ✅ Version history stores at most 10 versions per note
2. ✅ Full-text search operates on decrypted note content in memory
3. ✅ Embedded images use `vault://` URI scheme, not file paths

---

## L07–L11 — PDF Advanced Features

### Function Signatures

```csharp
// Text search (L07):
    IReadOnlyList<SearchHit> SearchText(string query)
    // Use PdfiumCore text extraction + search

// Bookmarks (L08):
    IReadOnlyList<PdfBookmark> GetBookmarks()
    // Parse PDF outline/bookmarks via Pdfium

// Copy text (L09):
    string GetSelectedText(int page, Rect selectionRect)
    // Use PdfiumCore text extraction for selected region

// Remember page (L10):
    void SaveLastPage(Guid fileGuid, int page)
    // Store in cache, restore on next open

// Pre-render (L11):
    void PrefetchAdjacentPages(int currentPage)
    // Background render currentPage ± 1
```

---

## K01–K13 — File Manager + Archives

### Module & File Placement

- **File:** `src/SecureVault.App/Views/FileManagerPage.xaml` + `.cs`
- **File:** `src/SecureVault.App/ViewModels/FileManagerViewModel.cs`
- **File:** `src/SecureVault.Core/Archives/ArchiveReader.cs` (K09–K11)
- **Dependencies:** VirtualFolderService, VaultIndex, SharpCompress

### Function Signatures

```csharp
public sealed class FileManagerViewModel
    ObservableCollection<FileTreeItem> FolderTree { get; }
    ObservableCollection<FileListItem> CurrentFiles { get; }
    string CurrentPath { get; }

    // K01–K06: Navigation, selection, context menu
    IRelayCommand NavigateCommand
    IRelayCommand<IList<FileListItem>> BulkSelectCommand
    IRelayCommand CutCommand
    IRelayCommand CopyCommand
    IRelayCommand PasteCommand
    IRelayCommand DragDropMoveCommand

    // K07: Folder size
    long CalculateFolderSize(Guid folderGuid)
    // Sum OriginalSize of all files recursively

    // K08: Duplicate finder
    IReadOnlyList<DuplicateGroup> FindDuplicates()
    // Group files by PlaintextSHA256 — groups with >1 file are duplicates

    // K12: File type statistics
    Dictionary<FileCategory, (int count, long totalSize)> GetStatistics()

public sealed class ArchiveReader
    ArchiveReader(byte[] archiveData)

    IReadOnlyList<ArchiveEntry> ListContents()
    // Use SharpCompress to list ZIP/RAR/7Z contents (K09)

    byte[] ExtractSingle(string entryPath)
    // Extract one file from archive to memory (K10)

    IReadOnlyList<(string path, byte[] data)> ExtractAll()
    // Extract all files to memory (K11)
```

### Exact Library Calls

- `SharpCompress.Archives.ArchiveFactory.Open(stream)` — open any archive format
- `IArchive.Entries` — list contents
- `IArchiveEntry.OpenEntryStream()` — extract single entry

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Browse ZIP | Add ZIP to vault, browse | Shows file listing |
| Extract single | Extract one file from ZIP | File added to vault |
| Extract all | Extract all from ZIP | All files added to vault, preserving structure |
| Duplicate finder | Add same file twice (different names) | Found as duplicates (same SHA-256) |
| Folder size | Folder with 3 files (100B, 200B, 300B) | Returns 600 |
| Statistics | 5 photos, 3 videos, 2 docs | Correct counts and sizes per category |

### Verification Checklist

1. ✅ Archive contents are extracted to vault, not to disk
2. ✅ SharpCompress is used for all archive formats — no custom ZIP/RAR parsing
3. ✅ Duplicate finder uses PlaintextSHA-256 from index (no re-hashing needed)

---

## Source File Summary

```
src/SecureVault.Core/
├── Operations/
│   └── ProtectionModeOperation.cs     (A17, A18)
├── Media/
│   └── ThumbnailGenerator.cs          (E08-E14)
│   └── ThumbnailService.cs            (E08-E14)
│   └── ImagePrefetcher.cs             (E16)
├── Cache/
│   ├── ChunkLruCache.cs               (E15)
│   └── PlaybackPositionCache.cs       (E17)
├── IO/
│   └── ParallelChunkPipeline.cs       (E20)
├── Notes/
│   ├── NoteVersionHistory.cs          (J09, J10)
│   └── NotebookService.cs             (J07 extended)
├── Archives/
│   └── ArchiveReader.cs               (K09-K11)

src/SecureVault.App/
├── Services/
│   ├── IdleLockService.cs             (A08)
│   └── SystemLockDetector.cs          (A08, M08)
├── Views/
│   ├── ImageEditorOverlay.xaml        (H07, H08)
│   ├── SlideshowWindow.xaml           (H10)
│   ├── AlbumsPage.xaml                (H11)
│   ├── TimelinePage.xaml              (H12)
│   ├── PictureInPictureWindow.xaml    (I10)
│   ├── PlaylistPage.xaml              (I18)
│   ├── MiniPlayerControl.xaml         (I20)
│   ├── NoteSearchResults.xaml         (J11)
│   └── FileManagerPage.xaml           (K01)
├── ViewModels/
│   ├── ImageEditorViewModel.cs        (H07-H09)
│   ├── PlaylistViewModel.cs           (I18)
│   └── FileManagerViewModel.cs        (K01-K13)
├── Controls/
│   └── WaveformDisplay.xaml           (I25)
```

## Test Vector Files

```
tests/vectors/
└── thumbnail-dimensions.json          (E09 — verify max 200x200 output)
```

## Branch & PR

- **Branch:** `phase-4/advanced-features`
- **PR Title:** "Phase 4: Advanced Features — Thumbnails, Auto-lock, Editing, File Manager"
- **PR Description:**

```
Adds advanced capabilities across all integrated apps and core systems.

## Core
- Password hint (stored unencrypted in header for recovery)
- Auto-lock on idle timeout + system screen lock
- "Encrypt Everything" batch conversion (Fast → Secure mode)
- Per-file protection mode toggle

## Performance
- Thumbnail generation: images (SkiaSharp), video (frame at 10%), audio (album art), PDF (first page)
- WebP thumbnails, 200x200 max, parallel generation
- LRU chunk cache (16 chunks / 16MB)
- Image prefetcher (current + 2 adjacent)
- Parallel chunk encryption pipeline

## Gallery
- Crop, flip horizontal/vertical, save edits back to vault
- Slideshow with configurable interval
- Albums (virtual collections)
- Timeline view (grouped by date)
- HEIC/RAW support, large image handling

## Player
- Picture-in-Picture, subtitles, chapters
- Playlists, shuffle, repeat modes
- Mini player for audio, background playback
- Resume position, keyboard shortcuts, waveform

## Notes
- Version history (last 10 per note), restore
- Full-text search across all notes
- Attachments (file references), embedded vault images
- Export as PDF, TXT, Markdown
- Pins, tags, timestamps

## PDF
- Text search, bookmarks, copy text selection
- Remember last page, pre-render adjacent pages

## File Manager
- Full file manager with virtual folder tree
- Bulk selection, cut/copy/paste, drag-drop between folders
- Folder size calculation, file type statistics
- Duplicate file finder (by SHA-256)
- Archive browsing and extraction (ZIP/RAR/7Z via SharpCompress)
```

## CONTRIBUTING Note for Phase 4

```
CONTRIBUTING — Phase 4 (Advanced Features)

1. Thumbnail generation must produce WebP format at 200x200 max.
   Do not change the format or dimensions without updating the cache schema.

2. Auto-lock must zero all keys — test by checking that file reads
   fail after auto-lock triggers.

3. Archive extraction goes TO the vault, never to disk.

4. The parallel chunk pipeline must maintain chunk order — out-of-order
   writes will corrupt the vault format.
```

## STATUS.md Entries for Phase 4

```
A05 🔨 Password hint
A07 🔨 Recovery key unlock (UI)
A08 🔨 Auto-lock on idle
A09 🔨 Failed attempt delay (finalized)
A17 🔨 "Encrypt Everything" button
A18 🔨 Toggle protection mode per file
E08 🔨 Background thumbnail generation
E09 🔨 Thumbnail format: WebP, 200x200
E10 🔨 Image thumbnails (SkiaSharp)
E11 🔨 Video thumbnails (frame at 10%)
E12 🔨 Audio thumbnails (album art)
E13 🔨 PDF thumbnails (first page)
E14 🔨 Parallel thumbnail generation
E15 🔨 LRU chunk cache
E16 🔨 Pre-render adjacent images
E17 🔨 Cache playback positions
E18 🔨 Cache UI state
E19 🔨 Streaming decryption
E20 🔨 Parallel chunk processing
H07 🔨 Crop
H08 🔨 Flip
H09 🔨 Save edits to vault
H10 🔨 Slideshow
H11 🔨 Albums
H12 🔨 Timeline view
H13 🔨 Favorites filter (gallery)
H14 🔨 All image formats
H15 🔨 Decode in memory
H16 🔨 Pre-load adjacent
H17 🔨 SkiaSharp GPU rendering
H18 🔨 Large image handling
I10 🔨 Picture-in-Picture
I11 🔨 Subtitle support
I12 🔨 Audio track selection
I13 🔨 Chapter navigation
I14 🔨 Screenshot during playback
I15 🔨 Loop/repeat modes
I16 🔨 Keyboard shortcuts
I17 🔨 Resume playback
I18 🔨 Playlists
I19 🔨 Play next/previous
I20 🔨 Mini player for audio
I21 🔨 Album art display
I22 🔨 Background audio
I23 🔨 Hardware accelerated video
I24 🔨 All media formats
I25 🔨 Waveform visualization
J09 🔨 Version history
J10 🔨 Restore version
J11 🔨 Full-text search
J12 🔨 Word count
J13 🔨 Attach vault files
J14 🔨 Embed images
J15 🔨 Export as PDF
J16 🔨 Export as TXT/MD
J17 🔨 Pin notes
J18 🔨 Tags on notes
J19 🔨 Note timestamps
J20 🔨 Mixed content
K01 🔨 Virtual folder tree navigation
K02 🔨 File list with details
K03 🔨 Bulk selection
K04 🔨 Cut/copy/paste
K05 🔨 Drag-drop between folders
K06 🔨 Context menu
K07 🔨 Folder size calculation
K08 🔨 Duplicate file finder
K09 🔨 Browse archives
K10 🔨 Extract single from archive
K11 🔨 Extract all from archive
K12 🔨 File type statistics
K13 🔨 SharpCompress library
L07 🔨 Text search in PDF
L08 🔨 Bookmarks panel
L09 🔨 Copy text selection
L10 🔨 Remember last page
L11 🔨 Pre-render adjacent pages
```
