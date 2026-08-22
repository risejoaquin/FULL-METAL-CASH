# GA-05 Go / No-Go

Status: **PASS REAL PRODUCTION**

## GO
GO requires:
- fresh GA-04 PASS;
- stable production release count remains zero;
- valid beta rollback baseline;
- Release build/tests PASS;
- deterministic publish fingerprints equal;
- real Velopack Setup.exe and release package produced;
- Authenticode main executable and installer verification PASS;
- SHA-256/provenance/dependency inventory/retention evidence present;
- packageType velopack;
- universal installer true;
- rollback version not null;
- mandatory false;
- tenant scoped true;
- schemaVersion 4;
- GA not activated.

GO output:
`PASS GA STABLE RELEASE CANDIDATE BUILD SIGNING PROVENANCE / GO GA-06`

## NO-GO
Any missing/invalid signature, non-reproducible publish, package failure, missing rollback, stable promotion drift, schema drift, test failure or provenance failure results in:

`FAIL / HOTFIX REQUIRED`
