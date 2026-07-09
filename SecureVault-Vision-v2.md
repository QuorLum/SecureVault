# SecureVault — Complete Project Vision Document (v2, Revised)

> This is a revision of a 6-month-old spec. Structure and numbering are kept identical
> to the original so you can diff against it. Changed items are marked **🔧 FIXED**,
> new items are marked **🆕 NEW**. Everything else is unchanged from your original plan.

---

## Revision Summary (read this first)

Five real gaps were fixed:

1. **XOR "Level 1 encryption" was mislabeled.** It's obfuscation, not encryption. Renamed
   and reworked so it can't be mistaken for a security guarantee (by you, later, or by a user).
2. **Per-chunk authentication was missing.** A single SHA-256 on the whole block doesn't
   let you verify a chunk you jump to mid-stream. Each chunk now carries its own AEAD tag.
3. **Concurrency/file-locking was never addressed.** Two processes (or a crash mid-write)
   touching the same vault file needs an explicit single-writer lock — added.
4. **Reed-Solomon was over-specified (4 configurable levels).** Collapsed to one sane
   default, with the option exposed later only if you actually want it.
5. **No threat model.** Added one — it changes some of your other decisions once written down.

Everything else (feature list, phases, tech stack) is intact. Nothing was cut.

---

## Threat Model 🆕 NEW

Write this down explicitly, because your other decisions (auto-lock, screen capture
protection, recovery key) only make sense in relation to it.

**Protects against:**
- Someone else getting physical access to your laptop or a copy of the vault file
  (theft, loss, borrowed laptop, cloud backup account compromise).
- Casual inspection of the raw file (a curious person opening it in a hex editor
  shouldn't see filenames, thumbnails, or content).
- Silent bit rot / transfer corruption over long-term storage and cloud sync.

**Does NOT protect against** (state this to yourself so you don't over-build later):
- Someone with a keylogger or screen-recorder already running on your machine.
- Forensic memory analysis of a live, *unlocked* session (mitigated, not eliminated,
  by key-zeroing on lock).
- Losing both your password AND your recovery key — **this is permanent, irreversible
  data loss by design.** Write this down in the app's UI, not just here. There is no
  backdoor, which is the point, but it means a lost password + lost recovery phrase =
  gone forever. Decide now if you want a "confirm you've saved your recovery key"
  gate during vault creation.

---

## What We're Building (In Simple Words)

```
we're building a PERSONAL DIGITAL SAFE that works like a
private operating system for your files.

It's a single Windows app that:
- Stores EVERYTHING you own digitally
- In encrypted vault files that nobody else can open
- With built-in apps to view/play/edit without extracting
- Self-healing files that survive corruption
- Portable backups you can restore anywhere
- Password + recovery key protection

Think of it as:
Windows Explorer + VLC + Photo Gallery + Notes App + 7-Zip
All wrapped inside an encrypted container
That only YOU can access
```

---

## Complete Feature Master List

```
Status markers:
📋 = Planned   🔨 = In Progress   ✅ = Done   🧪 = Testing
```

### CATEGORY A: VAULT CORE ENGINE

```
A01 📋 Create new vault with password
A02 📋 Unlock vault with password
A03 📋 Lock vault (zero all keys from memory)
A04 📋 Change password (without re-processing files)
A05 📋 Password hint (shown on login screen)
A06 📋 Recovery key generation (24-word phrase)
A07 📋 Recovery key unlock (alternative to password)
A08 📋 Auto-lock after idle timeout (configurable)
A09 📋 Failed attempt delay (anti brute-force)
A10 📋 Vault format version in header (backward compatibility)
A11 📋 Vault UUID (unique identifier per vault)
A12 📋 Master key architecture (password wraps master key)
A13 📋 Argon2id key derivation (memory-hard, slow brute force)
A14 📋 AES-256-GCM for index encryption (always encrypted)
A15 🔧 FIXED: "Fast Obfuscation Mode" for file data (NOT encryption —
       XOR keystream, default, protects against casual viewing only)
A16 📋 AES-256-GCM per-file encryption ("Secure Mode", optional, per file)
A17 📋 "Encrypt Everything" button (batch convert to Secure Mode)
A18 📋 Toggle protection mode per file (switch Fast ↔ Secure)
A19 🆕 NEW: Master key wrapped TWICE independently — once via
       Argon2id(password), once via HKDF(recovery key seed) — stored as
       two separate wrapped blobs in the header. Losing one doesn't
       affect the other.
A20 🆕 NEW: Single-writer file lock (named mutex + lock file) — prevents
       two app instances, or a crash-recovery process, from writing to
       the same vault concurrently. Attempting a second open shows
       "Vault already open elsewhere" instead of corrupting data.
A21 🆕 NEW: Key zeroing uses CryptographicOperations.ZeroMemory on
       pinned buffers, not plain managed byte[] — the .NET GC can
       relocate/copy managed arrays before you zero them, leaving
       stray copies in memory. Use fixed/pinned unmanaged buffers for
       master key, derived key, and XOR keystream material.
```

**Why A15 changed:** the original called XOR "Level 1 encryption." It isn't — it's a
keystream derived from your master key, which is fast but breakable via known-plaintext
(JPEG/MP4/PDF headers are predictable bytes) and has no authentication. That's fine as an
opt-in fast mode for low-sensitivity files, but it must never be presented to a user (even
future-you) as equivalent to AES-256-GCM. Consider making AES-256-GCM ("Secure Mode") the
default and Fast Obfuscation the opt-out, given the app's entire premise is security.

### CATEGORY B: FILE FORMAT AND STORAGE

```
B01 📋 Proprietary .vault binary format
B02 📋 Chunked file storage (1MB chunks)
B03 🔧 FIXED: Chunk index per file (offset, size, CRC32, AND per-chunk
       AEAD auth tag — see B22a)
B04 📋 64-bit offsets (supports huge vaults)
B05 📋 Block header per file (GUID, chunk count, protection level)
B06 🔧 FIXED: Block footer per file (SHA-256 of entire block) — kept as
       a fast whole-file integrity check, but this is NOT a substitute
       for per-chunk auth tags (needed for random-seek verification —
       see B22a)
B07 📋 Reed-Solomon error correction per chunk
B08 🔧 FIXED: One default RS level (~12% overhead), not 4 user-facing
       levels. Expose "Off / Default / Maximum" only if you actually
       want that UI surface later — don't build 4 tunable levels for v1.
B09 📋 Default RS level configurable by user (deferred — see B08 note)
B10 📋 Smart compression selection based on file type
B11 📋 Compression: None (for already compressed files)
B12 📋 Compression: LZ4 (fast, for large files)
B13 📋 Compression: Brotli (best ratio, for small text files)
B14 📋 Auto-detect: skip compression if result is larger
B15 📋 Primary index (encrypted, RS-protected, near start)
B16 📋 Backup index (encrypted, RS-protected, near end)
B17 📋 Floating index with pointer chain (append-friendly)
B18 📋 Vault header with encrypted section
B19 📋 Vault footer with backup pointers
B20 📋 Random prefix in header (prevent pattern detection)
B21 📋 XOR-masked magic bytes (not obvious file signature)
B22 📋 Per-chunk unique nonce for AES-GCM (base_nonce + counter)
B22a 🆕 NEW: Each chunk, when in Secure Mode, is its own independent
       AEAD unit — own nonce, own 16-byte GCM auth tag, stored with the
       chunk. This is what actually enables safe random-access seeking
       (video scrubbing, PDF page jump) without decrypting the whole
       file: a single big GCM stream can't be seeked into safely.
B23 📋 200GB per vault file limit (arbitrary, chosen for cloud-upload
       chunk-size friendliness — not an OS or format constraint; may
       revisit)
B24 📋 Multi-vault linking (.vault + .vault2 + .vault3)
B25 📋 Cross-vault verification data (detect missing vault files)
B26 📋 Vault manifest for multi-file vaults
B27 🆕 NEW: Reed-Solomon uses a well-tested library (e.g.
       STH1123.ReedSolomon), never a custom implementation. RS math has
       subtle edge cases (Galois field arithmetic, erasure vs error
       decoding) where a bug silently produces corrupted "repairs" that
       look successful. This is not a place to hand-roll.
```

### CATEGORY C: FILE OPERATIONS

```
C01 📋 Add single file to vault
C02 📋 Add multiple files to vault
C03 📋 Add entire folder recursively
C04 📋 Drag and drop files into app
C05 📋 Streaming file addition (never load full large file in RAM)
C06 🔧 FIXED: SHA-256 checksum computed on the PLAINTEXT before
       compression/encryption (used for both integrity baseline AND
       duplicate detection — see C21). Hashing after compression would
       make identical files with different compression settings look
       like different files.
C07 📋 Progress reporting during add (percentage, speed, ETA)
C08 📋 Delete file from vault (mark as free, don't rewrite)
C09 📋 Delete multiple files
C10 📋 Rename file
C11 📋 Move file to different virtual folder
C12 📋 Copy file to different virtual folder
C13 📋 Export file to disk
C14 📋 Export multiple files
C15 📋 Export entire folder
C16 📋 Read file into memory (for small files)
C17 📋 Read file as stream (for large files, video, audio)
C18 📋 VaultFileStream with chunk-based seeking
C19 📋 Read-ahead prefetch (preload next chunk while current plays)
C20 📋 Replace file data (for editing notes, rotating photos)
C21 📋 Duplicate detection (same plaintext SHA-256 = same file)
C22 📋 File properties view (size, dates, checksum, metadata)
C23 🔧 FIXED: Vault compaction — algorithm specified: rewrite all live
       chunks to a new temp vault file, verify every chunk's hash
       matches the index before deleting anything, then atomic
       rename over the original. Needs ~2x free disk space during
       compaction — surface that requirement in the UI.
C24 📋 Clipboard paste (paste screenshots directly into vault)
```

### CATEGORY D: ORGANIZATION

```
D01 📋 Virtual folder system (unlimited nesting)
D02 📋 Create / rename / delete folders
D03 📋 File categories: Photos, Videos, Audio, Documents, TextNotes,
       Applications, Archives, Other
D04 📋 Auto-categorization based on file type
D05 📋 Tags per file (user-defined)
D06 📋 Favorites / starred files
D07 📋 Notes/description per file
D08 📋 Search by filename
D09 📋 Search by tags
D10 📋 Search by notes content
D11 📋 Search by file type / category
D12 📋 Search by date range
D13 📋 Search by size range
D14 📋 Search by protection level (Fast Obfuscation or Secure Mode)
D15 📋 Sort by name / date added / size / type
D16 📋 Filter by favorites only
D17 📋 Albums for photos
D18 📋 Playlists for audio/video
D19 📋 Notebooks and sections for notes
D20 📋 Recent files list
D21 📋 View modes: grid (thumbnails), list (details), timeline
```

### CATEGORY E: PERFORMANCE AND CACHING

```
E01 📋 Encrypted local cache file
E02 📋 Cache stores: index snapshot, thumbnails, UI state
E03 📋 Instant app startup from cache (< 1 second after password)
E04 📋 Background cache freshness verification
E05 📋 Incremental cache update (don't rebuild on every change)
E06 📋 Progressive loading (show names first, thumbs later)
E07 📋 Virtualized UI lists (only render visible items)
E08 📋 Thumbnail generation in background
E09 📋 Thumbnail formats: WebP, 200x200 max
E10 📋 Thumbnail for images (SkiaSharp)
E11 📋 Thumbnail for videos (extract frame at 10%)
E12 📋 Thumbnail for audio (album art extraction)
E13 📋 Thumbnail for PDFs (render first page)
E14 📋 Parallel thumbnail generation (multi-core)
E15 📋 LRU chunk cache during playback (keep recent chunks)
E16 📋 Pre-render adjacent images in gallery
E17 📋 Cache video playback positions (resume where left off)
E18 📋 Cache UI state (window size, last category, scroll)
E19 📋 Streaming decryption/deobfuscation (per-chunk, not full file)
E20 📋 Parallel chunk processing (encrypt/write pipeline)
```

### CATEGORY F: INTEGRITY AND RESILIENCE

```
F01 📋 Reed-Solomon error correction on every chunk
F02 📋 Auto-repair corrupted chunks during read
F03 🔧 FIXED: Log warning when auto-repair happens, AND re-verify the
       repaired chunk's own auth tag/hash before committing it back to
       disk — don't let a bad repair silently overwrite a recoverable
       original.
F04 📋 Full vault integrity check (button in UI)
F05 📋 Repair vault (rewrite repaired chunks)
F06 📋 Dual index (primary + backup, survives truncation)
F07 📋 Atomic writes (temp file + rename, crash-safe)
F08 📋 Write-ahead approach for index updates
F09 📋 Block isolation (corruption in file A doesn't affect file B)
F10 📋 Recovery mode scan (find files even if index is destroyed)
F11 📋 Per-chunk CRC32 (fast integrity check)
F12 📋 Per-file SHA-256 (full integrity verification)
F13 🔧 FIXED: AES-GCM authentication tag is PER CHUNK, not per file
       (required for seekable streaming — see B22a)
F14 📋 HMAC on vault header (detect header tampering)
F15 📋 Integrity report (which files OK, which damaged, percentage)
F16 📋 Background auto-repair (fix chunks after detection)
```

### CATEGORY G: BACKUP AND RESTORE

```
G01 📋 Single-file backup (copy .vault)
G02 📋 Split backup (50GB parts for cloud limits)
G03 📋 Backup manifest file (JSON with part hashes)
G04 📋 Per-part SHA-256 verification
G05 📋 Whole-file SHA-256 verification
G06 📋 .vault.sha256 companion file for single backups
G07 📋 Restore from single file
G08 📋 Restore from split parts (join + verify)
G09 📋 Re-download specific corrupted part only
G10 📋 Backup verification (check backup without restoring)
G11 📋 Vault is 100% self-contained (no external dependencies)
G12 📋 No registry, no config files, no certificates needed
G13 📋 Any app version opens any vault version
G14 📋 Format version upgrade (optional, keeps old as backup)
G15 📋 Multi-vault verification (.vault + .vault2 cross-check)
G16 📋 List of files in each vault part (know what's where)
```

### CATEGORY H: INTEGRATED APP — GALLERY

```
H01 📋 Photo grid view with thumbnails
H02 📋 Full-screen photo viewer
H03 📋 Swipe left/right between photos
H04 📋 Pinch-to-zoom / scroll-to-zoom
H05 📋 EXIF data display (camera, GPS, date, settings)
H06 📋 Rotate (clockwise, counter-clockwise)
H07 📋 Crop
H08 📋 Flip (horizontal, vertical)
H09 📋 Save edits back to vault
H10 📋 Slideshow mode with configurable interval
H11 📋 Albums (create, rename, delete, add/remove photos)
H12 📋 Timeline view (group by date)
H13 📋 Favorites filter
H14 📋 Supported formats: JPG, PNG, GIF, WebP, BMP, SVG, TIFF, ICO,
       HEIC, RAW (CR2, NEF, ARW, DNG)
H15 📋 Image decoded in memory (never touches disk)
H16 📋 Pre-load adjacent images for smooth navigation
H17 📋 SkiaSharp rendering (GPU accelerated)
H18 📋 Large image handling (decode at screen res, full on zoom)
```

### CATEGORY I: INTEGRATED APP — MEDIA PLAYER

```
I01 📋 Video playback using libVLC engine
I02 📋 Audio playback using libVLC engine
I03 📋 Stream from vault (VaultFileStream → libVLC StreamMediaInput)
I04 📋 No temp files on disk for playback
I05 📋 Play / pause / stop
I06 📋 Seek bar with progress
I07 📋 Volume control
I08 📋 Playback speed (0.25x to 4x)
I09 📋 Full screen mode
I10 📋 Picture-in-Picture (floating mini player)
I11 📋 Subtitle support (embedded + external from vault)
I12 📋 Audio track selection (for multi-audio videos)
I13 📋 Chapter navigation (MKV chapters)
I14 📋 Take screenshot during playback (save to vault)
I15 📋 Loop / repeat single / repeat all
I16 📋 Keyboard shortcuts (space, arrows, F, M, etc)
I17 📋 Resume playback (remember position per video)
I18 📋 Playlists (create, edit, play queue)
I19 📋 Play next / previous
I20 📋 Mini player mode for audio (compact)
I21 📋 Album art display for audio
I22 📋 Background audio playback while browsing vault
I23 📋 Hardware accelerated video decoding (GPU)
I24 📋 All formats: MP4, MKV, AVI, MOV, WebM, FLV, WMV, MP3, FLAC,
       WAV, AAC, OGG, WMA, OPUS, and more
I25 📋 Waveform visualization for audio
```

### CATEGORY J: INTEGRATED APP — NOTES

```
J01 📋 Create new note
J02 📋 Plain text editor
J03 📋 Markdown editor with live preview
J04 📋 Rich text editor (bold, italic, lists, headings)
J05 📋 Checklist / to-do lists
J06 📋 Code snippets with syntax highlighting
J07 📋 Notebooks and sections hierarchy
J08 📋 Auto-save (debounced, every few seconds)
J09 📋 Version history (keep last 10 versions per note)
J10 📋 Restore previous version
J11 📋 Full-text search across all notes
J12 📋 Word count
J13 📋 Attach vault files to notes (link by reference)
J14 📋 Embed images from vault into notes
J15 📋 Export note as PDF
J16 📋 Export note as TXT / MD
J17 📋 Pin important notes
J18 📋 Tags on notes
J19 📋 Note timestamps (created, modified)
J20 📋 Mixed content (text + images + checklists)
```

### CATEGORY K: INTEGRATED APP — FILE MANAGER

```
K01 📋 Virtual folder tree navigation
K02 📋 File list with details (name, size, date, type)
K03 📋 Bulk selection (Ctrl+click, Shift+click, Select All)
K04 📋 Cut / copy / paste between folders
K05 📋 Drag and drop between folders
K06 📋 Context menu (open, rename, delete, properties, etc)
K07 📋 Folder size calculation
K08 📋 Duplicate file finder (by plaintext SHA-256 hash)
K09 📋 Browse archive contents (ZIP/RAR/7Z inside vault)
K10 📋 Extract single file from archive to vault
K11 📋 Extract all files from archive to vault folder
K12 📋 File type statistics (how much space per category)
K13 📋 Archive support via SharpCompress library
```

### CATEGORY L: INTEGRATED APP — PDF VIEWER

```
L01 📋 Open PDF from vault in memory
L02 📋 Page rendering (Pdfium engine)
L03 📋 Zoom in/out
L04 📋 Fit to width / fit to page
L05 📋 Page navigation (next, prev, go to page)
L06 📋 Scroll through pages
L07 📋 Text search within PDF
L08 📋 Bookmarks / outline panel
L09 📋 Copy text selection
L10 📋 Remember last page (per document)
L11 📋 Pre-render adjacent pages for smooth scroll
```

### CATEGORY M: SECURITY

```
M01 🔧 FIXED: Argon2id with at minimum 64MB memory cost — since this
       is a desktop app (not memory-constrained mobile), consider
       raising to 128–256MB memory cost, 2–3 iterations, parallelism
       matched to core count. Higher memory cost is the main lever
       against GPU/ASIC brute-force.
M02 📋 AES-256-GCM for all Secure Mode encryption (authenticated)
M03 📋 Unique nonce per chunk (never reused)
M04 📋 Master key zeroed from RAM on lock (see A21 — pinned buffers)
M05 📋 Obfuscation key zeroed on lock
M06 📋 No decrypted data written to disk (except video fallback)
M07 📋 Secure temp file deletion (overwrite + delete) if used
M08 📋 Auto-lock on system screen lock
M09 📋 Auto-lock on idle timeout
M10 📋 Auto-lock on minimize (optional setting)
M11 🔧 FIXED: Brute force delay — explicit formula: delay_seconds =
       min(2^failed_attempts, 60), capped, reset on success
M12 📋 Proprietary format (no standard tool can parse)
M13 📋 Encrypted index (file names hidden without password)
M14 📋 XOR obfuscation keystream derived via HKDF, position-dependent,
       UNIQUE PER FILE (never reuse the same keystream across two
       files — this is the #1 way XOR/stream-cipher schemes get
       broken)
M15 📋 No file signatures visible in raw data (headers masked)
M16 📋 Constant-time password comparison (prevent timing attack)
M17 📋 Screen capture protection (optional, SetWindowDisplayAffinity)
M18 🆕 NEW: Study VeraCrypt's container design before finalizing your
       header/format layout. You're not adopting it — you're checking
       your design against a codebase that's been through years of
       public scrutiny, specifically around header authentication and
       key-slot design.
```

### CATEGORY N: USER INTERFACE

```
N01 📋 WinUI 3 with Windows App SDK
N02 📋 Login screen (password entry, vault selection)
N03 📋 Password hint display on login
N04 📋 Recovery key entry option on login
N05 📋 Main library view (sidebar + content area)
N06 📋 Sidebar: categories, folders, integrated apps
N07 📋 Toolbar: add files, search, sort, view mode
N08 📋 Status bar: file count, vault size, free space
N09 📋 File grid view (thumbnails)
N10 📋 File list view (detailed columns)
N11 📋 File timeline view (grouped by date)
N12 📋 Context menu on right-click
N13 📋 File properties dialog
N14 📋 Settings page
N15 📋 Dark theme (default)
N16 📋 Light theme (optional)
N17 📋 Progress dialogs for long operations
N18 📋 Notification toasts (file added, auto-repaired, etc)
N19 📋 Keyboard shortcuts throughout app
N20 📋 Window state persistence (size, position)
N21 📋 Fluent Design (acrylic, rounded corners, animations)
N22 📋 Responsive layout (adapts to window size)
N23 🆕 NEW: One-time "confirm you've saved your recovery key" screen
       during vault creation (see Threat Model — loss is permanent)
```

### CATEGORY O: MULTI-VAULT SYSTEM

```
O01 📋 200GB per .vault file limit
O02 📋 Automatic overflow to .vault2 when limit reached
O03 📋 .vault2 overflow to .vault3 and so on
O04 📋 Master vault (.vault) contains global index of ALL files
O05 📋 Each vault file has local index of its own files
O06 📋 Cross-vault file reference (file in .vault2 referenced from .vault)
O07 📋 Vault chain manifest (which vault files belong together)
O08 📋 Missing vault detection (user forgot to download .vault2)
O09 📋 Graceful degradation (show files from available vaults, mark
       missing vault files as unavailable)
O10 📋 Per-vault integrity check
O11 📋 Move files between vault parts
O12 📋 Vault chain health dashboard
```

---

## Architecture Summary

```
WHAT EXISTS ON DISK:

C:\SecureVault\
├── SecureVault.exe (the app)
├── libs/ (libVLC, SkiaSharp, etc)
├── vaults/
│   ├── my.vault (0-200GB, main vault)
│   ├── my.vault2 (200-400GB, overflow)
│   ├── my.vault3 (400-600GB, if needed)
│   ├── my.vault.manifest (links all parts)
│   └── my.vault.lock 🆕 (single-writer lock file, deleted on clean close)
│
AppData/Local/SecureVault/
└── cache/
    └── {uuid}.cache (encrypted cache, local only)

WHAT GETS BACKED UP:
backup_location/
├── my.vault (or split parts)
├── my.vault2 (if exists)
├── my.vault3 (if exists)
├── my.vault.manifest (links all parts)
├── my.vault.sha256 (integrity hash)
└── my.vault2.sha256 (integrity hash)

WHAT DOESN'T GET BACKED UP:
├── cache files (rebuilt automatically)
├── lock file (session-only, never backed up)
├── app itself (reinstall from website/store)
└── anything else (nothing else exists)

WHAT YOU NEED TO RESTORE EVERYTHING:
1. The .vault files (all of them if multi-vault)
2. Your password OR 24-word recovery key
3. The app (download and install)

THAT'S IT. (And if you have neither password nor recovery key,
that's it in the other sense too — see Threat Model.)
```

---

## Data Flow Summary

```
ADDING A FILE:
User drops file
  → acquire single-writer lock (A20)
  → detect type and category
  → select compression (or none)
  → read in 1MB chunks
  → compress each chunk (if applicable)
  → hash plaintext (SHA-256, for dedup + integrity baseline)
  → Fast Obfuscation (XOR, unique per-file keystream) OR
    Secure Mode (AES-256-GCM, unique nonce + per-chunk auth tag)
  → Reed-Solomon encode each chunk (default level)
  → write chunks to vault
  → generate thumbnail (background)
  → update encrypted index
  → update cache
  → release lock, display in library

VIEWING A FILE:
User double-clicks
  → look up entry in index
  → determine which integrated app to use
  → read chunks from vault (streaming for large files)
  → Reed-Solomon decode (auto-fix if needed, re-verify before commit)
  → verify per-chunk auth tag (Secure Mode) or deobfuscate (Fast Mode)
  → decompress
  → feed to integrated app (gallery/player/notes/pdf)
  → all in memory, nothing on disk

BACKUP:
User clicks "Backup"
  → choose destination
  → choose single file or split
  → compute per-part hashes
  → copy/split vault files
  → write manifest + hash files
  → verify backup

RESTORE:
User clicks "Restore"
  → select manifest or vault file
  → verify all parts present
  → verify per-part hashes
  → join if split
  → verify whole-file hash
  → enter password
  → everything works
```

---

## Key Management Detail 🆕 NEW

```
                    ┌─────────────┐
                    │ Master Key  │  (random, generated once at vault creation)
                    └──────┬──────┘
           ┌───────────────┼────────────────┐
           │                                │
   wrapped by                       wrapped by
   Argon2id(password)                HKDF(recovery seed)
           │                                │
   ┌───────▼────────┐             ┌─────────▼────────┐
   │ Wrapped Key A   │             │ Wrapped Key B    │
   │ (in header)     │             │ (in header)      │
   └─────────────────┘             └──────────────────┘

Either path independently recovers the Master Key.
Changing your password only re-wraps Key A — Key B (recovery)
is untouched, and no file data is re-processed (A04).

Master Key derives, via HKDF with distinct context strings:
  → Index encryption key   (AES-256-GCM)
  → Secure Mode file key   (AES-256-GCM, per-chunk nonces)
  → Fast Obfuscation key   (XOR keystream, unique per file)
  → Header HMAC key        (tamper detection)
```

---

## Technology Stack

```
┌─────────────────────────┬────────────────────────────────┐
│ Component                │ Technology                    │
├─────────────────────────┼────────────────────────────────┤
│ UI Framework              │ WinUI 3 (Windows App SDK)     │
│ Language                  │ C# (.NET 8)                   │
│ Video/Audio playback      │ LibVLCSharp + LibVLC (LGPL —   │
│                           │ dynamic link, don't statically │
│                           │ embed, to stay license-clean)  │
│ Image rendering           │ SkiaSharp                      │
│ Image formats (HEIC,RAW)  │ Magick.NET (Apache 2.0)        │
│ PDF rendering             │ PdfiumCore                     │
│ Markdown parsing          │ Markdig                        │
│ Archive reading           │ SharpCompress (MIT)             │
│ Password hashing          │ Konscious.Security (Argon2)     │
│ Encryption                │ System.Security.Cryptography    │
│ Fast compression          │ K4os.Compression.LZ4            │
│ Better compression        │ Brotli (built-in .NET)          │
│ Reed-Solomon               │ STH1123.ReedSolomon (library —  │
│                           │ 🔧 no custom implementation)     │
│ Media metadata             │ TagLibSharp                     │
│ MVVM helpers               │ CommunityToolkit.WinUI          │
│ Syntax highlighting        │ TextMate or AvalonEdit           │
│ Rich text editing          │ WinUI RichEditBox                │
└─────────────────────────┴────────────────────────────────┘

🔧 Licensing note: LibVLC is LGPL. Fine to use as long as it's
   dynamically linked (a separate DLL you call into), which is how
   LibVLCSharp works by default — just don't statically link libVLC
   into your exe. Worth a 10-minute read of LibVLCSharp's licensing
   docs before you ship publicly.
```

---

## Implementation Order (Suggested)

```
PHASE 1: FOUNDATION (Must work first)
──────────────────────────────────────
A01-A04, A19-A21     Vault create, unlock, lock, change password,
                      dual key-wrap, single-writer lock
B01-B09, B22a, B27    File format, chunks, per-chunk AEAD, RS (library)
B15-B19               Index system (dual index)
C01, C05, C06         Add file (streaming, plaintext hash)
C08                   Delete file
C16-C18               Read file (memory + stream)
M01-M06, M14, M18     Core security (review against VeraCrypt design)
F01-F09, F13          Integrity and atomic writes, per-chunk auth

PHASE 2: BASIC UI + FILE OPS
─────────────────────────────
N01-N09, N23          Login screen + main library + recovery-key gate
C02-C04               Multi-file add, drag-drop
C10-C15               Rename, move, export
D01-D06               Folders, categories, tags, favorites
D08-D15               Search and sort
E01-E07               Cache system + progressive loading

PHASE 3: INTEGRATED APPS
─────────────────────────
H01-H06               Gallery (basic viewing)
I01-I09               Video player (basic playback)
I02                   Audio player
J01-J08               Notes (basic editing)
L01-L06               PDF viewer (basic)

PHASE 4: ADVANCED FEATURES
───────────────────────────
A05-A09               Password hint, recovery key, auto-lock
E08-E20               Thumbnails, prefetch, parallel processing
H07-H18               Gallery advanced features
I10-I25               Player advanced features
J09-J20               Notes advanced features
L07-L11               PDF advanced features
K01-K13               File manager + archives

PHASE 5: BACKUP AND MULTI-VAULT
────────────────────────────────
G01-G16               Backup and restore system
O01-O12               Multi-vault system
B23-B26               Vault limits and linking

PHASE 6: POLISH
────────────────
N10-N22               Advanced UI features
M07-M17               Advanced security
F10-F16               Recovery mode and advanced integrity
D17-D21               Albums, playlists, timeline view
C21-C24               Duplicate detection, compaction, clipboard

TOTAL FEATURES: ~205 items (5 added, 0 removed)
```

**One honest note on scope, separate from the technical fixes above:** you told me
you're okay with learning as you go rather than needing full mastery first — and given
your track record, I believe that. But even with AI pairing at every step, Phase 1 alone
(custom format + per-chunk crypto + RS + dual index + atomic writes) is a real multi-month
build before you have anything double-clickable. If you want a morale win, it's worth
considering a stripped Phase 1+2+3 slice — single vault, Secure Mode only, gallery viewer
only — as a working milestone before chasing the rest of Phase 3 onward. Not a scope cut,
just a suggested checkpoint.

---

## Quick Reference Card

```
╔════════════════════════════════════════════════════════════╗
║              SECUREVAULT QUICK REFERENCE (v2)                ║
╠════════════════════════════════════════════════════════════╣
║                                                                ║
║ WHAT:     Personal encrypted file library                     ║
║ WHO:      Single user (you)                                   ║
║ WHERE:    Windows desktop app                                 ║
║ FORMAT:   .vault proprietary binary files                     ║
║ LIMIT:    200GB per vault file, unlimited total                ║
║ PROTECT:  Index    = always AES-256-GCM                        ║
║           Files    = Fast Obfuscation (default, NOT encryption)║
║           Files    = Secure Mode AES-256-GCM (opt-in/all)      ║
║ RESILIENCE: Reed-Solomon, one default level (~12% overhead)    ║
║ BACKUP:   Single file or split parts + hash verification       ║
║ RESTORE:  Vault file + password = everything                  ║
║ RECOVER:  24-word recovery key (independent key-wrap, not a    ║
║           password derivative)                                 ║
║ IF BOTH LOST: data is permanently unrecoverable — by design    ║
║ APPS:     Gallery, Video/Audio Player, Notes, PDF, Files        ║
║ CACHE:    Encrypted local cache for instant startup             ║
║ UI:       WinUI 3, Fluent Design, dark/light themes             ║
║ LOCKING:  Single-writer lock file — one open instance at a time║
║                                                                ║
║ SECURITY MODEL:                                                ║
║ Password ──Argon2id──┐                                         ║
║                       ├──► Master Key                          ║
║ Recovery key ──HKDF───┘                                        ║
║ Master Key → AES-GCM → Encrypted Index                          ║
║ Master Key → HKDF → XOR Key (per-file) → Fast Obfuscation        ║
║ Master Key → AES-GCM → per-chunk nonce+tag → Secure Mode          ║
║                                                                ║
║ RESILIENCE MODEL:                                              ║
║ Every chunk → Reed-Solomon parity (library) → verified repair   ║
║ Every chunk → own auth tag → safe random-access seeking          ║
║ Dual index → survives partial file loss                          ║
║ Atomic writes → survives power failure                            ║
║ Single-writer lock → survives concurrent-access corruption         ║
║ Recovery scan → last resort file finding                            ║
║                                                                ║
║ PERFORMANCE MODEL:                                              ║
║ Cache → instant startup after first time                          ║
║ Chunked I/O → seek anywhere in large files                          ║
║ Stream → play 50GB video with 4MB RAM                                 ║
║ Prefetch → smooth playback and gallery browsing                       ║
║ Smart compress → skip already compressed formats                       ║
║ Virtualized UI → smooth with 100K+ files                                ║
║                                                                ║
╚════════════════════════════════════════════════════════════╝
```

**This document is the complete revised specification. All original features are
preserved; five categories of technical gap are now fixed or explicitly deferred with
a stated reason. Reference this as we build.**
