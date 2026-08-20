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

export type HealthState = 'unknown' | 'ready' | 'unavailable';

export type SyncStatusDto = {
  pendingCount?: number;
  processedCount?: number;
  failedCount?: number;
  deadLetterCount?: number;
  lastProcessedAt?: string;
  schemaVersion?: number;
};

export type SaleSummaryDto = {
  saleId?: string;
  id?: string;
  saleNumber?: string;
  status?: string;
  totalCents?: number;
  amountCents?: number;
  occurredAt?: string;
  storeId?: string;
  terminalId?: string;
  lineCount?: number;
  paymentCount?: number;
};

export type ReturnSummaryDto = {
  returnId?: string;
  id?: string;
  saleId?: string;
  totalRefundCents?: number;
  refundCents?: number;
  status?: string;
  occurredAt?: string;
  createdAt?: string;
};

export type CashShiftSummaryDto = {
  shiftId?: string;
  status?: string;
  openingFloatCents?: number;
  cashSalesCents?: number;
  nonCashSalesCents?: number;
  refundsCents?: number;
  cashInCents?: number;
  cashOutCents?: number;
  expectedCashCents?: number;
  countedCashCents?: number;
  differenceCents?: number;
  salesCount?: number;
  returnsCount?: number;
  movementsCount?: number;
};

export type AuditEntryDto = {
  auditId?: string;
  id?: string;
  action?: string;
  entityType?: string;
  entityId?: string;
  actorUserId?: string;
  occurredAt?: string;
  createdAt?: string;
  severity?: 'info' | 'warning' | 'critical' | string;
};

export type DashboardMetrics = {
  health: HealthState;
  totalSalesCents: number;
  salesCount: number;
  refundsCents: number;
  returnsCount: number;
  pendingSync: number;
  processedSync: number;
  deadLetter: number;
  auditEvents: number;
  criticalAuditEvents: number;
  lastUpdatedAt: string;
};

export type OperationsSnapshot = {
  health: HealthState;
  sync: SyncStatusDto;
  sales: SaleSummaryDto[];
  returns: ReturnSummaryDto[];
  cashShift?: CashShiftSummaryDto | null;
  audit: AuditEntryDto[];
  metrics: DashboardMetrics;
};

const defaultBaseUrl = import.meta.env.VITE_POSSERVER_BASE_URL ?? 'https://full-metal-cash-production.up.railway.app';
const jsonHeaders = { 'Content-Type': 'application/json' };

function authHeaders(accessToken: string) {
  return { Authorization: `Bearer ${accessToken}` };
}

function todayRange() {
  const to = new Date();
  const from = new Date(to);
  from.setHours(0, 0, 0, 0);
  return { from: from.toISOString(), to: to.toISOString() };
}

async function safeJson<T>(response: Response): Promise<T | null> {
  if (!response.ok) {
    return null;
  }

  return (await response.json()) as T;
}

export class PosServerClient {
  constructor(private readonly baseUrl: string = defaultBaseUrl) {}

  async login(request: LoginRequest): Promise<LoginResponse> {
    const response = await fetch(`${this.baseUrl}/api/v1/auth/login`, {
      method: 'POST',
      headers: jsonHeaders,
      body: JSON.stringify(request)
    });

    if (!response.ok) {
      throw new Error(`Login failed with HTTP ${response.status}`);
    }

    return (await response.json()) as LoginResponse;
  }

  async getHealth(): Promise<HealthState> {
    try {
      const response = await fetch(`${this.baseUrl}/health/ready`);
      return response.ok ? 'ready' : 'unavailable';
    } catch {
      return 'unavailable';
    }
  }

  async getSyncStatus(accessToken: string): Promise<SyncStatusDto> {
    try {
      const response = await fetch(`${this.baseUrl}/api/v1/sync/status`, {
        headers: authHeaders(accessToken)
      });

      return (await safeJson<SyncStatusDto>(response)) ?? {};
    } catch {
      return {};
    }
  }

  async getSales(accessToken: string, limit = 10): Promise<SaleSummaryDto[]> {
    const { from, to } = todayRange();
    try {
      const response = await fetch(`${this.baseUrl}/api/v1/sales?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}&limit=${limit}`, {
        headers: authHeaders(accessToken)
      });
      const payload = await safeJson<SaleSummaryDto[] | { items?: SaleSummaryDto[]; sales?: SaleSummaryDto[] }>(response);
      if (Array.isArray(payload)) {
        return payload;
      }

      return payload?.items ?? payload?.sales ?? [];
    } catch {
      return [];
    }
  }

  async getReturns(accessToken: string, limit = 10): Promise<ReturnSummaryDto[]> {
    const { from, to } = todayRange();
    try {
      const response = await fetch(`${this.baseUrl}/api/v1/returns?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}&limit=${limit}`, {
        headers: authHeaders(accessToken)
      });
      const payload = await safeJson<ReturnSummaryDto[] | { items?: ReturnSummaryDto[]; returns?: ReturnSummaryDto[] }>(response);
      if (Array.isArray(payload)) {
        return payload;
      }

      return payload?.items ?? payload?.returns ?? [];
    } catch {
      return [];
    }
  }

  async getAuditLog(accessToken: string, limit = 10): Promise<AuditEntryDto[]> {
    try {
      const response = await fetch(`${this.baseUrl}/api/v1/audit/events?limit=${limit}`, {
        headers: authHeaders(accessToken)
      });
      const payload = await safeJson<AuditEntryDto[] | { items?: AuditEntryDto[]; auditEvents?: AuditEntryDto[] }>(response);
      if (Array.isArray(payload)) {
        return payload;
      }

      return payload?.items ?? payload?.auditEvents ?? [];
    } catch {
      return [];
    }
  }

  async getCashShiftSummary(accessToken: string, shiftId?: string): Promise<CashShiftSummaryDto | null> {
    if (!shiftId) {
      return null;
    }

    try {
      const response = await fetch(`${this.baseUrl}/api/v1/cash-drawers/shifts/${shiftId}/summary`, {
        headers: authHeaders(accessToken)
      });
      return await safeJson<CashShiftSummaryDto>(response);
    } catch {
      return null;
    }
  }

  async getOperationsSnapshot(accessToken: string): Promise<OperationsSnapshot> {
    const [health, sync, sales, returns, audit] = await Promise.all([
      this.getHealth(),
      this.getSyncStatus(accessToken),
      this.getSales(accessToken, 10),
      this.getReturns(accessToken, 10),
      this.getAuditLog(accessToken, 10)
    ]);

    const salesCount = sales.length;
    const returnsCount = returns.length;
    const totalSalesCents = sales.reduce((sum, sale) => sum + (sale.totalCents ?? sale.amountCents ?? 0), 0);
    const refundsCents = returns.reduce((sum, item) => sum + (item.totalRefundCents ?? item.refundCents ?? 0), 0);
    const criticalAuditEvents = audit.filter((entry) => entry.severity === 'critical').length;

    return {
      health,
      sync,
      sales,
      returns,
      cashShift: null,
      audit,
      metrics: {
        health,
        totalSalesCents,
        salesCount,
        refundsCents,
        returnsCount,
        pendingSync: sync.pendingCount ?? 0,
        processedSync: sync.processedCount ?? 0,
        deadLetter: sync.deadLetterCount ?? 0,
        auditEvents: audit.length,
        criticalAuditEvents,
        lastUpdatedAt: new Date().toISOString()
      }
    };
  }

  async getOperationalSnapshot(accessToken: string): Promise<DashboardMetrics> {
    const snapshot = await this.getOperationsSnapshot(accessToken);
    return snapshot.metrics;
  }
}

export function formatMoney(cents: number) {
  return new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' }).format(cents / 100);
}
