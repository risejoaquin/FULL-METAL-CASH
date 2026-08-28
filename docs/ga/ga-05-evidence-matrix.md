# GA-05 Evidence Matrix

| Gate | Evidence | Required result |
|---|---|---|
| Entry | fresh GA-04 manifest | PASS / GO GA-05, blockers `{}` |
| Production release state | GA-05 SQL snapshot | active stable = 0, valid active beta rollback baseline >= 1 |
| Source | repository SHA-256 + optional Git commit | non-empty immutable source reference |
| Build | Release restore/build/test | PASS |
| Reproducibility | publish A/B payload fingerprints | equal SHA-256 fingerprints |
| Runtime integration | PosCore WPF Velopack package + startup hook | Velopack 1.2.0 + `VelopackApp.Build().Run()` |
| Packaging | `vpk pack` output | Setup.exe + `.nupkg` + `releases.stable.json` |
| Main executable signing | Authenticode evidence | Valid + expected signer thumbprint |
| Installer signing | Authenticode evidence | Valid + expected signer thumbprint |
| Artifact integrity | signed Setup.exe SHA-256 | 64-char SHA-256 |
| Rollback | latest valid active beta version | non-null |
| Update safety | candidate metadata | mandatory false, tenantScoped true, promotion false |
| Schema compatibility | SQL + metadata | schemaVersion 4, schema_version_4 |
| Dependencies | `dotnet list package --include-transitive --format json` | retained inventory |
| Provenance | ga-05-provenance.json | source/build/sign/artifact/compatibility fields present |
| Retention | ga-05-artifact-retention.json | >= 30 days; default 180; automaticDeletion false |
| GA state | SQL/manifest | generalAvailabilityActivated false |

A validation-only self-signed identity may demonstrate cryptographic readiness, but provenance must explicitly record `productionSigningRequiredBeforePublicPromotion = True`.
