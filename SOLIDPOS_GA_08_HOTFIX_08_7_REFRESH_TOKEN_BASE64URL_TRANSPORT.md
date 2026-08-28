# SolidPOS GA-08 Hotfix 08.7 — Refresh Token Base64Url Transport Hardening

## Motivo

GA-08.6 aisló el fallo en `refresh-rotation`: login PASS, JWT claims PASS, pero `POST /api/v1/auth/refresh` devolvió `401 invalid-refresh-token` inmediatamente con el token recién emitido.

El refresh token anterior se generaba con Base64 estándar, que puede contener `+`, `/` y `=`. Aunque JSON válido debería transportarlo correctamente, esos caracteres son frágiles cuando pasan por shells, proxies, tooling de validación o clientes que accidentalmente tratan el valor como form/url encoded.

## Corrección

- `JwtTokenService.CreateRefreshToken()` conserva 64 bytes aleatorios de entropía.
- La representación pública cambia de Base64 estándar a Base64Url.
- El hash persistido sigue siendo SHA-256 del token exacto emitido.
- Se añade test unitario para prohibir `+`, `/` y `=` en tokens nuevos.
- El validator GA-08 exige explícitamente `Base64UrlEncoder.Encode` como contrato de seguridad/transporte.

## Alcance

No cambia schemaVersion.
No cambia syncContract.
No cambia PostgreSQL.
No cambia RLS.
No cambia CORS.
No cambia Swagger.
No cambia PosDashboard.

## Resultado esperado

Después de desplegar PosServer con este hotfix, GA-08 debe poder pasar el subcheck `refresh-rotation` usando un refresh token nuevo emitido por el login del mismo validator.
