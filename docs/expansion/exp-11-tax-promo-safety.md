# EXP-11 Tax and Promotion Safety

## Tax mode

Supported product tax mode values are:

- taxable
- exempt

EXP-11 validates a controlled product with tax mode `exempt` to avoid changing production tax behavior.

## Promotion safety

Promotion safety means catalog/pricing operations must not mutate historical sales, refunds, receipts, or cash drawer totals.

## Do not mutate historical sales

Catalog pricing operations are forward-looking. They affect runtime catalog data and future sales only. Historical sale rows and receipt totals remain immutable operational evidence.

## Follow-up

Full promotion operations remain outside EXP-11 unless the tenant has promotion endpoints enabled and validated in a separate phase.
