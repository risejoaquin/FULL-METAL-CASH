# SolidPOS — G1 session/cart regression readiness (SOLIDPOS-20261014T20)

Date: 2026-10-15T03:01:18Z. Scope: review-only QA evidence, no functional code changes.

## Reconciled source
- Base: PR #15 head e11958238932e0ffc93942bb093cd191e42acd46, draft against AC-DEV.
- AC-DEV: a2961f4e14ebe0a18ec1c6a93db5ca3b6ac73655, unchanged in this run.
- Source: src/PosCore/SolidPOS.PosCore.Wpf/ViewModels/ProductionCartViewModel.cs at PR #15 head; explicit session ID and checkout lock checks exist.
- Three candidate regressions from T18 recovery are not present in GitHub. This note does not claim they were merged or executed by dotnet.

## Checks actually executed
- Command: python verify_static.py, Linux Python, against the recovered T18 candidate outside the repository.
- Result: 12/12 static source assertions PASS; not .NET compilation, runtime tests, SQLite integration, Windows UI or printer certification.
- Windows CI for e11958238932e0ffc93942bb093cd191e42acd46: SolidPOS CI run 38644942458 SUCCESS, predates the missing tests.

## Required next gate
1. Publish three deterministic session/checkout race tests on an authorized branch, without changing schemaVersion 4 or syncContract schema_version_4.
2. Run Windows .NET 8 tests on the exact new commit SHA; fix and rerun failures.
3. Review PR #15, then separately certify touch checkout, receipt printer, installer/restore and supervised 3-day pilot.
4. PO approval is mandatory before merge, deployment, hardware/pilot or contact with any prospect.

Acceptance matrix: 38 scenarios / 100 weighted points (71 automated category, 29 manual category). This run certifies zero new points; commercial completion remains N/D, NO-GO.

## Tool limitation
A GitHub create_file attempt for tests/SolidPOS.PosCore.UnitTests/ProductionCartSessionRaceTests.cs was blocked by integration safety checks; no C# file or new runtime test was published. The isolated branch is documentation-only until verified otherwise.
