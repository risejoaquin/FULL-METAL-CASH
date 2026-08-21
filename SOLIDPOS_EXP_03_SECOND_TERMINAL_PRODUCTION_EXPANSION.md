# SolidPOS EXP-03 — Second Terminal Production Expansion

Status: PENDING USER VALIDATION

EXP-03 validates controlled production expansion to a second terminal in the existing production tenant and store.

## Scope

- admin login and protected metrics.
- second terminal enrollment.
- terminal registration.
- sync bootstrap for the second terminal.
- independent cash shift.
- controlled real POS sale from the second terminal.
- digital receipt.
- cash shift close with zero difference.
- dashboard monitoring and audit evidence.
- SQL cross-check scoped to the new terminal.
- GO/NO-GO for EXP-04.

## Safety

This is a real production expansion drill. It creates one new terminal, one controlled sale, one cash shift and one digital receipt. It does not modify existing terminal credentials and does not delete production data.

## Expected result

```text
SolidPOS EXP-03 — Second Terminal Production Expansion = PASS REAL PRODUCTION / GO
```


## HOTFIX 03.1

Corrected EXP-03 SQL cross-check to use the real inventory ledger contract: `reference_type = 'sale'` and `reference_id = sale_id` instead of non-existent `inventory_ledger.sale_id`.

## HOTFIX 03.2 — Audit Entity UUID/Text Cast Contract

EXP-03 HOTFIX 03.2 corrects the final SQL cross-check by casting flexible reference identifiers to text before comparison:

```sql
il.reference_id::text = p.sale_id::text
ae.entity_id::text = p.sale_id::text
```

This keeps EXP-03 aligned with the production schema where audit entity identifiers may be stored as UUID while some validator parameters are normalized through text.

## HOTFIX 03.3 — SQL Cross-Check Hardening

Se auditó y corrigió el SQL cross-check final para remover supuestos no válidos sobre inventario:

- No existe `pos.inventory_current` en el contrato real.
- El stock actual se valida desde `pos.inventory_ledger` / `pos.inventory_stock`.
- Se mantiene cast explícito UUID/text en referencias de auditoría y ledger.

Estado: PENDING USER VALIDATION.
