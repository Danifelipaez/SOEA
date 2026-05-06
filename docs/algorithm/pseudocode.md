# Resumen de pseudocódigo

## Propósito
Proporcionar pseudocódigo de alto nivel para cada fase del pipeline de optimización de SOEA.
Esto sirve como referencia rápida para Copilot al generar implementaciones de algoritmos
y para los colaboradores que están aprendiendo el sistema.

## Alcance
Las tres fases y el pipeline de orquestación.

---

## Orquestador del pipeline (SOEA.Application)

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

## Fase 1 — Graph Coloring

```pseudocode
function GraphColoringPhase(sessions):
    G = BuildConflictGraph(sessions)
    // ordenar nodos por grado descendente (Welsh-Powell)
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

## Fase 2 — Programación por restricciones (CP-SAT)

```pseudocode
function ConstraintProgrammingPhase(partialSchedule, sessions):
    model = new CpModel()
    // definir variables
    for each session s in sessions:
        timeVar[s]  = model.NewIntVar(0, |timeSlots|-1, "time_" + s.Id)
        spaceVar[s] = model.NewIntVar(0, |spaces|,      "space_" + s.Id)  // last index = virtual
    // agregar restricciones duras
    for each hard constraint HC in HardConstraints:
        EncodeConstraint(model, HC, sessions, timeVar, spaceVar)
    // agregar indicios de warm start de la Fase 1
    for each session s:
        model.AddHint(timeVar[s], partialSchedule.TimeSlotIndex(s))
    // resolver
    solver = new CpSolver()
    solver.TimeLimit = config.TimeoutSeconds
    status = solver.Solve(model)
    if status is FEASIBLE or OPTIMAL:
        return ExtractSchedule(solver, sessions, timeVar, spaceVar)
    else:
        return InfeasibleResult
```

---

## Fase 3 — Algoritmo genético

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

## Preguntas abiertas

- ¿El paso de reparación en la Fase 3 debería usar CP-SAT (costoso pero correcto) o una reasignación codiciosa simple?
- ¿El orquestador debería reintentar la Fase 2 con restricciones relajadas si la primera ejecución es infactible?
