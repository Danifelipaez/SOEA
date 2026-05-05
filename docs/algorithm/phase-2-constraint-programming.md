# Phase 2 — Constraint Programming (CP-SAT)

## Purpose
Describe how SOEA uses Google OR-Tools CP-SAT to enforce all hard constraints and produce
a feasible schedule. Copilot uses this when implementing `SOEA.Engine.ConstraintProg`.

## Scope
Phase 2 only: hard constraint enforcement and feasibility solving.
Phase 1 (Graph Coloring warm start) and Phase 3 (Genetic optimization) are in their own docs.

---

## Goal of Phase 2

Take the Phase 1 warm-start `PartialSchedule` and find a **fully feasible** assignment —
one that violates zero hard constraints — by using OR-Tools CP-SAT.

Phase 2 assigns both **time slots** and **spaces** to all sessions.

---

## Technology

- **Library**: Google OR-Tools CP-SAT solver (`Google.OrTools` NuGet package)
- **Model type**: Constraint Programming (CP), not Linear Programming (LP)
- **Solver timeout**: configurable (default 600 seconds)

---

## CP Model Overview

### Variables

For each session `s`:
- `timeVar(s)`: integer variable ∈ {0, ..., |T|-1} — index into the time slot array
- `spaceVar(s)`: integer variable ∈ {0, ..., |R|-1, null_index} — index into the space array

### Constraints Added to the Model

Each hard constraint from `docs/business-rules/hard-constraints.md` is encoded as follows:

| Hard Constraint | CP-SAT Encoding |
|---|---|
| HC-I01: Instructor conflict | For each pair of sessions with same instructor: `timeVar(s₁) ≠ timeVar(s₂)` |
| HC-C01: Cohort conflict | For each pair of sessions with same cohort: `timeVar(s₁) ≠ timeVar(s₂)` |
| HC-S01: Space conflict (same alt type) | For each pair with same alt type and both in-person: `¬(timeVar(s₁) = timeVar(s₂) ∧ spaceVar(s₁) = spaceVar(s₂))` |
| HC-S02: Capacity | `enrolledStudents(coh(s)) ≤ capacity(r)` — enforced by restricting `spaceVar(s)` domain to valid spaces |
| HC-I02: Instructor availability | `timeVar(s) ∈ domain(available(inst(s)))` |
| HC-S03: Space type | `spaceVar(s) ∈ domain(spacesOfType(type(s)))` |
| HC-T01–T02: Time bounds | Variable domain restricted to valid time slots only |
| HC-T03: 3h block = consecutive | Block sessions linked via auxiliary `AllDifferent` / ordering constraints |

### Warm Start (Hint)

The Phase 1 assignment is passed to CP-SAT as a **solution hint**:
```
solver.AddHint(timeVar(s), phase1TimeIndex(s))
```
This helps CP-SAT find a feasible solution faster.

---

## Inputs

- `PartialSchedule` from Phase 1 (time slot hints)
- Full domain model: sessions, instructors, cohorts, spaces, time slots

## Outputs

- `FeasibleSchedule`: a complete assignment of sessions to time slots and spaces
- All hard constraints satisfied (zero violations)
- If no feasible solution exists within the timeout, Phase 2 returns an `InfeasibleResult`

---

## Infeasibility Handling

If CP-SAT cannot find a feasible solution:
1. Log which constraints are most likely causing infeasibility (using CP-SAT assumptions API)
2. Return an error to the Application layer with a constraint conflict report
3. The Application layer surfaces this to the user as a validation error before Phase 3 runs

---

## Performance Target

Phase 2 should find a feasible solution within 120 seconds for pilot data volumes.
The configurable timeout cap is 600 seconds.

---

## Open Questions

- Should CP-SAT also optimize a basic objective (e.g., minimize spread) or only enforce feasibility?
- Should Phase 2 skip sessions that Phase 1 flagged as unresolvable and report them separately?
