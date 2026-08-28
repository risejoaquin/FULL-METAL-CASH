# LGA-03 Sync Stability Record

## Baseline

Los sync conflicts conocidos quedan bajo baseline formal. El dead letter histórico queda bajo baseline formal.

## Reglas

- sync conflicts must not increase sobre el baseline permitido.
- dead letter must not increase sobre el baseline permitido.
- retry pending debe ser zero.
- stale processing debe ser zero.
- schemaVersion debe permanecer en 4.
- syncContract debe permanecer en schema_version_4.
