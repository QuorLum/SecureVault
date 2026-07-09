# SecureVault — Implementation Roadmaps

This directory contains detailed implementation roadmaps for each development phase
of SecureVault. Each roadmap is a complete specification that a coding agent or
developer can follow mechanically to implement the phase.

## Reading Order

Read the phases in order — each depends on all prior phases:

| Phase | File | Scope | Feature Count |
|-------|------|-------|---------------|
| 1 | [phase-1-foundation.md](phase-1-foundation.md) | Vault core engine, crypto, format, integrity | ~50 features |
| 2 | [phase-2-ui.md](phase-2-ui.md) | Basic UI, file operations, organization, cache | ~35 features |
| 3 | [phase-3-integrated-apps.md](phase-3-integrated-apps.md) | Gallery, video/audio player, notes, PDF viewer | ~25 features |
| 4 | [phase-4-advanced.md](phase-4-advanced.md) | Advanced features across all apps, thumbnails, file manager | ~60 features |
| 5 | [phase-5-backup-multivault.md](phase-5-backup-multivault.md) | Backup/restore, multi-vault chain | ~30 features |
| 6 | [phase-6-polish.md](phase-6-polish.md) | Themes, advanced security, recovery, compaction | ~35 features |

**Total: ~205 features across 6 phases**

## What Each Roadmap Contains

For every feature ID, each roadmap specifies:

1. **Module & file placement** — exact `src/` paths and dependency order
2. **Data structures** — field names, types, byte layouts (for format/crypto items)
3. **Function signatures** — parameters, return types, step-by-step algorithm
4. **Exact library calls** — specific classes/methods for crypto and format-critical code
5. **Test plan** — concrete test cases with input values and expected output
6. **Verification checklist** — plain-language yes/no checks for non-expert review

Plus:
- Source file tree for the phase
- Test vector filenames for `tests/vectors/`
- Branch name and PR description
- CONTRIBUTING notes specific to that phase
- STATUS.md entries

## Open Questions

Ambiguities from the vision doc are surfaced as `⚠️ OPEN QUESTION` items with
2-3 options and tradeoffs. These must be resolved before implementation begins.
Key open questions are concentrated in Phase 1 (crypto decisions).

## Branch Naming

```
phase-1/vault-core
phase-2/basic-ui
phase-3/integrated-apps
phase-4/advanced-features
phase-5/backup-multivault
phase-6/polish
```

## Reference Documents

- [Vision document (v2)](../vision.md) — complete project specification
- [Feature status tracker](../STATUS.md) — current state of all ~205 features
