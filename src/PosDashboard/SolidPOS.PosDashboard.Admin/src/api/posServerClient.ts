export type LoginRequest = {
  email: string;
  password: string;
  tenantId: string;
};

export type LoginResponse = {
  accessToken: string;
  refreshToken?: string;
  expiresAt?: string;
  userId?: string;
  tenantId?: string;
  email?: string;
  roles?: string[];
  permissions?: string[];
};

export type DashboardMetrics = {
  health: 'unknown' | 'ready' | 'unavailable';
  totalSalesCents: number;
  salesCount: number;
  refundsCents: number;
  pendingSync: number;
  deadLetter: number;
  lastUpdatedAt: string;
};

const defaultBaseUrl = import.meta.env.VITE_POSSERVER_BASE_URL ?? 'https://full-metal-cash-production.up.railway.app';

export class PosServerClient {
  constructor(private readonly baseUrl: string = defaultBaseUrl) {}

  async login(request: LoginRequest): Promise<LoginResponse> {
    const response = await fetch(`${this.baseUrl}/api/v1/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request)
    });

    if (!response.ok) {
      throw new Error(`Login failed with HTTP ${response.status}`);
    }

    return (await response.json()) as LoginResponse;
  }

  async getOperationalSnapshot(accessToken: string): Promise<DashboardMetrics> {
    const [ready, syncStatus] = await Promise.allSettled([
      fetch(`${this.baseUrl}/health/ready`),
      fetch(`${this.baseUrl}/api/v1/sync/status`, {
        headers: { Authorization: `Bearer ${accessToken}` }
      })
    ]);

    const syncPayload = syncStatus.status === 'fulfilled' && syncStatus.value.ok
      ? await syncStatus.value.json() as { processedCount?: number; deadLetterCount?: number; pendingCount?: number }
      : {};

    return {
      health: ready.status === 'fulfilled' && ready.value.ok ? 'ready' : 'unavailable',
      totalSalesCents: 0,
      salesCount: 0,
      refundsCents: 0,
      pendingSync: syncPayload.pendingCount ?? 0,
      deadLetter: syncPayload.deadLetterCount ?? 0,
      lastUpdatedAt: new Date().toISOString()
    };
  }
}
