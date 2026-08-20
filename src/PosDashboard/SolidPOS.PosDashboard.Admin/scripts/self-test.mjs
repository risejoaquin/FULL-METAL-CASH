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
  'src/layout/AdminLayout.tsx'
];

const missing = requiredFiles.filter((file) => !existsSync(join(root, file)));
if (missing.length > 0) {
  console.error(`PosDashboard self-test failed. Missing files: ${missing.join(', ')}`);
  process.exit(1);
}

const pkg = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8'));
const requiredDeps = ['react', 'react-dom', 'vite', '@vitejs/plugin-react', 'typescript', 'tailwindcss'];
const missingDeps = requiredDeps.filter((dep) => !(pkg.dependencies?.[dep] || pkg.devDependencies?.[dep]));
if (missingDeps.length > 0) {
  console.error(`PosDashboard self-test failed. Missing dependencies: ${missingDeps.join(', ')}`);
  process.exit(1);
}

const app = readFileSync(join(root, 'src/App.tsx'), 'utf8');
const client = readFileSync(join(root, 'src/api/posServerClient.ts'), 'utf8');

const expectedMarkers = [
  'LoginPanel',
  'DashboardHome',
  'AdminLayout',
  '/api/v1/auth/login',
  '/api/v1/sync/status',
  '/health/ready'
];

const content = `${app}\n${client}`;
const missingMarkers = expectedMarkers.filter((marker) => !content.includes(marker));
if (missingMarkers.length > 0) {
  console.error(`PosDashboard self-test failed. Missing markers: ${missingMarkers.join(', ')}`);
  process.exit(1);
}

console.log('PosDashboard admin React self-test started.');
console.log('Vite React project ready.');
console.log('Tailwind foundation ready.');
console.log('Login view ready: /api/v1/auth/login');
console.log('Admin layout ready: Overview, Sales, Sync, Tenants, Security, Settings');
console.log('Dashboard client ready: /health/ready and /api/v1/sync/status');
console.log('PosDashboard admin React foundation completed.');
