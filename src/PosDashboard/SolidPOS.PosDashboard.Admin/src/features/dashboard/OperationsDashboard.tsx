import type { OperationsSnapshot } from '../../api/posServerClient';
import { formatMoney } from '../../api/posServerClient';
import { Badge, Card } from '../../components/ui';

export function OperationsDashboard({ snapshot }: { snapshot: OperationsSnapshot | null }) {
  const metrics = snapshot?.metrics;
  const operational = snapshot?.operationalMetrics;
  const databaseReady = operational?.database?.ready === true && operational.database.requiredTablesPresent !== false;
  const syncHealthy = (metrics?.pendingConflicts ?? 0) === 0 && (metrics?.deadLetter ?? 0) === 0;
  const requestHealthy = (metrics?.failedRequests ?? 0) === 0;

  return (
    <div className="space-y-4">
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

        <Card>
          <h2 className="text-lg font-semibold">Sync health</h2>
          <div className="mt-4 space-y-3 text-sm">
            <HealthRow label="Pending" value={snapshot?.sync.pendingCount ?? 0} tone={(snapshot?.sync.pendingCount ?? 0) === 0 ? 'good' : 'warn'} />
            <HealthRow label="Processed" value={snapshot?.sync.processedCount ?? 0} tone="good" />
            <HealthRow label="Dead-letter" value={snapshot?.sync.deadLetterCount ?? metrics?.deadLetter ?? 0} tone={(snapshot?.sync.deadLetterCount ?? metrics?.deadLetter ?? 0) === 0 ? 'good' : 'warn'} />
            <HealthRow label="Failed" value={snapshot?.sync.failedCount ?? 0} tone={(snapshot?.sync.failedCount ?? 0) === 0 ? 'good' : 'warn'} />
          </div>
        </Card>
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card>
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-lg font-semibold">Database monitor</h2>
              <p className="text-sm text-slate-500">/api/v1/observability/metrics database.</p>
            </div>
            <Badge tone={databaseReady ? 'good' : 'warn'}>{databaseReady ? 'ready' : 'attention'}</Badge>
          </div>
          <div className="mt-4 space-y-3 text-sm">
            <HealthRow label="Active connections" value={metrics?.activeDbConnections ?? 0} tone="good" />
            <HealthRow label="Missing tables" value={operational?.database?.missingRequiredTables?.length ?? 0} tone={(operational?.database?.missingRequiredTables?.length ?? 0) === 0 ? 'good' : 'warn'} />
          </div>
        </Card>

        <Card>
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-lg font-semibold">API monitor</h2>
              <p className="text-sm text-slate-500">Latencia y errores capturados por middleware.</p>
            </div>
            <Badge tone={requestHealthy ? 'good' : 'warn'}>{requestHealthy ? 'healthy' : 'errors'}</Badge>
          </div>
          <div className="mt-4 space-y-3 text-sm">
            <HealthRow label="Total requests" value={operational?.requests?.totalRequests ?? 0} tone="good" />
            <HealthRow label="Failed requests" value={metrics?.failedRequests ?? 0} tone={requestHealthy ? 'good' : 'warn'} />
            <LatencyRow label="P95 latency" value={metrics?.requestP95LatencyMs ?? 0} />
          </div>
        </Card>

        <Card>
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-lg font-semibold">Conflict monitor</h2>
              <p className="text-sm text-slate-500">Conflictos, retry y DLQ para operación diaria.</p>
            </div>
            <Badge tone={syncHealthy ? 'good' : 'warn'}>{syncHealthy ? 'clear' : 'attention'}</Badge>
          </div>
          <div className="mt-4 space-y-3 text-sm">
            <HealthRow label="Pending conflicts" value={metrics?.pendingConflicts ?? 0} tone={(metrics?.pendingConflicts ?? 0) === 0 ? 'good' : 'warn'} />
            <HealthRow label="Resolved conflicts" value={metrics?.resolvedConflicts ?? 0} tone="good" />
            <HealthRow label="Retry pending" value={metrics?.retryPendingSync ?? 0} tone={(metrics?.retryPendingSync ?? 0) === 0 ? 'good' : 'warn'} />
            <HealthRow label="Dead-letter" value={metrics?.deadLetter ?? 0} tone={(metrics?.deadLetter ?? 0) === 0 ? 'good' : 'warn'} />
          </div>
        </Card>
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card>
          <h2 className="text-lg font-semibold">Sales latency</h2>
          <div className="mt-4 space-y-3 text-sm">
            <HealthRow label="Sales last 24h" value={metrics?.salesLast24Hours ?? 0} tone="good" />
            <LatencyRow label="Persist avg ms" value={operational?.sales?.averagePersistLatencyMsLast24Hours ?? 0} />
            <LatencyRow label="Sales API P95 ms" value={operational?.sales?.apiP95LatencyMs ?? 0} />
          </div>
        </Card>

        <Card>
          <h2 className="text-lg font-semibold">Payment monitor</h2>
          <div className="mt-4 space-y-3 text-sm">
            <HealthRow label="Failed payments 24h" value={metrics?.failedPaymentsLast24Hours ?? 0} tone={(metrics?.failedPaymentsLast24Hours ?? 0) === 0 ? 'good' : 'warn'} />
            <HealthRow label="Declined payments 24h" value={operational?.payments?.declinedPaymentsLast24Hours ?? 0} tone={(operational?.payments?.declinedPaymentsLast24Hours ?? 0) === 0 ? 'good' : 'warn'} />
          </div>
        </Card>

        <Card>
          <h2 className="text-lg font-semibold">Inventory risk</h2>
          <div className="mt-4 space-y-3 text-sm">
            <HealthRow label="Negative stock items" value={metrics?.negativeInventoryItemCount ?? 0} tone={(metrics?.negativeInventoryItemCount ?? 0) === 0 ? 'good' : 'warn'} />
            <HealthRow label="Low stock items" value={metrics?.lowStockItemCount ?? 0} tone={(metrics?.lowStockItemCount ?? 0) === 0 ? 'good' : 'warn'} />
          </div>
        </Card>
      </div>
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

function HealthRow({ label, value, tone }: { label: string; value: number; tone: 'good' | 'warn' }) {
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
