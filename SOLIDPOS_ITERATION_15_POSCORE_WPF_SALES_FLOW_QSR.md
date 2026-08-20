# SolidPOS Iteration 15 — PosCore WPF Sales Flow QSR

## Status

Pending local validation by user.

## Scope

This iteration converts the WPF shell foundation into the first QSR sales flow surface.

Implemented:

- QSR product selection from local catalog view model.
- Cart state.
- Cash payment preview.
- Change calculation through `LocalCashCalculator`.
- Visual offline sale status.
- Fake receipt print status.
- Visual sync status.
- Local cash shift expected-cash state.
- WPF self-test for the QSR sales flow.

## Architectural rule

WPF remains a presentation layer. It does not own sale persistence, sync processing, inventory mutation, cash drawer persistence, or hardware implementation.

```text
WPF Views
→ ViewModels
→ PosCore Application Services
→ PosCore Infrastructure SQLite / Hardware adapters
```

## Validation script

```text
scripts/poscore/validate-poscore-wpf-sales-flow-qsr.ps1
```

## Expected self-test result

```text
QSR totals: totalCents=4500; tenderedCents=5000; changeCents=500; expectedCashCents=14500
message = PosCore WPF sales flow QSR completed.
```
