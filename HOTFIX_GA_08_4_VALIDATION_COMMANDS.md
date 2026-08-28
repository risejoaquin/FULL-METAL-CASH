# GA-08.4 validation

Railway PosDashboard variables:

```text
NIXPACKS_NODE_VERSION=22.12.0
VITE_POSSERVER_BASE_URL=https://full-metal-cash-production.up.railway.app
```

Root directory:

```text
/src/PosDashboard/SolidPOS.PosDashboard.Admin
```

Expected build plan:

```text
setup   | nodejs_22
install | npm ci
build   | npm install --no-save --package-lock=false @rolldown/binding-linux-x64-gnu@1.2.5 @tailwindcss/oxide-linux-x64-gnu@4.3.3 lightningcss-linux-x64-gnu@1.33.0 && npm run build
start   | npx serve -s dist -l $PORT
```

Expected Vite closure: `built` and a generated `dist/` directory. Then generate the PosDashboard Railway domain, add it to PosServer as `Cors__AllowedOrigins__0`, redeploy PosServer, verify Swagger is 404, and re-run GA-08.
