# SolidPOS — Discount financial engine, offline replay and QA evidence

**Date:** 2026-10-13. **Scope:** development only on `AC-DEV`.
**GO/NO-GO:** NO-GO for production pending specified security and UAT gates.
**Invariants:** `schemaVersion=4`, `syncContract=schema_version_4`,
existing PostgreSQL migration allowlist and all V1.1 static validators are unchanged.

## Implemented executable code

- A small, dependency-free `SolidPOS.DiscountEngine` library is referenced by
  **both PosCore and PosServer**. All amounts are integer cents; percentage
  calculation uses `MidpointRounding.AwayFromZero`, fixed discounts are whole
  cents and capped to available line/cart value.
- `DiscountCalculator` supports line- and cart-scoped fixed/percentage rules,
  deterministic stacking (explicit non-stackable refusal), independent verified
  supervisor identity for rules flagged as approval-required, replay-stable
  line identity and **largest-remainder** allocation of cart reductions
  after line reductions. Sum of allocations = discounts; gross - discounts
  = net, without negative nets or floating-point cent loss.
- `OfflineDiscountQuoteService.Preview` calculates read-only offline quotes
  without altering sale or payment. `ApplyCartDiscount` can materialize a
  single, non-approval-required **globally scoped** cart rule into the existing
  `OfflineSaleLineDraft.DiscountId` and `DiscountCents` fields. It rejects
  stacking with previously discounted lines, and demands exact tender after
  the discount. The unchanged `sale.completed` v4 payload carries the
  preserved rule ID on every affected line, even when a particular allocation
  rounds to zero.
- PosServer's PostgreSQL sale creation keeps server prices/modifiers as the
  source of truth. Existing independently valid line discounts retain their
  legacy semantics. For amounts that do *not* match the legacy per-line
  calculation, only a **single** active/global, store-eligible discount used
  across **all** cart lines can be accepted; the server recalculates the
  exact cart allocation and refuses any altered line amount. This is an
  additive interpretation of the current v4 fields: no new JSON keys,
  database table or version negotiation.
- `SyncV4CartDiscountVerifier` independently exposes that financial proof
  to targeted unit tests, while the repository checks actual scope and
  discount eligibility in its existing tenant-scoped transaction.
- `DiscountsService.ValidateAsync` uses exactly the same shared formula
  and fails closed for unusable legacy discount values. Persisted rules
  cannot be created with a fractional fixed amount expressed in cents.
- `POST /api/v1/discounts/quote` is a **PREVIEW-ONLY** endpoint guarded by
  `discounts.validate`. It resolves discount IDs through server-side,
  tenant-filtered persisted definitions; never trusts a caller's discount
  value, user ID, current store, or claimed category eligibility. However,
  gross prices in the quote request are client supplied: this endpoint
  **never** authorizes a sale or payment.
- `DiscountRefundCalculator` computes cumulative partial-return bases:
  discounted net and savings reverse independently without sub-cent drift,
  and all refunds of a sold line sum exactly to its original discounted
  amount. It is a quotation helper, NOT an approved refund or payment ledger.

## Automated tests

`DiscountEngineFinancialTests`: line/cart rounding, deterministic ordering,
stacking, fixed caps, user separation, malformed and duplicate inputs, cent
conservation and no negative net.

`OfflineDiscountQuoteTests`: preview-only operation, deterministic IDs,
POS/server shared quote parity, exact checkout tender, refusal of unsupported
stacking, and real SQLite persistence plus unchanged sync-v4 outbox JSON.

`DiscountEngineParityTests`: server single-line legacy amount vs shared quote,
plus multi-line receipt/refund basis equality.

`DiscountCartQuoteServiceTests`: actual persisted-rule lookup abstraction,
cross-store rejection, product/category scope refusal, inactive rules,
duplicate rule refusal, and no payment mutation.

`SyncV4CartDiscountVerifierTests`: valid percentage/fixed cart allocations
and rejection of modified allocations even when the total is unchanged.

`DiscountRefundFinancialTests`: multiple partial refunds, sub-cent edge cases,
over-return blocking and invariant conservation.

### Reproducible validation

```powershell
dotnet restore .\solidpos-platform.sln
dotnet build .\solidpos-platform.sln -c Release --no-restore
dotnet test .\solidpos-platform.sln -c Release --no-build
```

All CI must be verified against the final commit SHA; intermediate runs that
were cancelled by newer commits are **not** evidence of PASS.

## Important remaining blockers to unrestricted use

1. **Approval authority:** the pure computation kernel requires independent
   verified approvals for flagged rules, but the actual central discount
   database has no approved `requires_approval` flag or end-to-end
   supervisor-authenticated application flow. The offline checkout method
   explicitly rejects approval-required rules; existing active legacy rules
   do not automatically become approval-required. No arbitrary approval
   bypass or fabricated production policy is permitted.
2. **Rule provenance:** offline `ApplyCartDiscount` expects a trusted
   centrally provisioned/cached rule but a signed/verified discount-rule
   distribution channel is not part of this change. A client with modified
   cache data may produce an offline sale that PosServer subsequently rejects;
   operations must reconcile rather than silently settle it.
3. **Mixed stacking:** a cart discount on top of a separately identified
   line discount requires multiple rule IDs on the same v4 sale line.
   This cannot be represented unambiguously by the frozen contract.
   Preview supports combined line/cart discounts, but settlement refuses
   this combination until a reviewed versioned contract/ADR exists.
4. **Category discounts:** server quotes refuse rules scoped to a category
   because the client-supplied category is not a trusted product mapping;
   normal product-verified sale creation already checks category in SQL.
5. **Refund payment workflows:** reversal calculation is implemented, but
   applying partial refunds to the live provider/ledger must use the
   existing trusted refund APIs, transactional checks, and UAT.
6. **PostgreSQL end-to-end:** CI's ephemeral PostgreSQL role fixture is not
   an exercised real PostgreSQL sale+discount acceptance/inventory/payment
   integration test. That gate remains open.
7. **Receipt/report rendering, WPF UI, stock and historical backfill:** verify
   behavior in a dedicated acceptance/UAT workflow before expanding release
   scope. No migration or automatic production deployment is authorized.

**Engineering decision:** do not merge or deploy this unreviewed, partially
gated commercial change. Prioritize approvals, provenance and an isolated
full-stack PostgreSQL acceptance test, then request PO GO/NO-GO.
