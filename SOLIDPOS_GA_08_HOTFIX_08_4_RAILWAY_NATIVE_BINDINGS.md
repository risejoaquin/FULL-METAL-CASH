# SolidPOS GA-08 Hotfix 08.4 — Railway native bindings

## Trigger
Railway Nixpacks installed dependencies with `npm ci`, but npm omitted Linux optional native packages required by Vite 8 / Rolldown. Build failed with `Cannot find module '@rolldown/binding-linux-x64-gnu'`.

The deployment also resolved Node 22.11.0 while Vite 8 requires Node `^20.19.0 || >=22.12.0`.

## Changes
- PosDashboard declares Node `>=22.12.0 <23`.
- Added `.nvmrc` with `22.12.0`.
- Railway build explicitly installs the exact Linux x64 GNU native packages already required by the resolved frontend toolchain, without replacing the dependency graph or deleting `node_modules`:
  - `@rolldown/binding-linux-x64-gnu@1.2.5`
  - `@tailwindcss/oxide-linux-x64-gnu@4.3.3`
  - `lightningcss-linux-x64-gnu@1.33.0`
- Existing deterministic Node-invoked TypeScript/Vite build remains in place.

## Railway variable
Set `NIXPACKS_NODE_VERSION=22.12.0` (not only `22`) so Nixpacks cannot resolve Node 22.11.x.

No PosServer, PostgreSQL, RLS, RBAC, JWT, schema v4 or GA activation behavior changed.
