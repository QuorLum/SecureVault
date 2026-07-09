# Phase 3: Integrated Apps — Implementation Roadmap

> **Branch:** `phase-3/integrated-apps`
>
> **Scope:** Gallery (basic viewing), video player (basic playback), audio player,
> notes (basic editing), PDF viewer (basic).
>
> **Feature IDs:** H01–H06, I01–I09, J01–J08, L01–L06
>
> **Prior Phases:** Phase 1 (core engine) + Phase 2 (UI + file ops) must be complete.

---

## Build Order & Dependency Graph

```
Level 0 (depends only on Phase 1+2):
  All integrated apps are independent of each other.
  They all depend on:
    - VaultFileStream (C18, Phase 1) for streaming reads
    - FileReadOperation (C16, Phase 1) for small-file reads
    - MainLibraryPage (N05, Phase 2) for navigation routing

  Build in any order, but recommended:
  1. Gallery (H01–H06) — simplest, validates VaultFileStream
  2. Notes (J01–J08) — text-only, no media libraries
  3. PDF Viewer (L01–L06) — single library dependency
  4. Video Player (I01–I09) — most complex, libVLC integration
  5. Audio Player (I01–I02, subset of video) — shares libVLC
```

---

## H01–H06 — Gallery (Basic Viewing)

### Module & File Placement

- **File:** `src/SecureVault.App/Views/GalleryPage.xaml` + `.cs`
- **File:** `src/SecureVault.App/ViewModels/GalleryViewModel.cs`
- **File:** `src/SecureVault.App/Views/PhotoViewerPage.xaml` + `.cs`
- **File:** `src/SecureVault.App/ViewModels/PhotoViewerViewModel.cs`
- **File:** `src/SecureVault.Core/Media/ImageDecoder.cs`
- **Dependencies:** VaultFileStream, SkiaSharp, MainLibraryPage
- **Depended on by:** Phase 4 (H07-H18, advanced gallery)

### Data Structures

```
PhotoItem (ViewModel for grid display)
  FileGUID      : Guid
  FileName      : string
  ThumbnailSource : ImageSource?     (null until loaded)
  DateAdded     : DateTime
  FileSize      : long

GalleryState
  CurrentIndex  : int               (position in photo list, for prev/next)
  ZoomLevel     : double            (1.0 = fit, higher = zoomed in)
  PanOffset     : Point             (for dragging zoomed image)
```

### Function Signatures

```csharp
public sealed class ImageDecoder
    static SKBitmap DecodeFromVault(VaultManager vault, Guid fileGuid)
    // 1. Read file to memory via FileReadOperation (small files) or stream
    // 2. SkiaSharp: SKBitmap.Decode(data)
    // 3. For HEIC/RAW: use Magick.NET to convert to PNG in memory, then SKBitmap.Decode
    // 4. Return decoded bitmap (never touches disk — H15)

    static SKBitmap DecodeAtResolution(VaultManager vault, Guid fileGuid, int maxWidth, int maxHeight)
    // 1. Decode full image
    // 2. If larger than maxWidth×maxHeight, resize to fit (maintain aspect ratio)
    // 3. Return resized bitmap (H18 — screen-res decode, full on zoom)

public sealed class GalleryViewModel : ObservableObject
    ObservableCollection<PhotoItem> Photos { get; }
    PhotoItem? SelectedPhoto { get; set; }

    void LoadPhotos(Guid? folderGuid, FileCategory? category)
    // 1. Query index for Photos category (or specific folder)
    // 2. Sort by DateAdded descending
    // 3. Populate Photos collection
    // 4. Start background thumbnail loading

    IRelayCommand OpenPhotoCommand
    // Navigate to PhotoViewerPage with selected photo

public sealed class PhotoViewerViewModel : ObservableObject
    SKBitmap? CurrentImage { get; }
    int CurrentIndex { get; set; }
    double ZoomLevel { get; set; }

    IAsyncRelayCommand LoadImageCommand
    // Decode current photo from vault

    IRelayCommand NextCommand        // H03 — swipe/arrow right
    IRelayCommand PreviousCommand    // H03 — swipe/arrow left
    IRelayCommand ZoomInCommand      // H04
    IRelayCommand ZoomOutCommand     // H04
    IRelayCommand RotateCWCommand    // H06
    IRelayCommand RotateCCWCommand   // H06
```

### Exact Library Calls

- `SkiaSharp.SKBitmap.Decode(ReadOnlySpan<byte>)` — image decode
- `SkiaSharp.SKBitmap.Resize(SKImageInfo, SKFilterQuality)` — resize
- `SkiaSharp.SKCanvas.RotateDegrees(float)` — rotation
- `Magick.NET.MagickImage(byte[])` → `.ToByteArray(MagickFormat.Png)` — HEIC/RAW conversion
- `SkiaSharp.Views.WinUI.SKXamlCanvas` — WinUI 3 rendering surface

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Decode JPEG | Add JPEG to vault, decode | SKBitmap with correct dimensions |
| Decode PNG | Add PNG to vault, decode | SKBitmap with alpha channel preserved |
| Decode at resolution | 4000x3000 image, maxWidth=800 | Decoded bitmap ≤ 800px wide |
| Navigate next/prev | 3 photos, start at 0 | Next→1, Next→2, Prev→1 |
| Zoom in/out | Default zoom | ZoomIn increases, ZoomOut decreases, minimum 1.0 |
| Rotate | Rotate CW | Image rotated 90° clockwise |
| HEIC support | Add HEIC file | Decoded correctly via Magick.NET |
| In-memory only | Decode any image | No temp files created on disk (H15) |

### Verification Checklist

1. ✅ Search for `File.Write` or `Path.GetTempPath()` in gallery code — must not appear (H15)
2. ✅ HEIC and RAW files decode without crashing
3. ✅ Gallery shows correct photo count matching the index
4. ✅ Zoom uses SkiaSharp GPU canvas, not software scaling

---

## I01–I09 — Video & Audio Player (Basic)

### Module & File Placement

- **File:** `src/SecureVault.App/Views/MediaPlayerPage.xaml` + `.cs`
- **File:** `src/SecureVault.App/ViewModels/MediaPlayerViewModel.cs`
- **File:** `src/SecureVault.Core/Media/VaultMediaInput.cs`
- **Dependencies:** VaultFileStream, LibVLCSharp, MainLibraryPage
- **Depended on by:** Phase 4 (I10-I25, advanced player)

### Data Structures

```
MediaPlayerState
  IsPlaying       : bool
  CurrentPosition : TimeSpan
  Duration        : TimeSpan
  Volume          : int (0-100)
  PlaybackSpeed   : double (0.25-4.0)
  IsFullScreen    : bool
```

### Function Signatures

```csharp
public sealed class VaultMediaInput : MediaInput
    // Bridges VaultFileStream to libVLC's StreamMediaInput
    VaultMediaInput(VaultFileStream stream)

    // Implements libVLC's MediaInput interface:
    override bool Open(out long size)
    // 1. size = stream.Length
    // 2. Return true

    override int Read(IntPtr buf, uint len)
    // 1. Read from VaultFileStream into buffer
    // 2. Return bytes read (chunk-based seeking handles the rest)

    override bool Seek(long offset)
    // 1. stream.Seek(offset, SeekOrigin.Begin)
    // 2. Return true

    override void Close()
    // Dispose VaultFileStream

public sealed class MediaPlayerViewModel : ObservableObject
    LibVLC LibVLC { get; }
    MediaPlayer Player { get; }

    IAsyncRelayCommand PlayCommand
    // 1. Open VaultFileStream for selected file
    // 2. Create VaultMediaInput wrapper
    // 3. Create Media from VaultMediaInput
    // 4. Player.Play(media)

    IRelayCommand PauseCommand    // Player.Pause()
    IRelayCommand StopCommand     // Player.Stop(), dispose media input

    double Progress { get; set; }  // 0.0–1.0, bound to seek bar
    // Getter: Player.Position
    // Setter: Player.Position = value (I06 — seek)

    int Volume { get; set; }       // I07
    // Getter: Player.Volume
    // Setter: Player.Volume = value

    double PlaybackSpeed { get; set; }  // I08
    // Player.SetRate(float)

    IRelayCommand ToggleFullScreenCommand  // I09
```

### Exact Library Calls

- `LibVLCSharp.Shared.Core.Initialize()` — one-time init (load libVLC native DLLs)
- `new LibVLC("--no-video-title-show")` — create engine (no splash)
- `new MediaPlayer(libVLC)` — create player
- `new Media(libVLC, new StreamMediaInput(vaultMediaInput))` — create media from stream
- `MediaPlayer.Play(media)` — start playback
- `MediaPlayer.Pause()` — pause
- `MediaPlayer.Stop()` — stop
- `MediaPlayer.Position` — get/set playback position (0.0f–1.0f)
- `MediaPlayer.Volume` — get/set volume (0–100)
- `MediaPlayer.SetRate(float)` — playback speed

⚠️ **OPEN QUESTION: LibVLC WinUI 3 rendering surface**
LibVLCSharp has a `VideoView` control for UWP/WinUI. Options:
1. **LibVLCSharp.WinUI** — official WinUI 3 package (may be in preview)
2. **Use HWND interop** — create a Win32 window for video rendering, embed in WinUI
3. **LibVLCSharp.WPF in a WPF island** — wrap WPF VideoView in a XAML island

**Recommendation:** Check `LibVLCSharp.WinUI` availability first. If stable, use it. If not, use HWND interop (option 2) — this is how VLC itself works.

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Play MP4 | Add MP4 to vault, play | Video plays, audio heard |
| Play MP3 | Add MP3, play | Audio plays |
| Seek | Play video, seek to 50% | Playback resumes from midpoint |
| Pause/Resume | Play, pause, play | Resumes from pause point |
| Volume | Set volume to 0 | Silent, set to 100 → audible |
| Speed | Set 2x speed | Plays at double speed |
| Stream from vault | Play 500MB video | Plays without extracting to disk (I04) |
| Full screen | Toggle full screen | Window goes full screen and back |

### Verification Checklist

1. ✅ Search for temp file creation in media player code — must not exist (I04)
2. ✅ libVLC is dynamically linked (DLL, not statically compiled — licensing, B27 note in vision)
3. ✅ Seeking works correctly across chunk boundaries (test with video > 1MB)
4. ✅ Audio-only files show a placeholder visual (album art or waveform placeholder)

---

## J01–J08 — Notes (Basic Editing)

### Module & File Placement

- **File:** `src/SecureVault.App/Views/NotesEditorPage.xaml` + `.cs`
- **File:** `src/SecureVault.App/ViewModels/NotesEditorViewModel.cs`
- **File:** `src/SecureVault.Core/Notes/NoteDocument.cs`
- **Dependencies:** VaultManager (read/write), Markdig (markdown parsing)
- **Depended on by:** Phase 4 (J09-J20, advanced notes)

### Data Structures

```
NoteDocument (stored as a file in the vault)
  Content      : string          (plain text or markdown)
  Format       : NoteFormat      (PlainText=0, Markdown=1, RichText=2)
  Created      : DateTime (UTC)
  Modified     : DateTime (UTC)
  WordCount    : int             (computed, J12)

NoteFormat enum:
  PlainText = 0
  Markdown  = 1
  RichText  = 2
```

### Function Signatures

```csharp
public sealed class NoteDocument
    string Content { get; set; }
    NoteFormat Format { get; set; }
    DateTime Created { get; }
    DateTime Modified { get; set; }
    int WordCount => Content.Split(WhitespaceChars, StringSplitOptions.RemoveEmptyEntries).Length

    byte[] Serialize()
    // 1. Create JSON: { "format": 1, "content": "...", "created": "...", "modified": "..." }
    // 2. Encode as UTF-8 bytes

    static NoteDocument Deserialize(byte[] data)
    // 1. Parse JSON
    // 2. Return NoteDocument

public sealed class NotesEditorViewModel : ObservableObject
    string Content { get; set; }
    string RenderedHtml { get; }        // Markdown preview (J03)
    NoteFormat Format { get; set; }
    int WordCount { get; }
    bool HasUnsavedChanges { get; }
    DateTime LastSaved { get; }

    IRelayCommand NewNoteCommand         // J01
    IAsyncRelayCommand SaveCommand       // J08 — auto-save, also manual
    IRelayCommand TogglePreviewCommand   // J03 — toggle markdown preview panel

    void StartAutoSave()
    // 1. Start timer (debounce: 3 seconds after last keystroke)
    // 2. On timer tick: if HasUnsavedChanges, save to vault
    // 3. Save = serialize NoteDocument → FileAddOperation or UpdateFile
```

### Exact Library Calls

- `Markdig.Markdown.ToHtml(markdownText, pipeline)` — markdown → HTML
- `Markdig.MarkdownPipelineBuilder().UseAdvancedExtensions().Build()` — full pipeline
- WinUI `RichEditBox` — rich text editing (J04)
- WinUI `TextBox` — plain text editing (J02)
- WinUI `WebView2` (or `MarkdownTextBlock` from CommunityToolkit) — markdown preview

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Create note | New plain text note, type "Hello" | Content = "Hello", Format = PlainText |
| Save and reload | Create, save, close, reopen | Content preserved |
| Markdown preview | Type `# Title` | Preview shows `<h1>Title</h1>` |
| Auto-save fires | Type text, wait 4 seconds | Note saved to vault without manual action |
| Word count | "The quick brown fox" | WordCount = 4 |
| Rich text | Bold/italic via RichEditBox | Formatting preserved on save/load |

### Verification Checklist

1. ✅ Auto-save debounce works — rapid typing doesn't trigger saves per keystroke
2. ✅ Notes are stored as vault files (same encryption as any other file)
3. ✅ Markdown preview renders correctly for headers, lists, bold, italic, code blocks
4. ✅ Word count updates in real time as user types

---

## L01–L06 — PDF Viewer (Basic)

### Module & File Placement

- **File:** `src/SecureVault.App/Views/PdfViewerPage.xaml` + `.cs`
- **File:** `src/SecureVault.App/ViewModels/PdfViewerViewModel.cs`
- **File:** `src/SecureVault.Core/Media/PdfRenderer.cs`
- **Dependencies:** VaultFileStream, PdfiumCore, SkiaSharp
- **Depended on by:** Phase 4 (L07-L11, advanced PDF)

### Data Structures

```
PdfViewState
  CurrentPage    : int            (0-based)
  TotalPages     : int
  ZoomLevel      : double         (1.0 = fit to width)
  ZoomMode       : ZoomMode       (FitWidth=0, FitPage=1, Custom=2)
```

### Function Signatures

```csharp
public sealed class PdfRenderer : IDisposable
    PdfRenderer(byte[] pdfData)
    // 1. Load PDF from memory using PdfiumCore (L01 — in memory, never disk)
    // 2. Get page count

    int PageCount { get; }

    SKBitmap RenderPage(int pageIndex, double dpi = 150)
    // 1. Get page dimensions from Pdfium
    // 2. Calculate pixel size at requested DPI
    // 3. Render page to bitmap via Pdfium
    // 4. Convert to SKBitmap for display
    // 5. Return bitmap

    void Dispose()
    // Close Pdfium document handle

public sealed class PdfViewerViewModel : ObservableObject
    int CurrentPage { get; set; }
    int TotalPages { get; }
    double ZoomLevel { get; set; }
    ZoomMode CurrentZoomMode { get; set; }
    SKBitmap? CurrentPageBitmap { get; }

    IAsyncRelayCommand LoadPdfCommand
    // 1. Read PDF from vault to memory
    // 2. Create PdfRenderer
    // 3. Render first page

    IRelayCommand NextPageCommand        // L05
    IRelayCommand PreviousPageCommand    // L05
    IRelayCommand GoToPageCommand        // L05 — input page number
    IRelayCommand ZoomInCommand          // L03
    IRelayCommand ZoomOutCommand         // L03
    IRelayCommand FitToWidthCommand      // L04
    IRelayCommand FitToPageCommand       // L04

    void ScrollToNextPage()              // L06 — continuous scroll between pages
```

### Exact Library Calls

- `PdfiumCore.PdfDocument.Load(byte[])` — load PDF from memory
- `PdfDocument.Pages[index]` — access page
- `PdfPage.Render(width, height, ...)` — render to bitmap
- `PdfiumCore.fpdfview.FPDF_InitLibrary()` — one-time initialization

⚠️ **OPEN QUESTION: PdfiumCore vs PDFium.NET vs Docnet**
The vision doc says "Pdfium engine." Several .NET wrappers exist:
1. **PdfiumCore** — thin wrapper, MIT license, actively maintained
2. **Docnet** — higher-level API, MIT, but less control
3. **PDFium.NET** — commercial, more features

**Recommendation:** PdfiumCore — closest to the raw Pdfium API, MIT licensed, small footprint.

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Load PDF | Add PDF to vault, open viewer | First page rendered |
| Page count | 10-page PDF | TotalPages = 10 |
| Navigate | Go to page 5 | Page 5 rendered correctly |
| Zoom in | Zoom from 1.0 to 2.0 | Higher resolution render |
| Fit to width | Any PDF | Page width fills viewer width |
| Fit to page | Any PDF | Entire page visible |
| Scroll | Scroll down | Pages transition smoothly |
| In-memory | Open PDF | No temp files on disk |

### Verification Checklist

1. ✅ PDF data is loaded from vault into memory — search for temp file usage, must not exist
2. ✅ `FPDF_InitLibrary()` is called once at app startup, not per document
3. ✅ Page rendering uses correct DPI for the display (consider high-DPI monitors)
4. ✅ Navigating to an invalid page number is handled gracefully

---

## Source File Summary

```
src/SecureVault.Core/
├── Media/
│   ├── ImageDecoder.cs               (H01-H06)
│   ├── PdfRenderer.cs                (L01-L06)
│   └── VaultMediaInput.cs            (I01-I09)
├── Notes/
│   └── NoteDocument.cs               (J01-J08)

src/SecureVault.App/
├── Views/
│   ├── GalleryPage.xaml / .cs         (H01)
│   ├── PhotoViewerPage.xaml / .cs     (H02-H06)
│   ├── MediaPlayerPage.xaml / .cs     (I01-I09)
│   ├── NotesEditorPage.xaml / .cs     (J01-J08)
│   └── PdfViewerPage.xaml / .cs       (L01-L06)
├── ViewModels/
│   ├── GalleryViewModel.cs            (H01)
│   ├── PhotoViewerViewModel.cs        (H02-H06)
│   ├── MediaPlayerViewModel.cs        (I01-I09)
│   ├── NotesEditorViewModel.cs        (J01-J08)
│   └── PdfViewerViewModel.cs          (L01-L06)
```

## Test Vector Files

No new test vector files for Phase 3 — testing is behavioral (UI + media rendering),
not cryptographic. Tests use sample media files, not computed vectors.

## Branch & PR

- **Branch:** `phase-3/integrated-apps`
- **PR Title:** "Phase 3: Integrated Apps — Gallery, Media Player, Notes, PDF"
- **PR Description:**

```
Adds four integrated viewers/editors that work directly with vault contents.

## Gallery (H01–H06)
- Photo grid with thumbnails, full-screen viewer
- Swipe/arrow key navigation, pinch/scroll zoom
- Rotate clockwise/counter-clockwise
- EXIF display (camera, GPS, date)
- Supports: JPG, PNG, GIF, WebP, BMP, SVG, TIFF, ICO, HEIC, RAW
- All decoding in memory (SkiaSharp + Magick.NET for HEIC/RAW)

## Media Player (I01–I09)
- Video and audio playback via libVLC
- Streams directly from vault via VaultMediaInput (no temp files)
- Play/pause/stop, seek bar, volume, playback speed (0.25x–4x)
- Full-screen mode
- Supports: MP4, MKV, AVI, MOV, WebM, FLV, WMV, MP3, FLAC, WAV, AAC, OGG, etc.

## Notes (J01–J08)
- Plain text, Markdown, and rich text editors
- Markdown live preview (Markdig)
- Auto-save with 3-second debounce
- Word count
- Notes stored as encrypted vault files

## PDF Viewer (L01–L06)
- Page rendering via PdfiumCore (in memory)
- Zoom in/out, fit to width/page
- Page navigation (next, prev, go-to)
- Continuous scroll

## Key design decision
All viewers decode/render in memory. No data touches disk during viewing.
libVLC is dynamically linked (LGPL compliance).
```

## CONTRIBUTING Note for Phase 3

```
CONTRIBUTING — Phase 3 (Integrated Apps)

1. All media is decoded from vault in memory — NEVER write temp files
   to disk for viewing. Search for Path.GetTempPath, File.Create,
   File.WriteAllBytes — they must not appear in this phase's code.

2. libVLC is LGPL — it must remain dynamically linked (separate DLL).
   Do NOT statically compile libVLC into the app binary.

3. SkiaSharp rendering should use SKXamlCanvas for GPU acceleration.
   Falling back to software rendering is acceptable only on systems
   without GPU support.

4. PDF rendering DPI should respect the system's display scaling factor.
```

## STATUS.md Entries for Phase 3

```
H01 🔨 Photo grid view
H02 🔨 Full-screen photo viewer
H03 🔨 Navigate between photos
H04 🔨 Zoom (pinch/scroll)
H05 🔨 EXIF data display
H06 🔨 Rotate
I01 🔨 Video playback (libVLC)
I02 🔨 Audio playback (libVLC)
I03 🔨 Stream from vault
I04 🔨 No temp files for playback
I05 🔨 Play/pause/stop
I06 🔨 Seek bar
I07 🔨 Volume control
I08 🔨 Playback speed
I09 🔨 Full screen
J01 🔨 Create new note
J02 🔨 Plain text editor
J03 🔨 Markdown editor + preview
J04 🔨 Rich text editor
J05 🔨 Checklists
J06 🔨 Code snippets (syntax highlighting)
J07 🔨 Notebooks/sections
J08 🔨 Auto-save
L01 🔨 Open PDF from vault
L02 🔨 Page rendering (Pdfium)
L03 🔨 Zoom in/out
L04 🔨 Fit to width/page
L05 🔨 Page navigation
L06 🔨 Scroll through pages
```
