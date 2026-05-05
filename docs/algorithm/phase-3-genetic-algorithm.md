# Phase 3 — Genetic Algorithm

## Purpose
Describe the Genetic Algorithm used in Phase 3 to optimize soft constraints in the schedule
produced by Phase 2. Copilot uses this when implementing `SOEA.Engine.Genetic`.

## Scope
Phase 3 only: soft-constraint optimization via Genetic Algorithm.
Phase 1 (Graph Coloring) and Phase 2 (CP-SAT) are described in their own docs.

---

## Goal of Phase 3

Take the Phase 2 **feasible** schedule and improve it by minimizing the weighted soft-constraint
violation score (fitness). Hard constraints must remain satisfied throughout.

---

## Chromosome Representation

A **chromosome** encodes one complete schedule assignment:

```
chromosome = [ (sessionId₁, timeSlotIndex₁, spaceIndex₁),
               (sessionId₂, timeSlotIndex₂, spaceIndex₂),
               ...
               (sessionIdₙ, timeSlotIndexₙ, spaceIndexₙ) ]
```

Each gene is a 3-tuple: session → assigned time slot → assigned space.

The Phase 2 feasible schedule is the **initial chromosome** (seed of the first generation).

---

## Fitness Function

```
fitness(chromosome) = Σᵢ wᵢ × violationCount(SC_i, chromosome)
```

Where:
- `SC_i` = soft constraint i (from `docs/business-rules/soft-constraints.md`)
- `wᵢ` = weight of constraint i
- `violationCount` = number of individual violations of that constraint in the schedule

**Lower fitness = better schedule.**

A chromosome with fitness = 0 perfectly satisfies all soft constraints.

---

## Genetic Operations

### Selection
- **Tournament selection**: randomly select k chromosomes, keep the best
- Tournament size k = 5 (configurable)

### Crossover
- **Single-point crossover** on the session list
- Both offspring inherit hard-constraint-preserving gene segments
- After crossover, verify that no hard constraints are violated; if violated, repair or discard

### Mutation
- **Random gene mutation**: for a randomly selected session, assign a different valid time slot
  or space (drawn from the set of hard-constraint-safe alternatives)
- Mutation probability: 0.05 per gene (configurable)
- Mutation always checks hard constraint compliance before accepting the change

### Repair Operator
If a crossover or mutation produces a hard constraint violation:
1. Try to reassign the conflicting session to a valid alternative slot/space
2. If no valid alternative exists, revert to the parent chromosome for that gene

---

## Algorithm Flow

```
1. Initialize population with N copies of the Phase 2 feasible schedule
   (optionally add perturbed variants for diversity)
2. Evaluate fitness for all chromosomes
3. Repeat for G generations (or until convergence):
   a. Select parents via tournament selection
   b. Apply crossover to produce offspring
   c. Apply mutation to offspring
   d. Evaluate fitness of offspring
   e. Replace worst chromosomes in population with offspring (if improvement)
4. Return the chromosome with the lowest fitness score
```

### Hyperparameters (defaults — all configurable)

| Parameter | Default |
|---|---|
| Population size N | 50 |
| Max generations G | 200 |
| Tournament size k | 5 |
| Crossover probability | 0.8 |
| Mutation probability per gene | 0.05 |
| Convergence threshold (no improvement for X generations) | 30 |

---

## Inputs

- `FeasibleSchedule` from Phase 2 (seed chromosome)
- Soft constraint definitions and weights from configuration

## Outputs

- `OptimizedSchedule`: the chromosome with the lowest fitness score after G generations
- Fitness score included in the output for reporting

---

## Performance Target

Phase 3 should complete within 5 minutes for pilot data volumes (≤ 200 sessions).

---

## Open Questions

- Should the initial population include diversity beyond copies of the Phase 2 seed?
  (e.g., perturb by randomly swapping compatible sessions)
- Should soft constraint weights be configurable per-run through the Admin UI?
- Is Simulated Annealing an acceptable alternative to Genetic Algorithm if GA convergence is slow?
