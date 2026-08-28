import type { LoginResponse } from '../api/posServerClient';

const key = 'solidpos.dashboard.session.v1';

export function saveSession(session: LoginResponse): void {
  window.localStorage.setItem(key, JSON.stringify(session));
}

export function loadSession(): LoginResponse | null {
  const raw = window.localStorage.getItem(key);
  if (!raw) return null;

  try {
    return JSON.parse(raw) as LoginResponse;
  } catch {
    window.localStorage.removeItem(key);
    return null;
  }
}

export function clearSession(): void {
  window.localStorage.removeItem(key);
}
