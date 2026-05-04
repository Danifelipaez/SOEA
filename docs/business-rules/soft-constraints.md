# Soft Constraints

## Purpose
List scheduling preferences that the system should optimize but may relax when a fully optimal
solution is not achievable. These constraints contribute to the fitness function in Phase 3
(Genetic Algorithm).
Copilot uses this when implementing the fitness function in `SOEA.Engine.Genetic`.

## Scope
All optimization preferences. Rules that must never be violated belong in `hard-constraints.md`.

---

## Constraint List

Each soft constraint has a **weight** (higher = more important to satisfy).
Weights are configurable and serve as initial defaults.

| ID | Rule | Default Weight |
|---|---|---|
| SC-01 | Instructor schedules should be compact (minimize idle gaps between sessions) | 3 |
| SC-02 | Cohort schedules should be compact (minimize idle gaps for students) | 3 |
| SC-03 | Sessions for the same cohort on the same day should not leave more than 1 gap hour | 2 |
| SC-04 | Avoid scheduling a cohort's first session before 07:00 or last session after 19:00 when possible | 2 |
| SC-05 | Assign the same cohort to the same room for the same subject across weeks (classroom stability) | 2 |
| SC-06 | Distribute instructor workload evenly across days (avoid front-loading or back-loading) | 2 |
| SC-07 | Minimize the number of different spaces used by the same cohort per day | 1 |
| SC-08 | Prefer scheduling related subjects (same program, same cohort) in adjacent time slots | 1 |
| SC-09 | Avoid assigning instructors to the maximum allowed hours every day | 1 |

---

## Fitness Function Overview

The fitness score for a schedule (chromosome) is computed as:

```
fitness = Σ (weight_i × violation_count_i)   for all soft constraints SC-01..SC-09
```

A **lower** fitness score is better (fewer weighted violations).
A schedule with fitness = 0 perfectly satisfies all soft constraints.

The Genetic Algorithm minimizes this score across generations.
See `docs/algorithm/phase-3-genetic-algorithm.md` for full details.

---

## Examples

- A cohort with sessions at 07:00–09:00 and 13:00–15:00 with no class in between scores a
  violation for SC-02 (4-hour idle gap).
- An instructor scheduled for 6 consecutive hours on Monday and 0 hours on Friday scores a
  violation for SC-06.

---

## Open Questions

- Are the default weights above correct, or should the institution configure them per program?
- Should SC-05 (classroom stability) be promoted to a hard constraint for accessibility reasons?
- Is SC-04 (preferred hours) configurable per cohort or institution-wide?
