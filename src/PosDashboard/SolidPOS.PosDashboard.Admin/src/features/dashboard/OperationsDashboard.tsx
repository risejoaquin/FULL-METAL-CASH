import type { LoginResponse, OperationsSnapshot } from '../../api/posServerClient';
import { formatMoney, hasPermission, PermissionCodes } from '../../api/posServerClient';
import { Badge, Card } from '../../components/ui';

export function OperationsDashboard({
  snapshot,
  session
}: {
  snapshot: OperationsSnapshot | null;
  session?: LoginResponse | null;
}) {
  const metrics = snapshot?.metrics;
  const operational = snapshot?.operationalMetrics;
  const readiness = snapshot?.readiness;
  const alerts = snapshot?.alerts?.alerts ?? [];
  const terminals = snapshot?.terminals ?? [];
  const versionAdoption = snapshot?.versionAdoption;
  const incidents = snapshot?.incidents;
  const sync = snapshot?.sync;

  const databaseReady = operational?.database?.ready === true || readiness?.database === 'ready' || readiness?.status === 'ready';
  const syncHealthy = (sync?.deadLetterCount ?? metrics?.deadLetter ?? 0) === 0 && (metrics?.pendingConflicts ?? 0) === 0;
  const requestHealthy = (metrics?.failedRequests ?? 0) === 0;

  const canManageTerminals = hasPermission(session, PermissionCodes.TerminalsManage);
  const canReadReports = hasPermission(session, PermissionCodes.ReportsRead);

  return (
    <div className="space-y-6">
      {/* 1. Incidents Banner */}
      {incidents && incidents.totalIncidents > 0 ? (
        <Card className="border-amber-200 bg-amber-50/50">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-lg font-semibold text-amber-900">Incident summary ({incidents.totalIncidents} active)</h2>
              <p className="text-sm text-amber-700">Condiciones operativas degradadas detectadas por alertas y colas de sincronización.</p>
            </div>
            <div className="flex gap-2">
              {incidents.criticalCount > 0 && <Badge tone="warn">{incidents.criticalCount} critical</Badge>}
              {incidents.warningCount > 0 && <Badge tone="neutral">{incidents.warningCount} warning</Badge>}
            </div>
          </div>
          <div className="mt-4 space-y-2">
            {incidents.incidents.map((inc) => (
              <div key={inc.id} className="flex items-start justify-between rounded-xl border border-amber-200/60 bg-white p-3 text-sm">
                <div>
                  <span className="font-semibold text-slate-800">{inc.title}</span>
                  <p className="mt-0.5 text-xs text-slate-600">{inc.detail}</p>
                </div>
                <Badge tone={inc.severity === 'critical' ? 'warn' : 'neutral'}>{inc.severity}</Badge>
              </div>
            ))}
          </div>
        </Card>
      ) : (
        <Card className="border-emerald-100 bg-emerald-50/30">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-lg font-semibold text-emerald-900">Incident summary</h2>
              <p className="text-sm text-emerald-700">Sin incidentes operacionales activos. Todos los subsistemas operando en rangos aprobados.</p>
            </div>
            <Badge tone="good">CLEAR</Badge>
          </div>
        </Card>
      )}

      {/* 2. Top Summary Tiles */}
      <div className="grid gap-4 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-lg font-semibold">Operations command center</h2>
              <p className="text-sm text-slate-500">Estado operativo general del tenant, API, base de datos y sync runtime.</p>
            </div>
            <Badge tone={metrics?.health === 'ready' ? 'good' : 'warn'}>{metrics?.health ?? 'unknown'}</Badge>
          </div>
          <div className="mt-5 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <OperationTile label="Ventas hoy" value={metrics?.salesCount.toString() ?? '0'} />
            <OperationTile label="Importe ventas" value={formatMoney(metrics?.totalSalesCents ?? 0)} />
            <OperationTile label="Reembolsos" value={formatMoney(metrics?.refundsCents ?? 0)} />
            <OperationTile label="Audit events" value={metrics?.auditEvents.toString() ?? '0'} />
          </div>
        </Card>

        {/* 3. API Health & Readiness */}
        <Card>
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-lg font-semibold">API health</h2>
              <p className="text-sm text-slate-500">/health/ready</p>
            </div>
            <Badge tone={readiness?.status === 'ready' ? 'good' : 'warn'}>{readiness?.status ?? metrics?.health ?? 'unknown'}</Badge>
          </div>
          <div className="mt-4 space-y-2 text-sm">
            <HealthRow label="Database latency" value={readiness ? `${Math.round(readiness.databaseLatencyMs)} ms` : '-'} tone="neutral" />
            <HealthRow label="Schema version" value={readiness?.schemaVersion ? `v${readiness.schemaVersion}` : 'v4'} tone="good" />
            <HealthRow label="Sync contract" value={readiness?.syncContract ?? 'schema_version_4'} tone="good" />
            <HealthRow label="Schema compat" value={readiness?.schemaCompatibility ?? 'compatible'} tone={readiness?.schemaCompatibility === 'incompatible' ? 'warn' : 'good'} />
            <HealthRow label="Storage readiness" value={readiness?.storageReadiness ?? 'healthy'} tone="good" />
          </div>
        </Card>
      </div>

      {/* 4. DB Health, API Monitor, and Sync Queues */}
      <div className="grid gap-4 lg:grid-cols-3">
        {/* DB Health */}
        <Card>
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-lg font-semibold">DB health</h2>
              <p className="text-sm text-slate-500">PostgreSQL central</p>
            </div>
            <Badge tone={databaseReady ? 'good' : 'warn'}>{databaseReady ? 'ready' : 'attention'}</Badge>
          </div>
          <div className="mt-4 space-y-3 text-sm">
            <HealthRow label="Active connections" value={metrics?.activeDbConnections ?? 0} tone="good" />
            <HealthRow label="Missing tables" value={operational?.database?.missingRequiredTables?.length ?? 0} tone={(operational?.database?.missingRequiredTables?.length ?? 0) === 0 ? 'good' : 'warn'} />
            <HealthRow label="Server version" value={operational?.database?.serverVersion ? operational.database.serverVersion.substring(0, 14) : 'PostgreSQL 16'} tone="neutral" />
          </div>
        </Card>

        {/* API Monitor */}
        <Card>
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-lg font-semibold">API monitor</h2>
              <p className="text-sm text-slate-500">Latencia y tasa de error middleware.</p>
            </div>
            <Badge tone={requestHealthy ? 'good' : 'warn'}>{requestHealthy ? 'healthy' : 'errors'}</Badge>
          </div>
          <div className="mt-4 space-y-3 text-sm">
            <HealthRow label="Total requests" value={operational?.requests?.totalRequests ?? 0} tone="good" />
            <HealthRow label="Failed requests" value={metrics?.failedRequests ?? 0} tone={requestHealthy ? 'good' : 'warn'} />
            <LatencyRow label="P95 latency" value={metrics?.requestP95LatencyMs ?? 0} />
          </div>
        </Card>

        {/* Sync Queues */}
        <Card>
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-lg font-semibold">Sync queues</h2>
              <p className="text-sm text-slate-500">/api/v1/sync/status</p>
            </div>
            <Badge tone={syncHealthy ? 'good' : 'warn'}>{syncHealthy ? 'clear' : 'recovery req'}</Badge>
          </div>
          <div className="mt-4 space-y-2 text-sm">
            <HealthRow label="Pending" value={sync?.pendingCount ?? metrics?.pendingSync ?? 0} tone={(sync?.pendingCount ?? 0) === 0 ? 'good' : 'warn'} />
            <HealthRow label="Processing" value={sync?.processingCount ?? 0} tone="neutral" />
            <HealthRow label="Processed" value={sync?.processedCount ?? metrics?.processedSync ?? 0} tone="good" />
            <HealthRow label="Retry pending" value={sync?.retryPendingCount ?? metrics?.retryPendingSync ?? 0} tone={(sync?.retryPendingCount ?? 0) === 0 ? 'good' : 'warn'} />
            <HealthRow label="Dead-letter" value={sync?.deadLetterCount ?? metrics?.deadLetter ?? 0} tone={(sync?.deadLetterCount ?? metrics?.deadLetter ?? 0) === 0 ? 'good' : 'warn'} />
            <HealthRow label="Oldest pending" value={sync?.oldestPendingAt ? new Date(sync.oldestPendingAt).toLocaleTimeString() : 'none'} tone="neutral" />
          </div>
        </Card>
      </div>

      {/* 5. Terminal Status & Version Adoption */}
      <div className="grid gap-4 lg:grid-cols-3">
        {/* Terminal Status Table */}
        <Card className="lg:col-span-2">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-lg font-semibold">Terminal status</h2>
              <p className="text-sm text-slate-500">Flota de terminales enrolados (/api/v1/terminals).</p>
            </div>
            <Badge tone={terminals.length > 0 ? 'good' : 'neutral'}>{terminals.length} enrolled</Badge>
          </div>

          {!canManageTerminals && (
            <p className="mt-3 rounded-xl bg-slate-100 p-3 text-xs text-slate-600">
              Visualización limitada: El usuario autenticado requiere permiso <code className="font-mono">terminals.manage</code> para administración completa.
            </p>
          )}

          <div className="mt-4 overflow-hidden rounded-xl border border-slate-100">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase text-slate-500">
                <tr>
                  <th className="p-3">Nombre</th>
                  <th className="p-3">Status</th>
                  <th className="p-3">Versión</th>
                  <th className="p-3">Último visto</th>
                </tr>
              </thead>
              <tbody>
                {terminals.length === 0 ? (
                  <tr>
                    <td colSpan={4} className="p-3 text-center text-slate-500">
                      Sin terminales enrolados o permiso de lectura restringido.
                    </td>
                  </tr>
                ) : (
                  terminals.map((t) => (
                    <tr key={t.id} className="border-t border-slate-100">
                      <td className="p-3 font-medium">{t.name}</td>
                      <td className="p-3">
                        <Badge tone={t.status.toLowerCase() === 'active' ? 'good' : 'warn'}>{t.status}</Badge>
                      </td>
                      <td className="p-3 text-slate-600">{t.appVersion ?? 'unknown'}</td>
                      <td className="p-3 text-slate-500">{t.lastSeenAt ? new Date(t.lastSeenAt).toLocaleString() : 'nunca'}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </Card>

        {/* Version Adoption */}
        <Card>
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-lg font-semibold">Version adoption</h2>
              <p className="text-sm text-slate-500">Distribución de versiones en la flota.</p>
            </div>
            <Badge tone="neutral">{versionAdoption?.serverVersion ?? 'v1.1-11'}</Badge>
          </div>
          <div className="mt-4 space-y-3 text-sm">
            <div className="flex items-center justify-between border-b border-slate-100 pb-2">
              <span className="text-slate-500">Total terminales</span>
              <span className="font-bold">{versionAdoption?.totalTerminals ?? 0}</span>
            </div>
            {versionAdoption?.versions && versionAdoption.versions.length > 0 ? (
              versionAdoption.versions.map((item) => (
                <div key={item.version} className="flex items-center justify-between rounded-xl border border-slate-100 p-3">
                  <span className="font-mono text-xs">{item.version}</span>
                  <div className="flex items-center gap-2">
                    <Badge tone="good">{item.count} POS</Badge>
                    <span className="text-xs text-slate-400">{item.percentage}%</span>
                  </div>
                </div>
              ))
            ) : (
              <p className="p-2 text-center text-xs text-slate-500">No hay versiones registradas aún.</p>
            )}
            {(versionAdoption?.unknownCount ?? 0) > 0 && (
              <div className="flex items-center justify-between rounded-xl border border-amber-100 bg-amber-50/50 p-3">
                <span className="text-xs text-amber-800">Versión desconocida</span>
                <Badge tone="warn">{versionAdoption?.unknownCount}</Badge>
              </div>
            )}
          </div>
        </Card>
      </div>

      {/* 6. Alert Summary */}
      <Card>
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-lg font-semibold">Alert summary</h2>
            <p className="text-sm text-slate-500">Evaluación continua de reglas de observabilidad (/api/v1/observability/alerts).</p>
          </div>
          <Badge tone={snapshot?.alerts?.anyActive ? 'warn' : 'good'}>{snapshot?.alerts?.anyActive ? 'ALERTS ACTIVE' : 'ALL NOMINAL'}</Badge>
        </div>

        {!canReadReports && (
          <p className="mt-3 rounded-xl bg-slate-100 p-3 text-xs text-slate-600">
            Requiere permiso <code className="font-mono">reports.read</code> para inspección completa de alertas de observabilidad.
          </p>
        )}

        <div className="mt-4 overflow-hidden rounded-xl border border-slate-100">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-xs uppercase text-slate-500">
              <tr>
                <th className="p-3">Código</th>
                <th className="p-3">Severidad</th>
                <th className="p-3">Estado</th>
                <th className="p-3">Observado</th>
                <th className="p-3">Umbral</th>
                <th className="p-3">Descripción</th>
              </tr>
            </thead>
            <tbody>
              {alerts.length === 0 ? (
                <tr>
                  <td colSpan={6} className="p-3 text-center text-slate-500">
                    Sin reglas de alerta registradas o endpoint no disponible.
                  </td>
                </tr>
              ) : (
                alerts.map((alt) => (
                  <tr key={alt.code} className="border-t border-slate-100">
                    <td className="p-3 font-mono text-xs font-semibold text-slate-800">{alt.code}</td>
                    <td className="p-3">
                      <Badge tone={alt.severity === 'critical' ? 'warn' : 'neutral'}>{alt.severity}</Badge>
                    </td>
                    <td className="p-3">
                      <Badge tone={alt.active ? 'warn' : 'good'}>{alt.active ? 'ACTIVE' : 'OK'}</Badge>
                    </td>
                    <td className="p-3 font-mono text-xs">{Math.round(alt.observed * 100) / 100}</td>
                    <td className="p-3 font-mono text-xs">
                      {alt.comparator} {alt.threshold}
                    </td>
                    <td className="p-3 text-xs text-slate-600">{alt.detail}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </Card>
    </div>
  );
}

function OperationTile({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-2xl bg-slate-50 p-4">
      <p className="text-xs uppercase tracking-wide text-slate-500">{label}</p>
      <p className="mt-2 text-2xl font-bold">{value}</p>
    </div>
  );
}

function HealthRow({ label, value, tone }: { label: string; value: string | number; tone: 'good' | 'warn' | 'neutral' }) {
  return (
    <div className="flex items-center justify-between rounded-xl border border-slate-100 p-3">
      <span>{label}</span>
      <Badge tone={tone}>{value}</Badge>
    </div>
  );
}

function LatencyRow({ label, value }: { label: string; value: number }) {
  const rounded = Math.round(value * 100) / 100;
  return (
    <div className="flex items-center justify-between rounded-xl border border-slate-100 p-3">
      <span>{label}</span>
      <Badge tone={rounded < 1000 ? 'good' : 'warn'}>{rounded} ms</Badge>
    </div>
  );
}
