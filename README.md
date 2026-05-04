# SOEA — Sistema de Optimización de Espacios Académicos

SOEA is a university course timetabling system (UCTP) designed to automate and optimize academic scheduling for Colombian institutions that follow the alternancia (hybrid) model. It combines a **.NET Clean Architecture modular monolith** backend with an **Angular** frontend and a three-phase optimization engine (Graph Coloring → Constraint Programming → Genetic Algorithm).

---

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | .NET 8, ASP.NET Core, Clean Architecture |
| ORM / Persistence | Entity Framework Core, SQL Server / PostgreSQL |
| Excel Ingestion | EPPlus |
| Optimization Engine | OR-Tools (CP-SAT), custom Graph Coloring, Genetic Algorithm |
| Frontend | Angular (role-based UI) |
| Testing | xUnit |

---

## Repository Layout

```text
/
├── README.md                        ← you are here
├── SOEA.sln                         ← .NET solution file
│
├── docs/                            ← all project documentation (Copilot context)
│   ├── requirements/                ← scope, stakeholders, glossary, SRS
│   ├── business-rules/              ← alternancia, hard/soft constraints, pilot limits
│   ├── architecture/                ← architecture overview, module map, deployment
│   ├── data/                        ← data dictionary, ER model, JSON output spec
│   ├── algorithm/                   ← UCTP problem definition + 3 algorithm phases
│   ├── testing/                     ← test plan and acceptance criteria
│   ├── roadmap/                     ← delivery plan and milestones
│   └── archive/                     ← reference / source documents
│
├── src/                             ← backend source code (.NET)
│   ├── SOEA.Domain/                 ← entities, value objects, domain rules
│   ├── SOEA.Application/            ← use cases, commands, queries, DTOs
│   ├── SOEA.Infrastructure.Data/    ← EF Core, DB access, repositories
│   ├── SOEA.Infrastructure.Excel/   ← EPPlus Excel ingestion
│   ├── SOEA.Engine.GraphColoring/   ← Phase 1: graph coloring pre-assignment
│   ├── SOEA.Engine.ConstraintProg/  ← Phase 2: OR-Tools CP-SAT feasibility solver
│   ├── SOEA.Engine.Genetic/         ← Phase 3: genetic algorithm soft-constraint optimizer
│   └── SOEA.API/                    ← ASP.NET Core Web API (controllers, middleware)
│
├── test/                            ← automated tests
│   └── SOEA.Tests/                  ← unit and integration tests (xUnit)
│
└── frontend/                        ← Angular frontend
    └── soea-angular/                ← Angular workspace (role-based scheduling UI)
```

---

## Important Documentation Files

Start here when working on a new feature or asking Copilot for help:

| Topic | File |
|---|---|
| What the system is and who uses it | [`docs/requirements/scope.md`](docs/requirements/scope.md) |
| Domain vocabulary | [`docs/requirements/glossary.md`](docs/requirements/glossary.md) |
| Hard scheduling constraints | [`docs/business-rules/hard-constraints.md`](docs/business-rules/hard-constraints.md) |
| Soft/optimization preferences | [`docs/business-rules/soft-constraints.md`](docs/business-rules/soft-constraints.md) |
| Alternancia rules (Type A / B) | [`docs/business-rules/alternancia.md`](docs/business-rules/alternancia.md) |
| System architecture | [`docs/architecture/architecture-overview.md`](docs/architecture/architecture-overview.md) |
| Module responsibilities | [`docs/architecture/module-map.md`](docs/architecture/module-map.md) |
| Optimization problem (UCTP) | [`docs/algorithm/problem-definition-uctp.md`](docs/algorithm/problem-definition-uctp.md) |
| Data fields and meanings | [`docs/data/data-dictionary.md`](docs/data/data-dictionary.md) |
| JSON output format | [`docs/data/json-output-spec.md`](docs/data/json-output-spec.md) |

---

## Backend Module Responsibilities

### `SOEA.Domain`
Core business concepts with no external dependencies.
- Entities: `Session`, `Cohort`, `Space`, `Instructor`, `TimeSlot`, `Schedule`
- Value objects and enums: `AlternanciaType`, `ConstraintWeight`, `SessionStatus`
- Domain interfaces (ports) implemented by Infrastructure
- Business invariants (e.g., a session cannot exceed its allowed duration)

### `SOEA.Application`
Orchestration layer — coordinates domain objects and infrastructure.
- Use cases / command handlers (e.g., `GenerateScheduleCommand`, `ValidateConstraintsQuery`)
- DTOs for input/output
- Optimization pipeline coordination (calls the three engine phases in order)
- No direct dependency on EF Core, Excel, or HTTP

### `SOEA.Infrastructure.Data`
Data access implementation.
- EF Core `DbContext` and entity configurations
- Repository implementations
- Database migrations

### `SOEA.Infrastructure.Excel`
Excel ingestion via EPPlus.
- Readers for curriculum, instructor availability, and space data
- Mappers from Excel rows to domain entities

### `SOEA.Engine.GraphColoring`
Phase 1 of the optimization pipeline.
- Builds a conflict graph from session data
- Assigns preliminary time slots using graph coloring heuristics
- Output feeds Phase 2

### `SOEA.Engine.ConstraintProg`
Phase 2 of the optimization pipeline.
- Uses OR-Tools CP-SAT to enforce all hard constraints
- Returns a feasible (not necessarily optimal) schedule
- Output feeds Phase 3

### `SOEA.Engine.Genetic`
Phase 3 of the optimization pipeline.
- Genetic algorithm to optimize soft constraints
- Chromosome = complete schedule assignment
- Fitness function based on weighted soft-constraint violations

### `SOEA.API`
HTTP entry point.
- ASP.NET Core controllers and minimal API endpoints
- Authentication and role-based authorization middleware
- Request/response models and OpenAPI documentation

---

## Frontend Responsibilities (`frontend/soea-angular`)

Role-based Angular SPA:
- **Admin**: configure spaces, upload Excel data, trigger optimization
- **Coordinator**: review and validate generated schedules
- **Instructor / Student**: view personal timetables

---

## How to Use This Repo with Copilot

1. **Open the relevant doc first** before asking Copilot to generate code.
2. **Reference the doc in your prompt**, for example:
   > "Using `docs/business-rules/hard-constraints.md`, implement the hard-constraint validator in `SOEA.Engine.ConstraintProg`."
3. **Work in small, focused steps** — one use case, one entity, or one constraint at a time.
4. **Keep domain terminology consistent** across docs and code (see `docs/requirements/glossary.md`).
