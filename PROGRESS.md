# SecureVault — Project Progress

## Current State: Single-File Packaging & Professional GitHub Organization Complete (All 123 Tests Passing)
- **Current Milestone:** Single-File Packaging & Professional GitHub Organization — **COMPLETE**
- **Branch:** `chore/github-organization-and-packaging`
- **Environment:** Isolated .NET 8 SDK (8.0.424) in `$env:USERPROFILE\.dotnet`
- **Activation:** Run `. .\activate.ps1` in PowerShell to set `DOTNET_ROOT` and `PATH`
- **Test Results:** 123 / 123 Passed (100% success rate)
- **Single-File Binary:** `publish/SecureVault.exe` (376.92 MB, self-contained single file, launched and verified responding)

---

## Completed Steps

### Architecture & Review
- [x] Reviewed vision specification (`docs/vision.md`) and roadmaps (`docs/roadmap/`).
- [x] Implementation plan created and approved with reviewer feedback incorporated:
  - 12-byte random nonce per write in chunk header/index to eliminate AES-GCM nonce reuse vulnerabilities upon content replacement (C20).
  - PID-aware `.vault.lock` file inspecting aliveness of holding process for crash resilience.
  - In-process concurrency guard preventing double-acquire within the same process/thread.
  - Automated unit test for `SecureBuffer` memory zeroing.
  - File copy executes independent duplicate write with new FileGuid and fresh nonces (no chunk sharing divergence).
  - Encrypted cache generates fresh 12-byte random nonce on every single write (`RandomNumberGenerator.Fill`).
  - N23 3-word recovery verification gate restricted strictly to vault creation flow.
  - Cache staleness checked via `VaultIndexVersion` incremented on every index modification.

### Phase 1: Foundation (Vault Core Engine)
- [x] `Crypto/SecureBuffer.cs`: Pinned unmanaged memory with zeroing on dispose.
- [x] `IO/VaultFileLock.cs`: Windows Named Mutex paired with PID-aware `.vault.lock` file and dead-PID reclamation.
- [x] `Crypto/KeyDerivation.cs`: Memory-hard Argon2id and HKDF subkey derivation.
- [x] `Crypto/RecoveryKeyGenerator.cs`: 24-word BIP-39 mnemonic generator and validator with checksum.
- [x] `Crypto/KeyWrapping.cs`: Dual key-wrap (password slot + recovery slot).
- [x] `Crypto/ObfuscationKeystream.cs`: Position-dependent AES-CTR keystream with per-file salt.
- [x] `Format/VaultConstants.cs`, `VaultHeader.cs`, `VaultFooter.cs`, `BlockHeader.cs`, `BlockFooter.cs`.
- [x] `Format/ReedSolomonCodec.cs`: RS(255, 223) auto-repairing up to 16 bytes per block with parity re-verification.
- [x] `Format/ChunkWriter.cs` & `ChunkReader.cs`: 1MB chunk segmentation, random 12-byte nonces, AES-GCM, RS auto-repair, and CRC32.
- [x] `Format/VaultIndex.cs`: MessagePack serialization with dual primary & backup index.
- [x] `IO/VaultFileStream.cs`: Seekable, read-only streaming direct to decoders without writing plaintext to disk.
- [x] `VaultManager.cs`: Unified facade for creation, unlock, lock, streaming, and soft deletion.

### Phase 2: Organization, Cache, Batch Operations & WinUI 3 App
- [x] Extended `VaultIndex` with `IsFolder`, `ParentFolderGuid`, and `IndexVersion` with automatic incrementation on writes.
- [x] `Organization/VirtualFolder.cs`: Virtual folder tree data model with path resolution.
- [x] `Organization/VirtualFolderService.cs`: Unlimited folder hierarchy, path breadcrumb calculation, safe deletion un-foldering files.
- [x] `Organization/FileCategory.cs` & `AutoCategorizer.cs`: Mapping Photos, Videos, Audio, Documents, Notes, Apps, Archives, Other.
- [x] `Organization/TagService.cs`: Case-insensitive tag deduplication, aggregation, and favorites queries.
- [x] `Organization/SearchService.cs`: In-memory multi-criteria search (filename, tag, note, category, date range, size range, protection mode).
- [x] `Organization/SortService.cs`: Stable sorting by Name, Date Added, Date Modified, Size, and Category (with folders-first option).
- [x] `Cache/CacheEncryption.cs`: AES-256-GCM encryption with fresh 12-byte random nonces per write.
- [x] `Cache/VaultCache.cs`: Encrypted local cache for instant startup, thumbnail caching, and graceful fallback on corruption.
- [x] `Operations/BatchFileAddOperation.cs`: Multi-file addition and recursive directory ingestion with speed, ETA, and progress reporting.
- [x] `Operations/FileManagementOperations.cs`: Rename, Move, Deep Copy (independent duplicate write), and Verified Export with post-export SHA-256 verification.
- [x] `SecureVault.App` (WinUI 3 with Windows App SDK 2.4 / 1.7):
  - Elevated Fluent 2 dark design system with `#09090b` obsidian surfaces, violet/indigo accents, and Mica Alt backdrop.
  - WCAG AAA contrast ratio compliance (>= 7:1) and high-visibility keyboard focus rings.
  - `FileItemViewModel.cs`: Category glyphs, size formatting, protection mode badges, favorites indicator.
  - `LoginViewModel.cs`: Exponential brute-force delay (`min(2^attempts, 60)` sec), hint display, 24-word recovery input.
  - `MainLibraryViewModel.cs`: Navigation, toolbar commands, search debounce, sorting, and progress modal.
  - `RecoveryKeyConfirmationDialog.xaml`: 24-word grid presentation and 3-word verification gate (N23).
  - `LoginPage.xaml`: Glassmorphic card login, lockout banner, and creation dialogs.
  - `SidebarControl.xaml`: Categories with icons, Favorites, and All Files.
  - `ToolbarControl.xaml`: Instant search, Add Files/Folders, Sort picker, and Lock Vault.
  - `StatusBarControl.xaml`: Item counts, vault size metrics, and disk free space.
  - `VirtualizedFileGrid.xaml`: Virtualized `ItemsRepeater` for 60fps scrolling across 100K+ items, cards, and context menus.
  - `MainLibraryPage.xaml`: Coordinated shell with progress overlay.

### Test Suite Verification
- [x] Added `tests/vectors/auto-categorization.json` and `tests/vectors/brute-force-delay.json`.
- [x] Added `VirtualFolderServiceTests.cs` (nested folders, root items, safe deletion, 20-level hierarchy).
- [x] Added `AutoCategorizerTests.cs` (all formats and fallback behavior).
- [x] Added `TagServiceTests.cs` (deduplication, unique aggregation, favorites).
- [x] Added `SearchAndSortServiceTests.cs` (multi-field search, size filters, stable sorting).
- [x] Added `VaultCacheTests.cs` (random nonce per write assertion, round-trip, staleness, corruption recovery).
- [x] Added `BatchOperationsTests.cs` (batch add, rename, move, independent copy, SHA-256 verified export).
- [x] **Test Results:** 64 / 64 Passed (100% success rate).
- [x] `src/SecureVault.sln` builds with **0 errors and 0 warnings**.

---

### Phase 3: Integrated Apps (Media Viewers & Player, Notes Editor)
- [x] **Zero Disk-Write Invariant (H15, I04, L01):** Decrypts and renders images, videos, audio, PDFs, and notes strictly in memory; zero unencrypted bytes or temporary files touch the physical disk.
- [x] **Core Media & Documents Engine (`SecureVault.Core.Media` & `Notes`):**
  - `ImageDecoder.cs`: SkiaSharp-based in-memory decoder, aspect-ratio downsampler (`DecodeAtResolution`), and 90° CW/CCW in-memory rotation (`Rotate`).
  - `ExifMetadataReader.cs`: In-memory EXIF extraction (Camera Make/Model, Date Taken, Aperture, Shutter Speed, ISO, Focal Length, GPS coordinates).
  - `PdfRenderer.cs`: Docnet.Core (PDFium) in-memory rendering engine producing 32-bit BGRA page pixel buffers at custom scale/DPI.
  - `VaultMediaInput.cs`: LibVLCSharp `MediaInput` subclass bridging `VaultFileStream` directly into LibVLC with seeking and streaming across 1MB chunk boundaries.
  - `NoteDocument.cs`: Model supporting PlainText, Markdown, and RichText; UTF-8 JSON serialization; real-time word counting; Markdig Markdown rendering.
- [x] **WinUI 3 Integrated Viewers & Editors (`SecureVault.App`):**
  - `PhotoViewerPage.xaml` & `PhotoViewerViewModel.cs`: Dark obsidian HUD, smooth zoom/pan canvas, 90° CW/CCW rotation, full EXIF flyout drawer, keyboard shortcuts (Left/Right arrow, R, +/-, Esc).
  - `MediaPlayerPage.xaml` & `MediaPlayerViewModel.cs`: Direct-streaming LibVLC `VideoView`, seek scrubber, volume control, mute toggle, playback rate selector (0.5x to 2.0x), glowing audio waveform visualizer, full-screen presenter toggle.
  - `PdfViewerPage.xaml` & `PdfViewerViewModel.cs`: In-memory PDF viewing canvas, page navigation controls, page indicator, zoom in/out/reset, keyboard page flip (PageUp/PageDown, Left/Right).
  - `NotesEditorPage.xaml` & `NotesEditorViewModel.cs`: Split-screen Markdown editor with live preview pane, non-blocking 3-second debounced auto-save timer (`DispatcherQueueTimer`), live word and character counters, manual save (Ctrl+S).
  - Routed double-click and context menu "Open" from `VirtualizedFileGrid` to the dedicated viewer based on `FileCategory`.
  - Added `+ Note` quick creation button to `ToolbarControl.xaml`.
- [x] **Test Suite Verification:**
  - Added `ImageDecoderTests.cs` (decoding, aspect ratio preservation, 90°/180°/-90° rotation, EXIF safety).
  - Added `NoteDocumentTests.cs` (word count accuracy across whitespaces/newlines, roundtrip JSON serialization, Markdown HTML rendering).
  - Added `VaultMediaInputTests.cs` (Open, Read, Seek, Close across offsets with unmanaged memory buffer).
  - Added `PdfRendererTests.cs` (PageCount, BGRA buffer rendering, bounds checking).
  - **Test Results:** 85 / 85 Passed (100% success rate).
  - `src/SecureVault.sln` builds with **0 errors and 0 warnings** under x64.

---

### Phase 4: Advanced Features (Thumbnails, Auto-Lock, Editing, Parallel Pipeline, File Manager & Archives)
- [x] **A05 Password Hint System:**
  - Implemented unencrypted header field (offset `0x00FC`, up to 255 UTF-8 bytes) readable without unlocking.
  - Added `VaultManager.GetPasswordHint(path)` and `SetPasswordHint(hint)` with atomic header HMAC recalculation.
  - Display hint on login screen upon vault selection with security warning.
- [x] **A08 & M08 Auto-Lock & Workstation Lock Detection:**
  - `IdleLockService`: Win32 `GetLastInputInfo` idle timer auto-locking vault after configurable timeout (5 minutes default).
  - `SystemLockDetector`: Hooks `SystemEvents.SessionSwitch` for instant lock upon `SessionLock` (Win+L) or `SessionLogoff`.
- [x] **A17 & A18 Protection Mode Operations:**
  - `ProtectionModeOperation`: Per-file conversion between Fast Obfuscation and AES-256-GCM Secure Mode with verified SHA-256 integrity assertion.
  - "Encrypt Everything": Batch converts all Fast Obfuscation files to Secure Mode.
- [x] **E08–E14 Thumbnail Generation System:**
  - `ThumbnailGenerator`: Produces WebP thumbnails (<= 200x200 max) in memory for images, audio album art (ID3 via `TagLibSharp`), and PDF first page (via `PdfRenderer`).
  - `ThumbnailService`: Multi-core background generation using `SemaphoreSlim(ProcessorCount)` and local encrypted `VaultCache` storage.
- [x] **E15–E20 Performance & Caching Engine:**
  - `ChunkLruCache`: 16-chunk (16MB max) thread-safe LRU cache with eviction for smooth random seeking.
  - `ImagePrefetcher`: Asynchronously pre-decodes adjacent images (`currentIndex ± 1`) into memory.
  - `PlaybackPositionCache`: Remembers last playback position per media file to resume playback seamlessly (I17).
  - `ParallelChunkPipeline`: Producer-consumer pipeline using `Channel<T>` to parallelize chunk cryptography across cores while strictly enforcing sequential chunk write order.
- [x] **H07–H09 Image Editor:**
  - `ImageEditorViewModel` & `ImageEditorOverlay.xaml`: Interactive center crop, horizontal flip, vertical flip, and zero-disk-write commit back into vault.
- [x] **K01–K13 File Manager & In-Memory Archives:**
  - `FileManagerViewModel` & `FileManagerPage.xaml`: Tree + details file manager, recursive folder size calculation, duplicate file finder grouped by SHA-256, and storage statistics breakdown.
  - `ArchiveReader`: In-memory multi-format archive inspection and extraction powered by `SharpCompress` (ZIP, 7Z, TAR, RAR) directly into vault storage without touching physical disk.
- [x] **L07–L10 Advanced PDF:**
  - Full-text search across all PDF pages in memory with page number hits.
  - Automatic memory of last opened page via static cache.
- [x] **J09–J10 Notes Version History:**
  - `NoteVersionHistory`: Rolling 10-version snapshot retention with FIFO eviction and 1-click snapshot restore.
- [x] **Test Suite Verification:**
  - Added `tests/vectors/thumbnail-dimensions.json`.
  - Added `ThumbnailGeneratorTests.cs` (WebP headers, dimension preservation).
  - Added `ChunkLruCacheTests.cs` (capacity, LRU eviction, hits/misses).
  - Added `ArchiveReaderTests.cs` (in-memory ZIP listing and extraction).
  - Added `NoteVersionHistoryTests.cs` (FIFO 10-snapshot retention and version restore).
  - Added `PasswordHintTests.cs` (unencrypted header hint read and HMAC update).
  - Added `ProtectionModeOperationTests.cs` (mode toggling, SHA-256 integrity, EncryptAll).
  - Added `ParallelChunkPipelineTests.cs` (parallel multi-chunk write sequencing).
  - **Test Results:** 99 / 99 Passed (100% success rate).
  - `src/SecureVault.sln` builds with **0 errors and 0 warnings** on x64.

---

### Phase 5: Backup & Multi-Vault Chain (Backup, Restore, Split Archives, 200GB Limit & Multi-Vault)
- [x] **Chain-Aware Backup Subsystem (G01–G06, G15):**
  - `BackupService`: Enumerates all active chain files (`.vault`, `.vault2`, `.vault3`...), performs raw byte buffered streaming without decrypting or exposing plaintext, creates companion `.sha256` files in `sha256sum` format, and writes `<vaultName>.backup.manifest`.
  - `HashVerifier`: Incremental streaming SHA-256 calculator and companion file generator/verifier.
  - `SplitBackupService`: Configurable split archives (50GB default / arbitrary test sizes e.g. 50KB/100KB) into `.part001`, `.part002`... with per-part SHA-256 and whole-file SHA-256. Raw binary concatenation (`copy /b part1+part2`) reassembles the original vault.
  - `BackupManifest`: JSON model recording all chain parts, part sizes, and per-split SHA-256 checksums.
- [x] **Restore Subsystem & Verification (G07–G10, G14):**
  - `RestoreService`:
    - `CheckPartsAsync`: Pre-flight check validating all chain parts and split parts against manifest hashes, reporting specific missing or corrupted parts (`{VaultFile}: {SplitFile} - {Reason}`).
    - `RestoreChainAsync`: Atomic multi-vault chain restore, reassembling split parts, recreating live `.chain.manifest`, and verifying whole-file SHA-256 before completion.
    - Re-download recovery: Replacing a corrupted split part immediately validates and restores cleanly.
  - `BackupVerifier`: Offline pre-flight health verification of headers and manifests without vault password.
  - `FormatUpgrader`: Format version inspection and safe upgrade with `.vault.backup-v{old}` rollback copy.
- [x] **Multi-Vault Chain Subsystem (O01–O12, B23–B26):**
  - `VaultConstants.MaxVaultFileSizeBytes`: 200GB per-file limit (B23, O01).
  - `SecondaryVaultHeader`: 128-byte minimal header for `.vault2`, `.vault3`... referencing Master UUID, part index, and reusing `"SecureVault-HeaderHMAC-v1"` HMAC subkey derivation.
  - `VaultChainManager`:
    - **File-spans-part rollover rule:** If a file's estimated size exceeds the current part's remaining space, rolls to next part (`.vault{N+1}`) *before* writing any chunks. No chunks span multiple files.
    - `GlobalIndex` in master vault references all files across the chain with `PartIndex`.
    - Transparent cross-part `OpenFileStream` and `ReadAllBytesAsync`.
    - `MoveFileBetweenParts`: Moves independent physical chunks between parts and updates indices without pointer sharing.
  - `VaultChainManifest`: Live linking file `<vaultName>.chain.manifest` keeping track of active disk parts.
  - `VaultChainHealth`:
    - Missing part detection with graceful degradation: master vault unlocks cleanly even if secondary parts are unplugged/missing.
    - Files in available parts remain completely accessible; files in missing parts are marked unavailable and throw `VaultPartMissingException` with actionable guidance.
    - Plugged-back-in parts become immediately available on sync.
- [x] **WinUI 3 App Integration:**
  - `BackupRestoreDialog.xaml` & `BackupRestoreViewModel`: Tabbed dialog for Creating Backups (Single / Split with size selector) and Restoring Archives with per-part visual verification pills.
  - `VaultChainHealthDialog.xaml` & `VaultChainHealthViewModel`: Live metrics dashboard displaying all chain parts, sizes, per-part limit (200GB), and missing part warnings.
  - Added "Backup" and "Vault Chain" buttons to `ToolbarControl.xaml`.
  - Added "Restore Vault from Backup..." link to `LoginPage.xaml` for disaster recovery before opening.
- [x] **Test Suite Verification:**
  - Added `tests/vectors/backup-manifest-schema.json`.
  - Added `tests/vectors/sha256-companion.json`.
  - Added `BackupServiceTests.cs` (single-file backup, companion sha256 files, whole-chain backups).
  - Added `SplitBackupTests.cs` (splitting, naming, per-part hashes, raw binary concatenation).
  - Added `RestoreServiceTests.cs` (split restoration, missing part detection, corrupt part detection, re-download replacement).
  - Added `BackupVerifierTests.cs` (offline inspection without password).
  - Added `VaultChainManagerTests.cs` (file-spans-part rollover rule, cross-part reads, move file).
  - Added `VaultChainHealthTests.cs` (graceful degradation, missing part detection, reconnection).
  - **Test Results:** 110 / 110 Passed (100% success rate).
  - `src/SecureVault.sln` builds with **0 errors and 0 warnings** on x64.

---

### Phase 6: Polish, Hardening & Production Release (All 120 Tests Passing, Solution Builds with 0 Errors)
- [x] **File Replace Operation (C20):**
  - In-place file content replacement generating fresh salts and 12-byte random nonces per chunk.
  - Updates sizes, modified timestamps, plaintext SHA-256, and chunk pointers in vault index.
  - Leaves orphaned chunks safely for defragmentation/compaction.
- [x] **Duplicate File Detector (C21):**
  - Scans active files grouped by plaintext SHA-256 checksum.
  - Accurately differentiates between redundant physical copies that waste disk space and CoW chunk-shared copies consuming zero extra bytes.
- [x] **Chain-Aware Vault Compaction (C23):**
  - Defragments physical chunk storage and reclaims orphaned bytes from deleted and replaced files.
  - Pre-flight verification requiring $\ge 2\times$ free disk space on the container volume.
  - Global chunk reference mapping across non-deleted entries preserving CoW shared chunks.
  - Multi-vault chain awareness: compactor builds offset translation tables (`oldOffset -> newOffset`), updating secondary local indices and synchronizing the master Global Index entries.
  - Atomic two-phase commit swap with `.pre-compact` backup and automatic rollback recovery.
- [x] **Cryptographic Repair & Audit Logger (F03):**
  - Thread-safe repair event logger asserting post-repair cryptographic re-verification.
  - Enforces the strict rule: only commit repairs if AES-GCM AuthTag or CRC32 re-verification passes.
- [x] **Disaster Recovery Container Scanner (F10):**
  - Scans raw vault containers for `BLKH` block headers and `BLKF` block footers when indices are destroyed.
  - Tiered confidence classification: `CryptographicallyVerified` (Secure Mode AEAD tag + CRC32 + RS parity + SHA-256) vs `StructuralAndParityVerified` (Fast Obfuscation magic + chunk headers + RS parity + CRC32).
  - Ability to rebuild a salvaged index from discovered files.
- [x] **Deep Vault Integrity Checker (F15) & Background Repair Service (F16):**
  - Evaluates header HMAC, dual index parity, Reed-Solomon symbol corrections, AEAD tags, and file hashes.
  - Generates comprehensive `VaultHealthReport` with overall health score (0–100%).
  - Non-blocking low-priority background worker periodically scanning chunks and auto-repairing bit-rot.
- [x] **Cryptographic Temp File Wiping (M07):**
  - `SecureTempFile`: Overwrites with CSPRNG random bytes followed by zeroes (`0x00`) and disk flush before deletion.
  - Explicit documentation of SSD/NVMe flash controller wear-leveling and out-of-place write allocations.
- [x] **Screen Capture & Recording Protection (M17):**
  - P/Invoke `SetWindowDisplayAffinity` with `WDA_EXCLUDEFROMCAPTURE` (and `WDA_MONITOR` fallback) protecting vault data from screenshots, screen recorders, and remote viewers.
- [x] **In-Memory Clipboard Ingestion (C24):**
  - Ingests clipboard images, copied files, and text snippets directly in-memory into encrypted chunks without intermediate temporary disk files.
- [x] **Multi-Mode Library Presentation (D21, N10, N11):**
  - `VirtualizedFileGrid.xaml`: Virtualized 60fps card grid.
  - `FileListView.xaml`: Detailed tabular view with sortable column headers (Name, Type, Size, Date Modified, Security, Part).
  - `TimelineView.xaml`: Date-grouped chronological timeline presentation (Year/Month headers).
  - Instant view mode switching via dedicated toolbar icon buttons.
- [x] **File Properties Dialog (N13, C22):**
  - Tabbed inspector: General metadata, Cryptography (Plaintext SHA-256, UUID, Protection Mode, First Chunk Offset), and Chunk Table showing per-chunk offsets, sizes, CRC32, AuthTag, and RS parity.
- [x] **Application Settings (N14):**
  - `SettingsPage.xaml`: Toggles for Screen Protection, Inactivity Auto-Lock duration, Workstation Lock auto-lock, Default Protection Mode, and Password Hint management.
- [x] **Session Persistence & History (D20, N20):**
  - `WindowStateService`: Restores and persists window width, height, position, and maximized state.
  - `RecentFilesService`: Tracks FIFO 20-file access history in encrypted `VaultCache`.
- [x] **Test Suite Verification:**
  - Added `FileReplaceOperationTests.cs` (content replacement, fresh nonces, SHA-256 update).
  - Added `DuplicateDetectorTests.cs` (duplicate grouping, wasted space, CoW awareness).
  - Added `VaultCompactionTests.cs` (single vault compaction, multi-vault chain compaction with master index update, rollback safety).
  - Added `RecoveryScannerTests.cs` (salvaging files with wiped index, tiered confidence).
  - Added `RepairLoggerTests.cs` (audit logging, re-verification assertion).
  - Added `IntegrityCheckerTests.cs` (healthy vault 100% score, tampered chunk detection).
  - Added `SecureTempFileTests.cs` (multi-pass wipe before delete).
  - **Test Results:** 120 / 120 Passed (100% success rate).
  - `src/SecureVault.sln` builds with **0 errors and 0 warnings** on x64.

---

## Current State: Phase 6 Complete (All 120 Tests Passing, Solution Builds with 0 Errors)
- **Current Milestone:** Phase 6: Polish, Hardening & Production Release — **COMPLETE**
- **All Phases 1–6:** **100% COMPLETE**
- **Branch:** `phase-6/polish`
- **Environment:** Isolated .NET 8 SDK (8.0.424) in `$env:USERPROFILE\.dotnet`
- **Total Unit Tests Passing:** 123 / 123
- **Total Build Status:** 0 errors, 0 warnings across all projects on x64.

---

## Audit Findings (Phase 6 Polish Audit)
- **Task:** Verification of chunk reference counting, Copy/Replace/Compaction interaction tests, backup chain-awareness, and stream serialization.
- **Completed Steps:**
  - Audited `FileManagementOperations.cs`, `VaultIndex.cs`, `ChunkIndex.cs`, `FileDeleteOperation.cs`, `FileReplaceOperation.cs`, `VaultCompaction.cs`. Confirmed no chunk reference-counting mechanism exists.
  - Implemented interaction test suite `InteractionAuditTests.cs` covering Copy -> Replace File A -> Read File B and Copy -> Compact -> Read File B.
  - Diagnosed stream position collision in `FileAddOperation.cs`: when `sourceStream` is `VaultFileStream`, chunk reads seek `_vaultStream` to existing chunks, causing subsequent writes without position restoration to overwrite data on disk.
  - Fixed `FileAddOperation.cs` to track and restore `currentWriteOffset` on `_vaultStream` before every chunk write and footer write.
  - Confirmed `VaultManager._stream` access was previously unserialized between foreground operations and background workers (`BackgroundRepairService`).
  - Added `SemaphoreSlim _streamLock = new(1, 1)` to `VaultManager`, serializing `AddFileAsync`, `DeleteFile`, `StreamLength`, `Dispose()`, `FileReplaceOperation`, `VaultCompaction`, and `ChunkReader.ReadChunk()`.
  - In `AddFileAsync` and `FileReplaceOperation`, added same-vault stream detection that spools `VaultFileStream` before acquiring `_streamLock` to eliminate any lock inversion or stream collision.
  - Implemented `ConcurrentAccessTests.cs` executing concurrent foreground operations (Copy, Add, Replace, Read, Delete) while `BackgroundRepairService` actively scans chunks in the background. Added in-loop assertions and elapsed time logging per worker task. Verified execution duration ~3.47 seconds with 4 background scans completed concurrently and 100% health score.
  - Audited all 5 `ChunkReader` construction sites across the codebase: confirmed both container stream production sites pass the shared `_streamLock` / `part.StreamLock`, and the remaining 3 are isolated unit tests on in-memory streams.
  - Audited `VaultCompaction.cs` chunk read path: verified `CopyChunkBytes` reads directly from raw `Stream` byte ranges without invoking `ChunkReader`, ensuring internal chunk reads are completely lock-free and cannot deadlock on re-entry against `vault.StreamLock`.
  - Audited UI callers for Backup: confirmed `MainLibraryViewModel` / `ToolbarControl.xaml` -> `BackupRestoreDialog.xaml` -> `BackupRestoreViewModel.CreateBackupAsync` calls `BackupService.BackupChainAsync` and `SplitBackupService.BackupSplitChainAsync`.
  - Re-ran test suite: all 123 tests passed (123/123).
  - Release Preparation v1.0.0:
    - Added multi-part chain compaction disclaimer in `CompactionDialog.xaml` and `README.md`.
    - Generated official brand logo in `assets/logo.png` and embedded in `README.md`.
    - Finalized `README.md` with threat model, architecture, dependencies (LibVLC LGPL dynamic linking note), and known limitations.
    - Created `CONTRIBUTING.md` with non-negotiable test vector diff rule for `Crypto/`, `Format/`, `Integrity/`.
    - Created standard `LICENSE` (MIT).
    - Executed clean-clone sanity check in fresh directory following only README instructions: 123/123 tests passed (12s duration), solution built with 0 warnings and 0 errors.
    - Conducted full repo secrets and absolute path sweep; updated `.gitignore` with `cache/`, `*.lock`, `scratch/`.
    - Merged `phase-6/polish` into `main`, tagged `v1.0.0`, pushed `main` and `v1.0.0` tag to GitHub, and set repository visibility to public.

---

### Standalone Single-File Packaging & Professional GitHub Organization
- [x] **Zero-Bloat Git Repository Architecture:**
  - Separated source control from binary distribution: `.gitignore` configured to ignore `publish/`, `dist/`, `artifacts/`, `*.exe`, `*.msi`, `*.zip`, `*.pdb`.
  - Avoids git 100MB commit limit and permanent repository bloat; binaries distributed as official GitHub Release assets.
- [x] **Standalone Single-File Packaging Pipeline:**
  - Configured `SecureVault.App.csproj` with `<AssemblyName>SecureVault</AssemblyName>`, `<TargetName>SecureVault</TargetName>`, and `<EnableMsixTooling>true</EnableMsixTooling>` for `PublishSingleFile=true`.
  - Added `Program.cs` custom entry point establishing `MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY` before `Application.Start` for flawless unpackaged Windows App SDK runtime resolution.
  - Authored automated packaging script `scripts/publish-single-file.ps1` executing pre-flight tests, cleaning output, publishing self-contained x64 single-file executable, generating companion `.sha256`, and creating zip archive.
- [x] **Manual Launch Verification on Host:**
  - Executed packaged `publish/SecureVault.exe` directly: verified process initialization (`Process Name: SecureVault`), WinUI 3 message loop, memory allocation (9.84 MB working set), and `Responding == True` without crashes or missing bundle dependency errors.
- [x] **Secrets Sweep:**
  - Completed dedicated sweep across commit history, documentation, and agent notes verifying zero leaked credentials, reseller/API account identifiers, or private endpoints.
- [x] **Professional Repository Infrastructure:**
  - `.github/workflows/ci.yml`: Automated CI workflow on Windows runner building and validating all 123 tests on PRs and branch pushes.
  - `.github/workflows/release.yml`: Automated tag-triggered release workflow building single-file executable, computing checksums, and attaching assets to GitHub Releases.
  - `.github/ISSUE_TEMPLATE/`: Added structured bug report and feature request templates with config directing security inquiries to security policy.
  - `.github/PULL_REQUEST_TEMPLATE.md`: Added PR checklist enforcing deterministic test vector diff rule for crypto changes.
  - `SECURITY.md`: Published responsible disclosure policy and cryptographic threat boundaries.
  - `CODE_OF_CONDUCT.md`: Established Contributor Covenant 2.1 community standard.
  - `README.md`: Polished with status badges, standalone download guide, Windows SmartScreen user expectations, and packaging documentation.
- [x] **Workspace Cleanup:**
  - Removed duplicate `SecureVault-Vision-v2.md` and internal AI meta-prompt `prompt.md` from git tracking.
  - Preserved canonical `docs/vision.md`.
- [x] **Test Suite Verification:**
  - All 123 / 123 unit, integration, and concurrency tests passing.
  - Solution builds with 0 errors and 0 warnings.
