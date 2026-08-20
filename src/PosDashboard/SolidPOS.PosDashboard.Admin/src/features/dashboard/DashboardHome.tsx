import { useEffect, useState } from 'react';
import type { LoginResponse, OperationsSnapshot } from '../../api/posServerClient';
import { PosServerClient, formatMoney } from '../../api/posServerClient';
import { Badge, Button, Card } from '../../components/ui';
import { AuditDashboard } from './AuditDashboard';
import { OperationsDashboard } from './OperationsDashboard';
import { ReportsDashboard } from './ReportsDashboard';

export type DashboardSection = 'Overview' | 'Reports' | 'Operations' | 'Audit';

export function DashboardHome({ session, activeSection }: { session: LoginResponse; activeSection: DashboardSection }) {
  const [snapshot, setSnapshot] = useState<OperationsSnapshot | null>(null);
  const [message, setMessage] = useState('Dashboard listo para consultar PosServer.');
  const [isRefreshing, setIsRefreshing] = useState(false);

  async function refresh() {
    if (!session.accessToken) {
      setMessage('No hay access token activo.');
      return;
    }

    setIsRefreshing(true);
    try {
      const client = new PosServerClient();
      const nextSnapshot = await client.getOperationsSnapshot(session.accessToken);
      setSnapshot(nextSnapshot);
      setMessage('Operations snapshot actualizado desde PosServer.');
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'No se pudo actualizar dashboard.');
    } finally {
      setIsRefreshing(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, []);

  return (
    <div className="space-y-6">
      <div className="flex flex-col justify-between gap-4 md:flex-row md:items-center">
        <div>
          <h1 className="text-3xl font-bold tracking-tight">Reports / Audit / Operations Dashboard</h1>
          <p className="mt-1 text-slate-500">Reportes, auditoría y operación diaria con lectura protegida por token administrativo.</p>
        </div>
        <Button onClick={refresh} disabled={isRefreshing}>{isRefreshing ? 'Actualizando...' : 'Actualizar'}</Button>
      </div>

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <MetricCard title="API ready" value={snapshot?.metrics.health ?? 'unknown'} tone={snapshot?.metrics.health === 'ready' ? 'good' : 'warn'} />
        <MetricCard title="Ventas" value={snapshot?.metrics.salesCount.toString() ?? '0'} />
        <MetricCard title="Total ventas" value={formatMoney(snapshot?.metrics.totalSalesCents ?? 0)} />
        <MetricCard title="Dead-letter" value={snapshot?.metrics.deadLetter.toString() ?? '0'} tone={(snapshot?.metrics.deadLetter ?? 0) === 0 ? 'good' : 'warn'} />
      </div>

      <Card>
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-lg font-semibold">Estado de integración</h2>
            <p className="text-sm text-slate-500">{message}</p>
          </div>
          <Badge tone="good">Iteration 20</Badge>
        </div>
        <div className="mt-4 grid gap-3 text-sm text-slate-600 md:grid-cols-3">
          <p>Reports: /api/v1/sales, /api/v1/returns</p>
          <p>Operations: /health/ready, /api/v1/sync/status</p>
          <p>Audit: /api/v1/audit</p>
        </div>
      </Card>

      {activeSection === 'Overview' && <OperationsDashboard snapshot={snapshot} />}
      {activeSection === 'Reports' && <ReportsDashboard snapshot={snapshot} />}
      {activeSection === 'Operations' && <OperationsDashboard snapshot={snapshot} />}
      {activeSection === 'Audit' && <AuditDashboard snapshot={snapshot} />}
    </div>
  );
}

function MetricCard({ title, value, tone = 'neutral' }: { title: string; value: string; tone?: 'neutral' | 'good' | 'warn' }) {
  return (
    <Card>
      <p className="text-sm text-slate-500">{title}</p>
      <div className="mt-3 flex items-center justify-between">
        <p className="text-2xl font-bold">{value}</p>
        <Badge tone={tone}>{tone}</Badge>
      </div>
    </Card>
  );
}
