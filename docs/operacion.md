# Operación: desarrollo, pruebas y despliegue

## Desarrollo local

Requisitos: .NET 10 SDK, Node 24, PostgreSQL en `localhost:5432` (BD `SOEAdb`).

1. Crear `src/SOEA.API/appsettings.Development.json` (gitignored) con `ConnectionStrings:DefaultConnection`. Nunca commitear credenciales.
2. `dotnet run --project src/SOEA.API` → API en `http://localhost:5066` (OpenAPI en `/openapi/v1.json` en Desarrollo). Aplica las migraciones al arrancar.
3. `cd frontend/soea-angular && npm install && npm start` → `http://localhost:4200`.

Una BD nueva necesita los catálogos semilla (`TiposAlternancia`, `CriteriosCesionAlternancia`, `BloqueTiempos`); los siembran las migraciones. Si faltan, la alternancia responde 409.

## Migraciones

Desde `src/SOEA.Infrastructure.Data`:

```powershell
dotnet ef migrations add <Nombre> --startup-project ../SOEA.API
dotnet ef database update --startup-project ../SOEA.API
```

`SOEABdContextFactory` (design-time) toma la cadena de `SOEA_DESIGN_TIME_DB` o del `appsettings.Development.json` del API. Convención de nombres actual: `M<n>_<Descripción>`. Si la migración agrega una FK, debe sanear antes los datos huérfanos (como M14–M19).

## Pruebas

| Suite | Comando | Notas |
|---|---|---|
| Backend | `dotnet test SOEA.sln` | ~500 pruebas xUnit en `test/SOEA.Tests` (Architecture, Domain, Application, Engine, Infrastructure, API, Integracion) |
| Postgres real | definir `SOEA_TEST_DB` (cadena con servidor y credenciales) | Crea y borra BDs `soea_it_*`, nunca toca `SOEAdb`. Sin la variable se omiten; con ella y sin servidor, **fallan**. Comprobar "Omitido: 0" |
| Arquitectura | `dotnet test --filter "FullyQualifiedName~Architecture"` | NetArchTest |
| Frontend | `npm test` (vitest) · `npm run build` | Presupuesto de bundle inicial: 500 kB |

Dobles escritos a mano en `test/SOEA.Tests/Fakes/RepositorioFakes.cs` (no se usa NSubstitute). Si el API está corriendo y bloquea los binarios, compilar los tests con otro `OutDir`.

## CI/CD (GitHub Actions)

- `ci.yml`: en push a `Developing`, en PR a `main`/`Developing` y reutilizado por `deploy.yml` → build + test del backend (con contenedor `postgres:16` y `SOEA_TEST_DB`) y build + test del frontend.
- `deploy.yml`: en push a `main` → CI → `dotnet publish -r linux-x64` → App Service `soea-api` (login OIDC) → Static Web App `soea-frontend`. El frontend se despliega después del API.
- Flujo de ramas: se trabaja en `Developing`; pasar a producción = llevar `main` a `Developing`.
- La skill `/deploy-prod` cubre el despliegue manual, la verificación de la versión en prod y la limpieza de la BD.

## Producción (Azure, `rg-soea`)

| Recurso | Tipo |
|---|---|
| `soea-api` | App Service Linux .NET 10, plan Basic, sin slots — `https://soea-api.azurewebsites.net` |
| `soea-frontend` | Static Web App (Free) |
| `soea-pg-srv` | PostgreSQL Flexible Server |

- Configuración por variables de la App Service (cadena de conexión, `AllowedOrigins` con la URL del Static Web App, `CpSat__SweepGrupos=true`).
- Para saber qué corre en prod, verificar contra el endpoint vivo y `__EFMigrationsHistory`, no contra la fecha del despliegue. Detalle de recursos en `.azure/deployment-plan.md`.

## Scripts

- `docs/scripts/Fase2_FusionAsignaturasDuplicadas.sql`: fusión de asignaturas duplicadas heredadas (referenciado desde la migración `Fase2DocenteEnGrupo`; gitignored por la regla `*.sql`).
