# Phase 2: Basic UI + File Operations — Implementation Roadmap

> **Branch:** `phase-2/basic-ui`
>
> **Scope:** Login screen, main library UI, recovery-key confirmation gate, multi-file
> add, drag-drop, rename/move/export, folders, categories, tags, favorites, search, sort,
> cache system, progressive loading.
>
> **Feature IDs:** N01–N09, N23, C02–C04, C07, C10–C15, D01–D06, D08–D15, E01–E07
>
> **Prior Phase:** Phase 1 (vault core engine, crypto, format, integrity) must be complete.

---

## Build Order & Dependency Graph

```
Level 0 (depends only on Phase 1):
  D01  Virtual folder system
  D03  File categories enum
  D04  Auto-categorization

Level 1 (depends on Level 0):
  D02  Create/rename/delete folders
  D05  Tags per file
  D06  Favorites
  D08  Search by filename
  D15  Sort by name/date/size/type

Level 2 (depends on Level 1):
  D09–D14  Search by tags, notes, type, date, size, protection level

Level 3 (depends on Phase 1 + Levels 0–2, introduces UI):
  N01  WinUI 3 project setup
  E01  Encrypted cache file
  E02  Cache content definition
  E03  Instant startup from cache

Level 4 (depends on Level 3):
  N02  Login screen
  N03  Password hint display
  N04  Recovery key entry option
  N23  Recovery key confirmation gate (vault creation flow)

Level 5 (depends on Level 4):
  N05  Main library view
  N06  Sidebar
  N07  Toolbar
  N08  Status bar
  N09  File grid view

Level 6 (depends on Level 5):
  C02  Add multiple files
  C03  Add folder recursively
  C04  Drag and drop
  C07  Progress reporting
  C10  Rename file
  C11  Move file
  C12  Copy file
  C13  Export single file
  C14  Export multiple files
  C15  Export folder
  E04  Background cache freshness
  E05  Incremental cache update
  E06  Progressive loading
  E07  Virtualized UI lists
```

---

## D01 — Virtual Folder System

### Module & File Placement

- **File:** `src/SecureVault.Core/Organization/VirtualFolder.cs`
- **Dependencies:** VaultIndex (Phase 1) — folders are metadata in the index, not physical entities
- **Depended on by:** D02, N06 (sidebar), C11/C12 (move/copy)

### Data Structures

```
VirtualFolder
  FolderGUID     : Guid
  Name           : string
  ParentGUID     : Guid?          (null = root)
  DateCreated    : DateTime (UTC)
  Children       : List<Guid>     (computed at runtime, not stored — derived from index entries)

Folders are stored as special IndexEntries in the VaultIndex with a flag:
  IndexEntry.IsFolder = true
  IndexEntry.VirtualFolderPath = parent path (e.g., "/Photos/2024")
```

### Function Signatures

```csharp
public sealed class VirtualFolderService
    VirtualFolderService(VaultIndex index)

    VirtualFolder GetRoot()
    // Return virtual root folder containing top-level folders and files

    VirtualFolder GetFolder(Guid folderGuid)
    // Look up folder by GUID in index

    IReadOnlyList<VirtualFolder> GetSubfolders(Guid parentGuid)
    // Filter index entries where IsFolder=true and ParentGUID=parentGuid

    IReadOnlyList<IndexEntry> GetFiles(Guid folderGuid)
    // Filter index entries where IsFolder=false and ParentFolderGUID=folderGuid

    string GetFullPath(Guid folderGuid)
    // Walk parent chain to build path like "/Photos/2024/Vacation"
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Create root folder | GetRoot() | Returns folder with no parent, contains all top-level items |
| Nested folders | Create /A/B/C | GetFullPath(C) returns "/A/B/C" |
| Files in folder | Add file to /Photos | GetFiles(photosGuid) includes the file |
| Unlimited nesting | Create 20-level deep folder hierarchy | Works correctly |

### Verification Checklist

1. ✅ Folders exist only in the index — no physical directories on disk
2. ✅ Deleting a folder does not delete its files (files become "unfoldered" or moved to root)

---

## D03, D04 — File Categories & Auto-Categorization

### Module & File Placement

- **File:** `src/SecureVault.Core/Organization/FileCategory.cs`
- **Dependencies:** None
- **Depended on by:** D04, D11 (search by type), N06 (sidebar categories)

### Data Structures

```csharp
public enum FileCategory : byte
{
    Photos      = 0,    // .jpg, .png, .gif, .webp, .bmp, .svg, .tiff, .ico, .heic, .cr2, .nef, .arw, .dng
    Videos      = 1,    // .mp4, .mkv, .avi, .mov, .webm, .flv, .wmv
    Audio       = 2,    // .mp3, .flac, .wav, .aac, .ogg, .wma, .opus
    Documents   = 3,    // .pdf, .doc, .docx, .xls, .xlsx, .ppt, .pptx, .odt
    TextNotes   = 4,    // .txt, .md, .json, .xml, .html, .css, .js, .cs, .py, .yaml, .log
    Applications = 5,   // .exe, .msi, .appx, .bat, .ps1, .sh
    Archives    = 6,    // .zip, .rar, .7z, .tar, .gz, .bz2
    Other       = 7     // everything else
}
```

### Function Signatures

```csharp
public static class AutoCategorizer
    static FileCategory Categorize(string fileName)
    // 1. Extract extension (case-insensitive)
    // 2. Look up in extension→category dictionary
    // 3. If not found, return Other
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Photo | `"photo.jpg"` | `FileCategory.Photos` |
| Video | `"movie.MKV"` (case) | `FileCategory.Videos` |
| Unknown | `"data.xyz"` | `FileCategory.Other` |
| No extension | `"README"` | `FileCategory.Other` |

### Verification Checklist

1. ✅ Every extension listed in the vision doc (H14, I24) maps to the correct category
2. ✅ Case-insensitive matching works

---

## D02, D05, D06 — Folder CRUD, Tags, Favorites

### Module & File Placement

- **File:** `src/SecureVault.Core/Organization/VirtualFolderService.cs` (extends D01)
- **File:** `src/SecureVault.Core/Organization/TagService.cs`
- **Dependencies:** VaultIndex, VirtualFolderService
- **Depended on by:** D09 (search by tags), D16 (filter favorites)

### Function Signatures

```csharp
// VirtualFolderService additions:
    Guid CreateFolder(string name, Guid? parentGuid)
    void RenameFolder(Guid folderGuid, string newName)
    void DeleteFolder(Guid folderGuid)
    // Delete marks folder as deleted in index; files can be moved to root or deleted separately

// TagService:
public sealed class TagService
    TagService(VaultIndex index)

    void AddTag(Guid fileGuid, string tag)
    void RemoveTag(Guid fileGuid, string tag)
    IReadOnlyList<string> GetTags(Guid fileGuid)
    IReadOnlyList<string> GetAllTags()  // all unique tags across all files

// Favorites (part of IndexEntry — simple flag toggle):
    void SetFavorite(Guid fileGuid, bool isFavorite)
    IReadOnlyList<IndexEntry> GetFavorites()
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Create folder | CreateFolder("Photos", root) | Folder in index, GetSubfolders(root) includes it |
| Rename folder | RenameFolder(guid, "Images") | Name updated in index |
| Delete folder | DeleteFolder(guid) | Marked deleted, files unaffected |
| Add tag | AddTag(fileGuid, "vacation") | GetTags returns ["vacation"] |
| Remove tag | Remove "vacation" | GetTags returns [] |
| Set favorite | SetFavorite(guid, true) | GetFavorites includes the file |

### Verification Checklist

1. ✅ After any folder/tag/favorite change, the encrypted index is rewritten atomically
2. ✅ Tags are stored as string arrays in IndexEntry — no separate data structure

---

## D08–D15 — Search & Sort

### Module & File Placement

- **File:** `src/SecureVault.Core/Organization/SearchService.cs`
- **File:** `src/SecureVault.Core/Organization/SortService.cs`
- **Dependencies:** VaultIndex, TagService
- **Depended on by:** N07 (toolbar search), UI views

### Function Signatures

```csharp
public sealed class SearchService
    SearchService(VaultIndex index)

    IReadOnlyList<IndexEntry> SearchByFilename(string query)
    // Case-insensitive substring match on FileName

    IReadOnlyList<IndexEntry> SearchByTags(string tag)
    // Exact tag match (case-insensitive)

    IReadOnlyList<IndexEntry> SearchByNotes(string query)
    // Substring match on Notes field

    IReadOnlyList<IndexEntry> SearchByCategory(FileCategory category)
    // Filter by Category field

    IReadOnlyList<IndexEntry> SearchByDateRange(DateTime start, DateTime end)
    // Filter by DateAdded or DateModified

    IReadOnlyList<IndexEntry> SearchBySizeRange(long minBytes, long maxBytes)
    // Filter by OriginalSize

    IReadOnlyList<IndexEntry> SearchByProtection(ProtectionMode mode)
    // Filter by ProtectionMode

    IReadOnlyList<IndexEntry> SearchCombined(SearchQuery query)
    // Combine multiple filters with AND logic

public sealed class SortService
    static IReadOnlyList<IndexEntry> Sort(IEnumerable<IndexEntry> entries, SortField field, SortDirection direction)

public enum SortField { Name, DateAdded, Size, Type }
public enum SortDirection { Ascending, Descending }
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Search filename | "photo" in vault with "photo1.jpg", "video.mp4" | Returns ["photo1.jpg"] |
| Search case-insensitive | "PHOTO" | Same result |
| Search by date range | Date range covering 2 of 5 files | Returns 2 files |
| Search by size | 0–1KB when vault has 500B and 2MB files | Returns 500B file only |
| Sort by name | 3 files | Alphabetical order |
| Sort by size descending | 3 files | Largest first |
| Combined search | Category=Photos AND tag="vacation" | Only photos tagged "vacation" |

### Verification Checklist

1. ✅ Search never touches the vault file — operates entirely on the in-memory index
2. ✅ Sort is stable (equal items maintain their relative order)

---

## E01–E07 — Cache System & Progressive Loading

### Module & File Placement

- **File:** `src/SecureVault.Core/Cache/VaultCache.cs`
- **File:** `src/SecureVault.Core/Cache/CacheEncryption.cs`
- **Dependencies:** EncryptionService (Phase 1), VaultIndex
- **Depended on by:** N02 (fast startup), E06/E07 (UI performance)

### Data Structures

```
Cache file location: %LOCALAPPDATA%\SecureVault\cache\{vaultUUID}.cache

Cache contents (E02):
  - Index snapshot (serialized VaultIndex)
  - Thumbnail data (map of fileGuid → WebP thumbnail bytes)
  - UI state (window size, last viewed folder, scroll position)
  - Last sync timestamp

Cache is encrypted with a key derived from the master key:
  HKDF(masterKey, info="SecureVault-CacheKey-v1") → AES-256-GCM
```

### Function Signatures

```csharp
public sealed class VaultCache : IDisposable
    VaultCache(Guid vaultUUID, SecureBuffer cacheKey)

    void SaveSnapshot(VaultIndex index, Dictionary<Guid, byte[]> thumbnails, UIState uiState)
    // 1. Serialize all data to binary
    // 2. AES-256-GCM encrypt with cache key
    // 3. Write to cache file (atomic)

    (VaultIndex? index, Dictionary<Guid, byte[]>? thumbnails, UIState? uiState) LoadSnapshot()
    // 1. Read cache file
    // 2. Decrypt
    // 3. Deserialize
    // 4. Return (null if cache doesn't exist or is corrupted)

    void UpdateIndex(VaultIndex index)
    // Incremental update — only rewrite the index portion (E05)

    void AddThumbnail(Guid fileGuid, byte[] webpData)
    // Append thumbnail to cache (incremental)

    bool IsStale(VaultIndex currentIndex)
    // Compare cache timestamp with index modification timestamp

    void Invalidate()
    // Delete cache file
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Save and load | Index with 5 files + 3 thumbnails | All data survives round-trip |
| Cache miss | No cache file | LoadSnapshot returns (null, null, null) |
| Stale detection | Cache from 1 hour ago, index modified 5 min ago | IsStale returns true |
| Corrupted cache | Flip byte in cache file | LoadSnapshot returns nulls (graceful, no crash) |
| Encrypted | Read cache file raw | Not plaintext-readable |

### Verification Checklist

1. ✅ Cache file is encrypted — hex dump should show no readable filenames
2. ✅ Cache corruption does not crash the app — it falls back to reading from vault
3. ✅ Cache is in `%LOCALAPPDATA%`, not next to the vault file (not backed up)

---

## N01 — WinUI 3 Project Setup

### Module & File Placement

- **File:** `src/SecureVault.App/SecureVault.App.csproj` (WinUI 3 project)
- **File:** `src/SecureVault.App/App.xaml` + `App.xaml.cs`
- **Dependencies:** Phase 1 (`SecureVault.Core` project reference)
- **Depended on by:** All UI features

### Data Structures

```
Solution structure:
  SecureVault.sln
  ├── src/SecureVault.Core/SecureVault.Core.csproj       (class library, .NET 8)
  └── src/SecureVault.App/SecureVault.App.csproj          (WinUI 3, Windows App SDK)

NuGet dependencies for SecureVault.App:
  - Microsoft.WindowsAppSDK (latest stable)
  - CommunityToolkit.WinUI (MVVM helpers)
  - Microsoft.Extensions.DependencyInjection (DI container)
```

### Function Signatures

```csharp
// App.xaml.cs
public partial class App : Application
    void OnLaunched(LaunchActivatedEventArgs args)
    // 1. Configure DI container with services
    // 2. Create MainWindow
    // 3. Navigate to LoginPage (or main library if cache + auto-unlock)

// Service registration:
    void ConfigureServices(IServiceCollection services)
    // Register: VaultManager, EncryptionService, VaultCache, SearchService, etc.
```

### Verification Checklist

1. ✅ `dotnet build` succeeds for both projects
2. ✅ App launches and shows a window (even if empty)
3. ✅ SecureVault.Core is referenced as a project reference, not a DLL copy

---

## N02–N04, N23 — Login Screen + Recovery Key Gate

### Module & File Placement

- **File:** `src/SecureVault.App/Views/LoginPage.xaml` + `LoginPage.xaml.cs`
- **File:** `src/SecureVault.App/ViewModels/LoginViewModel.cs`
- **File:** `src/SecureVault.App/Views/RecoveryKeyConfirmationDialog.xaml`
- **Dependencies:** N01 (WinUI project), VaultManager (Phase 1)
- **Depended on by:** N05 (main library — navigated after successful login)

### Function Signatures

```csharp
public sealed class LoginViewModel : ObservableObject
    string VaultPath { get; set; }
    string Password { get; set; }
    string PasswordHint { get; }           // (A05 — displayed, from header)
    bool ShowRecoveryInput { get; set; }    // Toggle between password / recovery key entry
    string RecoveryWords { get; set; }      // 24-word input field
    bool IsLoading { get; set; }
    string ErrorMessage { get; set; }

    IAsyncRelayCommand UnlockCommand
    // 1. Show loading indicator (Argon2id is slow)
    // 2. Run on background thread: VaultManager.Open(path, password)
    // 3. On success: navigate to main library
    // 4. On InvalidPasswordException: show error, increment fail counter, apply delay (M11)
    // 5. On CorruptedVaultException: show error with "vault may be damaged" message

    IAsyncRelayCommand CreateVaultCommand
    // 1. Show "Choose vault location" file picker
    // 2. Show password entry (with confirm)
    // 3. Run VaultManager.Create(path, password)
    // 4. Show RecoveryKeyConfirmationDialog with 24 words (N23)
    // 5. User must type 3 random words to confirm they saved the phrase
    // 6. Only then navigate to main library

    IAsyncRelayCommand UnlockWithRecoveryCommand
    // 1. Parse 24 words from RecoveryWords input
    // 2. VaultManager.OpenWithRecovery(path, recoveryWords)
    // 3. On success: navigate to main library + prompt to set new password
```

### Data Structures — Brute Force Delay (M11)

```
delay_seconds = min(2 ^ failed_attempts, 60)

Attempt 1: 2s delay
Attempt 2: 4s delay
Attempt 3: 8s delay
Attempt 4: 16s delay
Attempt 5: 32s delay
Attempt 6+: 60s delay (capped)

Reset to 0 on successful unlock.
Store attempt count in memory only (not persisted — restart clears it).
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Correct password | Enter correct password | Navigates to library |
| Wrong password | Enter wrong password | Error shown, delay applied |
| Brute force delay | 3 wrong attempts | 8-second delay before next attempt allowed |
| Recovery key unlock | Enter 24 correct words | Unlocks vault |
| Recovery key confirmation | Create vault, type 3 words correctly | Proceeds |
| Recovery key confirmation fail | Type wrong words | Shows error, cannot proceed |

### Verification Checklist

1. ✅ Password is never displayed in plaintext (use PasswordBox, not TextBox)
2. ✅ Argon2id runs on a background thread (UI stays responsive)
3. ✅ Recovery words are shown only once during creation — they are never stored or displayed again
4. ✅ Brute force delay uses `await Task.Delay()`, not `Thread.Sleep()` (non-blocking)

---

## N05–N09 — Main Library View

### Module & File Placement

- **File:** `src/SecureVault.App/Views/MainLibraryPage.xaml` + `.cs`
- **File:** `src/SecureVault.App/ViewModels/MainLibraryViewModel.cs`
- **File:** `src/SecureVault.App/Views/SidebarControl.xaml` (N06)
- **File:** `src/SecureVault.App/Views/ToolbarControl.xaml` (N07)
- **File:** `src/SecureVault.App/Views/StatusBarControl.xaml` (N08)
- **File:** `src/SecureVault.App/Views/FileGridView.xaml` (N09)
- **Dependencies:** N02 (login), D01-D06 (folders/categories), E01-E07 (cache)
- **Depended on by:** All integrated apps (Phase 3)

### Function Signatures

```csharp
public sealed class MainLibraryViewModel : ObservableObject
    ObservableCollection<FileItemViewModel> CurrentFiles { get; }
    ObservableCollection<FolderTreeItem> FolderTree { get; }
    FileCategory? SelectedCategory { get; set; }
    string SearchQuery { get; set; }
    SortField CurrentSort { get; set; }
    SortDirection CurrentSortDirection { get; set; }
    int FileCount { get; }
    string VaultSize { get; }        // formatted, e.g. "1.2 GB"
    string FreeSpace { get; }        // disk free space

    IAsyncRelayCommand AddFilesCommand      // opens file picker, calls FileAddOperation
    IAsyncRelayCommand AddFolderCommand     // opens folder picker
    IRelayCommand LockCommand               // calls VaultManager.Lock(), navigate to login
    IRelayCommand RefreshCommand
    IRelayCommand<FileItemViewModel> OpenFileCommand    // double-click → open in integrated app

    void NavigateToFolder(Guid folderGuid)
    void NavigateToCategory(FileCategory category)
    void ApplySearch(string query)
    void ApplySort(SortField field, SortDirection direction)
```

### Verification Checklist

1. ✅ Sidebar shows: All Files, each Category, folder tree, Favorites
2. ✅ Status bar updates dynamically (file count, vault size)
3. ✅ Grid view uses virtualization (E07 — `ItemsRepeater` with `StackLayout` or `UniformGridLayout`)
4. ✅ Double-clicking a file opens it (routed to correct integrated app based on category)

---

## C02–C04, C07 — Multi-File Add, Folder Add, Drag-Drop, Progress

### Module & File Placement

- **File:** `src/SecureVault.Core/Operations/BatchFileAddOperation.cs`
- **File:** `src/SecureVault.App/Services/DragDropHandler.cs`
- **Dependencies:** FileAddOperation (Phase 1), N05 (UI)

### Function Signatures

```csharp
public sealed class BatchFileAddOperation
    BatchFileAddOperation(VaultManager vault)

    async IAsyncEnumerable<FileAddProgress> AddFiles(
        IReadOnlyList<string> filePaths,
        string virtualFolderPath,
        ProtectionMode mode,
        CancellationToken ct)
    // 1. For each file:
    //    a. Call FileAddOperation.AddFile
    //    b. Yield FileAddProgress { FileName, BytesProcessed, TotalBytes, FileIndex, TotalFiles, Speed, ETA }
    // 2. Support cancellation via CancellationToken

    async IAsyncEnumerable<FileAddProgress> AddFolder(
        string folderPath,
        string virtualFolderPath,
        ProtectionMode mode,
        bool recursive,
        CancellationToken ct)
    // 1. Enumerate all files (and subdirectories if recursive)
    // 2. Create virtual folder structure mirroring source
    // 3. Add each file, yielding progress

public record FileAddProgress(
    string FileName,
    long BytesProcessed,
    long TotalBytes,
    int FileIndex,
    int TotalFiles,
    double SpeedBytesPerSec,
    TimeSpan EstimatedTimeRemaining)
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Add 3 files | 3 small files | All 3 in vault, 3 progress reports |
| Add folder | Folder with 2 files + 1 subfolder (1 file) | 3 files added, folder structure created |
| Cancel mid-add | Start adding 10 files, cancel after 3 | 3 files in vault, remaining not added |
| Progress accuracy | Add 5MB file | Progress reaches 100%, speed > 0, ETA decreasing |

### Verification Checklist

1. ✅ Drag-drop onto the app window triggers AddFiles with the dropped paths
2. ✅ Cancellation stops processing without corrupting the vault
3. ✅ Progress percentage never exceeds 100%

---

## C10–C15 — Rename, Move, Copy, Export

### Module & File Placement

- **File:** `src/SecureVault.Core/Operations/FileManagementOperations.cs`
- **Dependencies:** VaultIndex, VaultManager, ChunkReader (for export)

### Function Signatures

```csharp
public sealed class FileManagementOperations
    FileManagementOperations(VaultManager vault)

    void Rename(Guid fileGuid, string newName)
    // 1. Find IndexEntry
    // 2. Update FileName
    // 3. Rewrite index (atomic)

    void Move(Guid fileGuid, Guid targetFolderGuid)
    // 1. Update IndexEntry.VirtualFolderPath / ParentFolderGUID
    // 2. Rewrite index (atomic)
    // NOTE: No data is moved — only index metadata changes

    void Copy(Guid fileGuid, Guid targetFolderGuid)
    // 1. Create new IndexEntry with same chunk references but new GUID
    // 2. Update folder path
    // 3. Rewrite index
    // NOTE: No data is duplicated — two index entries point to same chunks
    //       (copy-on-write if we ever implement edit-in-place)

    async Task ExportFile(Guid fileGuid, string destPath, IProgress<long>? progress, CancellationToken ct)
    // 1. Open VaultFileStream for the file
    // 2. Create output FileStream at destPath
    // 3. Copy stream (chunked, with progress reporting)
    // 4. Verify exported file SHA-256 matches PlaintextSHA256

    async Task ExportMultiple(IReadOnlyList<Guid> fileGuids, string destFolder, IProgress<FileAddProgress>? progress, CancellationToken ct)
    // Export each file to destFolder, preserving filenames

    async Task ExportFolder(Guid folderGuid, string destFolder, IProgress<FileAddProgress>? progress, CancellationToken ct)
    // Recursively export all files in folder, preserving subfolder structure
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Rename | Rename "a.txt" → "b.txt" | Index updated, no data change |
| Move | Move file from /A to /B | File appears in /B, not in /A |
| Copy | Copy file to /B | File appears in both /A and /B, vault size unchanged |
| Export single | Export to disk | Exported file is byte-identical to original |
| Export SHA256 | Export and verify | SHA-256 matches stored PlaintextSHA256 |
| Export multiple | Export 3 files | All 3 on disk, all verified |

### Verification Checklist

1. ✅ Rename/Move/Copy never touch file data chunks — only the index is rewritten
2. ✅ Export verifies SHA-256 after writing to disk
3. ✅ Export to an existing file prompts overwrite confirmation (UI layer)

---

## E06, E07 — Progressive Loading & Virtualized UI

### Module & File Placement

- **File:** `src/SecureVault.App/Controls/VirtualizedFileGrid.xaml`
- **Dependencies:** N09 (grid view), E01-E05 (cache)
- **Note:** This is primarily a UI concern using WinUI 3's built-in virtualization

### Function Signatures

```csharp
// Progressive loading strategy (E06):
// 1. Immediately show file names and sizes from cached/decrypted index
// 2. Start background task to generate/load thumbnails
// 3. As thumbnails become available, update the UI items
// 4. Use IncrementalLoadingCollection or similar pattern

// Virtualized UI (E07):
// Use ItemsRepeater with UniformGridLayout (for grid) or StackLayout (for list)
// Only items visible in the viewport are rendered
// Scroll events trigger loading of newly visible items
```

### Verification Checklist

1. ✅ Open vault with 1000+ files — app uses < 200MB RAM (not proportional to file count)
2. ✅ Scrolling through the file list is smooth (60fps, no jank)
3. ✅ File names appear instantly, thumbnails populate progressively

---

## Source File Summary

```
src/SecureVault.Core/
├── Organization/
│   ├── VirtualFolder.cs              (D01)
│   ├── VirtualFolderService.cs       (D01, D02)
│   ├── FileCategory.cs               (D03, D04)
│   ├── TagService.cs                 (D05)
│   ├── SearchService.cs              (D08-D14)
│   └── SortService.cs                (D15)
├── Cache/
│   ├── VaultCache.cs                 (E01-E05)
│   └── CacheEncryption.cs            (E01)
├── Operations/
│   ├── BatchFileAddOperation.cs      (C02, C03)
│   └── FileManagementOperations.cs   (C10-C15)

src/SecureVault.App/
├── App.xaml / App.xaml.cs             (N01)
├── Views/
│   ├── LoginPage.xaml / .cs           (N02-N04)
│   ├── RecoveryKeyConfirmationDialog.xaml (N23)
│   ├── MainLibraryPage.xaml / .cs     (N05)
│   ├── SidebarControl.xaml / .cs      (N06)
│   ├── ToolbarControl.xaml / .cs      (N07)
│   ├── StatusBarControl.xaml / .cs    (N08)
│   └── FileGridView.xaml / .cs        (N09)
├── ViewModels/
│   ├── LoginViewModel.cs              (N02)
│   └── MainLibraryViewModel.cs        (N05)
├── Controls/
│   └── VirtualizedFileGrid.xaml       (E07)
├── Services/
│   └── DragDropHandler.cs             (C04)
└── Helpers/
    └── FileIconHelper.cs              (file type → icon mapping)
```

## Test Vector Files

```
tests/vectors/
├── auto-categorization.json           (D04 — extension → category mapping)
└── brute-force-delay.json             (M11 — attempt count → delay seconds)
```

## Branch & PR

- **Branch:** `phase-2/basic-ui`
- **PR Title:** "Phase 2: Basic UI + File Operations"
- **PR Description:**

```
Adds the WinUI 3 UI layer and core file management operations.

## What's included
- Login screen with password entry, recovery key option, brute-force delay
- Recovery key confirmation gate during vault creation (type 3 words to proceed)
- Main library view: sidebar (folders, categories), toolbar (add, search, sort), status bar
- File grid view with virtualization for 100K+ files
- Multi-file add, folder add, drag-and-drop support
- Progress reporting (percentage, speed, ETA) during file operations
- Rename, move, copy (metadata-only), export (with SHA-256 verification)
- Virtual folder system with unlimited nesting
- File categories with auto-categorization by extension
- Tags, favorites
- Search by filename, tags, notes, category, date, size, protection level
- Sort by name, date, size, type
- Encrypted cache for instant startup (<1s after password)
- Progressive loading (names first, thumbnails later)

## Dependencies (new)
- Microsoft.WindowsAppSDK
- CommunityToolkit.WinUI
- Microsoft.Extensions.DependencyInjection
```

## CONTRIBUTING Note for Phase 2

```
CONTRIBUTING — Phase 2 (Basic UI)

1. All UI data binding goes through ViewModels — no code-behind logic
   beyond navigation and event wiring.

2. File operations (add, export) must always use CancellationToken
   and report progress. No fire-and-forget async.

3. The encrypted cache is a performance optimization, not a data store.
   The app must work correctly even if the cache is deleted or corrupted.

4. WinUI 3 has known issues with ItemsRepeater virtualization — test
   with 10K+ items before submitting UI changes.
```

## STATUS.md Entries for Phase 2

```
N01 🔨 WinUI 3 project setup
N02 🔨 Login screen
N03 🔨 Password hint display
N04 🔨 Recovery key entry
N05 🔨 Main library view
N06 🔨 Sidebar
N07 🔨 Toolbar
N08 🔨 Status bar
N09 🔨 File grid view
N23 🔨 Recovery key confirmation gate
C02 🔨 Add multiple files
C03 🔨 Add folder recursively
C04 🔨 Drag and drop
C07 🔨 Progress reporting
C10 🔨 Rename file
C11 🔨 Move file
C12 🔨 Copy file
C13 🔨 Export single file
C14 🔨 Export multiple files
C15 🔨 Export folder
D01 🔨 Virtual folder system
D02 🔨 Create/rename/delete folders
D03 🔨 File categories
D04 🔨 Auto-categorization
D05 🔨 Tags per file
D06 🔨 Favorites
D08 🔨 Search by filename
D09 🔨 Search by tags
D10 🔨 Search by notes
D11 🔨 Search by type/category
D12 🔨 Search by date range
D13 🔨 Search by size range
D14 🔨 Search by protection level
D15 🔨 Sort by name/date/size/type
E01 🔨 Encrypted cache
E02 🔨 Cache contents
E03 🔨 Instant startup from cache
E04 🔨 Background cache freshness
E05 🔨 Incremental cache update
E06 🔨 Progressive loading
E07 🔨 Virtualized UI lists
```
