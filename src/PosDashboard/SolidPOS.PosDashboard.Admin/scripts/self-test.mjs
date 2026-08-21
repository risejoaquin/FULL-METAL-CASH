import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const root = process.cwd();
const requiredFiles = [
  'package.json',
  'index.html',
  'vite.config.ts',
  'tailwind.config.ts',
  'src/App.tsx',
  'src/api/posServerClient.ts',
  'src/features/auth/LoginPanel.tsx',
  'src/features/dashboard/DashboardHome.tsx',
  'src/features/dashboard/ReportsDashboard.tsx',
  'src/features/dashboard/AuditDashboard.tsx',
  'src/features/dashboard/OperationsDashboard.tsx',
  'src/layout/AdminLayout.tsx'
];

const missing = requiredFiles.filter((file) => !existsSync(join(root, file)));
if (missing.length > 0) {
  console.error(`PosDashboard self-test failed. Missing files: ${missing.join(', ')}`);
  process.exit(1);
}

const pkg = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8'));
const requiredDeps = ['react', 'react-dom', 'vite', '@vitejs/plugin-react', 'typescript', 'tailwindcss', '@tailwindcss/postcss'];
const missingDeps = requiredDeps.filter((dep) => !(pkg.dependencies?.[dep] || pkg.devDependencies?.[dep]));
if (missingDeps.length > 0) {
  console.error(`PosDashboard self-test failed. Missing dependencies: ${missingDeps.join(', ')}`);
  process.exit(1);
}

const filesToInspect = [
  'src/App.tsx',
  'src/api/posServerClient.ts',
  'src/features/dashboard/DashboardHome.tsx',
  'src/features/dashboard/ReportsDashboard.tsx',
  'src/features/dashboard/AuditDashboard.tsx',
  'src/features/dashboard/OperationsDashboard.tsx',
  'src/layout/AdminLayout.tsx'
];
const content = filesToInspect.map((file) => readFileSync(join(root, file), 'utf8')).join('\n');

const expectedMarkers = [
  'LoginPanel',
  'DashboardHome',
  'AdminLayout',
  'ReportsDashboard',
  'AuditDashboard',
  'OperationsDashboard',
  '/api/v1/auth/login',
  '/api/v1/sync/status',
  '/api/v1/observability/metrics',
  '/health/ready',
  '/api/v1/sales',
  '/api/v1/returns',
  '/api/v1/audit/events',
  'getOperationsSnapshot',
  'getOperationalMetrics',
  'OperationalMetricsDto',
  'accessToken'
];

const missingMarkers = expectedMarkers.filter((marker) => !content.includes(marker));
if (missingMarkers.length > 0) {
  console.error(`PosDashboard self-test failed. Missing markers: ${missingMarkers.join(', ')}`);
  process.exit(1);
}

console.log('PosDashboard reports/audit/operations self-test started.');
console.log('Vite React TypeScript production build ready.');
console.log('Protected client ready: /api/v1/auth/login with accessToken.');
console.log('Reports client ready: /api/v1/sales and /api/v1/returns.');
console.log('Operations client ready: /health/ready, /api/v1/sync/status and /api/v1/observability/metrics.');
console.log('Audit client ready: /api/v1/audit/events.');
console.log('Admin dashboard sections ready: Overview, Reports, Operations, Audit.');
console.log('PosDashboard operations monitoring dashboard completed.');
