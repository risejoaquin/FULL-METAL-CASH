# LGA-03 Runbook

1. Ejecutar checkpoint diario.
2. Revisar manifest de `.runtime/lga-03-limited-ga-multi-day-stability-burn-in`.
3. No activar Public GA.
4. Si aparece blocker, detener avance y clasificar como producción, datos o validator.
5. Al tercer día, correr el mismo validator con `-FinalizeBurnIn`.
