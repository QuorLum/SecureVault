# Phase 6: Polish — Implementation Roadmap

> **Branch:** `phase-6/polish`
>
> **Scope:** Advanced UI, advanced security, recovery mode, advanced integrity,
> albums/playlists/timeline, duplicate detection, compaction, clipboard paste.
>
> **Feature IDs:** N10–N22, M07–M13, M15–M17, F03, F10, F14–F16,
> D16–D21, C20–C24
>
> **Prior Phases:** Phase 1–5 must be complete.

---

## Build Order & Dependency Graph

```
Level 0 (independent, depends on prior phases):
  N15  Dark theme (default) — can be implemented anytime
  N16  Light theme
  N21  Fluent Design (acrylic, rounded corners, animations)
  M07  Secure temp file deletion
  M12  Proprietary format (already achieved by Phase 1)
  M13  Encrypted index (already achieved by Phase 1)
  M15  No file signatures visible
  C24  Clipboard paste

Level 1 (depends on Level 0):
  N10  File list view (detailed columns)
  N11  File timeline view
  N12  Context menu (right-click)
  N13  File properties dialog
  N14  Settings page
  N17  Progress dialogs
  N18  Notification toasts
  N19  Keyboard shortcuts
  N20  Window state persistence
  N22  Responsive layout
  D16  Filter by favorites only
  D20  Recent files list
  D21  View modes (grid, list, timeline)
  M08  Auto-lock on system lock (already done in Phase 4, verify)
  M09  Auto-lock on idle (already done in Phase 4, verify)
  M10  Auto-lock on minimize (optional)
  M11  Brute force delay (already done in Phase 2, verify)
  M17  Screen capture protection

Level 2 (depends on Level 1):
  D17  Albums for photos (extends H11 from Phase 4)
  D18  Playlists for audio/video (extends I18 from Phase 4)
  D19  Notebooks and sections for notes (extends J07 from Phase 3)
  C21  Duplicate detection (extends K08 from Phase 4)
  C20  Replace file data
  F03  Log + re-verify auto-repairs
  F10  Recovery mode scan
  F14  HMAC on vault header (already done in Phase 1, verify)
  F15  Integrity report
  F16  Background auto-repair

Level 3 (depends on Level 2):
  C22  File properties view (final version)
  C23  Vault compaction
```

---

## N10, N11, D21 — Additional View Modes

### Module & File Placement

- **File:** `src/SecureVault.App/Views/FileListView.xaml` (N10)
- **File:** `src/SecureVault.App/Views/TimelineView.xaml` (N11)
- **File:** `src/SecureVault.App/ViewModels/ViewModeViewModel.cs` (D21)

### Function Signatures

```csharp
// File list view (N10):
// DataGrid/ListView with columns: Name, Size, Date Added, Type, Protection, Tags
// Supports column sorting by clicking headers
// Supports column resize and reorder

// Timeline view (N11):
// Group files by date: Year → Month → Day
// Each group shows a header with date and file count
// Files within each group shown as thumbnail grid

public enum ViewMode { Grid, List, Timeline }

public sealed class ViewModeViewModel : ObservableObject
    ViewMode CurrentMode { get; set; }
    // Switches between Grid (N09), List (N10), Timeline (N11) views
```

---

## N12–N14 — Context Menu, Properties, Settings

### Module & File Placement

- **File:** `src/SecureVault.App/Views/FileContextMenu.xaml` (N12)
- **File:** `src/SecureVault.App/Views/FilePropertiesDialog.xaml` (N13, C22)
- **File:** `src/SecureVault.App/Views/SettingsPage.xaml` (N14)
- **File:** `src/SecureVault.App/ViewModels/SettingsViewModel.cs`

### Function Signatures

```csharp
// Context menu items (N12):
// Open, Open With..., Rename, Move To, Copy To, Export, Delete
// Properties, Add Tag, Set Favorite, Change Protection Mode

// File properties (N13, C22):
public sealed class FilePropertiesViewModel
    string FileName { get; }
    string FileType { get; }
    string OriginalSize { get; }          // formatted
    string StoredSize { get; }            // compressed + encrypted size
    string DateAdded { get; }
    string DateModified { get; }
    string PlaintextSHA256 { get; }
    string ProtectionMode { get; }        // "Secure Mode (AES-256-GCM)" or "Fast Obfuscation"
    string CompressionType { get; }
    int ChunkCount { get; }
    IReadOnlyList<string> Tags { get; }
    string Notes { get; }
    bool IsFavorite { get; }
    string VaultPart { get; }             // which vault part contains this file

// Settings page (N14):
public sealed class SettingsViewModel
    TimeSpan AutoLockTimeout { get; set; }       // A08
    bool LockOnMinimize { get; set; }             // M10
    bool LockOnSystemLock { get; set; }           // M08
    bool ScreenCaptureProtection { get; set; }    // M17
    ProtectionMode DefaultProtection { get; set; } // default for new files
    string ThemeMode { get; set; }                 // Dark/Light/System
    bool ShowPasswordHint { get; set; }
    // Settings stored in encrypted cache (not in vault — they're app preferences)
```

---

## N15, N16, N21 — Themes & Fluent Design

### Module & File Placement

- **File:** `src/SecureVault.App/Themes/DarkTheme.xaml` (N15)
- **File:** `src/SecureVault.App/Themes/LightTheme.xaml` (N16)
- **File:** `src/SecureVault.App/Themes/SharedStyles.xaml` (N21)

### Implementation Notes

```
Dark theme (N15, default):
  - Background: #1A1A2E or system dark
  - Surface: #16213E
  - Primary accent: #0F3460 or #E94560 (security-red accent)
  - Text: #FFFFFF, #B0B0B0 (secondary)
  - Use WinUI 3 built-in dark theme with custom accent colors

Light theme (N16):
  - Standard WinUI 3 light theme with matching accent

Fluent Design (N21):
  - Acrylic background on sidebar and header (Mica/Acrylic material)
  - Rounded corners on cards and buttons (CornerRadius="8")
  - Connected animations for page transitions
  - Subtle hover/press animations on interactive elements
  - Use Windows.UI.Composition for smooth animations
```

---

## N17–N20, N22 — Progress, Toasts, Shortcuts, Window State, Responsive

### Module & File Placement

- **File:** `src/SecureVault.App/Services/NotificationService.cs` (N18)
- **File:** `src/SecureVault.App/Services/KeyboardShortcutService.cs` (N19)
- **File:** `src/SecureVault.App/Services/WindowStateService.cs` (N20)

### Function Signatures

```csharp
// Progress dialogs (N17):
// ContentDialog with ProgressBar, cancel button
// Used for: file add, export, backup, restore, integrity check, compaction

// Notification toasts (N18):
public sealed class NotificationService
    void ShowSuccess(string message)       // "File added successfully"
    void ShowWarning(string message)       // "Chunk auto-repaired"
    void ShowError(string message)         // "Operation failed"
    // Use WinUI 3 InfoBar or TeachingTip for in-app notifications
    // Use Windows.UI.Notifications for system toast (when app minimized)

// Keyboard shortcuts (N19):
public sealed class KeyboardShortcutService
    // Register accelerators:
    // Ctrl+O: Open vault      Ctrl+L: Lock vault
    // Ctrl+A: Select all      Delete: Delete selected
    // Ctrl+F: Search          Ctrl+N: New note
    // F5: Refresh             F11: Full screen
    // Ctrl+Shift+E: Encrypt all

// Window state (N20):
public sealed class WindowStateService
    void SaveState(double width, double height, double x, double y, bool isMaximized)
    (double w, double h, double x, double y, bool max) LoadState()
    // Stored in encrypted cache

// Responsive layout (N22):
// Use VisualStateManager with breakpoints:
//   Compact: < 800px width (sidebar collapses to icons)
//   Normal: 800–1200px (sidebar + content)
//   Wide: > 1200px (sidebar + content + detail panel)
```

---

## M07, M17 — Secure Temp Files & Screen Capture Protection

### Module & File Placement

- **File:** `src/SecureVault.Core/Security/SecureTempFile.cs` (M07)
- **File:** `src/SecureVault.App/Services/ScreenProtectionService.cs` (M17)

### Function Signatures

```csharp
// Secure temp file (M07 — only if temp files are ever needed):
public sealed class SecureTempFile : IDisposable
    SecureTempFile(int size)

    string Path { get; }
    Stream OpenWrite()
    Stream OpenRead()

    void Dispose()
    // 1. Open file for write
    // 2. Overwrite all bytes with random data
    // 3. Flush
    // 4. Delete file
    // 5. Attempt to delete from MFT (best-effort on NTFS)

// Screen capture protection (M17):
public sealed class ScreenProtectionService
    void Enable(IntPtr windowHandle)
    // Call SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)
    // This prevents screenshots and screen recording of the app window

    void Disable(IntPtr windowHandle)
    // Call SetWindowDisplayAffinity(hwnd, WDA_NONE)
```

### Exact Library Calls

- `User32.SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity)` via P/Invoke
- `WDA_EXCLUDEFROMCAPTURE = 0x00000011` — Windows 10 2004+

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Secure temp overwrite | Create temp, write data, dispose | File deleted, cannot recover content |
| Screen capture off | Enable, take screenshot | App window appears black in screenshot |
| Screen capture toggle | Enable then disable | App visible in screenshots again |

### Verification Checklist

1. ✅ Secure temp file overwrites before deleting — search for `File.Delete` in SecureTempFile, must be preceded by overwrite
2. ✅ Screen capture protection is optional (setting toggle) — some users need screenshots for support

---

## F03, F10, F14–F16 — Advanced Integrity

### Module & File Placement

- **File:** `src/SecureVault.Core/Integrity/RepairLogger.cs` (F03)
- **File:** `src/SecureVault.Core/Integrity/RecoveryScanner.cs` (F10)
- **File:** `src/SecureVault.Core/Integrity/IntegrityChecker.cs` (F04 extended, F15)
- **File:** `src/SecureVault.Core/Integrity/BackgroundRepair.cs` (F16)

### Function Signatures

```csharp
// F03: Repair logging and verification
public sealed class RepairLogger
    void LogRepair(Guid fileGuid, uint chunkSeq, int errorsFixed, bool verificationPassed)
    // Log to in-memory list + optional file log
    // IMPORTANT: After RS repair, re-verify the repaired chunk's auth tag/hash
    // before writing the repair back to disk

// F10: Recovery mode scan
public sealed class RecoveryScanner
    async Task<IReadOnlyList<RecoveredFile>> ScanForFiles(Stream vaultStream, IProgress<long>? progress, CancellationToken ct)
    // 1. Scan vault file sequentially
    // 2. Look for BlockHeader magic bytes (even XOR-masked)
    //    — Try XOR-unmasking at every offset, check for valid block structure
    // 3. For each found block: try to read chunks, verify CRC32
    // 4. Return list of recovered file fragments
    // This is the "last resort" when both primary and backup indices are destroyed

// F15: Integrity report
public sealed class IntegrityReport
    int TotalFiles { get; }
    int HealthyFiles { get; }
    int DamagedFiles { get; }
    int RepairedFiles { get; }
    double HealthPercentage { get; }
    List<FileIntegrityStatus> FileDetails { get; }

// F16: Background auto-repair
public sealed class BackgroundRepairService
    BackgroundRepairService(VaultManager vault, RepairLogger logger)

    void StartBackgroundScan()
    // 1. Low-priority background task
    // 2. Sequentially verify each chunk
    // 3. If RS-repairable damage found: repair, re-verify, commit if valid
    // 4. Log all repairs via RepairLogger
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Recovery scan | Destroy both indices, scan | Finds all files by block magic |
| Repair logging | Auto-repair a chunk | Log entry with fileGuid, chunk, errors, verification status |
| Re-verify after repair | Repair chunk, re-check auth tag | If verification fails, do NOT commit repair |
| Integrity report | Vault with 100 files (2 damaged) | Report: 98 healthy, 2 damaged, 98% |
| Background repair | Corrupt 1 chunk, run background scan | Chunk repaired, logged |

### Verification Checklist

1. ✅ F03: Repaired chunks are re-verified before being written back
2. ✅ F10: Recovery scan works even with completely destroyed indices
3. ✅ F16: Background repair runs at low priority and doesn't impact foreground performance

---

## D16–D21 — Favorites Filter, Albums, Playlists, Timeline, Recent

### Module & File Placement

- Extensions to existing files from Phase 2 and 4

### Function Signatures

```csharp
// D16: Favorites filter — already in SearchService, expose as sidebar button
// D17: Albums — extend AlbumsPage from Phase 4 to be accessible from organization sidebar
// D18: Playlists — extend PlaylistPage from Phase 4 to be accessible from sidebar
// D19: Notebooks — extend notes hierarchy from Phase 3/4

// D20: Recent files
public sealed class RecentFilesService
    RecentFilesService(VaultCache cache)

    void RecordAccess(Guid fileGuid)
    // Add to recent list with timestamp, max 50 entries (FIFO)

    IReadOnlyList<IndexEntry> GetRecent(int count = 20)
    // Return most recently accessed files

// D21: View modes — already defined in N10/N11/D21 above
```

---

## C20–C24 — Replace File, Duplicate Detection, Compaction, Clipboard

### Module & File Placement

- **File:** `src/SecureVault.Core/Operations/FileReplaceOperation.cs` (C20)
- **File:** `src/SecureVault.Core/Operations/DuplicateDetector.cs` (C21)
- **File:** `src/SecureVault.Core/Operations/VaultCompaction.cs` (C23)
- **File:** `src/SecureVault.App/Services/ClipboardService.cs` (C24)

### Function Signatures

```csharp
// C20: Replace file data
public sealed class FileReplaceOperation
    FileReplaceOperation(VaultManager vault)

    async Task ReplaceFileData(Guid fileGuid, Stream newData, CancellationToken ct)
    // 1. Write new chunks for the new data
    // 2. Update index entry (new chunk offsets, new SHA-256, new size)
    // 3. Mark old chunks as free
    // 4. Rewrite index atomically

// C21: Duplicate detection (extends K08 from Phase 4 with UI)
public sealed class DuplicateDetector
    IReadOnlyList<DuplicateGroup> FindDuplicates(VaultIndex index)
    // Group by PlaintextSHA256 — groups with count > 1 are duplicates

// C23: Vault compaction
public sealed class VaultCompaction
    VaultCompaction(VaultManager vault)

    async Task<CompactionResult> Compact(IProgress<long>? progress, CancellationToken ct)
    // ALGORITHM (from vision doc):
    // 1. Check free disk space — need ~2x vault size (surface in UI!)
    // 2. Create new temp vault file
    // 3. Copy header to temp vault
    // 4. For each live (non-deleted) file in index:
    //    a. Read all chunks from old vault
    //    b. Verify each chunk's hash/CRC32 matches index
    //    c. Write chunks to temp vault (recomputing offsets)
    // 5. Write new index (with updated offsets) to temp vault
    // 6. Write footer to temp vault
    // 7. Verify: re-read every chunk from temp vault, check hashes
    // 8. Atomic rename: old.vault → old.vault.pre-compact, temp → old.vault
    // 9. Delete old.vault.pre-compact only after full verification
    // 10. Return CompactionResult (bytes reclaimed, files processed)

    long EstimateReclaimableSpace(VaultIndex index)
    // Sum of chunk sizes for deleted files

// C24: Clipboard paste
public sealed class ClipboardService
    async Task<IndexEntry?> PasteFromClipboard(VaultManager vault, string virtualFolderPath)
    // 1. Check clipboard for image data (Bitmap)
    // 2. If image: encode as PNG, add to vault as "Screenshot_{timestamp}.png"
    // 3. Check clipboard for files (FileDrop)
    // 4. If files: add each to vault
    // 5. Return entry for pasted content (or null if clipboard empty/unsupported)
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Replace file | Replace 1MB file with 2MB file | Old data freed, new data readable |
| Duplicate detection | Add same file twice | Detected as duplicate |
| Compaction | Add 3 files, delete 1, compact | Vault shrinks by deleted file's size |
| Compaction verification | After compact | All remaining files readable, hashes match |
| Compaction needs 2x space | 100GB vault, 50GB free | Warns user "insufficient space" |
| Clipboard paste image | Copy screenshot, paste | Image added to vault as PNG |
| Clipboard paste file | Copy file in Explorer, paste in vault | File added to vault |

### Verification Checklist

1. ✅ C23: Compaction NEVER deletes the old vault until the new one is fully verified
2. ✅ C23: The old vault is renamed (not deleted) during compaction — recoverable if something goes wrong
3. ✅ C24: Clipboard paste handles empty clipboard gracefully (no crash)
4. ✅ C20: Replace updates SHA-256 hash to match new content

---

## Source File Summary

```
src/SecureVault.Core/
├── Integrity/
│   ├── RepairLogger.cs               (F03)
│   ├── RecoveryScanner.cs            (F10)
│   ├── IntegrityChecker.cs           (F15)
│   └── BackgroundRepairService.cs    (F16)
├── Operations/
│   ├── FileReplaceOperation.cs       (C20)
│   ├── DuplicateDetector.cs          (C21)
│   └── VaultCompaction.cs            (C23)
├── Security/
│   └── SecureTempFile.cs             (M07)

src/SecureVault.App/
├── Themes/
│   ├── DarkTheme.xaml                (N15)
│   ├── LightTheme.xaml               (N16)
│   └── SharedStyles.xaml             (N21)
├── Views/
│   ├── FileListView.xaml             (N10)
│   ├── TimelineView.xaml             (N11)
│   ├── FileContextMenu.xaml          (N12)
│   ├── FilePropertiesDialog.xaml     (N13, C22)
│   └── SettingsPage.xaml             (N14)
├── ViewModels/
│   ├── SettingsViewModel.cs          (N14)
│   ├── FilePropertiesViewModel.cs    (N13)
│   └── ViewModeViewModel.cs          (D21)
├── Services/
│   ├── NotificationService.cs        (N18)
│   ├── KeyboardShortcutService.cs    (N19)
│   ├── WindowStateService.cs         (N20)
│   ├── ScreenProtectionService.cs    (M17)
│   ├── RecentFilesService.cs         (D20)
│   └── ClipboardService.cs           (C24)
```

## Test Vector Files

No new cryptographic test vectors for Phase 6 — all crypto is finalized in Phase 1.

## Branch & PR

- **Branch:** `phase-6/polish`
- **PR Title:** "Phase 6: Polish — Themes, Advanced Security, Recovery, Compaction"
- **PR Description:**

```
Final polish phase — completes all remaining features.

## UI
- Dark theme (default) + Light theme with Fluent Design
- Acrylic/Mica materials, rounded corners, animations
- File list view (detailed columns), timeline view
- Context menu, file properties dialog, settings page
- Progress dialogs, notification toasts
- Keyboard shortcuts throughout app
- Window state persistence
- Responsive layout (compact/normal/wide breakpoints)

## Security
- Screen capture protection (SetWindowDisplayAffinity)
- Secure temp file deletion (overwrite + delete)
- Auto-lock on minimize (optional setting)
- Verified: encrypted index, proprietary format, no visible signatures

## Integrity
- Recovery mode scan (find files even without index)
- Auto-repair logging with re-verification
- Background auto-repair service
- Integrity report (file health %, details per file)

## Organization
- Albums, playlists, notebooks in sidebar
- Recent files list
- Favorites filter
- View mode toggle (grid/list/timeline)

## Operations
- Replace file data (for note editing, photo rotation)
- Duplicate detection with UI
- Vault compaction (reclaim deleted space, 2x space required)
- Clipboard paste (screenshots, files)
```

## CONTRIBUTING Note for Phase 6

```
CONTRIBUTING — Phase 6 (Polish)

1. Theme changes must work in both dark and light modes. Test both.

2. Vault compaction is the most dangerous operation in the app — it
   rewrites the entire vault. The old vault must NEVER be deleted
   until the new vault is fully verified. Any PR touching compaction
   must include integration test evidence showing crash-recovery works.

3. Recovery mode scan is a last-resort feature — don't optimize for
   speed, optimize for correctness. It's acceptable to be slow.

4. Screen capture protection is optional and user-controlled.
   Default should be OFF (some users need to take screenshots for
   support/documentation purposes).
```

## STATUS.md Entries for Phase 6

```
N10 🔨 File list view
N11 🔨 File timeline view
N12 🔨 Context menu
N13 🔨 File properties dialog
N14 🔨 Settings page
N15 🔨 Dark theme (default)
N16 🔨 Light theme
N17 🔨 Progress dialogs
N18 🔨 Notification toasts
N19 🔨 Keyboard shortcuts
N20 🔨 Window state persistence
N21 🔨 Fluent Design
N22 🔨 Responsive layout
M07 🔨 Secure temp file deletion
M08 🔨 Auto-lock on system lock (verify)
M09 🔨 Auto-lock on idle (verify)
M10 🔨 Auto-lock on minimize
M11 🔨 Brute force delay (verify)
M12 🔨 Proprietary format (verify)
M13 🔨 Encrypted index (verify)
M15 🔨 No file signatures visible
M17 🔨 Screen capture protection
D16 🔨 Filter by favorites
D17 🔨 Albums for photos
D18 🔨 Playlists for audio/video
D19 🔨 Notebooks and sections
D20 🔨 Recent files list
D21 🔨 View modes (grid, list, timeline)
C20 🔨 Replace file data
C21 🔨 Duplicate detection
C22 🔨 File properties view
C23 🔨 Vault compaction
C24 🔨 Clipboard paste
F03 🔨 Log + re-verify auto-repairs
F10 🔨 Recovery mode scan
F14 🔨 HMAC on vault header (verify)
F15 🔨 Integrity report
F16 🔨 Background auto-repair
```
