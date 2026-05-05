# Module Map

## Purpose
List every project/assembly in the SOEA solution, describe its single responsibility,
and define which other projects it may depend on. Copilot uses this when generating
new classes, deciding which project a file belongs to, and enforcing layer boundaries.

## Scope
All projects in the `src/` and `test/` directories.

---

## Module Dependency Rules

```
SOEA.API               → SOEA.Application
SOEA.API               → SOEA.Infrastructure.Data
SOEA.API               → SOEA.Infrastructure.Excel
SOEA.Application       → SOEA.Domain
SOEA.Application       → SOEA.Engine.GraphColoring
SOEA.Application       → SOEA.Engine.ConstraintProg
SOEA.Application       → SOEA.Engine.Genetic
SOEA.Infrastructure.*  → SOEA.Domain
SOEA.Engine.*          → SOEA.Domain
SOEA.Domain            → (no dependencies on other SOEA projects)
```

**Never allow**: Domain → Infrastructure, Domain → API, Application → Infrastructure (directly),
Application → API

---

## Module Details

### `SOEA.Domain`
**Path**: `src/SOEA.Domain/`
**Responsibility**: Core business model with no external dependencies.

Contains:
- Entities: `Session`, `Cohort`, `Space`, `Instructor`, `TimeSlot`, `Schedule`, `Subject`
- Value objects: `AlternanciaType`, `TimeRange`, `Capacity`
- Enums: `SpaceType`, `SessionStatus`, `ConstraintSeverity`
- Domain interfaces (ports): `IScheduleRepository`, `IOptimizationEngine`, `IExcelIngestionService`
- Domain exceptions: `ConstraintViolationException`, `InvalidSessionException`

**Must not reference**: EF Core, EPPlus, OR-Tools, ASP.NET, Angular

---

### `SOEA.Application`
**Path**: `src/SOEA.Application/`
**Responsibility**: Use cases, commands, queries, and pipeline orchestration.

Contains:
- Commands: `GenerateScheduleCommand`, `IngestExcelCommand`, `PublishScheduleCommand`
- Queries: `GetScheduleQuery`, `ValidateConstraintsQuery`
- DTOs: `ScheduleDto`, `SessionDto`, `CohortDto`
- Pipeline orchestrator: `ScheduleOptimizationPipeline` (calls Graph Coloring → CP → Genetic)
- Validation service: `ConstraintValidator`

**Depends on**: `SOEA.Domain`
**Must not reference**: EF Core, EPPlus, OR-Tools, ASP.NET

---

### `SOEA.Infrastructure.Data`
**Path**: `src/SOEA.Infrastructure.Data/`
**Responsibility**: Database access using EF Core.

Contains:
- `SoeaDbContext` and entity type configurations
- Repository implementations (`ScheduleRepository`, `CohortRepository`, etc.)
- Database migrations

**Depends on**: `SOEA.Domain`, Entity Framework Core
**Implements**: `IScheduleRepository` and other repository interfaces from `SOEA.Domain`

---

### `SOEA.Infrastructure.Excel`
**Path**: `src/SOEA.Infrastructure.Excel/`
**Responsibility**: Read institutional data from Excel files using EPPlus.

Contains:
- `CurriculumExcelReader` — reads subject/cohort/hours data
- `InstructorAvailabilityReader` — reads availability grids
- `SpaceInventoryReader` — reads room capacity and type data
- Mappers from Excel rows to domain entities

**Depends on**: `SOEA.Domain`, EPPlus
**Implements**: `IExcelIngestionService` from `SOEA.Domain`

---

### `SOEA.Engine.GraphColoring`
**Path**: `src/SOEA.Engine.GraphColoring/`
**Responsibility**: Phase 1 of the optimization pipeline.

Contains:
- `ConflictGraphBuilder` — creates the session conflict graph
- `GraphColoringScheduler` — assigns preliminary time slots using coloring heuristics (e.g., Welsh-Powell)
- Output: `PartialSchedule` passed to Phase 2

**Depends on**: `SOEA.Domain`
**Implements**: `IGraphColoringEngine` interface from `SOEA.Domain` or `SOEA.Application`

---

### `SOEA.Engine.ConstraintProg`
**Path**: `src/SOEA.Engine.ConstraintProg/`
**Responsibility**: Phase 2 — enforce all hard constraints using OR-Tools CP-SAT.

Contains:
- `CpSatSchedulerBuilder` — translates domain model to CP-SAT variables and constraints
- `HardConstraintEncoder` — adds each hard constraint from `docs/business-rules/hard-constraints.md` as a CP-SAT constraint
- `FeasibleScheduleExtractor` — converts CP-SAT solution back to domain objects

**Depends on**: `SOEA.Domain`, Google OR-Tools
**Implements**: `IConstraintProgrammingEngine` from `SOEA.Domain` or `SOEA.Application`

---

### `SOEA.Engine.Genetic`
**Path**: `src/SOEA.Engine.Genetic/`
**Responsibility**: Phase 3 — optimize soft constraints using a Genetic Algorithm.

Contains:
- `ScheduleChromosome` — encodes a complete schedule as a chromosome
- `FitnessEvaluator` — computes the weighted soft-constraint violation score
- `GeneticScheduleOptimizer` — runs selection, crossover, mutation, and convergence logic

**Depends on**: `SOEA.Domain`
**Implements**: `IGeneticOptimizationEngine` from `SOEA.Domain` or `SOEA.Application`

---

### `SOEA.API`
**Path**: `src/SOEA.API/`
**Responsibility**: HTTP entry point — exposes the system as a REST API.

Contains:
- Controllers: `ScheduleController`, `IngestionController`, `ValidationController`
- Middleware: JWT authentication, role-based authorization
- Request/response models (separate from Application DTOs)
- OpenAPI / Swagger configuration

**Depends on**: `SOEA.Application`, all Infrastructure and Engine projects (for DI registration)

---

### `SOEA.Tests`
**Path**: `test/SOEA.Tests/`
**Responsibility**: Automated test suite (unit + integration tests).

Contains:
- Domain unit tests (entity invariants, constraint checks)
- Application unit tests (use case logic, pipeline orchestration)
- Engine unit tests (graph coloring, CP model correctness, GA fitness)
- Integration tests (end-to-end pipeline with test data)

**Depends on**: All `SOEA.*` projects, xUnit, Moq (or NSubstitute)

---

## Open Questions

- Should each engine phase have its own test project (e.g., `SOEA.Engine.GraphColoring.Tests`)?
- Should Application DTOs be a separate project (`SOEA.Application.Contracts`) to allow
  sharing with the frontend TypeScript client?
