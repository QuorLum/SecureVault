# Contributing to SecureVault

Thank you for your interest in contributing to SecureVault! SecureVault is an encrypted single-container and multi-vault storage system engineered for zero-knowledge data protection, resilience against bit rot, and high-performance direct streaming.

Because SecureVault handles sensitive cryptographic operations and raw container file systems, code quality, security guarantees, and format stability are strictly enforced.

---

## 1. Non-Negotiable Core Rule

> [!IMPORTANT]
> **Changes to anything in `Crypto/`, `Format/`, or `Integrity/` require a test-vector diff in the PR, not just passing existing tests.**

If your PR touches any cryptographic primitives, container file header/footer/chunk schemas, Reed-Solomon erasure coding, or integrity verification logic:
- You must supply deterministic, cross-verified test vectors in `tests/vectors/` or `tests/SecureVault.Core.Tests/`.
- A passing existing test suite is necessary but insufficient on its own; test vector verification ensures long-term container backward compatibility and cryptographic predictability.

---

## 2. Development Workflow & Pull Request Process

1. **Fork & Branch**:
   - Create a dedicated feature branch with a descriptive conventional prefix:
     - `feat/` for new functionality
     - `fix/` for bug fixes
     - `chore/` for tooling or dependency updates
     - `docs/` for documentation
2. **Coding Standards**:
   - Write clean, modern C# targeting .NET 8.
   - All builds must compile with **0 errors and 0 warnings** under `TreatWarningsAsErrors`.
   - Never commit commented-out code, temporary debug logging, or hardcoded secrets/keys.
   - Code comments should explain *why* non-obvious algorithmic or concurrency decisions exist, not restate *what* the code does.
3. **Commit Messages**:
   - Use conventional commit style: `type(scope): concise description`.
   - Examples:
     - `feat(format): add secondary chunk header validation`
     - `fix(io): resolve seek collision in concurrent file streams`
4. **Verification**:
   - Run the full test suite locally before opening a PR:
     ```bash
     dotnet test tests/SecureVault.Core.Tests/SecureVault.Core.Tests.csproj
     ```
   - Ensure all tests pass with 100% success rate.
5. **Review & CI**:
   - Open your PR against the `main` branch.
   - Provide a clear PR description detailing:
     - Problem addressed
     - Architectural decisions
     - Threat model implications
     - Test vector diffs (if touching `Crypto/`, `Format/`, or `Integrity/`)

---

## 3. Security Disclosures

If you discover a security vulnerability or cryptographic flaw in SecureVault, please **do not open a public GitHub issue**. Instead, report it confidentially via GitHub Security Advisories or by emailing the project maintainers directly.
