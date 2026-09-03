# SecureVault — Project Progress

## Current State: Phase 2 Complete (All 64 Tests Passing, WinUI 3 App Built)
- **Current Milestone:** Phase 2: UI Foundation & Basic Operations — **COMPLETE**
- **Next Milestone:** Phase 3: Media Viewers & Player (Photo Viewer, Video/Audio Player, PDF Viewer, Notes Editor)
- **Branch:** `phase-2/basic-ui`
- **Environment:** Isolated .NET 8 SDK (8.0.424) in `$env:USERPROFILE\.dotnet`
- **Activation:** Run `. .\activate.ps1` in PowerShell to set `DOTNET_ROOT` and `PATH`

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

## Current State: Phase 3 Complete (All 85 Tests Passing, Solution Builds with 0 Errors)
- **Current Milestone:** Phase 3: Integrated Apps — **COMPLETE**
- **Next Milestone:** Phase 4: Enhanced Experience (Thumbnails, Full-Text Search, Recent Files, Extended Formats)
- **Branch:** `phase-3/integrated-apps`
- **Environment:** Isolated .NET 8 SDK (8.0.424) in `$env:USERPROFILE\.dotnet`
- **Activation:** Run `. .\activate.ps1` in PowerShell to set `DOTNET_ROOT` and `PATH`

