# Test Plan

## Purpose
Define the testing strategy for SOEA: what types of tests exist, what they cover, and how
to run them. Copilot uses this when generating test files and test data.

## Scope
All automated tests in `test/SOEA.Tests/`. Manual/acceptance testing is in `acceptance-criteria.md`.

---

## Test Levels

### Unit Tests

Cover individual classes in isolation with all dependencies mocked.

| Area | Target Class | What to Test |
|---|---|---|
| Domain | `Session` | Entity invariants (duration > 0, virtual session has no space) |
| Domain | `TimeSlot` | StartTime < EndTime validation |
| Domain | `Cohort` | AlternanciaType assignment |
| Application | `ConstraintValidator` | Hard constraint detection logic |
| Application | `ScheduleOptimizationPipeline` | Phase orchestration calls (mock engines) |
| Phase 1 | `ConflictGraphBuilder` | Edge construction for each conflict type |
| Phase 1 | `GraphColoringScheduler` | Coloring produces no adjacent nodes with same color |
| Phase 2 | `HardConstraintEncoder` | Each HC maps to the correct CP-SAT constraint type |
| Phase 3 | `FitnessEvaluator` | Fitness = 0 for a schedule with zero soft violations |
| Phase 3 | `GeneticScheduleOptimizer` | Fitness improves or stays equal across generations |

### Integration Tests

Test the interaction between layers using real (in-memory or test) data.

| Test | What to Verify |
|---|---|
| End-to-end pipeline (small dataset) | Full pipeline (Phase 1→2→3) produces a schedule with zero hard violations |
| Excel ingestion | Reading a sample Excel file produces the correct domain entities |
| API endpoint: POST /schedule/generate | Returns 200 with valid JSON output matching `json-output-spec.md` |
| Database round-trip | A saved schedule can be loaded and matches the original |

### Constraint-Specific Tests

Tests that directly validate each hard constraint from `hard-constraints.md`.

| Constraint | Test Scenario |
|---|---|
| HC-I01 | Two sessions with the same instructor in the same time slot → should be flagged |
| HC-S02 | 30 students assigned to a room of capacity 25 → should be flagged |
| HC-T02 | Lab session starting at 20:00 → should be flagged |
| HC-T05 | Split-block sessions on consecutive days → should be flagged |
| HC-S04 | Virtual session with a physical space assigned → should be flagged |

---

## Test Data

- Small test dataset: 5 cohorts, 3 instructors, 5 spaces, 20 sessions
- Edge case dataset: session that cannot be scheduled (all instructor slots occupied)
- Alternancia dataset: mix of Type A, Type B, and NonAlternating cohorts

Test data files should be placed in `test/SOEA.Tests/TestData/`.

---

## Running Tests

```bash
dotnet test test/SOEA.Tests/SOEA.Tests.csproj
```

---

## Test Coverage Target

- Domain layer: 90% line coverage
- Application layer: 80% line coverage
- Engine layers: 75% line coverage (complex algorithm paths)
- Infrastructure layers: 60% line coverage (focus on integration tests)

---

## Open Questions

- Should each engine phase have its own test project (e.g., `SOEA.Engine.GraphColoring.Tests`)?
- Should test data be embedded as C# objects or loaded from JSON/Excel files?
