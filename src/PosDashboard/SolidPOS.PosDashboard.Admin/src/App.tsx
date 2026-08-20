import { useState } from 'react';
import type { LoginResponse } from './api/posServerClient';
import { LoginPanel } from './features/auth/LoginPanel';
import { DashboardHome } from './features/dashboard/DashboardHome';
import type { DashboardSection } from './features/dashboard/DashboardHome';
import { AdminLayout } from './layout/AdminLayout';
import { clearSession, loadSession } from './lib/sessionStorage';

export function App() {
  const [session, setSession] = useState<LoginResponse | null>(() => loadSession());
  const [activeSection, setActiveSection] = useState<DashboardSection>('Overview');

  if (!session) {
    return <div className="p-6"><LoginPanel onAuthenticated={setSession} /></div>;
  }

  return (
    <AdminLayout
      session={session}
      activeSection={activeSection}
      onSectionChange={setActiveSection}
      onLogout={() => { clearSession(); setSession(null); }}
    >
      <DashboardHome session={session} activeSection={activeSection} />
    </AdminLayout>
  );
}
