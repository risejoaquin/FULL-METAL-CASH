# SolidPOS Git Ignore Update

Status: prepared after SolidPOS Iteration 19.

## Purpose

This update adds a repository-level `.gitignore` to prevent generated artifacts, local runtime files, database files, frontend build output, dependency folders, logs, secrets, local environment files, and packaging outputs from entering Git.

## Covered areas

- .NET / C# / WPF build outputs: `bin/`, `obj/`, test results, coverage.
- PosCore local runtime: `.runtime/`, SQLite/WAL files, backups.
- PosBuilder/updates generated files: local manifests, `.pkg`, setup/release artifacts.
- PosDashboard frontend outputs: `node_modules/`, `dist/`, build caches, npm logs.
- Secrets and local configuration: `.env`, local appsettings, keys/certificates, Railway/Supabase local env files.
- Logs and diagnostics: `*.log`, dumps, traces, temporary files.
- Docker local data: PostgreSQL data folders and local Docker state.

## Files that must remain versioned

- `.env.example`
- `package-lock.json`
- source code
- migrations
- validation scripts
- documentation

## Validation

Run:

```powershell
git status --ignored
```

Then verify that generated directories such as `.runtime/`, `bin/`, `obj/`, `node_modules/`, `dist/`, SQLite databases, logs, local env files, and ZIP/package outputs are ignored.
