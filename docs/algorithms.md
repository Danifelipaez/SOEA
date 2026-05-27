# Algoritmos SOEA

## Definición del problema (UCTP)

SOEA resuelve una variante del University Course Timetabling Problem (UCTP): asignar a cada sesión `s` una franja horaria `t(s)` y un espacio `r(s)` de forma que se satisfagan todas las restricciones duras y se minimice la suma ponderada de violaciones de restricciones blandas. Las variables de decisión son el par `(t, r)` por sesión. El espacio de búsqueda es combinatorio y NP-completo; se usa un pipeline de 3 fases para hacerlo tratable en la práctica piloto (≤ 200 cohortes).

---

## Fase 1 — Coloración de grafos (Welsh-Powell)

**Implementación:** `SOEA.Engine.GraphColoring` · `AgendadorColoracionGrafo`, `ConstructorGrafoConflictos`

**Input:** `List<Sesion>`, `List<BloqueTiempo>`
**Output:** `PreHorario` — asignación parcial de bloques (sin espacio asignado aún)

```pseudocode
función GraphColoringPhase(sesiones, bloques):
    G = ConstruirGrafoConflictos(sesiones)
    // ordenar por grado descendente (Welsh-Powell)
    sesionesOrdenadas = OrdenarPorGrado(G, descendente)
    asignacion = {}
    para cada sesión s en sesionesOrdenadas:
        coloresUsados = { asignacion[v] para v en G.vecinos(s) }
        asignacion[s] = PrimerBloqueDisponible(bloques, coloresUsados)
    retornar PreHorario(asignacion)

función ConstruirGrafoConflictos(sesiones):
    G = grafo vacío
    para cada par (s1, s2) con s1 ≠ s2:
        si MismoDocente(s1, s2)
           O MismaCohorte(s1, s2)
           O ConflictoAlternancia(s1, s2):  // mismo tipo O alguna es SinAlternancia
            G.agregarArista(s1, s2)
    retornar G
```

**Hard constraints procesadas en Fase 1:** HC-I01 (docente), HC-C01 (cohorte), ALT-02/03 (alternancia espacial)

---

## Fase 2 — Programación por restricciones (CP-SAT)

**Implementación:** `SOEA.Engine.ConstraintProg` · `MotorConstraintProgramming`
**Dependencia externa:** Google OR-Tools CP-SAT (timeout 120 s)

**Input:** `PreHorario` de Fase 1 + dominio completo de sesiones/espacios
**Output:** `Horario` factible completo (con espacio asignado) OR `InfeasibleResult` → HTTP 422

```pseudocode
función ConstraintProgrammingPhase(preHorario, sesiones, bloques, espacios):
    modelo = nuevo CpModel()
    // variables de decisión
    para cada sesión s:
        bloqueVar[s]  = modelo.NewIntVar(0, |bloques|-1, "bloque_" + s.Id)
        espacioVar[s] = modelo.NewIntVar(0, |espacios|,  "espacio_" + s.Id)  // último índice = virtual
    // codificar todas las restricciones duras
    para cada HC en [HC-S01, HC-S02, HC-S03, HC-S04,
                     HC-I01, HC-I02, HC-I03,
                     HC-T01..HC-T05,
                     HC-C01, HC-C02,
                     HC-SU01, HC-SU02]:
        CodificarRestriccion(modelo, HC, sesiones, bloqueVar, espacioVar)
    // warm-start con resultado de Fase 1
    para cada sesión s:
        modelo.AddHint(bloqueVar[s], preHorario.IndiceBloque(s))
    // resolver
    solver = nuevo CpSolver()
    solver.TimeLimit = 120
    estado = solver.Solve(modelo)
    si estado es FEASIBLE o OPTIMAL:
        retornar ExtraerHorario(solver, sesiones, bloqueVar, espacioVar)
    sino:
        retornar InfeasibleResult
```

**Hard constraints procesadas en Fase 2:** todas (HC-S01–S04, HC-I01–I03, HC-T01–T05, HC-C01–C02, HC-SU01–SU02)

---

## Fase 3 — Algoritmo genético

**Implementación:** `SOEA.Engine.Genetic` · `MotorGenetico`, `CromosomaHorario`, `EvaluadorFitness`, `OperadoresGeneticos`
**Hiperparámetros:** población 50 · generaciones máx 200 · convergencia 30 generaciones sin mejora

**Input:** `Horario` factible de Fase 2
**Output:** `Horario` optimizado (fitness minimizado)

```pseudocode
función GeneticAlgorithmPhase(horarioFactible):
    poblacion = InicializarPoblacion(horarioFactible, N=50)
    sinMejora = 0
    para generacion = 1 hasta 200:
        padre1, padre2 = SeleccionTorneo(poblacion, k=3)
        hijo = Crossover(padre1, padre2)
        hijo = Mutar(hijo, tasaMutacion)
        RepararRestriccionesDuras(hijo)   // codiciosa; mantiene feasibilidad
        si Fitness(hijo) < Fitness(PeorDe(poblacion)):
            ReemplazarPeor(poblacion, hijo)
            sinMejora = 0
        sino:
            sinMejora++
        si sinMejora >= 30:
            break
    retornar MejorDe(poblacion)

función Fitness(cromosoma):
    score = 0
    para cada SC con peso w en [SC-01..SC-09]:
        score += w × ContarViolaciones(cromosoma, SC)
    retornar score   // menor = mejor; 0 = óptimo
```

**Soft constraints procesadas en Fase 3:** SC-01 a SC-09 (ver `docs/domain.md` para pesos)

---

## Distribución de responsabilidades entre fases

| Restricción | Fase 1 | Fase 2 | Fase 3 |
|---|---|---|---|
| Sin solapamiento de docente (HC-I01) | Grafo ✓ | CP-SAT ✓ | reparación |
| Disponibilidad docente (HC-I02) | — | CP-SAT ✓ | reparación |
| Máx horas docente (HC-I03) | — | CP-SAT ✓ | reparación |
| Sin solapamiento de espacio (HC-S01) | — | CP-SAT ✓ | reparación |
| Capacidad espacio (HC-S02) | — | CP-SAT ✓ | reparación |
| Lab → espacio lab (HC-S03) | — | CP-SAT ✓ | reparación |
| Virtual sin espacio (HC-S04) | — | CP-SAT ✓ | reparación |
| Horario institucional (HC-T01) | — | CP-SAT ✓ | reparación |
| Consecutividad 3h (HC-T03) | — | CP-SAT ✓ | reparación |
| Sin solapamiento cohorte (HC-C01) | Grafo ✓ | CP-SAT ✓ | reparación |
| Conflicto alternancia (ALT-02/03) | Grafo ✓ | CP-SAT ✓ | reparación |
| Compacidad docente (SC-01) | — | — | fitness ✓ |
| Compacidad cohorte (SC-02) | — | — | fitness ✓ |
| Uniformidad carga docente (SC-06) | — | — | fitness ✓ |
| Estabilidad aula (SC-05) | — | — | fitness ✓ |
