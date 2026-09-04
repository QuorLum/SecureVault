# Security Policy

## Supported Versions

Only the latest release of SecureVault receives security updates and vulnerability patches.

| Version | Supported          |
| ------- | ------------------ |
| 1.0.x   | :white_check_mark: |
| < 1.0.0 | :x:                |

## Cryptographic Guarantees & Threat Model

SecureVault is built around explicit cryptographic invariants:
- **AEAD Authenticated Encryption**: AES-256-GCM with 12-byte CSPRNG random nonces per 1MB chunk.
- **Key Derivation**: Argon2id with memory-hard parameters and HKDF-SHA256 subkey extraction.
- **Zero Plaintext on Physical Disk**: All media playback, document viewing, image decoding, and editing are executed strictly in non-swappable unmanaged memory buffers.
- **Forward Error Correction**: Reed-Solomon $RS(255, 223)$ symbol correction for bit rot resilience.

Before reporting a security issue, please review the [Threat Model in README.md](README.md#threat-model) to verify whether the observed behavior falls within SecureVault's designated threat boundaries.

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues, discussions, or pull requests.**

If you believe you have discovered a vulnerability in SecureVault (especially involving cryptographic flaws, nonce reuse, side-channel leaks, or memory zeroing failures):

1. **GitHub Private Vulnerability Reporting**:
   Navigate to the [Security Advisories tab](https://github.com/QuorLum/SecureVault/security/advisories) on GitHub and click **"Report a vulnerability"**. This creates an encrypted, confidential channel between you and the maintainers.
2. **Details to Include**:
   - Detailed description of the vulnerability.
   - Reproduction steps or deterministic proof-of-concept.
   - Potential impact on confidentiality, integrity, or key security.
   - Any suggested mitigations.

## Disclosure Process

- Maintainers will acknowledge receipt within 48 hours.
- A private patch will be developed and verified against the deterministic test suite.
- Once confirmed, an advisory and updated release will be published simultaneously.
