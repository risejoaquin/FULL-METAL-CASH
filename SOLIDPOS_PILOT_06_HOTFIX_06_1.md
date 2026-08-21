# SolidPOS PILOT-06 HOTFIX 06.1 - PosCore bind fingerprint contract

## Estado

PENDING USER VALIDATION

## Motivo

PILOT-06 avanzo correctamente hasta el baseline local de PosCore, pero el comando `bind` fallo porque el contrato real de `SolidPOS.PosCore.Cli` exige `--fingerprint`.

Error observado:

```text
System.InvalidOperationException: Missing option --fingerprint.
```

## Cambio

Archivo corregido:

```text
scripts/pilot/validate-sync-recovery-conflict-field-test.ps1
```

Cambios aplicados:

- Se reutiliza el mismo fingerprint enviado a `/api/v1/auth/terminal/register`.
- El comando local `bind` ahora incluye `--fingerprint`.
- El wrapper `Invoke-PosCoreCli` ahora redacta valores sensibles al reportar errores:
  - `--terminal-token`
  - `--terminal-access-token`
  - `--access-token`
  - `--password`

## Decision tecnica

No se toca backend, PosCore, Dashboard ni migraciones. El fallo fue del validador piloto por no respetar el contrato real del CLI.

## Resultado esperado

El script debe superar:

```text
[PILOT-06] PosCore local runtime integrity baseline PASS
```

Y continuar con:

```text
[PILOT-06] Stuck processing recovery fixture...
```
