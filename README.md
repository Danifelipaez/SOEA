# SOEA — Sistema de Optimización de Espacios Académicos

Genera el horario semanal de la Universidad del Magdalena con alternancia presencial/virtual por semanas. La coordinación académica carga asignaturas, grupos, docentes y aulas (a mano o desde Excel). Un optimizador de tres fases (coloración de grafos → OR-Tools CP-SAT → algoritmo genético) produce un horario que cumple todas las restricciones duras. Después, el horario se ajusta a mano.

| Capa | Tecnología |
|---|---|
| Backend | .NET 10, ASP.NET Core Web API, Clean Architecture |
| Persistencia | PostgreSQL + EF Core (migraciones automáticas al arrancar) |
| Optimización | Coloración Welsh-Powell, Google OR-Tools CP-SAT, algoritmo genético propio |
| Excel | EPPlus |
| Frontend | Angular 21 + Angular Material, signals (un solo operador, sin login) |
| Pruebas | xUnit + NetArchTest, vitest |
| Despliegue | GitHub Actions → Azure App Service + Static Web App |

## Estructura

```text
src/
  SOEA.Domain/                 entidades, reglas y servicios de dominio (sin dependencias externas)
  SOEA.Application/            casos de uso: generar, reacomodar, importar, CRUD
  SOEA.Engine.GraphColoring/   Fase 1
  SOEA.Engine.ConstraintProg/  Fase 2 (CP-SAT)
  SOEA.Engine.Genetic/         Fase 3
  SOEA.Infrastructure.Data/    EF Core, repositorios, migraciones
  SOEA.Infrastructure.Excel/   lector de Excel
  SOEA.API/                    controllers REST
test/SOEA.Tests/               pruebas del backend
frontend/soea-angular/         SPA
docs/                          documentación (empieza por docs/README.md)
```

## Arranque rápido

```bash
dotnet build SOEA.sln
dotnet run --project src/SOEA.API
```

```bash
cd frontend/soea-angular && npm install && npm start
```

El API (`http://localhost:5066`) necesita `src/SOEA.API/appsettings.Development.json` con la cadena de conexión a PostgreSQL. Detalle en [docs/operacion.md](docs/operacion.md).

## Documentación

- [docs/README.md](docs/README.md): índice de arquitectura, dominio, algoritmos, frontend, operación y pendientes.
- [CLAUDE.md](CLAUDE.md): reglas no negociables, convenciones y comandos para quien modifica el código.
