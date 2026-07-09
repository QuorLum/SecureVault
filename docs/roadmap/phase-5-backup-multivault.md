# Phase 5: Backup & Multi-Vault — Implementation Roadmap

> **Branch:** `phase-5/backup-multivault`
>
> **Scope:** Full backup/restore system, split backup, multi-vault chain (200GB limit +
> overflow), cross-vault references, vault chain health.
>
> **Feature IDs:** G01–G16, O01–O12, B23–B26
>
> **Prior Phases:** Phase 1–4 must be complete.

---

## Build Order & Dependency Graph

```
Level 0 (depends only on Phase 1–4):
  G01  Single-file backup
  G06  .vault.sha256 companion file
  B23  200GB per vault file limit

Level 1 (depends on Level 0):
  G04  Per-part SHA-256 verification
  G05  Whole-file SHA-256 verification
  G10  Backup verification
  G11  Vault self-contained
  G12  No external dependencies
  G13  Any app version opens any vault version
  G14  Format version upgrade

Level 2 (depends on Level 1):
  G02  Split backup (50GB parts)
  G03  Backup manifest file
  G07  Restore from single file
  G09  Re-download corrupted part

Level 3 (depends on Level 2):
  G08  Restore from split parts
  G15  Multi-vault verification
  G16  List files per vault part

Level 4 (depends on B23):
  O01  200GB limit enforcement
  O02  Automatic overflow to .vault2
  O03  Overflow to .vault3+
  O04  Master vault with global index
  O05  Per-vault-file local index
  B24  Multi-vault linking
  B25  Cross-vault verification
  B26  Vault manifest

Level 5 (depends on Level 4):
  O06  Cross-vault file reference
  O07  Vault chain manifest
  O08  Missing vault detection
  O09  Graceful degradation
  O10  Per-vault integrity check
  O11  Move files between vault parts
  O12  Vault chain health dashboard
```

---

## G01, G06 — Single-File Backup + Hash

### Module & File Placement

- **File:** `src/SecureVault.Core/Backup/BackupService.cs`
- **File:** `src/SecureVault.Core/Backup/HashVerifier.cs`
- **Dependencies:** VaultManager (file lock awareness), System.Security.Cryptography
- **Depended on by:** G02 (split backup), G07 (restore)

### Function Signatures

```csharp
public sealed class BackupService
    BackupService(VaultManager vault)

    async Task BackupSingle(string destPath, IProgress<long>? progress, CancellationToken ct)
    // 1. Ensure vault is in a consistent state (no pending writes)
    // 2. Open vault file as read-only
    // 3. Copy to destPath using buffered streaming (64KB blocks)
    // 4. Report progress (bytes copied / total)
    // 5. Compute SHA-256 of the copied file
    // 6. Write destPath + ".sha256" with hash + filename (G06)
    //    Format: "<hex_hash>  <filename>\n"  (sha256sum-compatible format)
    // 7. Verify: re-read destPath, compute SHA-256, compare with written hash

public sealed class HashVerifier
    static async Task<string> ComputeFileHash(string filePath, IProgress<long>? progress, CancellationToken ct)
    // 1. Open file read-only
    // 2. SHA256 incremental hash, 64KB buffer
    // 3. Return hex string

    static async Task<bool> VerifyHashFile(string hashFilePath)
    // 1. Read .sha256 file
    // 2. Parse hash and filename
    // 3. Compute hash of referenced file
    // 4. Compare
```

### Exact Library Calls

- `System.Security.Cryptography.IncrementalHash.CreateHash(HashAlgorithmName.SHA256)` — streaming hash
- `File.Copy()` is NOT used — manual streaming copy for progress reporting

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Backup creates copy | Backup vault to dest | Dest file is byte-identical to source |
| SHA-256 file created | Backup | .sha256 file exists with correct hash |
| Verify succeeds | Valid backup | VerifyHashFile returns true |
| Verify fails | Corrupt backup (flip 1 byte) | VerifyHashFile returns false |
| Progress reports | Backup 10MB vault | Progress reaches 100% |

### Verification Checklist

1. ✅ Backup is a byte-identical copy — diff original and backup shows no differences
2. ✅ .sha256 file format is compatible with `sha256sum --check`
3. ✅ Backup does not require the vault to be unlocked (copies raw encrypted bytes)

---

## G02, G03 — Split Backup + Manifest

### Module & File Placement

- **File:** `src/SecureVault.Core/Backup/SplitBackupService.cs`
- **File:** `src/SecureVault.Core/Backup/BackupManifest.cs`
- **Dependencies:** BackupService, HashVerifier

### Data Structures

```
BackupManifest (G03, JSON format):
{
  "vault_name": "my",
  "vault_uuid": "...",
  "format_version": 1,
  "created": "2024-01-15T10:30:00Z",
  "total_size": 150000000000,
  "split_size": 50000000000,
  "parts": [
    {
      "filename": "my.vault.part001",
      "index": 0,
      "offset": 0,
      "size": 50000000000,
      "sha256": "abc123..."
    },
    {
      "filename": "my.vault.part002",
      "index": 1,
      "offset": 50000000000,
      "size": 50000000000,
      "sha256": "def456..."
    },
    {
      "filename": "my.vault.part003",
      "index": 2,
      "offset": 100000000000,
      "size": 50000000000,
      "sha256": "ghi789..."
    }
  ],
  "whole_file_sha256": "xyz000..."
}

File: my.vault.manifest
```

### Function Signatures

```csharp
public sealed class SplitBackupService
    SplitBackupService(VaultManager vault)

    async Task BackupSplit(string destFolder, long partSizeBytes, IProgress<FileAddProgress>? progress, CancellationToken ct)
    // 1. Open vault file read-only
    // 2. Calculate number of parts = ceil(vaultSize / partSizeBytes)
    // 3. For each part:
    //    a. Create destFolder/vaultName.part{NNN} (3-digit padded)
    //    b. Copy partSizeBytes from vault (or remainder for last part)
    //    c. Compute SHA-256 of the part (G04)
    // 4. Compute whole-file SHA-256 (G05)
    // 5. Create BackupManifest JSON
    // 6. Write manifest to destFolder/vaultName.vault.manifest

public sealed class BackupManifest
    string VaultName { get; }
    Guid VaultUUID { get; }
    int FormatVersion { get; }
    DateTime Created { get; }
    long TotalSize { get; }
    long SplitSize { get; }
    List<ManifestPart> Parts { get; }
    string WholeFileSHA256 { get; }

    void SaveToFile(string path)
    static BackupManifest LoadFromFile(string path)
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Split into 3 parts | 150MB vault, 50MB part size | 3 part files + manifest |
| Part sizes correct | 150MB vault, 50MB parts | Each part is 50MB (last may differ) |
| Per-part hashes | Verify each part | All match manifest |
| Whole-file hash | Concatenate parts, hash | Matches manifest whole_file_sha256 |
| Manifest valid JSON | Read manifest | Parses correctly with all fields |

### Verification Checklist

1. ✅ Parts are numbered .part001, .part002, etc. (3-digit padded)
2. ✅ Concatenating all parts in order produces the original vault file
3. ✅ Manifest contains per-part AND whole-file SHA-256

---

## G07, G08 — Restore from Single/Split

### Module & File Placement

- **File:** `src/SecureVault.Core/Backup/RestoreService.cs`
- **Dependencies:** BackupManifest, HashVerifier

### Function Signatures

```csharp
public sealed class RestoreService
    async Task RestoreSingle(string backupPath, string destPath, IProgress<long>? progress, CancellationToken ct)
    // 1. If .sha256 companion exists, verify hash first
    // 2. Copy backup to destPath (streaming)
    // 3. Verify copy matches source hash

    async Task RestoreSplit(string manifestPath, string destPath, IProgress<FileAddProgress>? progress, CancellationToken ct)
    // 1. Load BackupManifest
    // 2. Verify all parts are present (G08)
    //    — if missing, report which parts are needed (G09)
    // 3. Verify per-part SHA-256 (G04)
    //    — if a part fails, report which specific part to re-download (G09)
    // 4. Join parts into single vault file at destPath
    // 5. Verify whole-file SHA-256 (G05)
    // 6. Done — user can now unlock the restored vault

    MissingPartsReport CheckParts(string manifestPath)
    // Return list of missing or corrupted parts
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Restore single | Valid backup | Restored vault opens with correct password |
| Restore split | 3 valid parts + manifest | Joined vault opens correctly |
| Missing part | Remove part 2, try restore | Reports "part002 missing" |
| Corrupted part | Corrupt part 2 | Reports "part002 hash mismatch" |
| Re-download part | Replace corrupted part 2 with fresh copy | Restore succeeds |

### Verification Checklist

1. ✅ Restored vault is functionally identical to original — all files readable
2. ✅ Missing/corrupted parts are reported with specific filenames, not generic errors
3. ✅ Restore requires ~2x vault size free space during join (surface in UI)

---

## G10–G14 — Backup Verification & Format Compatibility

### Module & File Placement

- **File:** `src/SecureVault.Core/Backup/BackupVerifier.cs` (G10)
- **File:** `src/SecureVault.Core/Format/FormatUpgrader.cs` (G14)

### Function Signatures

```csharp
public sealed class BackupVerifier
    async Task<BackupHealthReport> VerifyBackup(string backupPathOrManifest, IProgress<long>? progress, CancellationToken ct)
    // 1. If manifest: verify all parts + whole file hash
    // 2. If single file: verify .sha256 companion if exists
    // 3. Try reading vault header (without unlocking)
    // 4. Report: { IsComplete, IsHashValid, FormatVersion, VaultUUID, FileCount (if readable) }

public sealed class FormatUpgrader
    static bool NeedsUpgrade(VaultHeader header)
    // Compare header.FormatVersion with current version

    async Task Upgrade(string vaultPath, CancellationToken ct)
    // 1. Read vault with old format reader
    // 2. Write to new temp vault in current format
    // 3. Keep old vault as .vault.backup-v{old}
    // 4. Rename new vault to original name
    // G13: old format readers are kept in codebase for backward compatibility
```

### Verification Checklist

1. ✅ Backup verification works without the vault password (it checks file integrity, not contents)
2. ✅ Format upgrade keeps the old vault as a backup before replacing
3. ✅ The app can read FormatVersion=1 vaults even after future format changes

---

## G15, G16 — Multi-Vault Verification & File Listing

### Function Signatures

```csharp
// G15: cross-check .vault + .vault2
    async Task<MultiVaultHealthReport> VerifyMultiVault(string manifestPath)
    // 1. Load vault chain manifest
    // 2. For each vault file: verify existence + hash
    // 3. Cross-reference: global index should reference files in present vaults only

// G16: list files per vault part
    Dictionary<string, IReadOnlyList<string>> ListFilesPerPart(string manifestPath, string password)
    // 1. Unlock vault
    // 2. For each file in index: determine which vault part it's stored in
    // 3. Return mapping: vault filename → list of contained filenames
```

---

## O01–O03 — 200GB Limit & Automatic Overflow

### Module & File Placement

- **File:** `src/SecureVault.Core/MultiVault/VaultChainManager.cs`
- **File:** `src/SecureVault.Core/MultiVault/VaultPartAllocator.cs`
- **Dependencies:** VaultManager (Phase 1), B23 (limit constant)

### Data Structures

```
VaultChain
  MasterVaultPath   : string          (path to .vault file)
  Parts             : List<VaultPart>
  GlobalIndex       : VaultIndex       (in master .vault, references ALL files)

VaultPart
  PartIndex         : int              (0 = .vault, 1 = .vault2, etc.)
  FilePath          : string
  CurrentSize       : long
  MaxSize           : long             (200GB = 214_748_364_800 bytes)
  LocalIndex        : VaultIndex       (files stored in THIS part only)
```

### Function Signatures

```csharp
public sealed class VaultChainManager
    VaultChainManager(string masterVaultPath)

    VaultPart GetWriteTarget()
    // 1. Check current (last) vault part size
    // 2. If size < 200GB: return current part
    // 3. If size >= 200GB: create new part (.vault{N+1})
    //    a. Initialize with header (same master key, same UUID base)
    //    b. Create local index
    //    c. Return new part

    VaultPart GetPartForFile(Guid fileGuid)
    // Look up in global index which part stores this file's chunks

    async Task MoveFileBetweenParts(Guid fileGuid, int targetPartIndex, IProgress<long>? progress, CancellationToken ct)
    // 1. Read file chunks from source part
    // 2. Write to target part
    // 3. Update global and local indices
    // 4. Mark old chunks as free in source part
```

### Exact Library Calls

- File naming convention: `.vault`, `.vault2`, `.vault3`, etc.
- Each part shares the same master key (wrapped in the master .vault header)
- Only the master .vault has the password/recovery key-wrap blobs

⚠️ **OPEN QUESTION: Secondary vault part headers**
Should .vault2, .vault3 files have their own full headers with key-wrap data, or minimal headers referencing the master?
1. **Full headers (independent):** Each part can be unlocked independently, but key-wrap duplication means password change must update all parts.
2. **Minimal headers (dependent):** Parts only have a UUID + part index + local index pointer. Unlock requires the master .vault. Simpler, but parts are useless without the master.

**Recommendation:** Option 2 (minimal headers). The master vault is always required, and key management stays centralized. This matches the vision doc's statement that the master vault "contains global index of ALL files" (O04).

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Under limit | Add files totaling 100GB | All in .vault, no .vault2 created |
| Overflow | Add files past 200GB | .vault2 created, new files go there |
| File in part 2 | Read file stored in .vault2 | Reads correctly via VaultChainManager |
| Move between parts | Move file from .vault to .vault2 | File accessible in .vault2, freed in .vault |
| Global index | 3 vault parts | Master .vault index references files in all 3 parts |

### Verification Checklist

1. ✅ No single vault file exceeds 200GB (B23)
2. ✅ Overflow creates .vault2 (not .vault.002 or other naming)
3. ✅ All parts share the same master key — password change in master updates all access

---

## O04–O07 — Master/Local Index, Cross-Vault References, Manifest

### Module & File Placement

- **File:** `src/SecureVault.Core/MultiVault/VaultChainIndex.cs`
- **File:** `src/SecureVault.Core/MultiVault/VaultManifest.cs`

### Data Structures

```
VaultManifest (my.vault.manifest, JSON):
{
  "vault_name": "my",
  "vault_uuid": "...",
  "format_version": 1,
  "parts": [
    { "filename": "my.vault", "index": 0, "size": 200000000000 },
    { "filename": "my.vault2", "index": 1, "size": 150000000000 }
  ],
  "total_files": 5000,
  "total_size": 350000000000,
  "last_modified": "2024-06-15T..."
}
```

### Function Signatures

```csharp
public sealed class VaultChainIndex
    VaultIndex GlobalIndex { get; }     // in master .vault (O04)
    Dictionary<int, VaultIndex> LocalIndices { get; }  // per part (O05)

    IndexEntry? FindFile(Guid fileGuid)
    // Search global index → returns entry with PartIndex field

    int GetPartForChunks(Guid fileGuid)
    // Return which vault part stores this file's data chunks (O06)

    void SyncLocalToGlobal()
    // Ensure global index has all entries from all local indices

public sealed class VaultManifest
    void Save(string path)
    static VaultManifest Load(string path)
    void Update(VaultChainManager chain)
    // Regenerate manifest from current chain state (O07)
```

---

## O08, O09 — Missing Vault Detection & Graceful Degradation

### Module & File Placement

- **File:** `src/SecureVault.Core/MultiVault/VaultChainHealth.cs`

### Function Signatures

```csharp
public sealed class VaultChainHealth
    VaultChainHealth(VaultManifest manifest)

    MissingVaultReport CheckAvailability()
    // 1. For each part in manifest: check if file exists at expected path
    // 2. Return list of missing parts

    IReadOnlyList<IndexEntry> GetAvailableFiles(VaultChainManager chain)
    // 1. Get all files from global index
    // 2. Filter to only files whose chunks are in available vault parts
    // 3. Return available files (O09)

    IReadOnlyList<IndexEntry> GetUnavailableFiles(VaultChainManager chain)
    // Files whose chunks are in missing vault parts
    // UI shows these as greyed-out / "Vault part missing" (O09)
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| All present | .vault + .vault2 both exist | No missing parts |
| Missing part | Remove .vault2 | CheckAvailability reports .vault2 missing |
| Graceful degradation | .vault2 missing | Files in .vault accessible, .vault2 files greyed out |
| Restored part | Re-add .vault2 | All files accessible again |

### Verification Checklist

1. ✅ Missing vault parts do NOT crash the app — it degrades gracefully
2. ✅ Files in missing parts are shown with clear "unavailable" status in UI
3. ✅ Re-adding a missing part makes its files accessible without re-importing

---

## O10–O12 — Per-Vault Integrity, Move Files, Health Dashboard

### Function Signatures

```csharp
// O10: Per-vault integrity check
    async Task<IntegrityReport> CheckPartIntegrity(int partIndex, IProgress<long>? progress, CancellationToken ct)
    // Run F04 integrity check on a specific vault part only

// O11: Move files between parts
    // Already defined in VaultChainManager.MoveFileBetweenParts above

// O12: Health dashboard
    VaultChainHealthReport GetHealthReport()
    // 1. For each part: file count, size, integrity status, last check date
    // 2. Overall: total files, total size, missing parts, cross-check status
    // Returns data for UI dashboard display
```

---

## B24–B26 — Multi-Vault Linking & Verification

### Module & File Placement

- Part of `VaultChainManager` and `VaultManifest` (above)

### Data Structures

```
B24: Multi-vault linking
  - Each part header contains the master vault UUID
  - Parts are linked by sharing the UUID

B25: Cross-vault verification
  - Global index stores ChunkHash for each file
  - Verification reads a file from its vault part and checks the hash

B26: Vault manifest (already defined in O07)
```

### Verification Checklist

1. ✅ B24: Each vault part header references the same master UUID
2. ✅ B25: Cross-vault verification detects files whose data doesn't match the global index hash
3. ✅ B26: Manifest is auto-updated when vault chain state changes

---

## Source File Summary

```
src/SecureVault.Core/
├── Backup/
│   ├── BackupService.cs              (G01)
│   ├── SplitBackupService.cs         (G02, G03)
│   ├── BackupManifest.cs             (G03)
│   ├── RestoreService.cs             (G07, G08)
│   ├── HashVerifier.cs               (G04-G06)
│   ├── BackupVerifier.cs             (G10)
│   └── FormatUpgrader.cs             (G14)
├── MultiVault/
│   ├── VaultChainManager.cs          (O01-O03)
│   ├── VaultPartAllocator.cs         (O02)
│   ├── VaultChainIndex.cs            (O04-O07)
│   ├── VaultManifest.cs              (O07, B26)
│   └── VaultChainHealth.cs           (O08-O12, B25)
```

## Test Vector Files

```
tests/vectors/
├── backup-manifest-schema.json       (G03 — valid manifest structure)
└── sha256-companion.json             (G06 — sha256sum-compatible format verification)
```

## Branch & PR

- **Branch:** `phase-5/backup-multivault`
- **PR Title:** "Phase 5: Backup System + Multi-Vault Chain"
- **PR Description:**

```
Implements full backup/restore and multi-vault support.

## Backup (G01–G16)
- Single-file backup with SHA-256 companion file
- Split backup into 50GB parts for cloud upload limits
- JSON manifest with per-part and whole-file hashes
- Restore from single file or split parts
- Re-download specific corrupted part
- Backup verification without unlocking
- Format version upgrade with old-vault backup

## Multi-Vault (O01–O12)
- 200GB per vault file limit with automatic overflow to .vault2, .vault3
- Master vault contains global index, each part has local index
- Cross-vault file references (file in .vault2 accessible via master index)
- Missing vault detection with graceful degradation
- Move files between vault parts
- Vault chain health dashboard
- Per-vault integrity check

## Design decisions
- Split parts use .part001/.part002 naming (3-digit padded)
- Secondary vault parts have minimal headers (no key-wrap duplication)
- Password change only updates the master .vault header
- Vault files are self-contained — no registry, no certificates needed
```

## CONTRIBUTING Note for Phase 5

```
CONTRIBUTING — Phase 5 (Backup & Multi-Vault)

1. Backup operations must never require the vault password.
   Backups copy encrypted bytes — no decryption needed.

2. Split parts must be joinable with simple concatenation.
   Don't add part-specific headers that would break cat/copy /b joining.

3. The vault manifest (.vault.manifest) is NOT encrypted — it contains
   metadata (filenames of parts, hashes) but no file content or keys.
   Don't put sensitive data in the manifest.

4. Multi-vault operations must hold locks on ALL affected vault parts
   during cross-part moves to prevent corruption.
```

## STATUS.md Entries for Phase 5

```
G01 🔨 Single-file backup
G02 🔨 Split backup (50GB parts)
G03 🔨 Backup manifest (JSON)
G04 🔨 Per-part SHA-256
G05 🔨 Whole-file SHA-256
G06 🔨 .vault.sha256 companion
G07 🔨 Restore from single file
G08 🔨 Restore from split parts
G09 🔨 Re-download corrupted part
G10 🔨 Backup verification
G11 🔨 Vault self-contained
G12 🔨 No external dependencies
G13 🔨 Any version opens any vault
G14 🔨 Format version upgrade
G15 🔨 Multi-vault verification
G16 🔨 List files per vault part
O01 🔨 200GB per vault limit
O02 🔨 Automatic overflow to .vault2
O03 🔨 Overflow to .vault3+
O04 🔨 Master vault global index
O05 🔨 Per-vault local index
O06 🔨 Cross-vault file reference
O07 🔨 Vault chain manifest
O08 🔨 Missing vault detection
O09 🔨 Graceful degradation
O10 🔨 Per-vault integrity check
O11 🔨 Move files between parts
O12 🔨 Vault chain health dashboard
B23 🔨 200GB per vault limit
B24 🔨 Multi-vault linking
B25 🔨 Cross-vault verification
B26 🔨 Vault manifest
```
