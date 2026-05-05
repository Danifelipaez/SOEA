# Delivery Plan

## Purpose
Outline the development roadmap for SOEA: phases, milestones, and recommended work order.
This document guides the sequence in which Copilot should be asked to scaffold and implement features.

## Scope
Project timeline from initial setup to pilot deployment.

---

## Development Phases

### Phase 0 — Project Setup (Week 1)
- [x] Initialize .NET solution with Clean Architecture project structure
- [x] Create documentation folder structure (`docs/`)
- [ ] Set up Angular workspace (`frontend/soea-angular/`)
- [ ] Configure database connection (EF Core + SQL Server/PostgreSQL)
- [ ] Set up xUnit test project

### Phase 1 — Domain Model (Week 2)
- [ ] Implement domain entities: `Session`, `Cohort`, `Space`, `Instructor`, `TimeSlot`, `Schedule`, `Subject`
- [ ] Implement value objects: `AlternanciaType`, `TimeRange`
- [ ] Define domain interfaces: `IScheduleRepository`, `IOptimizationEngine`
- [ ] Write unit tests for domain entity invariants

### Phase 2 — Data Ingestion (Week 3)
- [ ] Implement `CurriculumExcelReader` using EPPlus
- [ ] Implement `InstructorAvailabilityReader`
- [ ] Implement `SpaceInventoryReader`
- [ ] Write integration tests for Excel readers
- [ ] Set up EF Core `DbContext` and initial migration

### Phase 3 — Optimization Engine: Graph Coloring (Week 4)
- [ ] Implement `ConflictGraphBuilder`
- [ ] Implement `GraphColoringScheduler` (Welsh-Powell heuristic)
- [ ] Write unit tests for graph construction and coloring

### Phase 4 — Optimization Engine: CP-SAT (Week 5–6)
- [ ] Add OR-Tools NuGet dependency
- [ ] Implement `CpSatSchedulerBuilder`
- [ ] Encode all hard constraints from `docs/business-rules/hard-constraints.md`
- [ ] Implement infeasibility reporting
- [ ] Write unit tests for each encoded constraint

### Phase 5 — Optimization Engine: Genetic Algorithm (Week 7–8)
- [ ] Implement `ScheduleChromosome`
- [ ] Implement `FitnessEvaluator` with all soft constraints from `docs/business-rules/soft-constraints.md`
- [ ] Implement `GeneticScheduleOptimizer` (selection, crossover, mutation, repair)
- [ ] Write unit tests for fitness function and genetic operations

### Phase 6 — Application Layer (Week 9)
- [ ] Implement `GenerateScheduleCommand` and handler
- [ ] Implement `ScheduleOptimizationPipeline` (orchestrates Phases 1→2→3)
- [ ] Implement `ConstraintValidator` (post-generation validation)
- [ ] Implement `IngestExcelCommand` and handler
- [ ] Write application-layer unit tests

### Phase 7 — API Layer (Week 10)
- [ ] Implement `ScheduleController` (generate, retrieve, publish)
- [ ] Implement `IngestionController` (upload Excel files)
- [ ] Add JWT authentication and role-based authorization
- [ ] Configure Swagger/OpenAPI
- [ ] Write API integration tests

### Phase 8 — Frontend (Week 11–12)
- [ ] Scaffold Angular workspace with routing and role-based guards
- [ ] Admin: Excel upload form + trigger optimization button
- [ ] Coordinator: schedule review grid + approval workflow
- [ ] Instructor/Student: personal timetable view

### Phase 9 — Pilot Validation (Week 13–14)
- [ ] Run pilot dataset through full pipeline
- [ ] Verify all acceptance criteria from `docs/testing/acceptance-criteria.md`
- [ ] Collect coordinator feedback
- [ ] Fix any blocking issues
- [ ] Final sign-off

---

## Recommended Ask-Copilot Order

When asking Copilot to generate code, follow this order for best results:

1. Domain entities (use `docs/data/data-dictionary.md`)
2. Domain interfaces (use `docs/architecture/module-map.md`)
3. Infrastructure implementations (use `docs/data/relational-model.md`)
4. Excel readers (use `docs/data/data-dictionary.md`)
5. Phase 1 engine (use `docs/algorithm/phase-1-graph-coloring.md`)
6. Phase 2 engine (use `docs/algorithm/phase-2-constraint-programming.md` + `docs/business-rules/hard-constraints.md`)
7. Phase 3 engine (use `docs/algorithm/phase-3-genetic-algorithm.md` + `docs/business-rules/soft-constraints.md`)
8. Application use cases (use `docs/requirements/SRS.md`)
9. API controllers (use `docs/data/json-output-spec.md`)
10. Angular components (use `docs/requirements/stakeholders.md`)

---

## Open Questions

- Is the 14-week timeline aligned with the academic semester calendar?
- Are there dependencies on IT infrastructure (server setup, database provisioning) that affect the timeline?
