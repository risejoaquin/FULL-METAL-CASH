# Iteration 12 validation commands

```powershell
dotnet restore solidpos-platform.sln
```

```powershell
dotnet build solidpos-platform.sln
```

```powershell
dotnet test solidpos-platform.sln
```

```powershell
.\scripts\smoke-test-deployment.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -Email "admin@micafeteria.com" `
  -Password "AdminSeguro123!" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde"
```

```powershell
.\scripts\poscore\validate-poscore-offline-auth-rbac.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -AdminEmail "admin@micafeteria.com" `
  -AdminPassword "AdminSeguro123!"
```

Expected final message:

```text
PosCore offline auth/session/RBAC runtime completed.
```

## Hotfix 12.2

Si la validación se detiene en la prueba del usuario con cache offline vencido, aplicar Hotfix 12.2 y repetir:

```powershell
.\scripts\poscore\validate-poscore-offline-auth-rbac.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -StoreId "8e446c29-e9ad-41ed-a738-125aff7608b6" `
  -AdminEmail "admin@micafeteria.com" `
  -AdminPassword "AdminSeguro123!"
```
