# Alternancia Rules

## Purpose
Define the alternancia (hybrid) scheduling model used by the institution. This document is the
authoritative source for any code that assigns cohorts to in-person vs. virtual weeks.
Copilot uses this when generating session assignment logic and cohort scheduling constraints.

## Scope
All rules governing how cohorts alternate between presencial (in-person) and virtual modalities
across weeks in a semester.

---

## What Is Alternancia?

In the alternancia model, cohorts do not attend in-person every week. Instead, they alternate
between:
- **Presencial weeks** — students physically attend their assigned space
- **Virtual weeks** — sessions are delivered online (no physical space required)

This means two different cohorts can share the same physical space in the same time slot,
provided they are on opposite alternancia schedules.

---

## Types

### Type A (Tipo A)
- Attends in-person on **odd weeks** (weeks 1, 3, 5, …)
- Attends virtually on **even weeks** (weeks 2, 4, 6, …)

### Type B (Tipo B)
- Attends in-person on **even weeks** (weeks 2, 4, 6, …)
- Attends virtually on **odd weeks** (weeks 1, 3, 5, …)

---

## Key Rules

| Rule ID | Rule | Notes |
|---|---|---|
| ALT-01 | A Type A cohort and a Type B cohort may share the same space in the same time slot | They are never physically present at the same time |
| ALT-02 | Two Type A cohorts may NOT share the same space in the same time slot | They are always physically present on the same weeks |
| ALT-03 | Two Type B cohorts may NOT share the same space in the same time slot | Same reason as ALT-02 |
| ALT-04 | Virtual sessions do not consume physical space capacity | No space constraint applies during virtual weeks |
| ALT-05 | A session assigned to a fixed day/time applies to all weeks in the semester | The modality (presencial/virtual) changes, but the slot does not |
| ALT-06 | Some subjects require presencial attendance every week (non-alternating) | E.g., laboratory sessions — must be flagged in the curriculum data |

---

## Fixed vs. Flexible Sessions

- **Fixed sessions**: time slot assigned once and remains throughout the semester (most sessions)
- **Flexible sessions**: may be rescheduled per week (rare — only with explicit institutional approval)

SOEA currently only supports fixed session scheduling.

---

## Examples

- Cohort A-3 (Type A, Systems Engineering Sem 3) and Cohort B-7 (Type B, Systems Engineering Sem 7)
  can share Room 204 at 07:00–09:00 Monday because they are never both in-person on the same week.
- Cohort A-3 and Cohort A-5 (both Type A) cannot share the same space — they are in-person on
  the same weeks.

---

## Impact on Conflict Graph (Phase 1)

Two sessions conflict in the space dimension if and only if:
- Both cohorts have the same `AlternanciaType` (both A or both B), OR
- Either cohort is flagged as non-alternating (always presencial)

See `docs/algorithm/phase-1-graph-coloring.md` for how this affects edge construction.

---

## Open Questions

- Can a single cohort contain students with different alternancia types?
- How are virtual sessions tracked in the output JSON — are they listed as separate session records?
