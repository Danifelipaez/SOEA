# Phase 1 — Graph Coloring

## Purpose
Describe the graph coloring approach used in the first phase of SOEA's optimization pipeline.
Copilot uses this when implementing `SOEA.Engine.GraphColoring`.

## Scope
Phase 1 only: conflict graph construction and initial slot pre-assignment.
Phase 2 (CP feasibility) and Phase 3 (Genetic optimization) are described in their own docs.

---

## Goal of Phase 1

Produce a fast, plausible (but not necessarily constraint-perfect) initial assignment of sessions
to time slots. This **warm-start** seed reduces the search space for Phase 2 (CP-SAT).

Phase 1 does not assign rooms — only time slots. Room assignment is completed in Phase 2.

---

## The Conflict Graph

A **conflict graph** `G = (V, E)` is constructed where:
- Each **node** `v ∈ V` represents one session to be scheduled
- An **edge** `(v₁, v₂) ∈ E` means sessions `v₁` and `v₂` cannot be assigned the same time slot

### Edge Rules

Two sessions conflict (are connected by an edge) if ANY of the following is true:

| Condition | Reason |
|---|---|
| Same instructor | An instructor cannot teach two sessions simultaneously |
| Same cohort | A cohort cannot attend two sessions simultaneously |
| Same alternancia type OR non-alternating cohort involved | Both cohorts would physically occupy a space at the same time |
| Same cohort AND sequential dependency (split block constraint) | Consecutive-day restriction from HC-T05 |

### Alternancia Edge Logic

- TypeA + TypeA sharing a space → conflict edge (both physically present on the same weeks)
- TypeB + TypeB sharing a space → conflict edge
- TypeA + TypeB sharing a space → **no** conflict edge (never physically present simultaneously)
- Any session involving NonAlternating → conflict edge for space sharing

---

## Graph Coloring Algorithm

**Colors** = available time slots in `T`

Goal: assign a color (time slot) to each node (session) such that no two adjacent nodes share
the same color.

### Recommended Algorithm: Welsh-Powell Heuristic

1. Sort nodes by **degree** (number of edges) in descending order
2. Assign the lowest-numbered available color to each node in order, skipping colors used by neighbors
3. If no color is available, mark the session as uncolored so Phase 2 can resolve or reject it explicitly

This is a greedy heuristic — it is fast but may not minimize colors or satisfy all constraints.
If no available color exists, the session is marked as uncolored so Phase 2 can resolve or reject it explicitly.
Its output is refined in Phase 2.

---

## Inputs

- Full list of sessions `S` with their instructor, cohort, and alternancia type
- Set of available time slots `T`

## Outputs

- `PartialSchedule`: a mapping `session → timeSlot` for all sessions
- Sessions that could not be colored (if any) are flagged as `Conflict` status

---

## Integration with Phase 2

The `PartialSchedule` output is passed to `SOEA.Engine.ConstraintProg` as a warm-start hint.
Phase 2 may reassign slots for conflicting or infeasible sessions.

---

## Performance Target

Phase 1 should complete in under 5 seconds for up to 500 sessions.

---

## Open Questions

- Should the graph coloring use a DSatur algorithm instead of Welsh-Powell for better chromatic number minimization?
- Should soft constraints (e.g., prefer morning slots for certain cohorts) be incorporated as hints in Phase 1?
