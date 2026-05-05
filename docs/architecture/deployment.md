# Deployment

## Purpose
Describe how SOEA is deployed, what infrastructure it requires, and how environments differ.
Copilot uses this when generating configuration files, Docker setups, or environment-specific code.

## Scope
Development, staging, and production deployment configurations.

---

## Deployment Model

SOEA is deployed as a **single-process monolith** with the following components:

| Component | Deployment Unit |
|---|---|
| .NET API | Single ASP.NET Core process |
| Database | SQL Server or PostgreSQL instance |
| Frontend | Angular SPA (static files served by the API or a CDN/reverse proxy) |

---

## Environment Matrix

| Environment | Database | Frontend | Notes |
|---|---|---|---|
| Development | Local SQL Server / SQLite | `ng serve` (Angular dev server) | Developer workstation |
| Staging | SQL Server / PostgreSQL | Built Angular SPA | Integration testing |
| Production | PostgreSQL (recommended) | Served via Nginx or Azure Static Web Apps | Pilot deployment |

---

## Configuration

Environment-specific settings are stored in:
- `src/SOEA.API/appsettings.json` — base configuration
- `src/SOEA.API/appsettings.Development.json` — development overrides
- Environment variables — secrets and production overrides (never committed to source control)

Required configuration keys:
- `ConnectionStrings:DefaultConnection` — database connection string
- `Jwt:Secret` — JWT signing key
- `Jwt:Issuer` / `Jwt:Audience` — token metadata
- `OptimizationEngine:TimeoutSeconds` — max solver time (default: 600)

---

## Docker (future)

A `Dockerfile` and `docker-compose.yml` are planned for staging deployment.
The compose file will define:
- `soea-api` service (ASP.NET Core)
- `soea-db` service (PostgreSQL)
- Volume mount for Excel input files

---

## Open Questions

- Is Azure, on-premises Linux, or a university server the target production host?
- Is Docker available on the target production server?
- Should the Angular app be served by the ASP.NET Core API (embedded) or separately?
