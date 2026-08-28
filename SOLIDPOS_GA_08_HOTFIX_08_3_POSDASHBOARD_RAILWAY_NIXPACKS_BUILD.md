# SolidPOS GA-08 Hotfix 08.3 — PosDashboard Railway/Nixpacks Build

## Scope
Fixes the PosDashboard Railway build failure `sh: 1: tsc: Permission denied` without changing PosServer runtime, database schema, GA security contracts, or production data.

## Changes
- `src/PosDashboard/SolidPOS.PosDashboard.Admin/package.json`
  - Build now invokes TypeScript and Vite through `node` directly instead of executable shims in `node_modules/.bin`.
- `src/PosDashboard/SolidPOS.PosDashboard.Admin/railway.toml`
  - Dashboard-specific Nixpacks configuration is included in the repo.
  - Build command: `npm run build`.
  - Start command: `npx serve -s dist -l $PORT`.

## Railway variables
- `NIXPACKS_NODE_VERSION=22`
- `VITE_POSSERVER_BASE_URL=https://full-metal-cash-production.up.railway.app`

No backend secrets belong in the dashboard service.
