import { useState } from 'react';
import type { ChangeEvent, FormEvent } from 'react';
import type { LoginResponse } from '../../api/posServerClient';
import { PosServerClient } from '../../api/posServerClient';
import { Button, Card, Input, Label } from '../../components/ui';
import { saveSession } from '../../lib/sessionStorage';

export function LoginPanel({ onAuthenticated }: { onAuthenticated: (session: LoginResponse) => void }) {
  const [tenantId, setTenantId] = useState('0ce5bbd0-528b-4aee-9fe3-93df001a4fde');
  const [email, setEmail] = useState('admin@micafeteria.com');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setIsSubmitting(true);
    setError(null);

    try {
      const client = new PosServerClient();
      const session = await client.login({ tenantId, email, password });
      saveSession(session);
      onAuthenticated(session);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Login failed');
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <Card className="mx-auto max-w-xl">
      <h1 className="text-2xl font-bold">SolidPOS Admin Dashboard</h1>
      <p className="mt-2 text-sm text-slate-500">Ingresa con el admin del tenant para operar reportes y monitoreo inicial.</p>
      <form className="mt-6 space-y-4" onSubmit={submit}>
        <div className="space-y-1">
          <Label>Tenant ID</Label>
          <Input value={tenantId} onChange={(event: ChangeEvent<HTMLInputElement>) => setTenantId(event.target.value)} required />
        </div>
        <div className="space-y-1">
          <Label>Email</Label>
          <Input type="email" value={email} onChange={(event: ChangeEvent<HTMLInputElement>) => setEmail(event.target.value)} required />
        </div>
        <div className="space-y-1">
          <Label>Password</Label>
          <Input type="password" value={password} onChange={(event: ChangeEvent<HTMLInputElement>) => setPassword(event.target.value)} required />
        </div>
        {error && <p className="rounded-xl bg-red-50 p-3 text-sm text-red-700">{error}</p>}
        <Button disabled={isSubmitting}>{isSubmitting ? 'Validando...' : 'Entrar'}</Button>
      </form>
    </Card>
  );
}
