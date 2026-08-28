# SolidPOS Iteration 19 Hotfix 19.3 — PosDashboard Tailwind PostCSS v4 Alignment

## Estado
Hotfix correctivo para Iteration 19.

## Problema
El build de PosDashboard avanzó hasta Vite, pero falló en PostCSS porque `tailwindcss` ya no debe usarse directamente como plugin PostCSS en la versión instalada.

Error observado:

```text
[plugin vite:css]
Error: [postcss] It looks like you're trying to use `tailwindcss` directly as a PostCSS plugin.
The PostCSS plugin has moved to a separate package...
```

## Causa
`postcss.config.cjs` configuraba:

```js
tailwindcss: {}
```

Con Tailwind actual, el plugin correcto es:

```js
'@tailwindcss/postcss': {}
```

## Cambios

- `src/PosDashboard/SolidPOS.PosDashboard.Admin/package.json`
  - agrega `@tailwindcss/postcss` en `devDependencies`.
- `src/PosDashboard/SolidPOS.PosDashboard.Admin/postcss.config.cjs`
  - cambia el plugin PostCSS de `tailwindcss` a `@tailwindcss/postcss`.

## Alcance
No modifica backend, PosServer, PosCore, WPF, auth ni contratos API.

## Validación

```powershell
dotnet restore solidpos-platform.sln
dotnet build solidpos-platform.sln
dotnet test solidpos-platform.sln
```

```powershell
.\scripts\posdashboard\validate-posdashboard-admin-react.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com"
```

## Resultado esperado

```text
Building dashboard...
vite build
built in ...
Running dashboard self-test...
PosDashboard admin React foundation completed.
message : PosDashboard admin React foundation completed.
```
