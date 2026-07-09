# SecureVault

**A personal encrypted file vault for Windows** — store, view, and manage all your digital files inside encrypted containers that only you can open.

## What Is This?

SecureVault is a single Windows desktop app that acts as an encrypted operating system for your files:

- **Stores everything** you own digitally in encrypted `.vault` files
- **Built-in apps** to view photos, play video/audio, edit notes, and read PDFs — all without extracting files to disk
- **Self-healing storage** with Reed-Solomon error correction that survives bit rot and corruption
- **Password + 24-word recovery key** protection with Argon2id key derivation
- **Portable backups** — vault files are 100% self-contained, restore anywhere

Think of it as: **Windows Explorer + VLC + Photo Gallery + Notes App + 7-Zip**, all wrapped inside an encrypted container that only you can access.

## Security Model

| Protects Against | Does NOT Protect Against |
|---|---|
| Physical access to your vault file (theft, cloud compromise) | Active keylogger/screen-recorder on your machine |
| Casual inspection (hex editor shows nothing useful) | Forensic memory analysis of a live, unlocked session |
| Bit rot and transfer corruption | Losing both password AND recovery key (data gone forever — by design) |

## Technology Stack

| Component | Technology |
|-----------|------------|
| UI Framework | WinUI 3 (Windows App SDK) |
| Language | C# (.NET 8) |
| Video/Audio | LibVLCSharp (LGPL, dynamically linked) |
| Images | SkiaSharp + Magick.NET (HEIC/RAW) |
| PDF | PdfiumCore |
| Encryption | System.Security.Cryptography (AES-256-GCM) |
| Key Derivation | Konscious.Security (Argon2id) |
| Error Correction | STH1123.ReedSolomon |
| Compression | K4os.Compression.LZ4 + Brotli (built-in) |
| Archives | SharpCompress |
| Markdown | Markdig |

## Project Structure

```
SecureVault/
├── docs/
│   ├── vision.md                    # Complete project specification (v2)
│   ├── roadmap/                     # Phase-by-phase implementation roadmaps
│   │   ├── README.md                # Roadmap overview & reading order
│   │   ├── phase-1-foundation.md    # Vault core, crypto, format (~50 features)
│   │   ├── phase-2-ui.md            # Basic UI, file ops, search (~35 features)
│   │   ├── phase-3-integrated-apps.md # Gallery, player, notes, PDF (~25 features)
│   │   ├── phase-4-advanced.md      # Advanced features, thumbnails (~60 features)
│   │   ├── phase-5-backup-multivault.md # Backup, multi-vault (~30 features)
│   │   └── phase-6-polish.md        # Themes, security, recovery (~35 features)
│   └── STATUS.md                    # Feature tracker (~205 features)
├── tests/
│   └── vectors/                     # Cryptographic test vectors (JSON)
├── src/                             # Source code (implementation begins here)
└── README.md                        # This file
```

## Implementation Phases

| Phase | Focus | Feature Count | Branch |
|-------|-------|---------------|--------|
| 1 | Foundation — vault format, crypto, integrity | ~50 | `phase-1/vault-core` |
| 2 | Basic UI — login, library, file operations | ~35 | `phase-2/basic-ui` |
| 3 | Integrated Apps — gallery, player, notes, PDF | ~25 | `phase-3/integrated-apps` |
| 4 | Advanced — thumbnails, editing, file manager | ~60 | `phase-4/advanced-features` |
| 5 | Backup & Multi-Vault — backup/restore, vault chain | ~30 | `phase-5/backup-multivault` |
| 6 | Polish — themes, security, recovery, compaction | ~35 | `phase-6/polish` |

**Total: ~205 features across 6 phases**

## Getting Started

### Prerequisites

- Windows 10 version 1809+ (or Windows 11)
- .NET 8 SDK
- Visual Studio 2022 (17.8+) with:
  - .NET Desktop Development workload
  - Windows App SDK

### Building

```powershell
# Clone the repository
git clone https://github.com/QuorLum/SecureVault.git
cd SecureVault

# Build (once src/ is populated)
dotnet build src/SecureVault.sln
```

### Running Tests

```powershell
dotnet test src/SecureVault.sln
```

## Key Architecture Decisions

- **Single-file vault format** — everything in one `.vault` binary file (up to 200GB), overflow to `.vault2`
- **Dual key-wrap** — master key wrapped independently by password (Argon2id) and recovery key (HKDF), either unlocks the vault
- **Per-chunk AEAD** — each 1MB chunk has its own AES-GCM nonce and auth tag, enabling safe random-access seeking
- **Two protection modes** — Secure Mode (AES-256-GCM, full encryption) and Fast Obfuscation (XOR keystream, speed over security)
- **Dual encrypted index** — primary + backup, both Reed-Solomon protected, survives partial file corruption
- **In-memory only** — all viewing/playing happens in RAM, no decrypted data touches disk

## Contributing

See the CONTRIBUTING notes in each phase roadmap for phase-specific guidelines. General rules:

1. **Never use `System.Random`** for anything — all randomness from `RandomNumberGenerator`
2. **Never store keys in `byte[]`** — use `SecureBuffer` (pinned + zeroed on dispose)
3. **Never write decrypted data to disk** — all processing in memory
4. **Test vectors are mandatory** for any crypto or format change

## License

TBD

## Acknowledgments

- Vision document based on the SecureVault project specification (v2, revised)
- Crypto design informed by [VeraCrypt](https://veracrypt.fr/) container format study
