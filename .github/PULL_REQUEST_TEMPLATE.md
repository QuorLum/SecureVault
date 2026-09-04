## Description
Briefly explain the intent, design decisions, and context of your changes.

## Type of Change
- [ ] Bug fix (non-breaking change fixing an issue)
- [ ] New feature (non-breaking change adding functionality)
- [ ] Breaking change (fix or feature causing existing functionality/vault format to change)
- [ ] Documentation update
- [ ] Performance / Refactor

## Cryptographic & Format Invariants Checklist
If this PR touches anything in `Crypto/`, `Format/`, or `Integrity/`:
- [ ] **Deterministic Test Vector Diff Included:** Non-negotiable per CONTRIBUTING.md.
- [ ] **Zero Disk-Write Preserved:** Plaintext is never spooled to physical disk.
- [ ] **Nonce Uniqueness Verified:** Fresh random 12-byte CSPRNG nonces per write.
- [ ] **Memory Zeroing:** Sensitive buffers pinned in unmanaged memory and zeroed on disposal.

## Quality & Testing
- [ ] Solution builds with 0 errors and 0 warnings: `dotnet build src/SecureVault.sln -c Release -p:Platform=x64`
- [ ] Complete test suite passes (all 123 tests): `dotnet test tests/SecureVault.Core.Tests/SecureVault.Core.Tests.csproj`
- [ ] Added new unit tests or test vectors covering modified behavior.
