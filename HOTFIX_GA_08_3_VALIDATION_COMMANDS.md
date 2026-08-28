# GA-08.3 validation

Redeploy only the PosDashboard Railway service.

Expected Nixpacks plan:
- setup: nodejs_22
- install: npm ci
- build: npm run build
- start: npx serve -s dist -l $PORT

Expected build script:
`node ./node_modules/typescript/bin/tsc -b && node ./node_modules/vite/bin/vite.js build`

After deployment, generate the PosDashboard public domain and configure that exact origin in PosServer as `Cors__AllowedOrigins__0`.
