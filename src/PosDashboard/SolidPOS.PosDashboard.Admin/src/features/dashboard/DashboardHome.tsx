import { useEffect, useState } from 'react';
import type { DashboardMetrics, LoginResponse } from '../../api/posServerClient';
import { PosServerClient } from '../../api/posServerClient';
import { Badge, Button, Card } from '../../components/ui';

const initialMetrics: DashboardMetrics = {
  health: 'unknown',
  totalSalesCents: 0,
  salesCount: 0,
  refundsCents: 0,
  pendingSync: 0,
  deadLetter: 0,
  lastUpdatedAt: new Date().toISOString()
};

export function DashboardHome({ session }: { session: LoginResponse }) {
  const [metrics, setMetrics] = useState<DashboardMetrics>(initialMetrics);
  const [message, setMessage] = useState('Dashboard listo para consultar PosServer.');

  async function refresh() {
    if (!session.accessToken) {
      setMessage('No hay access token activo.');
      return;
    }

    try {
      const client = new PosServerClient();
      const snapshot = await client.getOperationalSnapshot(session.accessToken);
      setMetrics(snapshot);
      setMessage('Snapshot actualizado desde PosServer.');
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'No se pudo actualizar dashboard.');
    }
  }

  useEffect(() => {
    void refresh();
  }, []);

  return (
    <div className="space-y-6">
      <div className="flex flex-col justify-between gap-4 md:flex-row md:items-center">
        <div>
          <h1 className="text-3xl font-bold tracking-tight">Overview operativo</h1>
          <p className="mt-1 text-slate-500">Base React para administración, monitoreo inicial y reportes.</p>
        </div>
        <Button onClick={refresh}>Actualizar</Button>
      </div>

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <MetricCard title="API ready" value={metrics.health} tone={metrics.health === 'ready' ? 'good' : 'warn'} />
        <MetricCard title="Ventas" value={metrics.salesCount.toString()} />
        <MetricCard title="Sync pending" value={metrics.pendingSync.toString()} tone={metrics.pendingSync === 0 ? 'good' : 'warn'} />
        <MetricCard title="Dead-letter" value={metrics.deadLetter.toString()} tone={metrics.deadLetter === 0 ? 'good' : 'warn'} />
      </div>

      <Card>
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-lg font-semibold">Estado de integración</h2>
            <p className="text-sm text-slate-500">{message}</p>
          </div>
          <Badge tone="good">Foundation</Badge>
        </div>
        <div className="mt-4 grid gap-3 text-sm text-slate-600 md:grid-cols-3">
          <p>Auth: /api/v1/auth/login</p>
          <p>Health: /health/ready</p>
          <p>Sync: /api/v1/sync/status</p>
        </div>
      </Card>
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
