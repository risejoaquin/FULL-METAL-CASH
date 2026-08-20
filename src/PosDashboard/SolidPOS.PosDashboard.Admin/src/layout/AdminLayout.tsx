import { BarChart3, Building2, LogOut, MonitorCheck, Receipt, Settings, ShieldCheck } from 'lucide-react';
import type { PropsWithChildren } from 'react';
import type { LoginResponse } from '../api/posServerClient';
import { Button } from '../components/ui';

const nav = [
  { label: 'Overview', icon: BarChart3 },
  { label: 'Sales', icon: Receipt },
  { label: 'Sync', icon: MonitorCheck },
  { label: 'Tenants', icon: Building2 },
  { label: 'Security', icon: ShieldCheck },
  { label: 'Settings', icon: Settings }
];

export function AdminLayout({ session, onLogout, children }: PropsWithChildren<{ session: LoginResponse; onLogout: () => void }>) {
  return (
    <div className="min-h-screen bg-solid-surface">
      <aside className="fixed inset-y-0 left-0 hidden w-72 border-r border-slate-200 bg-white p-5 lg:block">
        <div className="rounded-2xl bg-solid-ink p-4 text-white">
          <p className="text-xs uppercase tracking-widest text-slate-300">SolidPOS</p>
          <h1 className="mt-1 text-xl font-bold">Admin Dashboard</h1>
        </div>
        <nav className="mt-6 space-y-1">
          {nav.map((item) => (
            <button key={item.label} className="flex w-full items-center gap-3 rounded-xl px-3 py-2 text-left text-sm font-medium text-slate-600 hover:bg-slate-100">
              <item.icon size={18} />
              {item.label}
            </button>
          ))}
        </nav>
      </aside>
      <main className="lg:pl-72">
        <header className="flex items-center justify-between border-b border-slate-200 bg-white px-6 py-4">
          <div>
            <p className="text-sm text-slate-500">Tenant</p>
            <h2 className="font-semibold">{session.tenantId ?? 'Mi Cafeteria'}</h2>
          </div>
          <div className="flex items-center gap-3">
            <p className="hidden text-sm text-slate-600 md:block">{session.email ?? 'admin@micafeteria.com'}</p>
            <Button className="bg-slate-800" onClick={onLogout}><LogOut size={16} className="mr-2 inline" />Salir</Button>
          </div>
        </header>
        <div className="p-6">{children}</div>
      </main>
    </div>
  );
}
