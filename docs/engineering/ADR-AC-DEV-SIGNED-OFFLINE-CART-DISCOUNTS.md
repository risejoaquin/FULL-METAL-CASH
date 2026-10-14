# SolidPOS — signed offline cart discount provenance (AC-DEV)

**Status:** proposed and test-only, not connected to a real authority. **Production NO-GO.**
**Owner:** SolidBit engineering / PO review.
**Contracts:** remote `sale.completed` `schemaVersion=4`, `syncContract=schema_version_4` frozen.

## Problem and threat boundary

The deterministic offline discount calculator was sound on cents, but
`OfflineDiscountQuoteService.ApplyCartDiscount(sale, DiscountRule, payments)`
accepted any local rule value with no proof of provenance. An untrusted client
must never be able to label an arbitrary 100% discount as a centrally approved
promotion. Signed rules also need tenant/store/terminal isolation, rule
withdrawal, bounded offline freshness and replay protection.

## Implementation in this increment

- New `SignedOfflineCartDiscountRule` includes tenant, store, terminal,
  discount ID, **monotonic revision**, central issuance and validity timestamps,
  discount kind/decimal value, stacking/approval bits and trusted signer key ID.
- `PinnedOfflineDiscountSignatureVerifier` verifies ECDSA P-256 / SHA-256
  against **pinned SubjectPublicKeyInfo public keys only**, using versioned
  canonical binary bytes. The payload never carries a trusted public key.
  Reject unknown keys, malformed signatures, changed money, changed scope,
  changed rights and invalid validity periods. The allowed issued-to-expiry
  horizon is at most **7 days** (revised by policy only).
- `SQLiteSignedOfflineCartDiscountRuleCache` is a new **opt-in** SQLite
  table. A signed rule can only be persisted for its existing terminal
  binding; a later revision supersedes an earlier one transactionally. Decimal
  bits are stored losslessly so signature verification survives restart.
- `SignedOfflineCartDiscountCheckout.PublishAsync` verifies before cache
  insertion. `ApplyAsync` looks up the rule by exact sale scope, re-verifies
  the signature and central validity on each checkout, blocks expired/future
  promotions and declines all approval-required discounts. After those checks,
  the previously tested v4-compatible calculator materializes exact cents and
  original rule ID into each sale line, never changing the remote contract.
- The direct unsigned `ApplyCartDiscount` helper is **internal** to the
  PosCore Application assembly; compatibility fixtures access it via a
  test-only `InternalsVisibleTo` declaration. The public finalization
  route for this new feature is the signed-checkout service.

## Test matrix

Disposable local SQLite + test-generated ephemeral ECDSA P-256 signer.
The fixture includes **no production signing secrets**:

1. Signed cart promotion survives SQLite restart with exact decimal bits,
   exact discounted money and unchanged sale.completed v4 event.
2. Invalid signature, changed scope, value, kind or signer ID rejected.
3. Cross-store/terminal issue and checkout rejected.
4. Revision downgrade/replay rejected even after higher revision is issued.
5. Centrally signed future/expired windows cannot be redeemed.
6. Direct cache row tampering detected at each checkout by signature.
7. Supervisor-required discount remains fail-closed offline.
8. Replacing the pinned key with an unrelated signer rejects old signatures.

## Unresolved production prerequisites

1. No PosServer **signed discount issuance endpoint** or dedicated production
   signing key/KMS exists yet. Test private keys must **never** ship with POS.
2. Trusted public-key enrollment, rotation and revocation need an approved
   deployment and key-lifecycle design. Signing and public-key configuration
   should come from verified application provisioning, not terminal-supplied
   JSON. A compromised local OS or direct DB write can delete/rollback cache
   rows; strong rollback resistance requires additional trusted persistent
   state. The current monotonic guard is transactional against normal stale
   import, **not** tamper-proof hardware storage.
3. Rule-revocation events and online reconciliation are not implemented.
   A **signed seven-day cached rule** might remain usable until expiry if a
   central admin revokes it while the terminal is offline. Configure tighter
   lifetimes as required by business risk.
4. The live WPF checkout has **not yet been wired** to the new service.
   This increment demonstrates a verified local path, not a fully integrated
   real-world sale path. The central PosServer still revalidates the persisted
   discount independently when it receives sync v4.
5. Automatic supervisor approval and stacks of cart + line promotions remain
   blocked by the frozen v4 representation / unapproved policy.
6. Production UAT on real printer/cash drawer, partial refunds, tax/receipts
   and risk/incident controls remains mandatory.

## Related audit intake

The external audit report from the user's parallel chat is **not received**.
When uploaded, reconcile its findings against the relevant commits, tests,
scope and exact CI SHA. Do not claim external audit approval before review.

No merge, undraft, branch protection change, production database write,
Railway deployment or live payment connection is authorized by this ADR.
