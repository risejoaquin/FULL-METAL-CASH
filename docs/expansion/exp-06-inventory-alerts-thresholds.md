# EXP-06 Inventory Alerts and Thresholds

| Metric | Threshold | Owner | Action |
| --- | --- | --- | --- |
| negative inventory | 0 after reconciliation | Operations | run reconciliation and investigate root cause |
| low stock threshold coverage | every stock-tracked product should have threshold per active store | Operations | add missing threshold |
| pending conflicts | 0 | Engineering | resolve before inventory reconciliation |
| dead letter sync | known and triaged | Engineering | keep in incident queue |
| retry pending sync | monitored | Engineering | verify retry recovery |
| invalid substitute modifier | 0 | Catalog/Admin | fix `replaces_product_id` and consumption fields |
| invalid recipe item | 0 | Catalog/Admin | fix quantity/unit/product references |

## Daily review

Inventory health must be reviewed after store expansion, terminal expansion and sync recovery events.
