# Hard Constraints

## Purpose
List every rule that the generated schedule must satisfy without exception.
A schedule that violates any hard constraint is invalid and must not be accepted as output.
Copilot uses this document when implementing constraint models in `SOEA.Engine.ConstraintProg`
and validators in `SOEA.Application`.

## Scope
All non-negotiable scheduling rules. Optimization preferences belong in `soft-constraints.md`.

---

## Constraint List

### Space Constraints

| ID | Rule |
|---|---|
| HC-S01 | A space may not host two sessions in the same time slot if both cohorts are Type A, both are Type B, or either cohort is NonAlternating |
| HC-S02 | The total enrolled students of sessions physically present in the same space and time slot must not exceed the space capacity |
| HC-S03 | A session that requires a specific space type (lab, auditorium) must be assigned to a space of that type |
| HC-S04 | Virtual sessions must not be assigned to a physical space |

### Instructor Constraints

| ID | Rule |
|---|---|
| HC-I01 | An instructor may not be assigned to two sessions in the same time slot |
| HC-I02 | A session must be assigned only to a time slot within the instructor's declared availability |
| HC-I03 | An instructor may not exceed the maximum contracted weekly hours |

### Time Constraints

| ID | Rule |
|---|---|
| HC-T01 | Sessions must be scheduled within the institution's operating hours (07:00–21:30) |
| HC-T02 | Laboratory sessions may not start after 19:30 (to end by 21:30 max) |
| HC-T03 | A 3-hour block must be assigned to consecutive hours on the same day |
| HC-T04 | No session may be scheduled during the midday break (12:00–13:00) unless the institution explicitly allows it |
| HC-T05 | Sessions that form a split block (hours distributed across days) must not be scheduled on consecutive days |

### Cohort Constraints

| ID | Rule |
|---|---|
| HC-C01 | A cohort may not have two sessions in the same time slot |
| HC-C02 | A cohort's total scheduled hours must match the curriculum-defined hours per subject per week |

### Subject-Specific Constraints

| ID | Rule |
|---|---|
| HC-SU01 | Subjects flagged as "always 8+8" (e.g., Química Orgánica) must be scheduled as two consecutive 8-hour sessions |
| HC-SU02 | Subjects flagged as non-alternating must have sessions in all weeks, not just alternating weeks |

---

## Validation Logic Location

Hard constraints are enforced in two places:
1. **Phase 2** (`SOEA.Engine.ConstraintProg`) — CP-SAT model enforces all HCs as hard constraints
2. **Post-generation validator** (`SOEA.Application`) — verifies output before returning the schedule

---

## Examples

- Assigning Instructor López to two sessions at Monday 07:00–09:00 → violates HC-I01
- Scheduling a lab session at 20:00 → violates HC-T02
- Assigning 30 students to a room with capacity 25 → violates HC-S02

---

## Open Questions

- Is HC-T04 (lunch break) configurable per institution, or always fixed at 12:00–13:00?
- What is the exact curriculum flag used to mark "always 8+8" subjects, and does it always mean two consecutive 8-hour sessions?
- Are there any hard limits on consecutive teaching hours for instructors?
