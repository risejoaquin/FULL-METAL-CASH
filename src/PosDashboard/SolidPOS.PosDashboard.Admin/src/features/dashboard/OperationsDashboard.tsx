import type { OperationsSnapshot } from '../../api/posServerClient';
import { formatMoney } from '../../api/posServerClient';
import { Badge, Card } from '../../components/ui';

export function OperationsDashboard({ snapshot }: { snapshot: OperationsSnapshot | null }) {
  const metrics = snapshot?.metrics;

  return (
    <div className="grid gap-4 lg:grid-cols-3">
      <Card className="lg:col-span-2">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-lg font-semibold">Operations command center</h2>
            <p className="text-sm text-slate-500">Estado operativo general del tenant y del runtime cloud.</p>
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
          <HealthRow label="Dead-letter" value={snapshot?.sync.deadLetterCount ?? 0} tone={(snapshot?.sync.deadLetterCount ?? 0) === 0 ? 'good' : 'warn'} />
          <HealthRow label="Failed" value={snapshot?.sync.failedCount ?? 0} tone={(snapshot?.sync.failedCount ?? 0) === 0 ? 'good' : 'warn'} />
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

function HealthRow({ label, value, tone }: { label: string; value: number; tone: 'good' | 'warn' }) {
  return (
    <div className="flex items-center justify-between rounded-xl border border-slate-100 p-3">
      <span>{label}</span>
      <Badge tone={tone}>{value}</Badge>
    </div>
  );
}
