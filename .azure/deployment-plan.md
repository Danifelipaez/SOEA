# Azure Deployment Plan — SOEA

## 1. Plan Status

**Status:** Validated (verified against live Azure state)
**Date:** 2026-09-08
**Recipe Type:** azcli (App Service + Static Web App)
**Environment:** Production (rg-soea)

⚠️ Deploy es 100% manual — no hay CI/CD (`.github/workflows/ci.yml` solo build+test). No confíes en el timestamp de un deploy sin verificar el commit real contra `az webapp deployment list` y, si aplica, un campo de respuesta ligado a un commit conocido.

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
| asp-soea | App Service Plan (Basic, sin deployment slots) | mexicocentral |
| soea-api | App Service (Linux, .NET 10) | mexicocentral |
| soea-frontend | Static Web App (Free, no enlazado a GitHub) | eastus2 |

---

## 4. Deploy Targets

### Backend — soea-api (App Service)

- Runtime: `DOTNETCORE|10.0`, Linux, Basic tier (sin slots — no hay blue/green)
- URL: https://soea-api.azurewebsites.net
- SCM/Kudu: `basicPublishingCredentialsPolicies/scm.allow = false` → solo auth AAD, nunca usuario/password de publish profile

**Procedimiento validado (2026-09-08):**

1. Crear un **git worktree limpio en `origin/main`** (nunca publicar desde el working directory normal — ver §8, riesgo de filtrar `appsettings.Development.json`).
2. Gate de calidad: `dotnet build -c Release` + `dotnet test` (verde antes de tocar Azure).
3. **Publicar con runtime explícito**, no un publish genérico:
   ```
   dotnet publish src/SOEA.API/SOEA.API.csproj -c Release -r linux-x64 --self-contained false -o publish_out
   ```
   Sin `-r linux-x64`, el publish arrastra binarios nativos de OR-Tools/SCIP/HiGHS para **5 plataformas** (win-x64, linux-x64, linux-arm64, osx-x64, osx-arm64) → zip de **342MB** en vez de **~84MB** (zip comprimido ~31MB). Es contenido que no corresponde a un App Service Linux — verificar siempre el tamaño de `publish_out/` antes de zipear.
4. Verificar que `publish_out/` no tenga `appsettings.Development.json`, `.env` ni `cp_model_debug.txt`.
5. Aplicar migraciones pendientes ANTES del deploy de código (ver §5) — código viejo tolera esquema nuevo, código nuevo contra esquema viejo puede 500.
6. Deploy: `az webapp deploy --type zip --clean true --restart true` (`--clean true` es necesario para de verdad descartar la versión anterior; sin él OneDeploy puede dejar archivos huérfanos).

### Frontend — soea-frontend (Static Web App)

- Deploy method: `npm ci` → `npm test` → `ng build` (production es la configuración default, no hace falta `--configuration production`) → `swa deploy`
- Output path: `dist/soea-angular/browser`
- URL: https://thankful-ground-0812d1f0f.7.azurestaticapps.net
- Deployment token: `az staticwebapp secrets list --name soea-frontend --resource-group rg-soea --query properties.apiKey -o tsv`
- `swa deploy` siempre reemplaza el contenido completo de producción — no requiere flag de "clean" aparte.

---

## 5. Migraciones

**Estado 2026-09-08:** las 20 migraciones commiteadas en `main` están aplicadas en prod (verificado vía `__EFMigrationsHistory`). La más reciente: `20260908003020_M8_UniqueEspacioBloqueSemana` (índice único `espacio_id+semana+bloque_tiempo_id` en `AsignacionesSemanales`, con limpieza de duplicados incluida en el propio `Up()` — segura de reaplicar sobre datos viejos).

**Cómo aplicar una migración pendiente contra prod** (sin `appsettings.Production.json` — todo vía env var, usando el mismo AAD login de `az`):
```bash
ACCESS_TOKEN=$(az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv)
cd src/SOEA.Infrastructure.Data
SOEA_DESIGN_TIME_DB="Host=soea-pg-srv.postgres.database.azure.com;Database=SOEAdb;Username=<tu-upn>;Password=$ACCESS_TOKEN;SSL Mode=Require;Trust Server Certificate=true" \
  dotnet ef database update --startup-project ../SOEA.API
```
Verificar con `SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 3;` vía el MCP de Postgres (o `postgres_database_query`, que es SELECT-only — no sirve para DDL).

---

## 6. App Settings relevantes

| Setting | Valor (2026-09-08) | Nota |
|---|---|---|
| `CpSat__SweepGrupos` | `true` | Diagnóstico de infactibilidad (qué grupo(s) causan el conflicto). Default en código es `false` — no hay `appsettings.Production.json`, así que si no está como App Setting explícito en Azure queda inerte sin fallar. Verificado end-to-end en prod (ver `sweepgrupos-config-gap` en memoria). |
| `CpSat__ExportarModelo` | `false` | Volcado de debug del modelo CP-SAT — dejar en `false` en prod. |
| `CpSat__TimeoutSegundos` | `120` | Timeout del solver por corrida. |
| `AllowedOrigins__0` / `__1` | URLs de `soea-frontend` (prod + preview) | CORS — actualizar si cambia el hostname del Static Web App. |

Ver/editar: `az webapp config appsettings list|set --resource-group rg-soea --name soea-api`.

---

## 7. Security Warnings (no reverificado hoy, heredado de auditoría previa)

- ⚠️ `httpsOnly: false` en `soea-api` — HTTPS no forzado (confirmado aún vigente 2026-09-08 vía `az webapp show`).
- ⚠️ Posible password placeholder sin rotar en algún app setting — pendiente de reverificar (no se auditaron todos los valores hoy, `appservice_webapp_settings_get-appsettings` del MCP requiere consentimiento interactivo no soportado en este cliente; usar `az webapp config appsettings list` directo si hace falta revisar).

---

## 8. Contenido que NO debe subirse (P0.1 / verificación 2026-09-08)

- `src/SOEA.API/appsettings.Development.json` — gitignored pero **existe en disco local** con la cadena de conexión de dev. `Microsoft.NET.Sdk.Web` copia automáticamente **cualquier** `appsettings*.json` presente en la carpeta del proyecto al publish, esté o no trackeado en git. Publicar desde el working directory normal lo filtraría al zip.
  **Mitigación:** publicar siempre desde un `git worktree` limpio de `origin/main` — un worktree solo contiene archivos trackeados, así que ese archivo no existe ahí.
- `cp_model_debug.txt` (raíz y `src/SOEA.API/`) — commiteado por error en `main` pese a estar en `.gitignore`. No se publica (no es content item del `.csproj`), así que no contamina el deploy, pero sigue siendo basura de repo pendiente de `git rm` (fuera de scope de un deploy normal).
- Binarios nativos multi-plataforma de OR-Tools — ver §4, mitigado con `-r linux-x64`.

---

## 9. Limitación conocida: subida lenta/inestable desde esta máquina

Confirmado 2026-09-08: la subida (upload) desde este entorno hacia internet es de **~10-150 KB/s** con cortes de conexión frecuentes, independientemente del destino (probado contra Kudu, Azure Blob Storage, y un endpoint neutral no-Azure). Las descargas (GET) van normales — es un problema específico de subida de esta red/máquina, no de Azure.

Consecuencia práctica: un `az webapp deploy --src-path <zip>` directo (push del cliente) puede colgarse o cortarse a mitad de camino con zips de más de unos pocos MB.

**Workaround validado que sí funciona, en orden de preferencia:**

1. Reducir el payload todo lo posible primero (ver §4 — `-r linux-x64` es la ganancia más grande).
2. Si aun así es grande (>~10MB), subir a un Azure Storage Blob con **AzCopy** en vez de `az webapp deploy --src-path` directo:
   ```bash
   az storage account create --name <nombre-temporal> --resource-group rg-soea --location mexicocentral --sku Standard_LRS --kind StorageV2 --allow-blob-public-access false
   az storage container create --account-name <nombre-temporal> --name deploy --auth-mode login
   SAS=$(az storage container generate-sas --account-name <nombre-temporal> --name deploy --permissions rwl --expiry <ISO+2h> -o tsv)  # SIN --auth-mode login: SAS por account key, evita el 403 AuthorizationPermissionMismatch de un user-delegation SAS sin RBAC de datos asignado
   AZCOPY_CONCURRENCY_VALUE=1 azcopy copy "<zip>" "https://<nombre-temporal>.blob.core.windows.net/deploy/<archivo>?${SAS}" --block-size-mb=1
   ```
   `AZCOPY_CONCURRENCY_VALUE=1` es clave: con concurrencia default (16 bloques en paralelo), la banda disponible se reparte entre tantos streams que ninguno llega a completar un bloque antes de que la conexión se corte — 0 progreso indefinidamente. Serializado a 1 conexión, cada bloque sí recibe toda la banda disponible y termina completando (lento — puede tomar ~1h para 30MB a este ritmo — pero termina).
3. Una vez el blob está subido, deploy vía `az webapp deploy --src-url "<blob-url-con-SAS-de-lectura>" --type zip --clean true --restart true` — esto es Azure-to-Azure (Azure jala el blob internamente), no depende de la subida local y termina en segundos.
4. Borrar el storage account temporal al terminar (`az storage account delete --name <nombre-temporal> --resource-group rg-soea --yes`).

Si `Microsoft.Storage` no está registrado en la suscripción (típico en una sub "Azure for Students" nueva), primero: `az provider register --namespace Microsoft.Storage` y esperar a `registrationState == Registered` antes de crear la cuenta.

**Si la subida directa a Kudu falla silenciosamente** (proceso vivo pero sin avance, CPU casi 0%, una sola conexión TCP establecida sin actividad durante minutos): es un cuelgue, no lentitud real — matar el proceso y pasar directamente al workaround del blob en vez de reintentar el mismo comando.

---

## 10. Validation Proof (2026-09-08)

| Check | Result |
|-------|--------|
| `dotnet build SOEA.sln -c Release` | ✅ 0 errores |
| `dotnet test SOEA.sln -c Release` | ✅ 427/427 |
| `npm test` (frontend) | ✅ 93/93 (18 archivos) |
| `ng build` (frontend, producción) | ✅ sin errores (solo warnings de budget preexistentes) |
| Migración `M8_UniqueEspacioBloqueSemana` | ✅ aplicada y confirmada en `__EFMigrationsHistory` |
| `az webapp deploy --clean true --restart true` (vía `--src-url` desde blob) | ✅ deployment activo, timestamp 2026-09-08T10:16 UTC |
| `CpSat__SweepGrupos=true` | ✅ confirmado con `az webapp config appsettings list` |
| `GET /api/grupos` (soea-api) | ✅ 200 |
| `GET /` (soea-frontend) | ✅ 200 |
| Test funcional de SweepGrupos: 2 grupos forzados al mismo salón/franja, sin persistir nada en BD | ✅ `motivoInfactibilidad: "Otro"`, `gruposEnConflicto` identificó correctamente ambos grupos |
| Storage account temporal | ✅ creado y borrado al finalizar |
