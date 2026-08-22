# GA-04 Production Data Integrity and Financial Reconciliation

GA-04 revalidates GA-03 fresh and then performs a read-only PostgreSQL reconciliation.

## Sales / Payments
- sale/payment reconciliation
- approved payment consistency
- sale line subtotal/discount/tax/total consistency
- paid/change consistency
- returned sale linkage
- orphan approved payments

## Receipts
- active receipt-to-sale consistency
- public token integrity
- receipt number/token uniqueness

## Returns / Refunds
- completed return/refund pairing
- refund totals
- return-line totals
- original sale links

## Cash
- open shifts
- stale shifts
- counted vs expected
- difference formula consistency
- recent cash differences

## Inventory
- negative inventory
- ledger consistency
- sale/return reference integrity
- reconciliation adjustments remain append-only
- recipes
- modifiers
- substitute semantics

## Catalog / Pricing
- negative/invalid prices
- invalid price windows
- taxes
- modifier behavior
- invalid references

## Users / Access
- orphan roles
- invalid store access
- inactive user/store relationships

## Sync / Audit
- retry queue remains closed
- no pending conflicts
- schema v4 only
- no new dead-letter since GA-03
- historical dead-letter decision evidence remains present
- audit evidence remains present
