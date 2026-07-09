# Phase 1: Foundation — Implementation Roadmap

> **Branch:** `phase-1/vault-core`
>
> **Scope:** Vault create, unlock, lock, change password, dual key-wrap, single-writer lock,
> file format, chunks, per-chunk AEAD, Reed-Solomon, index system, add/delete/read file,
> core security, integrity, atomic writes.
>
> **Feature IDs:** A01–A04, A12–A16, A19–A21, B01–B09, B15–B19, B20–B22a, B27,
> C01, C05, C06, C08, C16–C18, M01–M06, M14, M18, F01–F09, F11–F13

---

## Build Order & Dependency Graph

```
Level 0 (no deps):
  A21  Key zeroing primitives (pinned buffers)
  A20  Single-writer file lock
  M18  VeraCrypt design study (doc-only, no code)

Level 1 (depends on Level 0):
  M01  Argon2id key derivation
  A12  Master key architecture
  A13  Argon2id integration

Level 2 (depends on Level 1):
  A19  Dual key-wrap (password + recovery)
  A01  Create new vault (needs key-wrap)
  M14  XOR obfuscation keystream (HKDF-derived, per-file)

Level 3 (depends on Level 2):
  A02  Unlock vault with password
  A03  Lock vault (zero keys)
  A04  Change password
  B01  Vault binary format (header)
  B18  Vault header with encrypted section
  B20  Random prefix in header
  B21  XOR-masked magic bytes

Level 4 (depends on Level 3):
  B02  Chunked file storage
  B03  Chunk index (offset, size, CRC32, auth tag)
  B04  64-bit offsets
  B05  Block header per file
  B06  Block footer per file
  B22  Per-chunk unique nonce
  B22a Per-chunk AEAD unit

Level 5 (depends on Level 4):
  A14  AES-256-GCM for index encryption
  A15  Fast Obfuscation Mode (XOR keystream)
  A16  AES-256-GCM per-file encryption (Secure Mode)
  B07  Reed-Solomon error correction per chunk
  B08  One default RS level
  B09  RS level config (deferred)
  B27  RS uses STH1123.ReedSolomon library

Level 6 (depends on Level 5):
  B15  Primary index (encrypted, RS-protected)
  B16  Backup index
  B17  Floating index with pointer chain
  B19  Vault footer with backup pointers
  F01  RS error correction on every chunk
  F02  Auto-repair corrupted chunks
  F07  Atomic writes (temp file + rename)
  F08  Write-ahead for index updates
  F09  Block isolation
  F11  Per-chunk CRC32
  F12  Per-file SHA-256
  F13  AES-GCM auth tag per chunk

Level 7 (depends on Level 6):
  C01  Add single file to vault
  C05  Streaming file addition
  C06  SHA-256 checksum on plaintext
  C08  Delete file from vault
  C16  Read file into memory
  C17  Read file as stream
  C18  VaultFileStream with chunk-based seeking
  M02  AES-256-GCM for Secure Mode
  M03  Unique nonce per chunk
  M04  Master key zeroed on lock
  M05  Obfuscation key zeroed on lock
  M06  No decrypted data written to disk
```

---

## A21 — Key Zeroing Primitives (Pinned Buffers)

### Module & File Placement

- **File:** `src/SecureVault.Core/Crypto/SecureBuffer.cs`
- **Dependencies:** None (Level 0)
- **Depended on by:** Everything in the crypto layer

### Data Structures

```
SecureBuffer : IDisposable
  Fields:
    _handle   : GCHandle           (pinned handle to byte[])
    _buffer   : byte[]             (pinned, never moved by GC)
    _length   : int
    _disposed : bool
```

### Function Signatures

```csharp
public sealed class SecureBuffer : IDisposable
    SecureBuffer(int length)
    // 1. Allocate byte[] of `length`
    // 2. Pin it via GCHandle.Alloc(buffer, GCHandleType.Pinned)
    // 3. Store handle and buffer reference

    ReadOnlySpan<byte> AsReadOnlySpan()
    // Return span over pinned buffer

    Span<byte> AsSpan()
    // Return writable span over pinned buffer

    void Dispose()
    // 1. Call CryptographicOperations.ZeroMemory(buffer)
    // 2. Free GCHandle
    // 3. Set _disposed = true
```

### Exact Library Calls

- `System.Runtime.InteropServices.GCHandle.Alloc(byte[], GCHandleType.Pinned)` — pins buffer
- `System.Security.Cryptography.CryptographicOperations.ZeroMemory(Span<byte>)` — zeros content
- `GCHandle.Free()` — releases pin

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Create and read | `new SecureBuffer(32)`, write `0xFF` to all bytes | `AsReadOnlySpan()` returns 32 bytes of `0xFF` |
| Dispose zeros | Create buffer with `0xFF`, dispose | After dispose, attempting `AsSpan()` throws `ObjectDisposedException` |
| Double dispose safe | Dispose twice | No exception |

Test vector file: `tests/vectors/secure-buffer.json`

### Verification Checklist

1. ✅ Search the crypto module for `new byte[` — every key/derived-key allocation should use `SecureBuffer`, not raw `byte[]`
2. ✅ Search for `Array.Clear` or manual zeroing loops — should not appear; only `CryptographicOperations.ZeroMemory` is used
3. ✅ Run test: create `SecureBuffer(32)`, dispose, confirm `ObjectDisposedException` on access

---

## A20 — Single-Writer File Lock

### Module & File Placement

- **File:** `src/SecureVault.Core/IO/VaultFileLock.cs`
- **Dependencies:** None (Level 0)
- **Depended on by:** A01 (create vault), A02 (unlock vault)

### Data Structures

```
VaultFileLock : IDisposable
  Fields:
    _mutex    : Mutex              (named, system-wide)
    _lockFile : string             (path to .vault.lock file)
    _acquired : bool
```

### Function Signatures

```csharp
public sealed class VaultFileLock : IDisposable
    static VaultFileLock Acquire(string vaultPath)
    // 1. Compute mutex name: "SecureVault_" + SHA256(vaultPath)[0..16].ToHex()
    // 2. Try create named Mutex with that name
    // 3. Try WaitOne(timeout: 0) — if false, throw VaultAlreadyOpenException
    // 4. Write lock file at vaultPath + ".lock" with PID and timestamp
    // 5. Return VaultFileLock instance

    void Dispose()
    // 1. Delete .lock file if it exists
    // 2. ReleaseMutex()
    // 3. Dispose Mutex
```

### Exact Library Calls

- `new System.Threading.Mutex(false, name, out bool createdNew)` — named mutex
- `Mutex.WaitOne(TimeSpan.Zero)` — non-blocking acquire
- `Mutex.ReleaseMutex()` — release
- `File.WriteAllText(lockPath, $"{Process.GetCurrentProcess().Id}\n{DateTime.UtcNow:O}")` — lock file

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Acquire lock | `Acquire("test.vault")` | Returns `VaultFileLock`, `.lock` file exists |
| Double acquire fails | Acquire same path twice in same process | Second call throws `VaultAlreadyOpenException` |
| Release and re-acquire | Acquire, dispose, acquire again | Succeeds, no exception |
| Lock file cleaned up | Acquire, dispose | `.lock` file deleted |

Test vector file: N/A (behavioral test, no vectors)

### Verification Checklist

1. ✅ Search for `new FileStream(` with `FileAccess.Write` on vault files — every write path must be preceded by a lock acquire
2. ✅ Run test: open vault, try opening same vault in second process — should show error, not corrupt

---

## M01 / A12 / A13 — Argon2id Key Derivation + Master Key Architecture

### Module & File Placement

- **File:** `src/SecureVault.Core/Crypto/KeyDerivation.cs`
- **Dependencies:** A21 (SecureBuffer)
- **Depended on by:** A19, A01, A02, A04

### Data Structures

```
Argon2idParams
  Fields:
    MemoryCostKB  : int    = 262144    (256 MB)
    Iterations    : int    = 3
    Parallelism   : int    = 4         (match typical core count)
    SaltLength    : int    = 32        (bytes)
    OutputLength  : int    = 32        (bytes, for AES-256 key)
```

### Function Signatures

```csharp
public static class KeyDerivation
    static (SecureBuffer derivedKey, byte[] salt) DeriveFromPassword(
        string password,
        byte[]? existingSalt = null)
    // 1. If existingSalt is null, generate 32 random bytes via RandomNumberGenerator.Fill
    // 2. Create Konscious.Security.Cryptography.Argon2id instance
    // 3. Set parameters: MemorySize=262144, Iterations=3, DegreeOfParallelism=4
    // 4. Set password bytes (UTF-8 encoding)
    // 5. Set salt
    // 6. Call GetBytes(32) → copy into SecureBuffer
    // 7. Zero the intermediate byte[] from GetBytes
    // 8. Return (SecureBuffer, salt)
```

### Exact Library Calls

- `new Konscious.Security.Cryptography.Argon2id(passwordBytes)` — Argon2id instance
- `.MemorySize = 262144` — 256 MB memory cost
- `.Iterations = 3` — 3 passes
- `.DegreeOfParallelism = 4` — 4 lanes
- `.Salt = salt` — 32-byte random salt
- `.GetBytes(32)` — derive 32-byte key
- `System.Security.Cryptography.RandomNumberGenerator.Fill(Span<byte>)` — salt generation

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Deterministic derivation | password=`"test_password_123"`, salt=`0x00*32` (32 zero bytes) | Derived key is deterministic — record the exact output as a test vector |
| Same password, different salt | Same password, two random salts | Different derived keys |
| Output length | Any input | Derived key is exactly 32 bytes |

⚠️ **OPEN QUESTION: Argon2id memory cost — 64MB vs 128MB vs 256MB**
The vision doc says "at minimum 64MB, consider raising to 128–256MB." This roadmap specifies 256MB as the default since this is a desktop app. Tradeoffs:
- **256MB:** Best GPU/ASIC resistance, but unlock takes ~1-2s on a modern machine. Some low-RAM machines (8GB) might struggle.
- **128MB:** Good resistance, faster unlock (~0.5-1s). Safer for broader hardware.
- **64MB:** Minimum acceptable. Faster but weaker against GPU attacks.
**Recommendation:** 256MB with a fallback: if the system has < 16GB RAM, auto-reduce to 128MB. Store the actual params used in the vault header so any vault can be unlocked regardless.

Test vector file: `tests/vectors/argon2id-derivation.json`
```json
{
  "description": "Argon2id key derivation test vectors",
  "vectors": [
    {
      "password": "test_password_123",
      "salt_hex": "0000000000000000000000000000000000000000000000000000000000000000",
      "memory_kb": 262144,
      "iterations": 3,
      "parallelism": 4,
      "output_length": 32,
      "expected_key_hex": "TO_BE_COMPUTED_AT_IMPLEMENTATION_TIME"
    }
  ]
}
```

### Verification Checklist

1. ✅ Run the test vector — output must match `expected_key_hex` exactly
2. ✅ Search for `Argon2` in the codebase — `MemorySize` should never be less than 65536 (64MB)
3. ✅ Search for `Encoding.UTF8.GetBytes` near password handling — the byte[] must be zeroed after use
4. ✅ Confirm the Argon2id params (memory, iterations, parallelism) are stored in the vault header

---

## A19 — Dual Key-Wrap (Password + Recovery)

### Module & File Placement

- **File:** `src/SecureVault.Core/Crypto/KeyWrapping.cs`
- **Dependencies:** A21 (SecureBuffer), M01/A12/A13 (KeyDerivation)
- **Depended on by:** A01 (create vault), A02 (unlock), A04 (change password), A06/A07 (recovery)

### Data Structures

```
WrappedKeyPair (stored in vault header)
  Fields:
    PasswordSalt             : byte[32]     (Argon2id salt)
    PasswordWrappedKey       : byte[32+16]  (AES-256-GCM: 32 bytes ciphertext + 16 bytes tag)
    PasswordWrappedKeyNonce  : byte[12]     (AES-GCM nonce)
    RecoverySalt             : byte[32]     (HKDF salt for recovery key)
    RecoveryWrappedKey       : byte[32+16]  (AES-256-GCM: 32 bytes ciphertext + 16 bytes tag)
    RecoveryWrappedKeyNonce  : byte[12]     (AES-GCM nonce)

Total header space for key material: 32 + 48 + 12 + 32 + 48 + 12 = 184 bytes
```

### Function Signatures

```csharp
public static class KeyWrapping
    static WrappedKeyPair WrapMasterKey(
        SecureBuffer masterKey,
        string password,
        byte[] recoveryKeySeed)  // 32 bytes from BIP-39 mnemonic
    // 1. Derive passwordDerivedKey via KeyDerivation.DeriveFromPassword(password)
    // 2. Generate random 12-byte nonce for password wrap
    // 3. AES-256-GCM encrypt masterKey using passwordDerivedKey + nonce → ciphertext + tag
    // 4. Derive recoveryDerivedKey via HKDF(recoveryKeySeed, salt=random32, info="SecureVault-Recovery-v1")
    // 5. Generate random 12-byte nonce for recovery wrap
    // 6. AES-256-GCM encrypt masterKey using recoveryDerivedKey + nonce → ciphertext + tag
    // 7. Zero all derived keys
    // 8. Return WrappedKeyPair with all salts, nonces, and wrapped blobs

    static SecureBuffer UnwrapWithPassword(
        WrappedKeyPair wrapped,
        string password)
    // 1. Derive key via KeyDerivation.DeriveFromPassword(password, wrapped.PasswordSalt)
    // 2. AES-256-GCM decrypt wrapped.PasswordWrappedKey using derived key + nonce
    // 3. If auth tag fails → throw InvalidPasswordException
    // 4. Copy plaintext to SecureBuffer
    // 5. Zero derived key
    // 6. Return SecureBuffer containing master key

    static SecureBuffer UnwrapWithRecoveryKey(
        WrappedKeyPair wrapped,
        byte[] recoveryKeySeed)
    // 1. Derive key via HKDF(recoveryKeySeed, wrapped.RecoverySalt, "SecureVault-Recovery-v1")
    // 2. AES-256-GCM decrypt wrapped.RecoveryWrappedKey using derived key + nonce
    // 3. If auth tag fails → throw InvalidRecoveryKeyException
    // 4. Copy plaintext to SecureBuffer
    // 5. Zero derived key
    // 6. Return SecureBuffer containing master key

    static WrappedKeyPair RewrapPasswordOnly(
        WrappedKeyPair existing,
        SecureBuffer masterKey,
        string newPassword)
    // 1. Derive new passwordDerivedKey from newPassword with new salt
    // 2. Re-encrypt masterKey → new PasswordWrappedKey, new nonce
    // 3. Return new WrappedKeyPair with updated password blob, unchanged recovery blob
```

### Exact Library Calls

- `System.Security.Cryptography.AesGcm(derivedKey, tagSizeInBytes: 16)` — AES-256-GCM with 128-bit tag
- `AesGcm.Encrypt(nonce, plaintext, ciphertext, tag)` — nonce is 12 bytes
- `AesGcm.Decrypt(nonce, ciphertext, tag, plaintext)` — throws `CryptographicException` on auth failure
- `System.Security.Cryptography.HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, outputLength: 32, salt, info)` — recovery key derivation
- `System.Security.Cryptography.RandomNumberGenerator.Fill(Span<byte>)` — nonce and salt generation

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Round-trip via password | Create master key, wrap, unwrap with same password | Unwrapped key equals original |
| Round-trip via recovery | Create master key, wrap, unwrap with same recovery seed | Unwrapped key equals original |
| Wrong password fails | Wrap with "correct", unwrap with "wrong" | Throws `InvalidPasswordException` (GCM auth failure) |
| Wrong recovery key fails | Wrap, unwrap with different recovery seed | Throws `InvalidRecoveryKeyException` |
| Password change preserves recovery | Wrap, change password, unwrap via recovery | Recovery still works, returns same master key |
| Password change works | Wrap, change password, unwrap with new password | Returns same master key |
| Independence | Wrap once — `PasswordWrappedKey` and `RecoveryWrappedKey` are different byte sequences | True (different keys, nonces) |

Test vector file: `tests/vectors/key-wrapping.json`

### Verification Checklist

1. ✅ Run round-trip test — unwrapped key must exactly equal original master key for both paths
2. ✅ Search for `new AesGcm(` — every call must specify `tagSizeInBytes: 16` (128-bit tag)
3. ✅ Run wrong-password test — must throw, never return garbage silently
4. ✅ Confirm password change does NOT modify recovery blob fields

---

## A06 — Recovery Key Generation (24-word Phrase)

### Module & File Placement

- **File:** `src/SecureVault.Core/Crypto/RecoveryKeyGenerator.cs`
- **Dependencies:** A21 (SecureBuffer)
- **Depended on by:** A19 (KeyWrapping)

### Data Structures

```
Recovery key = 256 bits of entropy encoded as 24 BIP-39 words
  - 256 bits = 32 bytes random
  - BIP-39 English wordlist (2048 words)
  - 24 words = 264 bits (256 entropy + 8 checksum)
```

### Function Signatures

```csharp
public static class RecoveryKeyGenerator
    static (string[] words, byte[] seed) Generate()
    // 1. Generate 32 random bytes via RandomNumberGenerator
    // 2. Compute SHA-256 of the 32 bytes, take first byte as checksum
    // 3. Concatenate: 256 bits + 8 checksum bits = 264 bits
    // 4. Split into 24 groups of 11 bits each
    // 5. Each 11-bit value indexes into BIP-39 English wordlist (0–2047)
    // 6. Return (24 words, 32-byte seed)

    static byte[] WordsToSeed(string[] words)
    // 1. Validate 24 words
    // 2. Look up each word's index in BIP-39 wordlist
    // 3. Reconstruct 264 bits from 24 × 11-bit indices
    // 4. Split into 256 bits entropy + 8 bits checksum
    // 5. Verify checksum (SHA-256 of entropy, first byte)
    // 6. If checksum invalid → throw InvalidRecoveryKeyException
    // 7. Return 32-byte seed
```

### Exact Library Calls

- `System.Security.Cryptography.RandomNumberGenerator.Fill(Span<byte>)` — entropy
- `System.Security.Cryptography.SHA256.HashData(ReadOnlySpan<byte>)` — checksum

⚠️ **OPEN QUESTION: BIP-39 wordlist source**
Should the BIP-39 English wordlist be:
1. **Embedded as a resource file** in the assembly (2048 words, ~10KB) — simplest, no external dependency
2. **From a NuGet package** (e.g., `NBitcoin` has BIP-39 support) — heavier dependency but battle-tested
3. **Hardcoded as a string array** — smallest, but harder to verify correctness

**Recommendation:** Option 1 — embed the official BIP-39 wordlist as a text resource. It's stable (hasn't changed since 2013), small, and avoids pulling in a Bitcoin library for a single wordlist.

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Generate produces 24 words | Call `Generate()` | Returns exactly 24 words, all in BIP-39 list |
| Round-trip | Generate, then `WordsToSeed(words)` | Returns same 32-byte seed |
| Checksum validation | Modify one word in valid phrase | `WordsToSeed` throws `InvalidRecoveryKeyException` |
| Known vector | entropy=`0x00*32` | Known BIP-39 output: `"abandon abandon ... about"` (standard test vector) |

Test vector file: `tests/vectors/recovery-key.json`

### Verification Checklist

1. ✅ Run the all-zeros entropy vector — output must match the well-known BIP-39 test vector
2. ✅ Generate 100 recovery keys — all should have exactly 24 words, all words in the BIP-39 list
3. ✅ Search for `Random()` or `new Random(` in the crypto module — must never appear (only `RandomNumberGenerator`)

---

## B01 / B18 / B20 / B21 — Vault Binary Format (Header)

### Module & File Placement

- **File:** `src/SecureVault.Core/Format/VaultHeader.cs`
- **File:** `src/SecureVault.Core/Format/VaultConstants.cs`
- **Dependencies:** A19 (KeyWrapping), A21 (SecureBuffer)
- **Depended on by:** A01 (create vault), A02 (unlock vault), B15/B16 (index)

### Data Structures

**Vault Header Layout (byte-level, little-endian throughout):**

```
Offset   Size    Field                    Notes
──────   ────    ─────                    ─────
0x0000   32      RandomPrefix             Random bytes (B20 — prevent pattern detection)
0x0020   8       MaskedMagic              XOR of "SVAULT01" with first 8 bytes of
                                          SHA256(RandomPrefix) (B21 — no obvious signature)
0x0028   2       FormatVersion            uint16, currently = 1 (A10)
0x002A   16      VaultUUID                Guid bytes (A11)
0x003A   4       HeaderLength             uint32, total header size including this field
0x003E   4       Argon2MemoryKB           uint32, Argon2id memory cost in KB
0x0042   1       Argon2Iterations         uint8
0x0043   1       Argon2Parallelism        uint8
0x0044   32      PasswordSalt             Argon2id salt
0x0064   12      PasswordWrapNonce        AES-GCM nonce for password-wrapped key
0x0070   48      PasswordWrappedKey       32 bytes ciphertext + 16 bytes GCM tag
0x00A0   32      RecoverySalt             HKDF salt for recovery key
0x00C0   12      RecoveryWrapNonce        AES-GCM nonce for recovery-wrapped key
0x00CC   48      RecoveryWrappedKey       32 bytes ciphertext + 16 bytes GCM tag
0x00FC   5       PasswordHintLength       uint8 (max 255 chars) — A05 (Phase 4, write 0 now)
0x00FD   255     PasswordHintUTF8         UTF-8 encoded hint, padded to 255 bytes
0x01FC   8       PrimaryIndexOffset       uint64, absolute offset (B15)
0x0204   8       PrimaryIndexLength       uint64 (B15)
0x020C   8       BackupIndexOffset        uint64 (B16)
0x0214   8       BackupIndexLength        uint64 (B16)
0x021C   32      HeaderHMAC               HMAC-SHA256 of bytes 0x0000–0x021B (F14)
0x023C   --      (end of fixed header, 572 bytes total)
```

### Function Signatures

```csharp
public sealed class VaultHeader
    byte[] RandomPrefix           // 32 bytes
    byte[] MaskedMagic            // 8 bytes
    ushort FormatVersion
    Guid VaultUUID
    Argon2idParams Argon2Params
    WrappedKeyPair KeyData
    byte[] PasswordHint           // UTF-8, max 255 bytes
    ulong PrimaryIndexOffset
    ulong PrimaryIndexLength
    ulong BackupIndexOffset
    ulong BackupIndexLength
    byte[] HeaderHMAC             // 32 bytes

    static VaultHeader Create(string password, byte[] recoveryKeySeed, Argon2idParams? argon2Params)
    // 1. Generate 32-byte RandomPrefix via RandomNumberGenerator
    // 2. Compute MaskedMagic = XOR("SVAULT01", SHA256(RandomPrefix)[0..8])
    // 3. Set FormatVersion = 1
    // 4. Generate VaultUUID = Guid.NewGuid()
    // 5. Generate master key: 32 random bytes in SecureBuffer
    // 6. Wrap master key via KeyWrapping.WrapMasterKey(masterKey, password, recoveryKeySeed)
    // 7. Initialize index offsets to 0 (no index yet)
    // 8. Compute HMAC using HKDF-derived HMAC key from master key
    // 9. Return header

    void WriteTo(Stream stream)
    // Write all fields in order, little-endian, at exact offsets above

    static VaultHeader ReadFrom(Stream stream)
    // Read and parse all fields from stream at exact offsets above

    bool VerifyMagic()
    // 1. Compute expected = XOR("SVAULT01", SHA256(RandomPrefix)[0..8])
    // 2. Compare with MaskedMagic — constant-time comparison
```

### Exact Library Calls

- `System.Security.Cryptography.HMACSHA256` — header HMAC
- `System.Security.Cryptography.SHA256.HashData()` — magic masking
- `BinaryWriter` with `BitConverter.IsLittleEndian` assertion — serialization
- `System.Security.Cryptography.CryptographicOperations.FixedTimeEquals()` — constant-time HMAC comparison

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Round-trip | Create header, write to MemoryStream, read back | All fields match original |
| Magic masking | Known RandomPrefix `0x01*32` | `MaskedMagic = XOR("SVAULT01", SHA256(0x01*32)[0..8])` — compute exact expected value |
| Header size | Write header | Stream position = 572 (0x023C) |
| HMAC verification | Read valid header | `VerifyHMAC()` returns true |
| Tampered header | Flip one byte in header, re-check HMAC | `VerifyHMAC()` returns false |

Test vector file: `tests/vectors/vault-header.json`

### Verification Checklist

1. ✅ Run round-trip test — every field must survive write→read unchanged
2. ✅ Confirm header is exactly 572 bytes — no off-by-one
3. ✅ Search for `"SVAULT"` as a raw string in the binary output — it must NOT appear (magic is masked)
4. ✅ Tamper test — flipping any single byte in the header must cause HMAC verification to fail

---

## B02–B06, B22, B22a — Chunk Storage and Per-Chunk AEAD

### Module & File Placement

- **File:** `src/SecureVault.Core/Format/ChunkWriter.cs`
- **File:** `src/SecureVault.Core/Format/ChunkReader.cs`
- **File:** `src/SecureVault.Core/Format/BlockHeader.cs`
- **File:** `src/SecureVault.Core/Format/BlockFooter.cs`
- **File:** `src/SecureVault.Core/Format/ChunkIndex.cs`
- **Dependencies:** A14/A15/A16 (encryption modes), B07 (Reed-Solomon)
- **Depended on by:** C01, C16–C18 (file operations)

### Data Structures

**Chunk Layout (each chunk on disk):**

```
Offset   Size       Field             Notes
──────   ────       ─────             ─────
0x0000   4          ChunkDataLength   uint32, actual data bytes (after compress, before RS)
0x0004   4          CRC32             uint32, CRC32 of plaintext chunk (F11)
0x0008   1          ProtectionMode    0=FastObfuscation, 1=SecureMode
0x0009   12         Nonce             AES-GCM nonce (B22) — only used if ProtectionMode=1,
                                      for mode 0 this is zeroed
0x0015   16         AuthTag           AES-GCM auth tag (B22a) — only if ProtectionMode=1,
                                      zeroed for mode 0
0x0025   2          RSParityLength    uint16, Reed-Solomon parity bytes that follow data
0x0027   N          EncryptedData     N = ChunkDataLength bytes of encrypted/obfuscated data
0x0027+N M          RSParity          M = RSParityLength bytes of Reed-Solomon parity

Chunk overhead: 39 bytes fixed + M bytes RS parity per chunk
```

**Block Header (per file stored in vault):**

```
Offset   Size    Field              Notes
──────   ────    ─────              ─────
0x0000   4       BlockMagic         uint32 = 0x424C4B48 ("BLKH" XOR-masked)
0x0004   16      FileGUID           Guid, unique identifier for this file entry
0x0014   4       ChunkCount         uint32
0x0018   8       OriginalFileSize   uint64
0x0020   1       ProtectionMode     0=FastObfuscation, 1=SecureMode
0x0021   1       CompressionType    0=None, 1=LZ4, 2=Brotli
0x0022   32      PlaintextSHA256    SHA-256 of original plaintext file (C06)
0x0042   --      (end, 66 bytes)
```

**Block Footer (per file):**

```
Offset   Size    Field              Notes
──────   ────    ─────              ─────
0x0000   4       FooterMagic        uint32 = 0x424C4B46 ("BLKF" XOR-masked)
0x0004   16      FileGUID           same as block header (cross-reference)
0x0014   32      BlockSHA256        SHA-256 of entire block (header + all chunks)
0x0034   --      (end, 52 bytes)
```

**Chunk Index Entry (per chunk, stored in the file index):**

```
ChunkIndexEntry
  ChunkSequence   : uint32     (0-based position within the file)
  AbsoluteOffset  : uint64     (byte offset in the vault file, B04 — 64-bit)
  ChunkDataLength : uint32     (encrypted data length)
  CRC32           : uint32     (plaintext CRC32)
  AuthTag         : byte[16]   (GCM tag, zeroed for FastObfuscation mode)
```

### Function Signatures

```csharp
public sealed class ChunkWriter
    ChunkWriter(Stream vaultStream, SecureBuffer encryptionKey, ProtectionMode mode)

    ChunkIndexEntry WriteChunk(ReadOnlySpan<byte> plaintext, uint chunkSequence, Guid fileGuid)
    // 1. Compute CRC32 of plaintext
    // 2. Compress plaintext (based on configured compression)
    // 3. If mode = SecureMode:
    //    a. Derive nonce: HKDF(masterKey, salt=fileGuid + chunkSequence.ToBytes(), info="SecureVault-ChunkNonce-v1", length=12)
    //    b. AES-256-GCM encrypt compressed data → ciphertext + 16-byte authTag
    // 4. If mode = FastObfuscation:
    //    a. Derive per-file XOR keystream via HKDF(masterKey, salt=fileGuid, info="SecureVault-XOR-v1")
    //    b. XOR compressed data with keystream at offset (chunkSequence * chunkSize)
    //    c. Nonce = 0, AuthTag = 0
    // 5. Compute Reed-Solomon parity of ciphertext
    // 6. Write chunk header (length, CRC32, mode, nonce, authTag, RSParityLength)
    // 7. Write encrypted data
    // 8. Write RS parity
    // 9. Return ChunkIndexEntry

public sealed class ChunkReader
    ChunkReader(Stream vaultStream, SecureBuffer encryptionKey)

    byte[] ReadChunk(ChunkIndexEntry entry, Guid fileGuid)
    // 1. Seek to entry.AbsoluteOffset
    // 2. Read chunk header
    // 3. Read encrypted data + RS parity
    // 4. RS decode — attempt repair if errors detected (F02)
    // 5. If mode = SecureMode:
    //    a. Derive nonce (same as write path)
    //    b. AES-256-GCM decrypt — if auth tag fails, throw CorruptedChunkException
    // 6. If mode = FastObfuscation:
    //    a. Derive XOR keystream (same as write path)
    //    b. XOR to deobfuscate
    // 7. Decompress
    // 8. Verify CRC32 of decompressed plaintext
    // 9. Return plaintext bytes
```

### Exact Library Calls

- `System.Security.Cryptography.AesGcm(key, tagSizeInBytes: 16)` — per-chunk AEAD
- `System.Security.Cryptography.HKDF.DeriveKey(SHA256, ikm, 12, salt, info)` — nonce derivation
- `System.Security.Cryptography.HKDF.DeriveKey(SHA256, ikm, keystream_length, salt, info)` — XOR keystream
- `System.IO.Hashing.Crc32` (.NET 7+) — fast CRC32
- `STH1123.ReedSolomon.ReedSolomonEncoder` / `ReedSolomonDecoder` — RS error correction

⚠️ **OPEN QUESTION: Nonce derivation — deterministic (HKDF) vs random**
The spec says "per-chunk unique nonce" and "base_nonce + counter." Two options:
1. **Deterministic via HKDF(masterKey, fileGuid + chunkSeq):** Nonces are reproducible, no nonce storage needed, but nonce uniqueness depends on fileGuid + chunkSeq uniqueness (guaranteed if GUIDs are unique).
2. **Random 12-byte nonce per chunk, stored with chunk:** Simple, universally unique, but requires 12 extra bytes per chunk in storage.

**Recommendation:** Option 1 (deterministic HKDF). Since file GUIDs are random and chunk sequences are ordered, the input space is unique. This matches the vision doc's "base_nonce + counter" concept but is cryptographically cleaner than simple concatenation.

⚠️ **OPEN QUESTION: XOR keystream length for Fast Obfuscation**
For files larger than the HKDF output, how to extend the keystream?
1. **Use HKDF to derive a per-file AES-CTR key, then use AES-CTR as the keystream** — effectively makes XOR mode = AES-CTR without authentication. Fast, secure keystream.
2. **Use HKDF with chunk-sequence as additional salt** — derive a fresh keystream block per chunk.

**Recommendation:** Option 2 — derive per-chunk keystream via HKDF with `fileGuid + chunkSequence` as salt. This naturally limits each HKDF output to chunk size (1MB) and ensures per-file + per-chunk uniqueness per M14.

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Write-read round-trip (Secure) | 1MB of `0xAA` bytes, SecureMode | Read returns 1MB of `0xAA` |
| Write-read round-trip (Fast) | 1MB of `0xAA` bytes, FastObfuscation | Read returns 1MB of `0xAA` |
| Small chunk | 100 bytes, SecureMode | Works correctly, chunk data length = compressed size |
| Auth tag failure | Write SecureMode chunk, flip one byte in ciphertext | Read throws `CorruptedChunkException` |
| CRC32 verification | Write chunk, flip one byte in plaintext before verification | CRC32 mismatch detected |
| RS repair | Write chunk, corrupt 1 byte in ciphertext within RS repair capacity | Read succeeds after RS repair |
| Nonce uniqueness | Write 3 chunks for same file | All 3 nonces are different |
| Cross-file nonce | Write chunk 0 for file A and chunk 0 for file B | Different nonces (different fileGuid) |

Test vector file: `tests/vectors/chunk-format.json`

### Verification Checklist

1. ✅ Run round-trip test for both protection modes — plaintext must exactly match
2. ✅ Search for `AesGcm` — every `Encrypt` call must use a 12-byte nonce and produce a 16-byte tag
3. ✅ Run auth-tag-failure test — corrupted ciphertext must throw, never return garbage
4. ✅ Hex-dump a written chunk — confirm the on-disk layout matches the byte table above exactly

---

## B07, B08, B27 — Reed-Solomon Error Correction

### Module & File Placement

- **File:** `src/SecureVault.Core/Format/ReedSolomonCodec.cs`
- **Dependencies:** None (wraps external library)
- **Depended on by:** ChunkWriter, ChunkReader

### Data Structures

```
RS Configuration (B08 — one default level):
  DataShards    : int = 223    (RS(255,223) — standard codec)
  ParityShards  : int = 32     (~14.3% overhead, within "~12%" target)
  SymbolSize    : int = 8      (GF(2^8), standard)

For a 1MB (1,048,576 byte) chunk:
  Number of RS blocks = ceil(1048576 / 223) = 4703 blocks
  Parity bytes = 4703 * 32 = 150,496 bytes (~14.3% overhead)
```

### Function Signatures

```csharp
public sealed class ReedSolomonCodec
    ReedSolomonCodec()
    // Initialize with RS(255,223) using STH1123.ReedSolomon library

    byte[] Encode(ReadOnlySpan<byte> data)
    // 1. Split data into 223-byte blocks
    // 2. Pad last block with zeros if needed (record actual length)
    // 3. Encode each block → 255 bytes (223 data + 32 parity)
    // 4. Return concatenated parity bytes only (data already written separately)

    (byte[] repairedData, int errorsFixed) Decode(ReadOnlySpan<byte> data, ReadOnlySpan<byte> parity)
    // 1. Reconstruct 255-byte blocks from data + parity
    // 2. Run RS decode on each block
    // 3. Count corrected errors
    // 4. Return (repaired data, total errors fixed)
    // 5. If uncorrectable, throw UncorrectableCorruptionException
```

### Exact Library Calls

- `STH1123.ReedSolomon.ReedSolomonEncoder(255, 223)` — encoder
- `STH1123.ReedSolomon.ReedSolomonDecoder(255, 223)` — decoder
- NuGet package: `STH1123.ReedSolomon`

⚠️ **OPEN QUESTION: STH1123.ReedSolomon API surface**
The exact API of `STH1123.ReedSolomon` NuGet package needs verification at implementation time. The package may use different class/method names than assumed here. The implementer should:
1. Install the package and check the actual API
2. If the API is significantly different, adapt the wrapper but keep the same `ReedSolomonCodec` interface
3. If the package is unmaintained/broken, consider `ReedSolomon.Net` as an alternative

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Encode-decode clean | 223 bytes of `0x42` | Decode returns same 223 bytes, 0 errors |
| Single byte error | Corrupt 1 byte in encoded block | Decode repairs, returns original, errorsFixed=1 |
| Max correctable errors | Corrupt 16 bytes (t=32/2=16 correctable errors) | Decode repairs all |
| Beyond repair | Corrupt 17+ bytes | Throws `UncorrectableCorruptionException` |
| Empty input | 0 bytes | Returns empty, no errors |
| Large input | 1MB of random data | Encode-decode round-trip succeeds |

Test vector file: `tests/vectors/reed-solomon.json`

### Verification Checklist

1. ✅ Confirm `STH1123.ReedSolomon` (or alternative) is in the NuGet references — no custom RS implementation
2. ✅ Run the max-correctable-errors test — must repair exactly 16 byte errors per block
3. ✅ Run the beyond-repair test — must throw, never return silently corrupted data

---

## A14, A15, A16 — Encryption Modes (Index + Fast Obfuscation + Secure Mode)

### Module & File Placement

- **File:** `src/SecureVault.Core/Crypto/EncryptionService.cs`
- **Dependencies:** A21 (SecureBuffer), A12 (master key)
- **Depended on by:** B15/B16 (index encryption), ChunkWriter, ChunkReader

### Data Structures

```
ProtectionMode enum:
  FastObfuscation = 0   (XOR keystream, NOT encryption)
  SecureMode      = 1   (AES-256-GCM per chunk)

Key derivation tree (from master key, via HKDF with distinct info strings):
  Master Key
    → HKDF(info="SecureVault-IndexKey-v1")      → Index encryption key (AES-256-GCM)
    → HKDF(info="SecureVault-SecureModeKey-v1")  → Secure Mode file key (AES-256-GCM)
    → HKDF(info="SecureVault-ObfuscationKey-v1") → Fast Obfuscation base key (XOR keystream)
    → HKDF(info="SecureVault-HMACKey-v1")        → Header HMAC key
```

### Function Signatures

```csharp
public sealed class EncryptionService : IDisposable
    EncryptionService(SecureBuffer masterKey)
    // Derive all 4 keys via HKDF from master key
    // Store each in its own SecureBuffer

    (byte[] ciphertext, byte[] nonce, byte[] tag) EncryptIndex(ReadOnlySpan<byte> plaintext)
    // 1. Generate random 12-byte nonce
    // 2. AES-256-GCM encrypt with indexKey
    // 3. Return (ciphertext, nonce, 16-byte tag)

    byte[] DecryptIndex(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> tag)
    // 1. AES-256-GCM decrypt with indexKey
    // 2. If auth fails → throw CorruptedIndexException

    void Dispose()
    // Zero all 4 derived key SecureBuffers
```

### Exact Library Calls

- `HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, salt: ReadOnlySpan<byte>.Empty, info)` — key derivation
- `AesGcm(indexKey, tagSizeInBytes: 16)` — index encryption
- Per-chunk encryption/XOR keystream: handled by ChunkWriter/ChunkReader (see above)

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Index encrypt-decrypt | 1KB of index data | Round-trip produces identical plaintext |
| Index tamper detection | Encrypt, flip 1 byte in ciphertext | Decrypt throws `CorruptedIndexException` |
| Key uniqueness | Derive all 4 keys from same master key | All 4 keys are different 32-byte values |
| Key determinism | Same master key twice | Same 4 derived keys both times |

Test vector file: `tests/vectors/encryption-service.json`

### Verification Checklist

1. ✅ Search for HKDF info strings — there must be exactly 4 distinct ones, matching the list above
2. ✅ No two derived keys should use the same info string
3. ✅ Index decryption failure must throw, never return garbage

---

## B15–B17, B19 — Index System (Primary, Backup, Floating, Footer)

### Module & File Placement

- **File:** `src/SecureVault.Core/Format/VaultIndex.cs`
- **File:** `src/SecureVault.Core/Format/VaultFooter.cs`
- **Dependencies:** A14 (EncryptionService for index encryption), B07 (RS), B02-B06 (chunk format)
- **Depended on by:** C01, C08, C16 (file operations)

### Data Structures

**Index Entry (per file in vault):**

```
IndexEntry
  FileGUID          : Guid (16 bytes)
  FileName          : string (UTF-8, length-prefixed)
  OriginalSize      : uint64
  CompressedSize    : uint64
  ProtectionMode    : byte (0 or 1)
  CompressionType   : byte (0, 1, or 2)
  PlaintextSHA256   : byte[32]
  DateAdded         : long (UTC ticks)
  DateModified      : long (UTC ticks)
  Category          : byte (D03 enum)
  IsDeleted         : bool (soft delete for C08)
  VirtualFolderPath : string (UTF-8, length-prefixed)
  ChunkCount        : uint32
  FirstChunkOffset  : uint64
  Tags              : string[] (D05, for Phase 2 — serialize as empty array now)
  Notes             : string (D07, for Phase 2 — empty now)
  IsFavorite        : bool (D06, for Phase 2 — false now)

Serialization: MessagePack or custom binary, then encrypted via EncryptionService.EncryptIndex
```

**Vault Footer:**

```
Offset   Size    Field                  Notes
──────   ────    ─────                  ─────
0x0000   4       FooterMagic            uint32 = 0x53564654 ("SVFT" XOR-masked)
0x0004   8       PrimaryIndexOffset     uint64 (mirrors header, for recovery)
0x000C   8       PrimaryIndexLength     uint64
0x0014   8       BackupIndexOffset      uint64
0x001C   8       BackupIndexLength      uint64
0x0024   8       VaultDataSize          uint64 (total vault file size)
0x002C   32      FooterHMAC             HMAC-SHA256 of footer bytes 0x0000–0x002B
0x004C   --      (end, 76 bytes)
```

### Function Signatures

```csharp
public sealed class VaultIndex
    List<IndexEntry> Entries { get; }

    byte[] Serialize()
    // 1. Serialize all entries to binary (MessagePack recommended for compactness)
    // 2. Return raw bytes (caller encrypts + RS encodes)

    static VaultIndex Deserialize(ReadOnlySpan<byte> data)
    // 1. Parse binary back to entries
    // 2. Return VaultIndex

    void WriteToVault(Stream vaultStream, EncryptionService encryption, ReedSolomonCodec rs)
    // 1. Serialize index
    // 2. Encrypt via EncryptionService.EncryptIndex
    // 3. RS-encode the encrypted index
    // 4. Write primary index near start (after header + some file data)
    // 5. Write backup index near end (before footer)
    // 6. Update header/footer with offsets and lengths

    static VaultIndex ReadFromVault(Stream vaultStream, EncryptionService encryption, ReedSolomonCodec rs, VaultHeader header)
    // 1. Try reading primary index at header.PrimaryIndexOffset
    // 2. RS decode, decrypt
    // 3. If primary fails → try backup index at header.BackupIndexOffset
    // 4. If both fail → throw CorruptedIndexException (recovery scan needed — Phase 6)
```

⚠️ **OPEN QUESTION: Index serialization format**
1. **MessagePack** — compact binary, well-supported in .NET via `MessagePack-CSharp`, fast
2. **Custom binary** — full control over byte layout, but more implementation effort and bug surface
3. **Protobuf** — Google standard, but heavier dependency for a local-only format

**Recommendation:** MessagePack — it's compact, fast, schema-flexible, and the `MessagePack-CSharp` NuGet package is mature. The index is encrypted anyway so format efficiency matters more than human readability.

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Serialize-deserialize | Index with 3 file entries | All fields survive round-trip |
| Dual index write | Write to vault stream | Primary and backup offsets are different, both valid |
| Primary index recovery | Corrupt primary, read | Falls back to backup successfully |
| Both corrupted | Corrupt both | Throws `CorruptedIndexException` |
| Empty index | 0 entries | Valid empty index, serializes and deserializes |

Test vector file: `tests/vectors/vault-index.json`

### Verification Checklist

1. ✅ Write an index with 100 entries, corrupt primary, read back — backup must return all 100 entries intact
2. ✅ The index is never written unencrypted — search for `Serialize()` calls; every one must be followed by encryption
3. ✅ Confirm primary and backup index are at different offsets (not accidentally the same copy)

---

## A01 — Create New Vault

### Module & File Placement

- **File:** `src/SecureVault.Core/VaultManager.cs`
- **Dependencies:** A20 (file lock), A19 (key wrapping), A21 (secure buffer), B01/B18 (header), B15-B17 (index), B19 (footer)
- **Depended on by:** A02, C01

### Function Signatures

```csharp
public sealed class VaultManager : IDisposable
    static VaultManager Create(string vaultPath, string password)
    // 1. Check if vault file already exists — if yes, throw VaultAlreadyExistsException
    // 2. Acquire VaultFileLock (A20)
    // 3. Generate 32-byte master key via RandomNumberGenerator → SecureBuffer
    // 4. Generate recovery key (24 words + seed) via RecoveryKeyGenerator
    // 5. Wrap master key via KeyWrapping.WrapMasterKey(masterKey, password, recoveryKeySeed)
    // 6. Create VaultHeader with all fields
    // 7. Create empty VaultIndex
    // 8. Derive all encryption keys via EncryptionService(masterKey)
    // 9. Open FileStream to vaultPath (FileMode.CreateNew)
    // 10. Write header
    // 11. Write encrypted empty index (primary + backup)
    // 12. Write footer
    // 13. Flush and sync to disk
    // 14. Return VaultManager with open vault + recovery words (must show to user ONCE)
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Create new vault | `Create("test.vault", "password123")` | File exists, valid header, empty index, recovery words returned |
| Duplicate path | Create twice at same path | Second call throws `VaultAlreadyExistsException` |
| File lock acquired | Create vault | `.lock` file exists |
| Header valid | Create, read header back | All fields parseable, HMAC valid |
| Recovery words valid | Create, convert words back to seed, unwrap recovery | Returns same master key |

### Verification Checklist

1. ✅ After `Create`, the vault file size should be header (572) + encrypted index (small) + footer (76) + RS parity — roughly 700-1500 bytes
2. ✅ Recovery words must be returned to the caller — search for where they're displayed in the UI (Phase 2)
3. ✅ The master key bytes should never be logged or serialized to disk

---

## A02 — Unlock Vault

### Module & File Placement

- **File:** `src/SecureVault.Core/VaultManager.cs` (same class as A01)
- **Dependencies:** A20 (file lock), A19 (key unwrap), header, index

### Function Signatures

```csharp
public sealed class VaultManager
    static VaultManager Open(string vaultPath, string password)
    // 1. Acquire VaultFileLock (A20)
    // 2. Open FileStream (FileMode.Open, FileAccess.ReadWrite, FileShare.None)
    // 3. Read VaultHeader
    // 4. Verify masked magic
    // 5. Unwrap master key via KeyWrapping.UnwrapWithPassword(header.KeyData, password)
    //    — if auth fails, release lock, throw InvalidPasswordException
    // 6. Verify header HMAC with derived HMAC key
    //    — if fails, release lock, throw CorruptedVaultException
    // 7. Create EncryptionService from master key
    // 8. Read and decrypt VaultIndex (primary, fallback to backup)
    // 9. Return VaultManager with open vault, decrypted index in memory
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Open valid vault | Create, close, reopen with correct password | Index loaded, all file entries present |
| Wrong password | Open with wrong password | Throws `InvalidPasswordException`, lock released |
| Corrupted header | Flip byte in header, try open | Throws `CorruptedVaultException` |

### Verification Checklist

1. ✅ Wrong password must release the file lock (no leaked lock on failure)
2. ✅ After open, `VaultManager` holds the master key in `SecureBuffer`, not raw `byte[]`

---

## A03 — Lock Vault (Zero Keys)

### Module & File Placement

- **File:** `src/SecureVault.Core/VaultManager.cs`
- **Dependencies:** A21 (SecureBuffer dispose), A20 (file lock release)

### Function Signatures

```csharp
public sealed class VaultManager
    void Lock()
    // 1. Dispose EncryptionService (zeros all 4 derived keys)
    // 2. Dispose master key SecureBuffer
    // 3. Clear in-memory index
    // 4. Close vault FileStream
    // 5. Release VaultFileLock
    // 6. Set internal state to Locked

    void Dispose()
    // Call Lock() if not already locked
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Lock clears keys | Open vault, lock, try read file | Throws `VaultLockedException` |
| Lock releases file | Open, lock | `.lock` file deleted, another process can open |
| Dispose = Lock | Open, dispose without lock | Same behavior as lock |

### Verification Checklist

1. ✅ After lock, no `SecureBuffer` instances remain undisposed — search for `SecureBuffer` field declarations, all must be null or disposed
2. ✅ After lock, the vault file is not held open (another process can read it)

---

## A04 — Change Password

### Module & File Placement

- **File:** `src/SecureVault.Core/VaultManager.cs`
- **Dependencies:** A19 (KeyWrapping.RewrapPasswordOnly)

### Function Signatures

```csharp
public sealed class VaultManager
    void ChangePassword(string currentPassword, string newPassword)
    // 1. Verify current password by attempting unwrap (defensive check)
    // 2. Call KeyWrapping.RewrapPasswordOnly(header.KeyData, masterKey, newPassword)
    // 3. Update header in memory
    // 4. Write updated header to vault file (atomic: write to temp, rename)
    // 5. Re-compute header HMAC with new wrapped-key data
    // 6. No file data is re-processed (A04 requirement)
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Change password | Open, change "old" → "new", close, reopen with "new" | Succeeds |
| Old password rejected | After change, try open with old password | Throws `InvalidPasswordException` |
| Recovery unaffected | After change, recovery key still unwraps | Returns same master key |
| Wrong current password | Change with wrong current password | Throws, password unchanged |

### Verification Checklist

1. ✅ After password change, NO file data has been re-encrypted — vault file size unchanged (except tiny header diff)
2. ✅ Recovery key blob in header is byte-identical before and after password change

---

## C01, C05, C06 — Add File (Streaming, Plaintext Hash)

### Module & File Placement

- **File:** `src/SecureVault.Core/Operations/FileAddOperation.cs`
- **Dependencies:** ChunkWriter, VaultIndex, EncryptionService, ReedSolomonCodec, VaultManager (lock)
- **Depended on by:** C02-C04 (multi-file, folder, drag-drop — Phase 2)

### Function Signatures

```csharp
public sealed class FileAddOperation
    FileAddOperation(VaultManager vault)

    IndexEntry AddFile(string sourcePath, string virtualFolderPath, ProtectionMode mode)
    // 1. Open source file as FileStream (read-only, sequential)
    // 2. Initialize SHA256 incremental hash
    // 3. Determine compression type based on file extension (B10-B14):
    //    - Already compressed (jpg, mp4, zip, etc): None
    //    - Large files (>10MB): LZ4
    //    - Small text files (<1MB): Brotli
    // 4. Write BlockHeader to vault stream
    // 5. Loop: read 1MB chunks from source
    //    a. Feed chunk to SHA256 incremental hash
    //    b. Pass chunk to ChunkWriter.WriteChunk (compress + encrypt + RS + write)
    //    c. Collect ChunkIndexEntry
    //    d. Report progress (C07 — Phase 2)
    // 6. Finalize SHA256 hash → PlaintextSHA256
    // 7. Write BlockFooter (with SHA256 of entire block)
    // 8. Create IndexEntry with all metadata
    // 9. Add to VaultIndex
    // 10. Rewrite encrypted index (primary + backup) — atomic
    // 11. Return IndexEntry

    static CompressionType SelectCompression(string extension, long fileSize)
    // Decision table:
    // .jpg, .png, .gif, .mp4, .mkv, .avi, .mov, .mp3, .flac, .ogg,
    // .zip, .rar, .7z, .gz, .bz2 → None
    // .txt, .md, .json, .xml, .html, .css, .js, .cs, .py (< 1MB) → Brotli
    // Everything else (< 10MB) → Brotli
    // Everything else (>= 10MB) → LZ4
```

### Exact Library Calls

- `System.Security.Cryptography.IncrementalHash.CreateHash(HashAlgorithmName.SHA256)` — streaming hash
- `K4os.Compression.LZ4.LZ4Codec.Encode()` — LZ4 compression
- `System.IO.Compression.BrotliEncoder` — Brotli compression

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Add small file | 100-byte text file | File in vault, readable, SHA256 matches independent hash |
| Add large file streaming | 5MB file | Added without loading full file in RAM (verify via peak memory) |
| SHA256 on plaintext | Add file, check stored hash | Matches `SHA256.HashData(originalFileBytes)` |
| Compression selection | `.jpg` file | CompressionType = None |
| Compression selection | `.txt` 500-byte file | CompressionType = Brotli |
| Compression selection | `.dat` 20MB file | CompressionType = LZ4 |
| Round-trip | Add file, read back | Byte-identical to original |

Test vector file: `tests/vectors/file-add.json`

### Verification Checklist

1. ✅ Search for `File.ReadAllBytes` in `FileAddOperation` — should NOT appear (streaming only)
2. ✅ Add a 100MB file, confirm vault file grew by ~100MB + overhead (not 200MB from double-buffering)
3. ✅ After add, both primary and backup index contain the new entry

---

## C08 — Delete File from Vault

### Module & File Placement

- **File:** `src/SecureVault.Core/Operations/FileDeleteOperation.cs`
- **Dependencies:** VaultIndex, VaultManager

### Function Signatures

```csharp
public sealed class FileDeleteOperation
    FileDeleteOperation(VaultManager vault)

    void DeleteFile(Guid fileGuid)
    // 1. Find IndexEntry by fileGuid
    // 2. Set IsDeleted = true (soft delete — space reclaimed by compaction, C23 Phase 6)
    // 3. Rewrite encrypted index (atomic)
    // 4. File's chunks remain on disk but are inaccessible
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Delete file | Add file, delete by GUID | File no longer in index, vault file size unchanged |
| Delete non-existent | Delete random GUID | Throws `FileNotFoundException` |
| Deleted file unreadable | Delete, try read | Throws `FileNotFoundException` |

### Verification Checklist

1. ✅ After delete, vault file size is unchanged (soft delete, no rewrite)
2. ✅ Deleted file's chunks are still on disk but the index entry has `IsDeleted = true`

---

## C16–C18 — Read File (Memory + Stream + Seeking)

### Module & File Placement

- **File:** `src/SecureVault.Core/Operations/FileReadOperation.cs`
- **File:** `src/SecureVault.Core/IO/VaultFileStream.cs`
- **Dependencies:** ChunkReader, VaultIndex

### Function Signatures

```csharp
public sealed class FileReadOperation
    FileReadOperation(VaultManager vault)

    byte[] ReadFileToMemory(Guid fileGuid)
    // 1. Look up IndexEntry and all ChunkIndexEntries
    // 2. For each chunk: ChunkReader.ReadChunk() → plaintext
    // 3. Concatenate all chunk plaintexts
    // 4. Verify PlaintextSHA256 matches (F12)
    // 5. Return full byte array

    VaultFileStream OpenFileStream(Guid fileGuid)
    // Return a seekable Stream backed by chunk-based reads

public sealed class VaultFileStream : Stream
    // Implements Read, Seek, Position, Length
    // Internally:
    //   - Maintains current chunk index and position within chunk
    //   - On Read: if current chunk exhausted, load next via ChunkReader
    //   - On Seek: compute target chunk index = position / chunkSize
    //     load that chunk, set position within chunk
    //   - Caches current chunk in memory (1MB max)
    //   - Write/SetLength throw NotSupportedException (read-only)
```

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Read small file to memory | Add 100 bytes, read | Returns original 100 bytes |
| Read large file to memory | Add 5MB, read | Returns original 5MB, SHA256 verified |
| Stream sequential read | Add 3MB, read in 4KB blocks | All blocks correct, total = 3MB |
| Stream seek forward | Add 5MB, seek to 2.5MB, read 1MB | Correct bytes from offset 2.5MB |
| Stream seek backward | Read to 3MB, seek to 0, read 1MB | Same as first 1MB of file |
| Stream seek to chunk boundary | Seek to exactly 1MB (chunk boundary) | Reads correct data |
| Stream length | Open stream | Length equals original file size |

### Verification Checklist

1. ✅ VaultFileStream only holds one chunk in memory at a time (not the entire file)
2. ✅ Seek to any position works correctly across chunk boundaries
3. ✅ Stream is read-only — Write and SetLength throw `NotSupportedException`

---

## F07, F08 — Atomic Writes & Write-Ahead

### Module & File Placement

- **File:** `src/SecureVault.Core/IO/AtomicWriter.cs`
- **Dependencies:** None
- **Depended on by:** VaultIndex writes, header updates

### Function Signatures

```csharp
public static class AtomicWriter
    static void WriteAtomic(string targetPath, Action<Stream> writeAction)
    // 1. Create temp file in same directory: targetPath + ".tmp." + Guid
    // 2. Open temp file as FileStream
    // 3. Call writeAction(tempStream) — caller writes content
    // 4. Flush and fsync temp file
    // 5. Close temp file
    // 6. File.Move(tempPath, targetPath, overwrite: true) — atomic rename on NTFS
    // 7. If any step fails, delete temp file in finally block
```

⚠️ **OPEN QUESTION: Atomic index update within a single vault file**
The vault is a single binary file, not a directory of files. Atomic rename only works for replacing entire files. For updating the index *within* the vault file:
1. **Write-ahead log (WAL):** Write new index to a separate `.wal` file first, then copy into the vault. On crash, replay WAL on next open.
2. **Double-write:** Write index to both primary and backup locations sequentially. If crash happens between writes, at least one is valid.
3. **New-index-then-pointer-update:** Write new index at end of vault, then update the header offset pointer. If crash happens before pointer update, old index is still valid.

**Recommendation:** Option 3 — it's the simplest and most crash-safe for a single-file format. The floating index (B17) already implies this approach.

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Atomic write succeeds | Write 1KB file | File contains expected content |
| Atomic write interrupted | Kill process mid-write (simulate) | Original file unchanged, temp file cleaned up |
| Temp file cleanup | Write that throws exception | No `.tmp.*` files left on disk |

### Verification Checklist

1. ✅ Search for `File.WriteAllBytes` or direct `FileStream` writes to the vault — they should all go through `AtomicWriter` or the append-then-update-pointer pattern
2. ✅ After a simulated crash, the vault opens successfully with the last-known-good index

---

## M14 — XOR Obfuscation Keystream (HKDF, Per-File)

### Module & File Placement

- **File:** `src/SecureVault.Core/Crypto/ObfuscationKeystream.cs`
- **Dependencies:** A21 (SecureBuffer), master key
- **Depended on by:** ChunkWriter (FastObfuscation mode)

### Function Signatures

```csharp
public sealed class ObfuscationKeystream
    ObfuscationKeystream(SecureBuffer obfuscationBaseKey)

    byte[] GenerateForChunk(Guid fileGuid, uint chunkSequence, int length)
    // 1. Compute salt = fileGuid.ToByteArray() + BitConverter.GetBytes(chunkSequence)  (20 bytes)
    // 2. HKDF.DeriveKey(SHA256, obfuscationBaseKey, length, salt, info="SecureVault-XOR-v1")
    // 3. Return keystream bytes of requested length
    // NOTE: Each (fileGuid, chunkSequence) pair produces a unique keystream
    //       Two different files NEVER share keystream (M14 requirement)

    static void XorInPlace(Span<byte> data, ReadOnlySpan<byte> keystream)
    // XOR data with keystream, in place
```

### Exact Library Calls

- `HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, outputLength, salt, info)` — keystream derivation
- Maximum HKDF output: ~8,160 bytes per call with SHA-256. For 1MB chunks, need to call HKDF multiple times with incrementing sub-counters or use HKDF-Expand in a loop.

⚠️ **OPEN QUESTION: HKDF output length limit**
HKDF-Expand can produce at most 255 * HashLen = 255 * 32 = 8,160 bytes per call. A 1MB chunk needs ~1,048,576 bytes of keystream. Options:
1. **Loop HKDF with sub-counter:** `HKDF(salt = fileGuid + chunkSeq + subBlockIndex)` for each 8KB sub-block — simple, secure.
2. **Use HKDF to derive an AES-CTR key, then AES-CTR as keystream:** More efficient for large chunks, well-understood construction.

**Recommendation:** Option 2 — derive a per-chunk AES-CTR key via HKDF, then use AES-CTR to generate the keystream. This is standard practice (it's essentially what TLS does) and handles arbitrary lengths efficiently. The "Fast Obfuscation" mode then becomes AES-CTR without authentication — fast, but no integrity guarantee (which is the documented tradeoff).

### Test Plan

| Test | Input | Expected |
|------|-------|----------|
| Unique per file | Same chunk 0, two different fileGuids | Different keystreams |
| Unique per chunk | Same fileGuid, chunks 0 and 1 | Different keystreams |
| XOR round-trip | XOR data, XOR again with same keystream | Returns original data |
| Deterministic | Same inputs twice | Same keystream |

Test vector file: `tests/vectors/obfuscation-keystream.json`

### Verification Checklist

1. ✅ Search for the string `XOR` — every XOR operation must use `ObfuscationKeystream`, never a fixed or reused key
2. ✅ Confirm the salt includes the fileGuid — keystreams must be per-file

---

## M16 — Constant-Time Password Comparison

### Module & File Placement

- **File:** Part of `src/SecureVault.Core/Crypto/KeyWrapping.cs` (already exists)
- **Dependencies:** None
- **Note:** This is handled implicitly by AES-GCM auth tag verification — if the wrong password produces the wrong derived key, the GCM tag check fails in constant time. No explicit password-string comparison is needed.

### Verification Checklist

1. ✅ Search for `==` or `.Equals(` comparing passwords, keys, or tags — should not appear. All comparisons use `CryptographicOperations.FixedTimeEquals()` or GCM's built-in auth check
2. ✅ Confirm: no code path checks `if (password == storedPassword)` — that pattern doesn't exist in this architecture (passwords are never stored)

---

## F11, F12, F13 — Per-Chunk CRC32, Per-File SHA-256, Per-Chunk Auth Tag

These are implemented as part of ChunkWriter (B02-B06) and FileAddOperation (C01). No separate module needed.

- **F11 (CRC32):** Computed in `ChunkWriter.WriteChunk()`, stored in chunk header
- **F12 (SHA-256):** Computed in `FileAddOperation.AddFile()` via incremental hash, stored in BlockHeader and IndexEntry
- **F13 (Auth tag):** Computed by AES-GCM in `ChunkWriter.WriteChunk()` for SecureMode, stored in chunk header

### Verification Checklist

1. ✅ Every chunk written has a non-zero CRC32
2. ✅ Every file added has a PlaintextSHA-256 in its IndexEntry
3. ✅ Every SecureMode chunk has a non-zero 16-byte auth tag

---

## F09 — Block Isolation

No separate code needed — this is an architectural property. Each file's chunks are independent blocks. Corruption in file A's chunks does not affect file B's chunks because:
1. Each file has its own BlockHeader + chunks + BlockFooter
2. The index has separate entries per file
3. ChunkReader reads by absolute offset, not relative to other files

### Verification Checklist

1. ✅ Corrupt one file's chunk data in a vault with 3 files — the other 2 files read correctly

---

## M18 — VeraCrypt Design Study

### Module & File Placement

- **File:** `docs/roadmap/veracrypt-study-notes.md` (documentation only, no code)
- **Dependencies:** None
- **Status:** This is a research task. The implementer should read VeraCrypt's format specification (publicly available) and compare:
  1. Header authentication approach
  2. Key-slot design
  3. Salt storage
  4. Nonce handling

This does NOT mean adopting VeraCrypt's format — it means checking our design against theirs for missed edge cases.

---

## Source File Summary

All Phase 1 source files under `src/`:

```
src/SecureVault.Core/
├── Crypto/
│   ├── SecureBuffer.cs              (A21)
│   ├── KeyDerivation.cs             (M01, A12, A13)
│   ├── KeyWrapping.cs               (A19, M16)
│   ├── RecoveryKeyGenerator.cs      (A06 — generate/validate recovery phrases)
│   ├── EncryptionService.cs         (A14, A15, A16)
│   └── ObfuscationKeystream.cs      (M14)
├── Format/
│   ├── VaultConstants.cs            (magic bytes, version, offsets)
│   ├── VaultHeader.cs               (B01, B18, B20, B21)
│   ├── VaultFooter.cs               (B19)
│   ├── VaultIndex.cs                (B15, B16, B17)
│   ├── BlockHeader.cs               (B05)
│   ├── BlockFooter.cs               (B06)
│   ├── ChunkWriter.cs               (B02, B03, B04, B22, B22a)
│   ├── ChunkReader.cs               (B02, B03, B04, B22, B22a)
│   ├── ChunkIndex.cs                (B03)
│   └── ReedSolomonCodec.cs          (B07, B08, B09, B27)
├── IO/
│   ├── VaultFileLock.cs             (A20)
│   ├── VaultFileStream.cs           (C18)
│   └── AtomicWriter.cs              (F07, F08)
├── Operations/
│   ├── FileAddOperation.cs          (C01, C05, C06)
│   └── FileDeleteOperation.cs       (C08)
├── VaultManager.cs                  (A01, A02, A03, A04)
└── Exceptions/
    ├── VaultAlreadyOpenException.cs
    ├── VaultAlreadyExistsException.cs
    ├── InvalidPasswordException.cs
    ├── InvalidRecoveryKeyException.cs
    ├── CorruptedChunkException.cs
    ├── CorruptedIndexException.cs
    ├── CorruptedVaultException.cs
    ├── VaultLockedException.cs
    └── UncorrectableCorruptionException.cs
```

## Test Vector Files

```
tests/vectors/
├── secure-buffer.json
├── argon2id-derivation.json
├── key-wrapping.json
├── recovery-key.json
├── vault-header.json
├── chunk-format.json
├── reed-solomon.json
├── encryption-service.json
├── vault-index.json
├── file-add.json
└── obfuscation-keystream.json
```

## Branch & PR

- **Branch:** `phase-1/vault-core`
- **PR Title:** "Phase 1: Vault Core Engine — Format, Crypto, Integrity"
- **PR Description:**

```
Implements the foundation of SecureVault:

## What's included
- Vault binary format (.vault) with 572-byte header, per-file blocks, and footer
- Master key architecture with dual key-wrap (password via Argon2id + recovery key via HKDF)
- 24-word BIP-39 recovery key generation
- AES-256-GCM per-chunk encryption (Secure Mode)
- XOR keystream obfuscation via HKDF-derived AES-CTR (Fast Obfuscation Mode)
- Reed-Solomon error correction per chunk (~14% overhead)
- Dual encrypted index (primary + backup) for crash resilience
- Single-writer file lock (named mutex + lock file)
- Streaming file add/read with chunk-based seeking (1MB chunks, 64-bit offsets)
- Secure key zeroing via pinned buffers + CryptographicOperations.ZeroMemory
- Atomic writes for index updates

## Security properties
- All keys derived via HKDF with distinct context strings (no key reuse)
- AES-GCM with 12-byte nonce, 16-byte auth tag per chunk
- Argon2id with 256MB memory cost, 3 iterations, 4 parallelism
- Constant-time auth via GCM tag verification (no string password comparison)
- Per-file unique XOR keystream (never reused across files)

## Test vectors
11 test vector files covering every crypto operation and format detail.
Run `dotnet test` to verify all vectors.

## Dependencies
- Konscious.Security.Cryptography (Argon2id)
- STH1123.ReedSolomon (Reed-Solomon codec)
- MessagePack-CSharp (index serialization)
- K4os.Compression.LZ4 (fast compression)
- System.IO.Hashing (CRC32)
```

## CONTRIBUTING Note for Phase 1

```
CONTRIBUTING — Phase 1 (Vault Core)

This phase contains ALL cryptographic operations and the binary vault format.
Rules for contributors:

1. NEVER change nonce generation, key derivation parameters, or HKDF info
   strings without updating the corresponding test vector AND getting review
   from someone who understands the crypto implications.

2. Any PR that touches Crypto/ or Format/ MUST include a test-vector diff.
   "All existing tests pass" is not sufficient — you must prove the
   change didn't silently alter crypto output by adding or updating
   a test vector with known inputs and expected outputs.

3. Do NOT use System.Random, Random.Shared, or any non-cryptographic
   RNG anywhere in this codebase. All randomness comes from
   RandomNumberGenerator.

4. Do NOT use plain byte[] for keys. All key material must use
   SecureBuffer (pinned + zeroed on dispose).

5. The vault binary format has exact byte offsets documented in
   phase-1-foundation.md. If you change any offset, update the doc
   AND add a format version bump.
```

## STATUS.md Entries for Phase 1

```
A01 🔨 Create new vault with password
A02 🔨 Unlock vault with password
A03 🔨 Lock vault (zero keys)
A04 🔨 Change password
A06 🔨 Recovery key generation (24-word phrase)
A12 🔨 Master key architecture
A13 🔨 Argon2id key derivation
A14 🔨 AES-256-GCM for index encryption
A15 🔨 Fast Obfuscation Mode (XOR, not encryption)
A16 🔨 AES-256-GCM per-file encryption (Secure Mode)
A19 🔨 Dual key-wrap (password + recovery)
A20 🔨 Single-writer file lock
A21 🔨 Key zeroing (pinned buffers)
B01 🔨 Vault binary format
B02 🔨 Chunked file storage (1MB)
B03 🔨 Chunk index (offset, size, CRC32, auth tag)
B04 🔨 64-bit offsets
B05 🔨 Block header per file
B06 🔨 Block footer per file
B07 🔨 Reed-Solomon error correction
B08 🔨 Default RS level (~12% overhead)
B15 🔨 Primary encrypted index
B16 🔨 Backup encrypted index
B17 🔨 Floating index with pointer chain
B18 🔨 Vault header with encrypted section
B19 🔨 Vault footer
B20 🔨 Random prefix in header
B21 🔨 XOR-masked magic bytes
B22 🔨 Per-chunk unique nonce
B22a 🔨 Per-chunk AEAD unit
B27 🔨 RS uses library (no custom impl)
C01 🔨 Add single file to vault
C05 🔨 Streaming file addition
C06 🔨 SHA-256 on plaintext
C08 🔨 Delete file (soft delete)
C16 🔨 Read file to memory
C17 🔨 Read file as stream
C18 🔨 VaultFileStream with chunk seeking
F01 🔨 RS error correction on every chunk
F02 🔨 Auto-repair corrupted chunks
F07 🔨 Atomic writes (temp + rename)
F08 🔨 Write-ahead for index updates
F09 🔨 Block isolation
F11 🔨 Per-chunk CRC32
F12 🔨 Per-file SHA-256
F13 🔨 Per-chunk AES-GCM auth tag
M01 🔨 Argon2id (256MB, 3 iter, 4 parallel)
M02 🔨 AES-256-GCM for Secure Mode
M03 🔨 Unique nonce per chunk
M04 🔨 Master key zeroed on lock
M05 🔨 Obfuscation key zeroed on lock
M06 🔨 No decrypted data to disk
M14 🔨 XOR keystream (HKDF, per-file unique)
M16 🔨 Constant-time comparison (via GCM)
M18 🔨 VeraCrypt design study (doc)
```

---

## Summary of Open Questions

1. **Argon2id memory cost:** 64MB vs 128MB vs 256MB — recommended 256MB with auto-fallback for low-RAM systems
2. **BIP-39 wordlist source:** Embedded resource vs NuGet package vs hardcoded — recommended embedded resource
3. **Index serialization format:** MessagePack vs custom binary vs Protobuf — recommended MessagePack
4. **Nonce derivation:** Deterministic HKDF vs random per-chunk — recommended deterministic HKDF
5. **XOR keystream for large chunks:** HKDF sub-blocks vs HKDF→AES-CTR — recommended HKDF→AES-CTR
6. **Atomic index update in single file:** WAL vs double-write vs append-then-pointer-update — recommended append-then-pointer-update
7. **STH1123.ReedSolomon API:** Needs verification at implementation time — may need alternative package
