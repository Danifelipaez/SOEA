# Pseudocode Summary

## Purpose
Provide high-level pseudocode for each phase of the SOEA optimization pipeline.
This serves as a quick reference for Copilot when generating algorithm implementations
and for contributors learning the system.

## Scope
All three phases plus the orchestration pipeline.

---

## Pipeline Orchestrator (SOEA.Application)

```pseudocode
function GenerateSchedule(inputData):
    sessions    = ParseAndValidate(inputData)
    partialPlan = GraphColoringPhase(sessions)
    feasiblePlan = ConstraintProgrammingPhase(partialPlan, sessions)
    if feasiblePlan is Infeasible:
        return Error("No feasible schedule found — check constraint conflicts")
    optimizedPlan = GeneticAlgorithmPhase(feasiblePlan)
    ValidateHardConstraints(optimizedPlan)   // final safety check
    return Serialize(optimizedPlan)
```

---

## Phase 1 — Graph Coloring

```pseudocode
function GraphColoringPhase(sessions):
    G = BuildConflictGraph(sessions)
    // sort nodes by degree descending (Welsh-Powell)
    sortedSessions = SortByDegree(G, descending)
    assignment = {}
    for each session s in sortedSessions:
        usedColors = {assignment[neighbor] for neighbor in G.neighbors(s)}
        assignment[s] = FirstAvailableTimeSlot(allTimeSlots, usedColors)
    return PartialSchedule(assignment)

function BuildConflictGraph(sessions):
    G = empty graph
    for each pair (s1, s2) in sessions × sessions where s1 ≠ s2:
        if SameInstructor(s1, s2) OR
           SameCohort(s1, s2) OR
           ConflictOnAlternanciaType(s1, s2):
            G.addEdge(s1, s2)
    return G
```

---

## Phase 2 — Constraint Programming (CP-SAT)

```pseudocode
function ConstraintProgrammingPhase(partialSchedule, sessions):
    model = new CpModel()
    // define variables
    for each session s in sessions:
        timeVar[s]  = model.NewIntVar(0, |timeSlots|-1, "time_" + s.Id)
        spaceVar[s] = model.NewIntVar(0, |spaces|,      "space_" + s.Id)  // last index = virtual
    // add hard constraints
    for each hard constraint HC in HardConstraints:
        EncodeConstraint(model, HC, sessions, timeVar, spaceVar)
    // add warm-start hints from Phase 1
    for each session s:
        model.AddHint(timeVar[s], partialSchedule.TimeSlotIndex(s))
    // solve
    solver = new CpSolver()
    solver.TimeLimit = config.TimeoutSeconds
    status = solver.Solve(model)
    if status is FEASIBLE or OPTIMAL:
        return ExtractSchedule(solver, sessions, timeVar, spaceVar)
    else:
        return InfeasibleResult
```

---

## Phase 3 — Genetic Algorithm

```pseudocode
function GeneticAlgorithmPhase(feasibleSchedule):
    population = InitializePopulation(feasibleSchedule, N)
    for generation = 1 to G:
        parent1, parent2 = TournamentSelect(population, k)
        offspring        = Crossover(parent1, parent2)
        offspring        = Mutate(offspring, mutationRate)
        RepairHardConstraints(offspring)
        if Fitness(offspring) < Fitness(WorstIn(population)):
            ReplaceWorst(population, offspring)
        if NoImprovementFor(convergenceThreshold) generations:
            break
    return BestIn(population)

function Fitness(chromosome):
    score = 0
    for each soft constraint SC with weight w:
        score += w * CountViolations(chromosome, SC)
    return score
```

---

## Open Questions

- Should the Repair step in Phase 3 use CP-SAT (expensive but correct) or a simple greedy reassignment?
- Should the orchestrator retry Phase 2 with relaxed constraints if the first run is infeasible?
