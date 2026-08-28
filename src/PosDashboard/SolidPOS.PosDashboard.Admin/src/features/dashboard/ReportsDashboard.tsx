import type { OperationsSnapshot } from '../../api/posServerClient';
import { formatMoney } from '../../api/posServerClient';
import { Badge, Card } from '../../components/ui';

export function ReportsDashboard({ snapshot }: { snapshot: OperationsSnapshot | null }) {
  const sales = snapshot?.sales ?? [];
  const returns = snapshot?.returns ?? [];

  return (
    <div className="grid gap-4 xl:grid-cols-2">
      <Card>
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-lg font-semibold">Reporte de ventas</h2>
            <p className="text-sm text-slate-500">Lectura protegida de /api/v1/sales por rango operativo.</p>
          </div>
          <Badge tone="neutral">/api/v1/sales</Badge>
        </div>
        <div className="mt-4 overflow-hidden rounded-xl border border-slate-100">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-xs uppercase text-slate-500">
              <tr><th className="p-3">Venta</th><th className="p-3">Status</th><th className="p-3 text-right">Total</th></tr>
            </thead>
            <tbody>
              {sales.length === 0 ? <EmptyRow label="Sin ventas cargadas todavía." /> : sales.map((sale, index) => (
                <tr key={sale.saleId ?? sale.id ?? index} className="border-t border-slate-100">
                  <td className="p-3 font-medium">{sale.saleNumber ?? sale.saleId ?? sale.id ?? `sale-${index + 1}`}</td>
                  <td className="p-3">{sale.status ?? 'completed'}</td>
                  <td className="p-3 text-right">{formatMoney(sale.totalCents ?? sale.amountCents ?? 0)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>

      <Card>
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-lg font-semibold">Returns / refunds</h2>
            <p className="text-sm text-slate-500">Lectura protegida de /api/v1/returns para control operativo.</p>
          </div>
          <Badge tone="neutral">/api/v1/returns</Badge>
        </div>
        <div className="mt-4 overflow-hidden rounded-xl border border-slate-100">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-xs uppercase text-slate-500">
              <tr><th className="p-3">Return</th><th className="p-3">Sale</th><th className="p-3 text-right">Refund</th></tr>
            </thead>
            <tbody>
              {returns.length === 0 ? <EmptyRow label="Sin devoluciones cargadas todavía." /> : returns.map((item, index) => (
                <tr key={item.returnId ?? item.id ?? index} className="border-t border-slate-100">
                  <td className="p-3 font-medium">{item.returnId ?? item.id ?? `return-${index + 1}`}</td>
                  <td className="p-3">{item.saleId ?? '-'}</td>
                  <td className="p-3 text-right">{formatMoney(item.totalRefundCents ?? item.refundCents ?? 0)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>
    </div>
  );
}

function EmptyRow({ label }: { label: string }) {
  return <tr><td className="p-3 text-slate-500" colSpan={3}>{label}</td></tr>;
}
