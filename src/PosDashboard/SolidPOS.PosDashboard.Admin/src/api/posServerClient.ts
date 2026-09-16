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

export type ReadinessDependencyDto = {
  name: string;
  status: string;
  latencyMs: number;
  detail?: string | null;
  errorCode?: string | null;
};

export type ReadinessResponse = {
  status: string;
  database: string;
  timestamp: string;
  detail?: string | null;
  errorCode?: string | null;
  missingTables?: string[];
  connectionStringSource?: string;
  databaseLatencyMs: number;
  schemaVersion: number;
  syncContract: string;
  schemaCompatibility?: string;
  syncReadiness?: string;
  storageReadiness?: string;
  dependencies?: ReadinessDependencyDto[];
};

export type ProductionAlertResponse = {
  code: string;
  severity: 'warning' | 'critical' | string;
  active: boolean;
  observed: number;
  threshold: number;
  comparator: string;
  detail: string;
};

export type ProductionAlertsResponse = {
  generatedAt: string;
  anyActive: boolean;
  alerts: ProductionAlertResponse[];
};

export type TerminalResponse = {
  id: string;
  tenantId: string;
  storeId: string;
  name: string;
  fingerprint: string;
  status: string;
  appVersion?: string | null;
  lastSeenAt?: string | null;
};

export type UpdateHealthEvidenceDto = {
  state: string;
  currentVersion: string;
  targetVersion?: string | null;
  channel?: string | null;
  packageFileName?: string | null;
  packageSha256?: string | null;
  isSigned?: boolean | null;
  signingThumbprint?: string | null;
  errorMessage?: string | null;
  attemptedAtUtc?: string | null;
  completedAtUtc?: string | null;
  rollbackVersion?: string | null;
  rollbackReason?: string | null;
};

export type CrashReportEvidenceDto = {
  crashId: string;
  correlationId: string;
  occurredAtUtc: string;
  exceptionType: string;
  sanitizedMessage: string;
  crashSource?: string | null;
  isFatal?: boolean | null;
};

export type TerminalDeviceHealthDto = {
  batteryStatus?: string | null;
  batteryLevelPercent?: number | null;
  availableDiskSpaceBytes?: number | null;
  totalDiskSpaceBytes?: number | null;
  memoryUsageBytes?: number | null;
  cpuArchitecture?: string | null;
  osVersion?: string | null;
  isStorageHealthy?: boolean | null;
  reportedAtUtc?: string | null;
  updateHealth?: UpdateHealthEvidenceDto | null;
  crashEvidence?: CrashReportEvidenceDto | null;
  recentCrashes?: CrashReportEvidenceDto[] | null;
};

export type SyncStatusBucketDto = {
  status: string;
  count: number;
};

export type SyncStatusDto = {
  tenantId?: string;
  storeId?: string | null;
  terminalId?: string | null;
  serverTime?: string;
  totalInboxEvents?: number;
  pendingCount?: number;
  processingCount?: number;
  processedCount?: number;
  duplicateCount?: number;
  rejectedCount?: number;
  retryPendingCount?: number;
  conflictCount?: number;
  deadLetterCount?: number;
  oldestPendingAt?: string | null;
  lastProcessedAt?: string | null;
  buckets?: SyncStatusBucketDto[];
  failedCount?: number;
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

export type OperationalMetricsDto = {
  generatedAt?: string;
  database?: {
    ready?: boolean;
    databaseName?: string;
    serverVersion?: string;
    activeConnections?: number;
    requiredTablesPresent?: boolean;
    missingRequiredTables?: string[];
  };
  requests?: {
    totalRequests?: number;
    failedRequests?: number;
    averageLatencyMs?: number;
    p95LatencyMs?: number;
    topRoutes?: Array<{
      method?: string;
      route?: string;
      count?: number;
      failedCount?: number;
      averageLatencyMs?: number;
      p95LatencyMs?: number;
    }>;
  };
  sync?: {
    inboxByStatus?: Record<string, number>;
    pendingConflicts?: number;
    resolvedConflicts?: number;
    deadLetterEvents?: number;
    retryPendingEvents?: number;
  };
  sales?: {
    salesLast24Hours?: number;
    averagePersistLatencyMsLast24Hours?: number;
    apiAverageLatencyMs?: number;
    apiP95LatencyMs?: number;
  };
  payments?: {
    failedPaymentsLast24Hours?: number;
    declinedPaymentsLast24Hours?: number;
  };
  inventory?: {
    negativeInventoryItemCount?: number;
    lowStockItemCount?: number;
  };
  audit?: {
    auditEventsLast24Hours?: number;
    lastAuditEventAt?: string | null;
  };
};

export type VersionAdoptionItem = {
  version: string;
  count: number;
  percentage: number;
};

export type VersionAdoptionSummary = {
  serverVersion: string;
  totalTerminals: number;
  versions: VersionAdoptionItem[];
  unknownCount: number;
};

export type OperationalIncident = {
  id: string;
  title: string;
  severity: 'warning' | 'critical';
  detail: string;
  source: string;
};

export type IncidentSummary = {
  totalIncidents: number;
  criticalCount: number;
  warningCount: number;
  incidents: OperationalIncident[];
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
  activeDbConnections: number;
  requestP95LatencyMs: number;
  failedRequests: number;
  pendingConflicts: number;
  resolvedConflicts: number;
  retryPendingSync: number;
  salesLast24Hours: number;
  failedPaymentsLast24Hours: number;
  negativeInventoryItemCount: number;
  lowStockItemCount: number;
};

export type OperationsSnapshot = {
  health: HealthState;
  readiness: ReadinessResponse | null;
  sync: SyncStatusDto;
  sales: SaleSummaryDto[];
  returns: ReturnSummaryDto[];
  cashShift?: CashShiftSummaryDto | null;
  audit: AuditEntryDto[];
  operationalMetrics?: OperationalMetricsDto | null;
  alerts: ProductionAlertsResponse | null;
  terminals: TerminalResponse[];
  versionAdoption: VersionAdoptionSummary;
  incidents: IncidentSummary;
  metrics: DashboardMetrics;
  permissionErrors: string[];
};

export const PermissionCodes = {
  ReportsRead: 'reports.read',
  TerminalsManage: 'terminals.manage',
  SyncConflictsRead: 'sync.conflicts.read',
  AuditRead: 'audit.read'
} as const;

export function hasPermission(session: LoginResponse | null | undefined, permission: string): boolean {
  if (!session?.permissions) return false;
  return session.permissions.includes(permission);
}

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

  async getHealthReadiness(): Promise<ReadinessResponse | null> {
    try {
      const response = await fetch(`${this.baseUrl}/health/ready`);
      return await safeJson<ReadinessResponse>(response);
    } catch {
      return null;
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

  async getOperationalMetrics(accessToken: string): Promise<OperationalMetricsDto | null> {
    try {
      const response = await fetch(`${this.baseUrl}/api/v1/observability/metrics`, {
        headers: authHeaders(accessToken)
      });
      return await safeJson<OperationalMetricsDto>(response);
    } catch {
      return null;
    }
  }

  async getAlerts(accessToken: string): Promise<ProductionAlertsResponse | null> {
    try {
      const response = await fetch(`${this.baseUrl}/api/v1/observability/alerts`, {
        headers: authHeaders(accessToken)
      });
      return await safeJson<ProductionAlertsResponse>(response);
    } catch {
      return null;
    }
  }

  async getTerminals(accessToken: string): Promise<TerminalResponse[]> {
    try {
      const response = await fetch(`${this.baseUrl}/api/v1/terminals`, {
        headers: authHeaders(accessToken)
      });
      const data = await safeJson<TerminalResponse[]>(response);
      return Array.isArray(data) ? data : [];
    } catch {
      return [];
    }
  }

  async getTerminalHealth(accessToken: string, terminalId: string): Promise<TerminalDeviceHealthDto | null> {
    try {
      const response = await fetch(`${this.baseUrl}/api/v1/terminals/${terminalId}/health`, {
        headers: authHeaders(accessToken)
      });
      return await safeJson<TerminalDeviceHealthDto>(response);
    } catch {
      return null;
    }
  }

  async getSales(accessToken: string, limit = 10): Promise<SaleSummaryDto[]> {
    const { from, to } = todayRange();
    try {
      const response = await fetch(`${this.baseUrl}/api/v1/sales?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}&limit=${limit}`, {
        headers: authHeaders(accessToken)
      });
      const payload = await safeJson<SaleSummaryDto[] | { items?: SaleSummaryDto[]; sales?: SaleSummaryDto[]; data?: SaleSummaryDto[]; results?: SaleSummaryDto[] }>(response);
      if (Array.isArray(payload)) {
        return payload;
      }

      return payload?.items ?? payload?.sales ?? payload?.data ?? payload?.results ?? [];
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
      const payload = await safeJson<ReturnSummaryDto[] | { items?: ReturnSummaryDto[]; returns?: ReturnSummaryDto[]; data?: ReturnSummaryDto[]; results?: ReturnSummaryDto[] }>(response);
      if (Array.isArray(payload)) {
        return payload;
      }

      return payload?.items ?? payload?.returns ?? payload?.data ?? payload?.results ?? [];
    } catch {
      return [];
    }
  }

  async getAuditLog(accessToken: string, limit = 10): Promise<AuditEntryDto[]> {
    try {
      const response = await fetch(`${this.baseUrl}/api/v1/audit/events?limit=${limit}`, {
        headers: authHeaders(accessToken)
      });
      const payload = await safeJson<AuditEntryDto[] | { items?: AuditEntryDto[]; auditEvents?: AuditEntryDto[]; events?: AuditEntryDto[]; data?: AuditEntryDto[]; results?: AuditEntryDto[] }>(response);
      if (Array.isArray(payload)) {
        return payload;
      }

      return payload?.items ?? payload?.auditEvents ?? payload?.events ?? payload?.data ?? payload?.results ?? [];
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
    const results = await Promise.allSettled([
      this.getHealthReadiness(),
      this.getSyncStatus(accessToken),
      this.getOperationalMetrics(accessToken),
      this.getAlerts(accessToken),
      this.getTerminals(accessToken),
      this.getSales(accessToken, 10),
      this.getReturns(accessToken, 10),
      this.getAuditLog(accessToken, 10)
    ]);

    const permissionErrors: string[] = [];

    const readiness = results[0].status === 'fulfilled' ? results[0].value : null;
    const sync = results[1].status === 'fulfilled' ? results[1].value : {};
    const operationalMetrics = results[2].status === 'fulfilled' ? results[2].value : null;
    const alerts = results[3].status === 'fulfilled' ? results[3].value : null;
    const terminals = results[4].status === 'fulfilled' ? results[4].value : [];
    const sales = results[5].status === 'fulfilled' ? results[5].value : [];
    const returns = results[6].status === 'fulfilled' ? results[6].value : [];
    const audit = results[7].status === 'fulfilled' ? results[7].value : [];

    if (results[1].status === 'rejected') permissionErrors.push('sync:status');
    if (results[2].status === 'rejected') permissionErrors.push('observability:metrics');
    if (results[3].status === 'rejected') permissionErrors.push('observability:alerts');
    if (results[4].status === 'rejected') permissionErrors.push('terminals');

    const health: HealthState = readiness?.status === 'ready' || readiness?.status === 'healthy' ? 'ready' : (readiness ? 'unavailable' : 'unknown');

    const salesCount = sales.length;
    const returnsCount = returns.length;
    const totalSalesCents = sales.reduce((sum, sale) => sum + (sale.totalCents ?? sale.amountCents ?? 0), 0);
    const refundsCents = returns.reduce((sum, item) => sum + (item.totalRefundCents ?? item.refundCents ?? 0), 0);
    const criticalAuditEvents = audit.filter((entry) => entry.severity === 'critical').length;

    // Version adoption calculation
    const versionCounts = new Map<string, number>();
    let unknownCount = 0;
    for (const term of terminals) {
      const v = term.appVersion?.trim();
      if (!v) {
        unknownCount++;
      } else {
        versionCounts.set(v, (versionCounts.get(v) ?? 0) + 1);
      }
    }

    const totalTerminals = terminals.length;
    const versions: VersionAdoptionItem[] = Array.from(versionCounts.entries())
      .map(([version, count]) => ({
        version,
        count,
        percentage: totalTerminals > 0 ? Math.round((count / totalTerminals) * 100) : 0
      }))
      .sort((a, b) => b.count - a.count);

    const versionAdoption: VersionAdoptionSummary = {
      serverVersion: readiness?.schemaVersion ? `Schema v${readiness.schemaVersion}` : 'v1.1-11',
      totalTerminals,
      versions,
      unknownCount
    };

    // Incident derivation
    const incidents: OperationalIncident[] = [];
    if (alerts?.alerts) {
      for (const alert of alerts.alerts) {
        if (alert.active) {
          incidents.push({
            id: `alert-${alert.code}`,
            title: `Alert: ${alert.code.replace(/_/g, ' ')}`,
            severity: alert.severity === 'critical' ? 'critical' : 'warning',
            detail: `${alert.detail} (Observed: ${Math.round(alert.observed * 100) / 100}, Threshold: ${alert.threshold})`,
            source: 'Observability'
          });
        }
      }
    }

    const deadLetterCount = sync.deadLetterCount ?? operationalMetrics?.sync?.deadLetterEvents ?? 0;
    if (deadLetterCount > 0) {
      incidents.push({
        id: 'sync-dead-letter',
        title: 'Sync Dead-Letter Queue Active',
        severity: 'critical',
        detail: `There are ${deadLetterCount} event(s) in dead-letter state requiring review.`,
        source: 'Sync'
      });
    }

    const pendingSyncCount = sync.pendingCount ?? operationalMetrics?.sync?.inboxByStatus?.received ?? 0;
    if (pendingSyncCount > 50) {
      incidents.push({
        id: 'sync-queue-backlog',
        title: 'High Sync Backlog',
        severity: 'warning',
        detail: `${pendingSyncCount} pending events awaiting processing.`,
        source: 'Sync'
      });
    }

    const incidentSummary: IncidentSummary = {
      totalIncidents: incidents.length,
      criticalCount: incidents.filter((i) => i.severity === 'critical').length,
      warningCount: incidents.filter((i) => i.severity === 'warning').length,
      incidents
    };

    return {
      health,
      readiness,
      sync,
      sales,
      returns,
      cashShift: null,
      audit,
      operationalMetrics,
      alerts,
      terminals,
      versionAdoption,
      incidents: incidentSummary,
      permissionErrors,
      metrics: {
        health,
        totalSalesCents,
        salesCount,
        refundsCents,
        returnsCount,
        pendingSync: sync.pendingCount ?? operationalMetrics?.sync?.inboxByStatus?.received ?? 0,
        processedSync: sync.processedCount ?? operationalMetrics?.sync?.inboxByStatus?.processed ?? 0,
        deadLetter: deadLetterCount,
        auditEvents: audit.length,
        criticalAuditEvents,
        lastUpdatedAt: new Date().toISOString(),
        activeDbConnections: operationalMetrics?.database?.activeConnections ?? 0,
        requestP95LatencyMs: operationalMetrics?.requests?.p95LatencyMs ?? 0,
        failedRequests: operationalMetrics?.requests?.failedRequests ?? 0,
        pendingConflicts: operationalMetrics?.sync?.pendingConflicts ?? 0,
        resolvedConflicts: operationalMetrics?.sync?.resolvedConflicts ?? 0,
        retryPendingSync: sync.retryPendingCount ?? operationalMetrics?.sync?.retryPendingEvents ?? 0,
        salesLast24Hours: operationalMetrics?.sales?.salesLast24Hours ?? 0,
        failedPaymentsLast24Hours: operationalMetrics?.payments?.failedPaymentsLast24Hours ?? 0,
        negativeInventoryItemCount: operationalMetrics?.inventory?.negativeInventoryItemCount ?? 0,
        lowStockItemCount: operationalMetrics?.inventory?.lowStockItemCount ?? 0
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
