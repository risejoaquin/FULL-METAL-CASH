# SolidBit — Protocolo de confiabilidad del ciclo horario (v1.0)

**Estado:** propuesta documental sujeta a revisión (no concede permiso de merge, implementación, despliegue ni gastos).  
**Ámbito:** cada ejecución de ChatGPT Automation / GitHub del proyecto.  
**Precedencia:** decisiones explícitas del Product Owner > gobernanza, contratos congelados y autorizaciones actuales > este protocolo. En SACL, `IMPLEMENTATION_AUTHORIZED=NO` sigue vigente.

## 1. Contrato obligatorio de cada ejecución

1. Definir `run_id` determinista como `<proyecto>-<YYYYMMDDTHH>` en `America/Hermosillo`; no inventar resultados. Consultar primero SHA de HEAD, PR, CI por SHA y último checkpoint; detectar tareas humanas y duplicados. Si `run_id` ya existe, conciliar y completar únicamente pasos faltantes.
2. Ejecutar trabajo **permitido**: seleccionar gate/slice no duplicado, diseñar → implementar (solo si autorizado) → probar → corregir → validar → preparar PR; si bloqueado, avanzar labores independientes permitidas. Si no puede modificar código, registrar auditoría, documentación, pruebas autorizadas o evidencia de bloqueo.
3. Al final preparar entrada append-only en `docs/ITERATION_STATE.md`. Registrar también ejecuciones sin cambios y fallidas: omitirlas falsea el indicador. Leer SHA y blob SHA inmediatamente antes de escribir; usar ramas autorizadas y protección optimista; nunca sobrescribir contenido humano ni hacer force push.
4. Releer y **verificar que GitHub devolvió el commit**; indicar SHA final, URL comprobable y nueva entrada. `commit intentado` ≠ `checkpoint persistido`; `CI solicitado` ≠ `CI PASS`. Un check verde solo vale para el SHA comprobado.
5. Estados permitidos: `VERIFIED_PASS`, `VERIFIED_FAIL`, `PARTIAL`, `BLOCKED_HUMAN`, `BLOCKED_TOOL`, `NOOP_AUTHORIZED` y `CHECKPOINT_NOT_PERSISTED`. No registrar progreso técnico 100% sin criterios/denominador.

## 2. Esquema mínimo de checkpoint

```yaml
run_id: "PROJECT-YYYYMMDDTHH"
scheduled_at: "YYYY-MM-DDTHH:MM:SS-07:00"
started_at_utc: null
finished_at_utc: null
project: null
gate: null
state: "PARTIAL"
branch: null
base_sha: null
final_sha: null
po_concurrent_changes: []
files_changed: []
pull_requests: []
tests_executed: [] # comando, entorno, SHA, resultado
ci_checks: []      # URL, SHA, conclusion; N/D si no consultable
acceptance: {passed: null, total: null, evidence: []}
blocked: []        # tipo, causa, dependencia, siguiente acción
checkpoint: {persisted: false, url: null, commit_sha: null}
escalation: {required: false, fingerprint: null, email_sent: false}
next_allowed_action: null
```

Completar datos reales; null/N/D cuando no exista evidencia. Mantener historial de iteraciones bajo el encabezado correspondiente; nunca reescribir entradas anteriores.

## 3. Recuperación y persistencia alternativa

- **Fallo de escritura por conflicto 409/422:** refrescar HEAD y blob SHA; recomponer el append sin perder lo nuevo, y reintentar una vez sobre rama de trabajo; no reemplazar la versión ajena.
- **403/401, herramientas no disponibles o write timeout:** **NO** cambiar a `main` ni usar vías alternativas que eludan gobernanza. Probar solo una ruta de respaldo autorizada y distinta: comentario append-only en PR o issue de continuidad preexistente, cuando los permisos realmente lo permitan; indicar `BACKUP_ONLY` y URL comprobable.
- Si ambas persistencias fallan, devolver el checkpoint completo en la respuesta, etiquetado **CHECKPOINT NO PERSISTIDO**, con causa/error exacto y `run_id`; no afirmar continuidad garantizada. En el próximo ciclo reconciliar GitHub y reintentar por `run_id`; nunca duplicar PR o commits.
- En cada ciclo, contabilizar `persisted / attempted`, `runs with verified CI`, `BLOCKED_HUMAN` y `recovered` sin inflar implementaciones por reportes.

## 4. Escalamiento humano seguro

Para un bloqueo **que realmente requiera acción del PO** (aprobación, acceso, documento, decisión, verificación manual o coste), **detener solo esa dependencia** y continuar trabajo independiente.

- Fingerprint estable: `<repo>|<gate>|<type>|<resource>`.
- Consultar si el incidente ya fue notificado; máximo un aviso inicial por fingerprint durante 24 horas, salvo cambio material/urgencia real. No enviar resúmenes horarios ni alertas por fallos transitorios recuperables.
- Usar **Gmail conectado** únicamente si la cuenta autenticada es `uniquedraken@gmail.com`; enviar a `lawlieeet@outlook.es` con asunto `[SOLIDBIT][ACCION REQUERIDA][PROYECTO] <gate> — <fingerprint breve>`.
- Incluir: bloqueador exacto, evidencia SHA/PR/CI y URL, quién debe actuar, instrucciones de pasos específicos y lugar donde realizarlos, qué archivo aportar, impacto, trabajo que continúa, y cómo reanudar. Nunca incluir tokens, contraseñas, datos de clientes ni secretos.
- Confirmar `email_sent: true` **solo después del ID de envío**. Si falla Gmail, registrar `ESCALATION_PENDING` sin afirmar que se notificó, y reintentar en la próxima ejecución tras comprobación antduplicados. No contactar prospectos/clientes.
- El correo de escalamiento se autoriza por la petición expresa del PO de corregir las alertas, **no** concede autorización de merge, deploy, pagos ni implementación prohibida.

## 5. Condición de aceptación

Se considera implementado este control solo si una ejecución futura demuestra **A)** checkpoint nuevo persistido con SHA/URL, **B)** recuperación real o simulación controlada documentada, y **C)** alerta de bloqueo enviada (o prueba controlada no intrusiva / trazabilidad explícita de que nunca ocurrió un bloqueo). Este documento por sí solo no certifica la automatización.

## 6. Prohibiciones

No modificar `main` como workaround, no merge protegido, no despliegue/producción, no cambios de contratos congelados, no datos reales del prospecto, no gastos o secretos; SACL preserva `IMPLEMENTATION_AUTHORIZED=NO`.
