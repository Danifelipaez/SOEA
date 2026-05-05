# Pilot Limits

## Purpose
Define the scope and constraints of the initial pilot deployment of SOEA.
This document prevents over-engineering for the first release and sets clear acceptance
boundaries for testing.

## Scope
Boundaries of the pilot — what data, programs, and volumes are included in the first test run.

---

## Pilot Definition

The pilot is a controlled initial deployment with a limited dataset to:
1. Validate the optimization pipeline end-to-end
2. Verify hard constraint compliance
3. Collect feedback from Academic Coordinators before institution-wide rollout

---

## Pilot Scope (to be confirmed with domain expert)

| Parameter | Pilot Limit | Notes |
|---|---|---|
| Academic programs | TBD (e.g., 2–3 programs) | Systems Engineering + one other program suggested |
| Cohorts | ≤ 20 cohorts | Across all included programs |
| Instructors | ≤ 30 instructors | Only those teaching pilot-program subjects |
| Spaces | ≤ 15 spaces | Classrooms and labs assigned to pilot programs |
| Subjects per cohort | As per real curriculum | No simplification |
| Semester | 1 complete semester | Spring or Fall — to be defined |

---

## Acceptance Criteria for Pilot

1. Zero hard constraint violations in the generated schedule
2. Optimization runs in under 10 minutes for pilot data volume
3. JSON output is valid and matches the spec in `docs/data/json-output-spec.md`
4. At least 2 coordinators review and approve the pilot schedule
5. Soft-constraint fitness score is ≤ 20% above the documented pilot baseline score

---

## Known Risks

- Input data (Excel files) may have inconsistencies that must be caught during ingestion
- Instructor availability data may be incomplete for the first run
- Alternancia type assignments may not be uniform across cohorts

---

## Open Questions

- Which programs are confirmed for the pilot?
- Who are the 2 coordinators designated to validate the pilot output?
- What semester is the pilot targeting?
