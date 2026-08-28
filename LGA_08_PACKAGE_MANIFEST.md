# LGA-08 Package Manifest

## Source package

- SHA-256: `0e322fd1525726f0dd88a1c7d1d7b2812b804df6b85c2952218abdb134235948`
- Source: `solidpos-platform-lga-07-hotfix-07-4-1-document-contract-fix-20260827(1).zip`

## Added files and SHA-256

| File | SHA-256 |
|---|---|
| `LGA_08_VALIDATION_COMMANDS.md` | `0e31b0103b0a7b570f8ba0b007d112cc895d7fd13a9f842a42f747c9356de98c` |
| `SOLIDPOS_LGA_08_LIMITED_GA_POST_UPGRADE_VERIFICATION_OR_CONTINUED_MONITORING.md` | `e08ff41c30ea5a4fd7dd059d69468da5e4e1f43f2a2dbe88abd1638397a15cd9` |
| `docs/ga/lga-08-limited-ga-post-upgrade-verification-or-continued-monitoring.md` | `f31c14d942a20574453a3db98e6b344236805161d3aee511e20e611880027826` |
| `docs/ga/lga-08-post-upgrade-verification-plan.md` | `c416c21f101b81c4c0f76f9a1fd71a7d69e05ef630e3424343519ac88c23b19e` |
| `docs/ga/lga-08-continued-monitoring-checklist.md` | `ed8059d09d0afe0a268cdf8774f06ef6c5d27a27930a557a3b9809db71cdc866` |
| `docs/ga/lga-08-evidence-matrix.md` | `38be070aae053d08188eb8e1340ad5d1d92f5eda5eb2891cbd017875f5e70a4b` |
| `scripts/ga/validate-lga-08-limited-ga-post-upgrade-verification-or-continued-monitoring.ps1` | `d46526cf62a2c1bf2a1593ccf8fcb78b2c8dc4beef3bf3fb74dc00b2dd4113cf` |
| `scripts/ga/lga-08-limited-ga-post-upgrade-verification-or-continued-monitoring-check.sql` | `1e47cf0f10971f5b609d893c4b4018b69c4b752bf2ebd190ec172f81165615fc` |

## Package controls

- Public GA remains `NOT_ACTIVATED`.
- LGA-08 default mode is `CONTINUED_MONITORING`.
- Post-upgrade mode requires a passing capacity probe.
- Waiting connection baseline remains 12 and is not raised.
- Negative stock baseline remains 0.
- Sync remains schema version 4.
- No subsequent phase is authorized until LGA-08 PASS logs are reviewed.
