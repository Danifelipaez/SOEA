# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SOEA (Sistema de Optimización de Espacios Académicos) is an academic schedule generation system for universities. It takes courses (asignaturas), teachers (docentes), and rooms (espacios) as inputs and produces a weekly schedule (horario) as output. The backend is an ASP.NET Core 10 Web API backed by PostgreSQL; the frontend is Angular at `../../frontend/soea-angular`.

## Commands

All commands run from the solution root (`A:\Unversidad\_PRACTICAS_UNI\CODIGO\SOEA`), unless stated otherwise.

**Build:**
```
dotnet build SOEA.sln
```

**Run the API** (from `src/SOEA.API`):
```
dotnet run
```
The API starts on `https://localhost:7xxx` / `http://localhost:5xxx`. CORS allows `http://localhost:4200`.

**EF Core migrations** (run from `src/SOEA.Infrastructure.Data`, targeting the API project for the connection string):
```
dotnet ef migrations add <MigrationName> --startup-project ../SOEA.API
dotnet ef database update --startup-project ../SOEA.API
```

**Angular frontend** (from `frontend/soea-angular`):
```
npm install
npm start    # ng serve at http://localhost:4200
```

## Architecture

The solution follows Clean Architecture. Dependency direction: API → Application → Domain ← Infrastructure/Engine.

```
SOEA.Domain              — Entities, Value Objects, Enums, Repository interfaces, Engine interfaces
SOEA.Application         — Use-case services (Features/Asignaturas, Features/Horario)
SOEA.Infrastructure.Data — EF Core + PostgreSQL (SOEABdContext, Repositories, Migrations)
SOEA.Infrastructure.Excel— EPPlus-based Excel import (ILectorExcel)
SOEA.Engine.GraphColoring— Phase 1: Welsh-Powell graph-coloring pre-assignment
SOEA.Engine.ConstraintProg— Phase 2: Google OR-Tools CP-SAT feasibility solver
SOEA.Engine.Genetic      — Phase 3: Genetic algorithm soft-constraint optimizer
SOEA.API                 — ASP.NET Core controllers + DI wiring
SOEA.ConsoleRunner       — Standalone CLI entry point
```

### Schedule generation pipeline (`GenerarHorarioService`)

`POST /api/horario/generar` drives a 3-phase pipeline:

1. **Phase 1 – Graph Coloring** (`AgendadorColoracionGrafo`): Uses the Welsh-Powell heuristic to pre-assign time blocks to sessions, minimizing conflicts by ordering sessions by conflict-graph degree.
2. **Phase 2 – Constraint Programming** (`MotorConstraintProgramming`): Google OR-Tools CP-SAT enforces hard constraints (HC-I01 no teacher overlap, HC-I02 teacher availability, HC-S01 no room overlap, HC-S03 lab rooms for lab sessions). Phase 1 results are used as warm-start hints. Timeout is 120 s.
3. **Phase 3 – Genetic Algorithm** (`MotorGenetico`): Starting from the Phase 2 feasible solution, optimizes soft constraints via selection, crossover, and mutation over up to 200 generations (population 50, convergence threshold 30 stale generations). Fitness is minimized (lower = better).

The final `Horario` entity is persisted to PostgreSQL and the response contains all assigned `SesionGeneradaDto` objects ready for the frontend grid.

### Key domain entities

- `Asignatura` — immutable duration (`HorasPorSesion`, `SesionesPorSemana`); `Alternancia` derived from `sesionesLaboratorioSemestre` vs. a threshold (`DeterminarAlternancia`, default 8 → TipoA; >8 → TipoB; <8 → SinAlternancia), or set manually via `EstablecerAlternancia` (Rosa's override).
- `Sesion` — a single scheduled class occurrence; links asignatura, docente, bloque de tiempo, and espacio.
- `BloqueTiempo` — canonical 1-hour slot (Mon–Fri 07:00–20:00, Sat 07:00–14:00); generated in memory per request, not stored.
- `Horario` — persisted aggregate that holds a list of session IDs and fitness score; cannot be published if hard-constraint violations > 0.

### DI registration pattern

Each engine/infrastructure project exposes an extension method registered in `Program.cs`:
- `services.AddGraphColoringEngine()`
- `services.AddConstraintProgEngine()`
- `services.AddGeneticEngine()`
- Application services are registered directly as `AddScoped<ConcreteService>()` (no interface abstraction).

### Database

PostgreSQL at `localhost:5432`, database `SOEAdb`. Connection string is in `appsettings.json` (also hardcoded in `SOEABdContextFactory` for design-time migrations). Entity configurations live in `SOEA.Infrastructure.Data/Configurations/`.

### Excel import

`ILectorExcel` (implemented by `LectorExcel`) has three methods:
- `LeerCurriculumAsync` — full horario Excel (cols A–J: Facultad, Programa, Asignatura, Código, TipoEspacio, Espacio, Duración, Día, Hora, Docente).
- `LeerAsignaturasModo2Async` — asignaturas-only Excel (cols A–H, no Día/Hora).
- `LeerDisponibilidadDocentesAsync` — teacher availability Excel (cols: Docente, Correo, MaxHoras, Días, Franjas).
