# Architecture Overview

## Purpose
Describe the high-level system architecture of SOEA so that Copilot and contributors can
understand how the layers interact, what technologies are used, and where to place new code.

## Scope
Backend architecture, frontend, database, and integration points. Low-level implementation
details belong in module-specific docs.

---

## Architecture Style

SOEA uses **Clean Architecture** organized as a **.NET Modular Monolith**.

- One deployable unit (single API process)
- Internal boundaries enforced by project/assembly separation
- No microservices — simplicity is intentional for a pilot-scale system
- Dependencies flow inward: `WebApi → Application → Domain` (Infrastructure implements Domain interfaces)

---

## Layer Diagram

```
┌─────────────────────────────────────────────────────────┐
│                     SOEA.API                            │
│         (ASP.NET Core controllers, middleware)          │
└────────────────────────┬────────────────────────────────┘
                         │ calls
┌────────────────────────▼────────────────────────────────┐
│                 SOEA.Application                        │
│   (use cases, commands, queries, pipeline orchestration)│
└───────┬────────────────┬────────────────────────────────┘
        │ domain         │ calls engines + infra (via interfaces)
        ▼                ▼
┌───────────────┐   ┌──────────────────────────────────────┐
│ SOEA.Domain   │   │ Infrastructure + Engine layers        │
│ (entities,    │   │ ┌──────────────────────────────────┐ │
│  value objs,  │   │ │ SOEA.Infrastructure.Data (EF Core)│ │
│  interfaces)  │   │ │ SOEA.Infrastructure.Excel (EPPlus)│ │
└───────────────┘   │ │ SOEA.Engine.GraphColoring         │ │
                    │ │ SOEA.Engine.ConstraintProg        │ │
                    │ │ SOEA.Engine.Genetic               │ │
                    │ └──────────────────────────────────┘ │
                    └──────────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────┐
│          SQL Server / PostgreSQL database               │
└─────────────────────────────────────────────────────────┘
```

---

## Technology Choices

| Concern | Technology | Rationale |
|---|---|---|
| Backend framework | ASP.NET Core (.NET 10) | Mature, cross-platform, strong ecosystem |
| ORM | Entity Framework Core | Reduces boilerplate, good LINQ support |
| Database | SQL Server or PostgreSQL | Relational model fits timetabling data |
| Excel ingestion | EPPlus | .NET-native, no Office dependency |
| Constraint solver | OR-Tools CP-SAT | Free, proven, supports large combinatorial problems |
| Genetic algorithm | Custom implementation | Fits SOEA-specific chromosome and fitness design |
| Frontend | Angular | TypeScript-first, good component architecture for scheduling UI |
| Testing | xUnit | Standard .NET testing framework |

---

## Optimization Pipeline

```
Excel Input → [Ingestion] → Domain Model → [Phase 1: Graph Coloring]
    → Partial Schedule → [Phase 2: CP-SAT] → Feasible Schedule
    → [Phase 3: Genetic Algorithm] → Optimized Schedule → JSON Output
```

Each phase is implemented in its own project under `src/`:
- `SOEA.Engine.GraphColoring` — Phase 1
- `SOEA.Engine.ConstraintProg` — Phase 2
- `SOEA.Engine.Genetic` — Phase 3

The Application layer orchestrates the pipeline without knowing implementation details.

---

## Key Design Decisions

1. **Monolith over microservices** — simplifies deployment for a university IT team
2. **Separate engine projects** — each algorithm phase is independently testable
3. **Interface-based dependency** — Application calls engine interfaces; implementations
   can be swapped (e.g., replace Genetic with Simulated Annealing in the future)
4. **JSON as the canonical output** — the schedule is serialized to JSON for export,
   frontend consumption, and audit trails

---

## Open Questions

- Should OR-Tools be wrapped in its own project (`SOEA.Engine.ConstraintProg`) or merged
  into `SOEA.Infrastructure`?
- Is PostgreSQL preferred over SQL Server for the production environment?
