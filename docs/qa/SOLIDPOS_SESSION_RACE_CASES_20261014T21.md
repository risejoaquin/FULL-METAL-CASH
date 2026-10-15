# SolidPOS — G1 deterministic session-race regression specification

Run: SOLIDPOS-20261014T21. Date: 2026-10-15 UTC. Status: **QA DESIGN ONLY; runtime tests not published or executed**.

## Baseline and scope
- Starting branch commit: `737b33f0c5e8bf1cf8670863cf2652e90d7d7b4e` (continuation from T20).
- Application source blob: `6f090b53cf3919ab784489816c8a4b6b4740f791`, `src/PosCore/SolidPOS.PosCore.Wpf/ViewModels/ProductionCartViewModel.cs`.
- Existing tests blob: `233e190d10f3de49794291a192c72889bc7667bc`, `tests/SolidPOS.PosCore.UnitTests/ProductionCheckoutViewModelTests.cs`.
- PR #15 is Draft against AC-DEV. Existing code guards stale catalog responses by session ID and checks checkout lock after awaited read.
- No schemaVersion 4 or syncContract schema_version_4 changes.

## Three deterministic runtime tests to publish when authorized
1. **Logout while lookup pending.** Use disposable SQLite to create a real local login; inject a delayed `ILocalPosRepository.GetCatalogProductBySkuAsync` response into the cart; start `AddBySkuAsync`, log out, complete the delayed response. Assert no line, zero total, disabled editing, and session-change status.
2. **Logout and re-login as same operator while lookup pending.** Capture first session ID, begin delayed lookup, log out and log in again, confirm session IDs differ, then complete lookup. Assert no cart mutation or cross-session transfer.
3. **Checkout lock while lookup pending.** Begin delayed lookup with valid session, call `SetCheckoutLocked(true)`, then complete lookup. Assert no line, zero total, disabled editing, and lock status.

Fixture: isolated SQLite file, fictional SKU and operator, real local authentication, deterministic `TaskCompletionSource<LocalCatalogProduct?>` with asynchronous continuations; no network, real payment, customer, printer or deployment. The three tests must be compiled and run in Windows .NET 8 CI on their **own final code SHA**, not inferred from existing green CI.

## Source-only checks actually executed in this run
Environment: GitHub connector + JavaScript source invariant assertions on the two blobs above (not `dotnet test`). **11/11 source assertions PASS**: session ID captured, SKU trimmed, awaited lookup, post-await session comparison, stale-result clear, checkout lock after await, checkout lock before mutation, busy reset in finally, existing checkout lock test present, existing SQLite checkout test present, and no delayed-session test in the inspected checkout test file. The proposed new regression file was independently checked as absent from the T20 branch.

## CI provenance
- `737b33f0c5e8bf1cf8670863cf2652e90d7d7b4e`: SolidPOS CI #38679153604, completed SUCCESS, https://github.com/risejoaquin/FULL-METAL-CASH/actions/runs/38679153604 . Windows .NET 8 job SUCCESS; this SHA **does not contain the three proposed tests**.
- This documentation commit: CI pending or N/D until separately checked on its exact SHA.

## Blocker and next permitted action
An attempt to create `tests/SolidPOS.PosCore.UnitTests/ProductionCartSessionRaceTests.cs` on the isolated T21 branch was rejected with `This tool call was blocked by OpenAI's safety checks`. This is **not** a confirmed HTTP 403 and does not justify changing repository permissions or bypassing branch protection. The candidate C# test design is not a committed, compiled, or executed regression.

Reattempt code publication only when the connector permits it; execute Windows CI on the resulting code SHA; correct and revalidate any failures. Independently continue fictional beverage touch-demo readiness. Physical printer, Windows operator UAT, installer/restore, supervised 3-day pilot and explicit PO GO remain unverified. Commercial gate **NO-GO**. Matrix: 38 scenarios / 100 weighted points (71 automated category, 29 manual category); **0 newly certified points** this run; total acceptance N/D.

No merge, production, deployment, customer contact, brand usage, secrets or expenses.
