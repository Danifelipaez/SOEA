# Problem Definition — University Course Timetabling Problem (UCTP)

## Purpose
Define SOEA's scheduling problem formally as a UCTP variant. Copilot uses this document
when generating optimization models, constraint encodings, and solver configurations.

## Scope
Formal problem definition, variables, constraints, and objective function.
Algorithm-specific implementation details are in the Phase docs.

---

## What Is UCTP?

The **University Course Timetabling Problem (UCTP)** is a combinatorial optimization problem
where the goal is to assign a set of events (sessions) to a set of time periods and rooms such
that a set of constraints is satisfied.

SOEA solves a specific variant of UCTP that includes:
- Alternancia (hybrid) cohort model
- Three-phase optimization pipeline
- Hard constraints (must satisfy) and soft constraints (optimize)

---

## Formal Definition

### Input

Let:
- **S** = set of sessions to be scheduled
- **T** = set of available time slots (day × start–end time)
- **R** = set of physical spaces (rooms)
- **I** = set of instructors
- **C** = set of cohorts

Each session `s ∈ S` has:
- A required instructor `inst(s) ∈ I`
- A required cohort `coh(s) ∈ C`
- A required duration `dur(s)` in hours
- An optional required space type `type(s)`
- An alternancia type `alt(s) ∈ {TypeA, TypeB, NonAlternating}`

### Decision Variables

For each session `s ∈ S`:
- `t(s) ∈ T` — assigned time slot
- `r(s) ∈ R ∪ {null}` — assigned space (null for virtual sessions)

### Feasibility Constraints (Hard)

1. **Instructor conflict**: `∀ s₁, s₂ ∈ S, s₁ ≠ s₂: inst(s₁) = inst(s₂) → t(s₁) ≠ t(s₂)`
2. **Cohort conflict**: `∀ s₁, s₂ ∈ S, s₁ ≠ s₂: coh(s₁) = coh(s₂) → t(s₁) ≠ t(s₂)`
3. **Space conflict (alternancia aware)**: `∀ s₁, s₂ ∈ S: r(s₁) = r(s₂) ∧ t(s₁) = t(s₂) → alt(s₁) = alt(s₂) ∨ alt(s₁) = NonAlternating ∨ alt(s₂) = NonAlternating`
4. **Capacity**: `∀ s ∈ S: r(s) ≠ null → enrolled(coh(s)) ≤ capacity(r(s))`
5. **Availability**: `∀ s ∈ S: t(s) ∈ available(inst(s))`
6. **Space type**: `∀ s ∈ S: type(s) ≠ null → spaceType(r(s)) = type(s)`
7. **Time bounds**: `∀ s ∈ S: startTime(t(s)) ≥ 07:00 ∧ endTime(t(s)) ≤ 21:30`

See `docs/business-rules/hard-constraints.md` for the complete list.

### Objective Function (Soft Optimization)

Minimize:
```
F(assignment) = Σᵢ wᵢ × violationCount(SCᵢ)
```

Where `SCᵢ` are the soft constraints and `wᵢ` are their weights.
See `docs/business-rules/soft-constraints.md` for the full list.

---

## Why Three Phases?

The UCTP is NP-hard. Solving it directly with a full CP model for large instances is too slow.
The three-phase decomposition reduces complexity:

1. **Phase 1 — Graph Coloring**: Fast heuristic that produces a plausible (but possibly infeasible) initial assignment by modeling conflicts as a graph coloring problem. Reduces the search space for Phase 2.

2. **Phase 2 — CP-SAT**: Exact feasibility solver that enforces all hard constraints. Takes the Phase 1 assignment as a warm start and finds the nearest feasible solution.

3. **Phase 3 — Genetic Algorithm**: Metaheuristic that optimizes soft constraints starting from the Phase 2 feasible solution.

---

## References

- Schaerf, A. (1999). "A Survey of Automated Timetabling." *Artificial Intelligence Review*, 13(2), 87–127.
- Tan, J. et al. (2021). "A Survey of the State-of-the-Art of Optimisation Methodologies in School Timetabling Problems." *Expert Systems with Applications*, 165.

---

## Open Questions

- What is the expected number of sessions per semester for the full institution (not just pilot)?
- Are there any sessions that must be assigned to specific fixed time slots (pinned sessions)?
