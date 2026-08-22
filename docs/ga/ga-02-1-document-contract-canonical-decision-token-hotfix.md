# HOTFIX GA-02.1 — Document Contract Canonical Decision Token

## Status
`PENDING USER VALIDATION`

## Failure corrected
GA-02 stopped in repository/document guardrails before any production query because the validator required the prose phrase `close as historical evidence`, while the main GA-02 document used equivalent wording without that exact phrase.

## Root cause
A machine gate depended on mutable human prose instead of the stable decision identifier already defined by the GA-02 dead-letter contract.

## Correction
- The canonical identifier `close_as_historical_evidence` is now declared in the main GA-02 delivery document.
- The GA-02 validator checks that stable identifier instead of prose spacing/wording.
- All other document contract terms were audited and already matched.

## Scope
No database, schema, sync state, inventory ledger, sales, payments, tenant isolation, API, or production data change. The failure happened before production access.

## Gate
GA-02 remains `PENDING USER VALIDATION` until the real validator produces:

`PASS GA SYNC QUEUE SLA CLOSURE / GO GA-03`
