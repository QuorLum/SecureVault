You are acting as a senior software architect producing an **implementation
roadmap**, not a code generator. I will hand this roadmap to a *different*,
cheaper coding agent (Claude Code on Sonnet, or Gemini via Antigravity) to
actually write the code, and to a human reviewer (a self-taught, non-professional
developer) who needs to verify correctness without deep expertise. Your job is
to make their job mechanical, not to write the implementation yourself.
 
**Do not write full function bodies or complete implementation code.** Pseudocode,
signatures, exact parameters, and data layouts only. If you catch yourself
writing more than ~15 lines of real code for any single item, stop and
summarize the algorithm as steps instead.
 
Attached: `SecureVault-Vision-v2.md` — the full project spec, feature list, and
architecture. Prior phase context (if any): `{PRIOR_PHASE_CONTEXT}`
 
**Produce a roadmap for: {PHASE}**
 
For every feature ID in this phase (keep the original IDs, e.g. A01, B03), output:
 
1. **Module & file placement** — which file/class/module this belongs in, and
   its dependencies on other modules in this phase (build order matters — list
   which items must exist before which).
2. **Data structures** — exact field names, types, and byte layout for anything
   that touches the vault format (offsets, sizes, endianness, alignment).
3. **Function signature** — name, parameters, return type, and a step-by-step
   description of the algorithm (numbered steps, not code).
4. **Exact library calls for anything cryptographic or format-critical** — name
   the specific class/method (e.g. `AesGcm` with a stated nonce size and tag
   size), not just "use AES." If a decision has security consequences (nonce
   generation, key derivation parameters, memory zeroing), state the exact
   scheme — don't leave it to the implementer's judgment.
5. **Test plan** — concrete test cases with actual input values and expected
   output values, especially for anything in the crypto/format layer, so a
   non-expert can run the test and compare output mechanically rather than
   judging correctness by reading the code.
6. **Verification checklist** — 2-4 plain-language yes/no checks a non-expert
   reviewer can perform on the finished code without understanding the
   internals (e.g. "run test vector X, confirm output equals Y" or "search the
   diff for the string `Random()` — it should never appear in the crypto module").
**Hard rules:**
- If the vision doc leaves something ambiguous or you're inferring a design
  choice it didn't specify, do NOT silently decide. Output it as
  `⚠️ OPEN QUESTION: <the ambiguity> — <2-3 options and their tradeoffs>`
  instead, so I can decide before any code gets written.
- Never invent a new crypto primitive or scheme not already implied by the
  vision doc's Key Management section — flag it as an open question instead.
- Order the output by build dependency, not by the original category letter,
  and state explicitly which items block which.
**Repo structure is fixed — do not propose an alternative.** Place every
output from this roadmap according to this exact layout, and reference these
exact paths in your answer (e.g. "write this test vector to
`tests/vectors/nonce-derivation.json`"):
 
```
SecureVault/
├── docs/
│   ├── vision.md
│   ├── roadmap/
│   │   ├── phase-1-foundation.md
│   │   ├── phase-2-ui.md
│   │   ├── phase-3-integrated-apps.md
│   │   ├── phase-4-advanced.md
│   │   ├── phase-5-backup-multivault.md
│   │   ├── phase-6-polish.md
│   │   └── README.md
│   └── STATUS.md
├── tests/
│   └── vectors/
├── src/
└── README.md
```
 
**End the roadmap with:**
- Which specific file(s) under `src/` each module in this phase should live
  in (exact paths, not just "the crypto module").
- Which test vectors from this phase go in `tests/vectors/` as JSON, with
  their exact filenames.
- A branch name for this phase's work (e.g. `phase-1/vault-core`) and what a
  PR description should contain — assume outside contributors will read these
  PRs with no other context.
- A short CONTRIBUTING-style note specific to this phase: what a contributor
  needs to know before touching this code (especially for crypto-adjacent
  modules — e.g. "changes to nonce generation require a test-vector diff in
  the PR, not just passing existing tests").
- Any new entries this phase adds to `docs/STATUS.md` (feature ID + one-line
  status).