# Azure Deployment Plan — SOEA

## 1. Plan Status

**Status:** Validated
**Date:** 2026-06-17
**Recipe Type:** azcli (App Service + Static Web App)
**Environment:** Production (rg-soea)

---

## 2. Subscription

| Field | Value |
|-------|-------|
| Subscription | Azure for Students |
| Tenant | fd69ce1b-20c6-42ec-b54e-6d1b3870ac6e |
| Resource Group | rg-soea |

---

## 3. Resources (existing — update only)

| Resource | Type | Location |
|----------|------|----------|
| soea-pg-srv | PostgreSQL Flexible Server | mexicocentral |
| asp-soea | App Service Plan | mexicocentral |
| soea-api | App Service (.NET 10) | mexicocentral |
| soea-frontend | Static Web App (Free) | eastus2 |

---

## 4. Deploy Targets

### Backend — soea-api (App Service)
- Runtime: DOTNETCORE|10.0
- Deploy method: `dotnet publish` → zip → `az webapp deploy --type zip`
- URL: https://soea-api.azurewebsites.net

### Frontend — soea-frontend (Static Web App)
- Deploy method: `npm run build --configuration production` → `swa deploy`
- Output path: `dist/soea-angular/browser`
- URL: https://thankful-ground-0812d1f0f.7.azurestaticapps.net

---

## 5. Pending Migrations (MUST apply in production)

| Migration | Added |
|-----------|-------|
| AddDocentePersistenciaFields | 2026-05-18 |
| AddDocenteIdToAsignatura | 2026-05-19 |
| HorarioBiSemanal | 2026-05-29 |
| CatalogoTiposAlternancia | 2026-06-05 |
| AddEspacioFijoIdToAsignatura | 2026-06-06 |

---

## 6. Security Warnings (non-blocking)

- ⚠️ `CambiaEsto2024!` password in app settings — placeholder never rotated
- ⚠️ `httpsOnly: false` on soea-api — HTTPS not enforced

---

## 7. Validation Proof

| Check | Result | Timestamp |
|-------|--------|-----------|
| `dotnet build SOEA.sln` | ✅ 0 errors, 4 warnings (CS8600 in ConsoleRunner) | 2026-06-17 |
| `dotnet test SOEA.sln` | ✅ 203/203 passing | 2026-06-17 |
| `az account show` | ✅ Azure for Students, linked | 2026-06-17 |
| `az resource list --resource-group rg-soea` | ✅ 4 resources confirmed | 2026-06-17 |
| Frontend env.prod.ts | ✅ apiBaseUrl = https://soea-api.azurewebsites.net/api | 2026-06-17 |
