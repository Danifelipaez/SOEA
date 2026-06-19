# Algoritmos SOEA

## Definición del problema (UCTP)

SOEA resuelve una variante bi-semanal del University Course Timetabling Problem (UCTP): asignar a cada sesión lógica `s` **dos asignaciones** `(t(s,A), r(s,A))` y `(t(s,B), r(s,B))` — una por semana del ciclo de alternancia — de forma que se satisfagan todas las restricciones duras en ambas semanas y se minimice la suma ponderada de violaciones de restricciones blandas. La modalidad por semana (presencial/virtual) es un dato derivado fijo de `TipoAlternancia`, no una variable de decisión. El espacio de búsqueda es combinatorio y NP-completo; se usa un pipeline de 3 fases para hacerlo tratable en la práctica piloto (≤ 200 cohortes).

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

**Input:** sesiones lógicas de Fase 1 + dominio completo de bloques/espacios/docentes
**Output:** `IReadOnlyList<AsignacionSemanal>` (2 por sesión, semanas A y B) OR `InfeasibleResult` → HTTP 422

El modelo CP-SAT indexa **todas las variables por `(sesionId, semana)`** en lugar de solo `sesionId`. La modalidad de cada par es derivada antes de construir el modelo (no es variable): `ModalidadDe(sesion, semana)`.

```pseudocode
función ConstraintProgrammingPhase(sesiones, bloques, espacios, docentes):
    modelo = nuevo CpModel()

    // 1. Variables: intervalo de longitud fija DuracionHoras por (sesión, semana)
    para cada sesión s, semana w ∈ {A, B}:
        start[(s,w)]    = modelo.NewIntVar(0, |bloques| - dur(s))
        end[(s,w)]      = modelo.NewIntVar(dur(s), |bloques|)
        interval[(s,w)] = modelo.NewIntervalVar(start, dur(s), end)
        si ModalidadDe(s,w) == Presencial:
            space[(s,w)] = modelo.NewIntVar(0, |espacios|-1)

    // 2. Enlace regla 9 (ALT-05): misma franja en ambas semanas para TipoA/TipoB
    para cada sesión s con s.Alternancia ∈ {TipoA, TipoB}:
        modelo.Add(start[(s,A)] == start[(s,B)])

    // 3. Warm-start desde Fase 1
    para cada sesión s con bloque preasignado:
        modelo.AddHint(start[(s,A)], indice(bloque)); modelo.AddHint(start[(s,B)], indice(bloque))

    // 4. HC-I02 + no-cruzar-día: dominio de start = StartsValidos(disponibilidad(docente))
    para cada sesión s, semana w:
        modelo.AddLinearExpressionInDomain(start[(s,w)], StartsValidos(s, bloques))

    // 5. HC-I03 pre-solve: totalHoras(docente) ≤ MaximoHorasSemanales (por semana)
    para cada docente d:
        si Σ DuracionHoras(sesiones de d) > d.MaximoHorasSemanales → InfeasibleResult

    // 6. HC-I01: NoOverlap por (docente, semana) — presenciales + virtuales
    para cada docente d, semana w:
        modelo.AddNoOverlap([interval[(s,w)] para s de d])

    // 7. HC-S01 + HC-S03 + HC-S04: intervalos opcionales por (espacio, semana), solo presenciales
    para cada sesión s, semana w con Modalidad=Presencial:
        candidatos = EspaciosCandidatos(s)  // HC-S03: lab si la sesión requiere lab
        literales = [modelo.NewBoolVar() para e en candidatos]
        modelo.AddExactlyOne(literales)     // exactamente un espacio activo
        para cada (lit, e):
            modelo.Add(space[(s,w)] == e).OnlyEnforceIf(lit)
            optInterval[(s,w,e)] = modelo.NewOptionalIntervalVar(..., lit)
    para cada espacio e, semana w:
        modelo.AddNoOverlap([optInterval[(s,w,e)] para s presencial en e y w])

    // 8. Resolver
    solver.TimeLimit = 120
    estado = solver.Solve(modelo)
    si estado es FEASIBLE o OPTIMAL:
        retornar [AsignacionSemanal(s, w, bloques[start(s,w)], espacio(s,w), ModalidadDe(s,w))
                  para cada s, w]
    sino:
        retornar InfeasibleResult
```

**Hard constraints procesadas en Fase 2:** HC-I01, HC-I02, HC-I03, HC-S01, HC-S03, HC-S04 — evaluadas por semana.

---

## Fase 3 — Algoritmo genético

> **Estado actual (Incremento 2):** la Fase 3 está **activa** en `GenerarHorarioService` y optimiza los objetivos blandos del docente (huecos, &gt; 6 horas seguidas, balance entre días disponibles, balance entre semanas) sobre dos genes de inicio por sesión: `CromosomaHorario.Start` (Semana A) y `StartB` (Semana B). Para TipoA/TipoB, `StartB` se mantiene igual a `Start` por construcción (ALT-05: misma franja en ambas semanas). Para `SinAlternancia`, `StartB` puede diferir de `Start` (ALT-06); la soft constraint SC-BAL penaliza el desbalance de carga horaria entre semanas que esa libertad puede introducir.

**Implementación:** `SOEA.Engine.Genetic` · `MotorGenetico`, `CromosomaHorario`, `EvaluadorFitness`, `OperadoresGeneticos`
**Hiperparámetros:** población 50 · generaciones máx 200 · convergencia 30 generaciones sin mejora

**Input:** lista de `AsignacionSemanal` de Fase 2 (pares A/B por sesión; ambas semanas siembran `Start`/`StartB`)
**Output:** lista optimizada de `AsignacionSemanal` (fitness minimizado)

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

**Soft constraints procesadas en Fase 3:** SC-01 a SC-09 y SC-BAL — Incremento 2 (ver `docs/domain.md` para pesos)

---

## Distribución de responsabilidades entre fases

| Restricción | Fase 1 | Fase 2 | Fase 3 (Inc.2) |
|---|---|---|---|
| Sin solapamiento de docente (HC-I01) | Grafo ✓ | CP-SAT ✓ por semana | reparación |
| Disponibilidad docente (HC-I02) | — | CP-SAT ✓ por semana | reparación |
| Máx horas docente (HC-I03) | — | CP-SAT ✓ por semana | reparación |
| Sin solapamiento de espacio (HC-S01) | — | CP-SAT ✓ por `(espacio, semana)` | reparación |
| Capacidad espacio (HC-S02) | — | CP-SAT ✓ | reparación |
| Lab → espacio lab (HC-S03) | — | CP-SAT ✓ | reparación |
| Virtual sin espacio (HC-S04) | — | invariante entidad ✓ | — |
| Regla 9 — misma franja A/B (ALT-05) | — | CP-SAT ✓ `start[A]==start[B]` | se restaura tras cruce |
| Sin solapamiento cohorte (HC-C01) | Grafo ✓ | CP-SAT ✓ por semana | reparación |
| Conflicto alternancia (ALT-02/03) | Grafo ✓ | CP-SAT ✓ por semana | reparación |
| Compacidad docente (SC-01) | — | — | fitness ✓ |
| Compacidad cohorte (SC-02) | — | — | fitness ✓ |
| Uniformidad carga docente (SC-06) | — | — | fitness ✓ |
| Estabilidad aula (SC-05) | — | — | fitness ✓ |
| Balance carga entre semanas (SC-BAL) | — | — | fitness ✓ (Inc.2) |
