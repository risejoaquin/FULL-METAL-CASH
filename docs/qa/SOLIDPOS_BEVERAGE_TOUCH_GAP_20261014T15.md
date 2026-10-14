# SolidPOS — G1 beverage touch-flow gap audit (2026-10-14)

Status: **analysis only / NOT DEMO_READY**. Scope: one fictional beverage counter. Source baseline: `AC-DEV` at `a2961f4e14ebe0a18ec1c6a93db5ca3b6ac73655`. No customer brand, hardware or real payments.

## Evidence from current source

| Area | Observed code | Gap to validate |
| --- | --- | --- |
| Commercial WPF | `src/PosCore/SolidPOS.PosCore.Wpf/MainWindow.xaml`, production `Activación / Estado real` tab | Product selection requires typed SKU; no touch-first category/product tiles or beverage modifier flow. |
| Cart | `ProductionCartViewModel.AddBySkuAsync` reads `GetCatalogProductBySkuAsync`, preserves `ProductId`, `VariantId`, `PriceCents` | Can add a sellable SKU, but no size/topping selector or authoritative modifier pricing demonstrated. |
| Checkout | `ProductionCheckoutViewModel` and `AtomicCashCheckoutService` | Existing cash/shift and uncertain-checkout controls must be reused, not replaced with demo `SalesViewModel`. |
| Simulator separation | `AppShellViewModel.SimulationTabVisibility` hides `Venta QSR` in commercial mode | Never move fake receipt/sync commands into production UI. |
| Data/contract | `schemaVersion: 4`, `syncContract: schema_version_4` | No new modifier/recipe schema or API payload without approved ADR and compatibility proof. |

## Smallest authorized P1 slice after the pending P0 session-race regression

1. Add **touch-first selection UI** for existing sellable catalog SKUs (categories/product tiles), keeping the current `AddBySkuAsync` and checkout pipeline as the sole source of financial state.
2. Use **fictional fixture SKUs** to represent sizes as existing catalog variants or separate priced SKUs *only if existing domain and sync contracts support it*. Do not infer recipes, topping inventory, taxes or modifier persistence.
3. Gate all buttons with the authenticated session, checkout lock and asynchronous session fence. Ensure 44x44 px minimum touch targets, visible MXN total and error feedback. Monetary state remains integer cents; UI formatting must not change stored values.
4. Test: logout during lookup; operator switch; checkout lock; unknown SKU; inactive SKU; non-MXN currency; price changed before commit; two drinks/quantity; cash/change; restart/recovery; no duplicate sale. Run Windows .NET 8 CI on exact feature SHA and capture actual tests. WPF physical/touch UAT is separate.

## G1 demo acceptance (fictitious data only)

- Operator authenticates and opens a shift in disposable SQLite.
- Selects a drink and size through touch targets; any toppings shown must be modeled by an approved contract or clearly marked unsupported.
- Cart shows quantity, itemized total and cash/change; persisted checkout creates exactly one sale, cash movement and outbox record.
- Simulated crash/retry does not duplicate a sale; no fake receipt or remote ACK appears.
- Evidence: exact commit SHA, Windows CI logs, SQLite assertions and an actual WPF operator recording if available. No CI-only claim of physical printer/installer acceptance.

## Risks / decisions

- **P0:** PR #15 session-race regressions not committed as of this audit. Do not ship touch UI before guard is covered by tests.
- **P1:** Size/topping pricing and inventory model not proven. Map existing `LocalCatalogProduct` and `OfflineSaleLineDraft` before modifying contracts.
- **Manual:** printer model, paper format, operator UAT, 3-day supervised pilot, real store scope and PO GO remain pending.
- **Commercial:** Oasis is a prospect only; no logo, contact, deployment, payment or live trial authorized.

Matrix reference: `docs/engineering/solidpos-mvp-acceptance-matrix.json` (38 scenarios / 100 points; 71 automated weight and 29 manual weight **by classification**, not 71% commercial acceptance). Next gate: G1 `DEMO_READY` only after executable evidence.
