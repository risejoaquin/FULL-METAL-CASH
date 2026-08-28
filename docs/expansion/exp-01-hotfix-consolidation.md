# EXP-01 Pilot Hotfix Consolidation Matrix

Status: PENDING USER VALIDATION

## Purpose

Consolidate pilot hotfixes into one post-pilot baseline so the next phase starts from a clean and documented release state.

## Hotfixes consolidated

| Area | Hotfixes | Consolidated baseline rule |
| --- | --- | --- |
| PILOT-02 | 02.1 to 02.6 | Validators must accept real API shapes and SQL contracts without brittle assumptions. |
| PILOT-04 | 04.1 | Receipt email stub statuses are valid when explicitly accepted by contract. |
| PILOT-05 | 05.1 to 05.4 | PowerShell parsing, encoding and recipe SQL contracts must match the real runtime. |
| PILOT-06 | 06.1 | PosCore bind requires fingerprint and must not leak tokens. |
| PILOT-07 | 07.1 to 07.2 | Empty conflict lists are valid and psql args must be ordered correctly. |
| PILOT-08 | 08.1 to 08.3 | PostgreSQL 17 tooling, restore extensions and TCP container restore are required. |
| PILOT-10 | 10.1 to 10.4 | Expansion docs and SQL must match real production schema, including pos.return_refunds. |

## Baseline hardening rules

- Do not require non-existent schema tables.
- Do not fail on empty but valid endpoint lists.
- Do not use stale LASTEXITCODE.
- Do not use psql -c with -f style arguments.
- Use PostgreSQL 17 tooling against PostgreSQL 17 production.
- Accept documented stub behavior where the product intentionally uses a stub.
- Keep validators scoped to tenant/store/phase context.

## Outcome

EXP-01 freezes the corrected state after all pilot hotfixes.
