import type { OperationsSnapshot } from '../../api/posServerClient';
import { Badge, Card } from '../../components/ui';

export function AuditDashboard({ snapshot }: { snapshot: OperationsSnapshot | null }) {
  const audit = snapshot?.audit ?? [];
  const critical = snapshot?.metrics.criticalAuditEvents ?? 0;

  return (
    <Card>
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">Audit log</h2>
          <p className="text-sm text-slate-500">Base para revisar eventos administrativos, seguridad y cambios críticos.</p>
        </div>
        <Badge tone={critical === 0 ? 'good' : 'warn'}>{critical} critical</Badge>
      </div>
      <div className="mt-4 overflow-hidden rounded-xl border border-slate-100">
        <table className="w-full text-left text-sm">
          <thead className="bg-slate-50 text-xs uppercase text-slate-500">
            <tr><th className="p-3">Action</th><th className="p-3">Entity</th><th className="p-3">Actor</th><th className="p-3">Time</th></tr>
          </thead>
          <tbody>
            {audit.length === 0 ? (
              <tr><td className="p-3 text-slate-500" colSpan={4}>Sin eventos cargados todavía. Cliente listo para /api/v1/audit/events.</td></tr>
            ) : audit.map((entry, index) => (
              <tr key={entry.auditId ?? entry.id ?? index} className="border-t border-slate-100">
                <td className="p-3 font-medium">{entry.action ?? 'unknown'}</td>
                <td className="p-3">{entry.entityType ?? '-'} {entry.entityId ? `/${entry.entityId}` : ''}</td>
                <td className="p-3">{entry.actorUserId ?? '-'}</td>
                <td className="p-3">{entry.occurredAt ?? entry.createdAt ?? '-'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  );
}
