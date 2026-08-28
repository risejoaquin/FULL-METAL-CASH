# LGA-11 Package Manifest

Base package: `solidpos-platform-lga-10-limited-ga-commercial-operations-confidence-gate-20260828.zip`

## New LGA-11 files

| File | Bytes | SHA-256 |
|---|---:|---|
| `LGA_11_VALIDATION_COMMANDS.md` | 2438 | `fde783d599c602d1071a23dfc25b32efefab6693918f36f5e540bf94295bdfca` |
| `SOLIDPOS_LGA_11_LIMITED_GA_PUBLIC_GA_READINESS_REASSESSMENT.md` | 1397 | `cb99118e78275e770f8c1c25f18cd300d063815f5344148bf64c6fb633bb608c` |
| `docs/ga/lga-11-limited-ga-public-ga-readiness-reassessment.md` | 1909 | `2ba8f736dd275bb910d51557ab37b92e83ce5a751a1da80bf1bf834cac66fd4c` |
| `docs/ga/lga-11-public-ga-readiness-reassessment-plan.md` | 1266 | `6c0c0cb2d702ed47ac3b671b3313bda888c91504c1a3e9e82f25adc3a6c308e7` |
| `docs/ga/lga-11-public-ga-readiness-checklist.md` | 1089 | `de822f92014773f565dd7792aeb2bf42c21e53ee0ceea523981c2d7e776ef41c` |
| `docs/ga/lga-11-evidence-matrix.md` | 1456 | `cebdb7dd788838fad1b898d6a74695edb88799cbe5cf05df3a021a1b84bcc000` |
| `scripts/ga/validate-lga-11-limited-ga-public-ga-readiness-reassessment.ps1` | 28919 | `35b6ecb39cf0bd5d2382fe9a65606ab405a40032fd900632f7d162caf86881d7` |
| `scripts/ga/lga-11-limited-ga-public-ga-readiness-reassessment-check.sql` | 7081 | `f3fe3871ed39d353d9968b7d890367719e6d787bc2e2356fddb58fde64b02b45` |

## Guardrails

- Public GA activation is not implemented by this package.
- `PublicGaDecision` is restricted to `KEEP_LIMITED_GA`.
- `schemaVersion = 4` / `schema_version_4` remains mandatory.
- Negative stock baseline remains 0.
- Waiting connections baseline remains 12.
- If the capacity probe fails, `KEEP_LIMITED_GA` + `CAPACITY_UPGRADE_REQUIRED_BEFORE_PUBLIC_GA` is mandatory.
- LGA-12 is not authorized until LGA-11 PASS logs are reviewed.
