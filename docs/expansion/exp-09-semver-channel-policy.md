# EXP-09 — SemVer and Release Channel Policy

## SemVer policy
SolidPOS releases use SemVer-compatible version strings:

```text
MAJOR.MINOR.PATCH[-prerelease]
```

Examples:

```text
0.10.0-exp09.20260821
0.10.0
1.0.0
```

## Channels
- `internal`: candidate validation channel for controlled production verification.
- `beta`: limited customer beta channel.
- `stable`: production channel for approved releases.

## Promotion rule
A release moves from internal/candidate to stable only after smoke test, migration preflight, rollback evidence, release notes, and support GO/NO-GO are PASS.

## Mandatory releases
Mandatory releases are blocked during limited expansion unless security or data-integrity incident response explicitly approves them.
