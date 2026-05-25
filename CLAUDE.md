# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

SOEA (Sistema de Optimización de Espacios Académicos) generates university weekly schedules. Given courses (asignaturas), teachers (docentes), and rooms (espacios), it runs a 3-phase optimization pipeline and returns a timetable. Backend: ASP.NET Core 10 Web API + PostgreSQL. Frontend: Angular 21 at `frontend/soea-angular`.

## Commands

All commands run from the solution root unless noted.

**Backend**
```powershell
dotnet build SOEA.sln
dotnet run --project src/SOEA.API          # API on http://localhost:5066
dotnet test SOEA.sln                        # all tests
dotnet test --filter "FullyQualifiedName~Architecture"   # single test class
dotnet test --filter "DisplayName~BloqueTiempo"          # single test
```

**EF Core migrations** (run from `src/SOEA.Infrastructure.Data`):
```powershell
dotnet ef migrations add <Name> --startup-project ../SOEA.API
dotnet ef database update --startup-project ../SOEA.API
```

**Frontend** (run from `frontend/soea-angular`):
```powershell
npm install
npm start        # ng serve → http://localhost:4200
npm run build
npm test         # vitest
```

## Architecture

Clean Architecture. Dependency direction: `API → Application → Domain ← Infrastructure / Engine`.

```
SOEA.Domain               Entities, Value Objects, Enums, repository & engine interfaces
SOEA.Application          Use-case services under Features/Asignaturas and Features/Horario
SOEA.Infrastructure.Data  EF Core + PostgreSQL: SOEABdContext, Repositories, Migrations
SOEA.Infrastructure.Excel EPPlus-based Excel import (ILectorExcel / LectorExcel)
SOEA.Engine.GraphColoring Phase 1: Welsh-Powell conflict-graph pre-assignment
SOEA.Engine.ConstraintProg Phase 2: Google OR-Tools CP-SAT hard-constraint solver (120 s timeout)
SOEA.Engine.Genetic        Phase 3: Genetic algorithm soft-constraint optimizer (200 gen, pop 50)
SOEA.API                  ASP.NET Core controllers + DI wiring (Program.cs)
SOEA.ConsoleRunner        Standalone CLI entry point
test/SOEA.Tests           xunit tests + NetArchTest architecture tests
```

### Schedule generation pipeline

`POST /api/horario/generar` → `GenerarHorarioService.EjecutarAsync`:

1. **Graph Coloring** (`AgendadorColoracionGrafo`): Welsh-Powell heuristic pre-assigns `BloqueTiempo` slots to sessions ordered by conflict-graph degree.
2. **CP-SAT** (`MotorConstraintProgramming`): enforces hard constraints — HC-I01 (no teacher overlap), HC-I02 (teacher availability), HC-S01 (no room overlap), HC-S03 (lab sessions in lab rooms). Phase 1 result used as warm-start hint.
3. **Genetic** (`MotorGenetico`): optimizes soft constraints via selection, crossover, mutation. Fitness is minimized; 30 stale-generation convergence threshold.

Returns `GenerarHorarioResponse` with `EsFactible`, `PuntajeFitness`, and `Sesiones[]`. On infeasibility, the API returns HTTP 422. The final `Horario` is persisted to PostgreSQL.

### Key domain concepts

- `BloqueTiempo` — 1-hour canonical slot (Mon–Fri 07:00–20:00, Sat 07:00–14:00). Generated in memory per request, **not stored** in DB.
- `Asignatura` — duration fixed at creation (`HorasPorSesion`, `SesionesPorSemana`); `Alternancia` auto-derived from name (`TipoA`/`TipoB`/`SinAlternancia`).
- `Sesion` — one scheduled occurrence; links asignatura, docente, bloque, and espacio.
- `Horario` — persisted aggregate; cannot be published with hard-constraint violations > 0.

### DI pattern

Each engine/infrastructure project exposes one extension method (`AddGraphColoringEngine()`, `AddConstraintProgEngine()`, `AddGeneticEngine()`, `AddInfrastructureData()`). Application services are registered as `AddScoped<ConcreteService>()` directly — no interface layer.

### Database

PostgreSQL `localhost:5432`, DB `SOEAdb`. Connection string lives in `src/SOEA.API/appsettings.json` and is also hardcoded in `SOEABdContextFactory` for design-time migrations. Entity configurations are in `SOEA.Infrastructure.Data/Configurations/`.

### Excel import format

`ILectorExcel` has three methods:
- `LeerCurriculumAsync` — cols A–J: Facultad, Programa, Asignatura, Código, TipoEspacio, Espacio, Duración, Día, Hora, Docente.
- `LeerAsignaturasModo2Async` — cols A–H (no Día/Hora).
- `LeerDisponibilidadDocentesAsync` — cols: Docente, Correo, MaxHoras, Días, Franjas.

### Frontend

Angular 21 with standalone components, Angular Material, and Chart.js. Routes:
- `/ingesta` — tabbed data entry (Asignaturas, Docentes, Espacios tabs), Excel upload supported.
- `/horario` — timetable grid, triggers `POST /api/horario/generar`.
- `/dashboard-admin`, `/dashboard-developer` — analytics views.

Global state lives in `StateService` (in-memory). `HorarioApiService` maps between Angular models and the API DTOs. The API base URL is hardcoded in `HorarioApiService` as `http://localhost:5066/api`.

### Architecture tests

`test/SOEA.Tests/Architecture/ArchitectureTests.cs` uses NetArchTest to enforce:
- Domain has no dependency on Application, Infrastructure, or API.
- Application has no dependency on Infrastructure.
- Infrastructure has no dependency on API.
- Repository classes reside in `SOEA.Infrastructure.Data.Repositories`.
