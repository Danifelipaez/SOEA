# Algoritmos SOEA

## Definición del problema (UCTP)

SOEA resuelve una variante bi-semanal del University Course Timetabling Problem (UCTP): asignar a cada sesión lógica `s` **dos asignaciones** `(t(s,A), r(s,A))` y `(t(s,B), r(s,B))` — una por semana del ciclo de alternancia — de forma que se satisfagan todas las restricciones duras en ambas semanas y se minimice la suma ponderada de violaciones de restricciones blandas. La modalidad por semana (presencial/virtual) es un dato derivado fijo de `TipoAlternancia`, no una variable de decisión. El espacio de búsqueda es combinatorio y NP-completo; se usa un pipeline de 3 fases para hacerlo tratable en la práctica piloto (≤ 200 cohortes).

> **Migración a Presencial-First (en curso).** El modelo está virando hacia un eje **asignatura + grupo + espacio** (ya no disponibilidad docente), con lógica presencial-first. La **Etapa 1** ya añadió el *soporte de datos*: `Sesion.TipoFlujo` (dos flujos schedulables), `Sesion.PatronAlternanciaId?`/`Bloqueada` (alternancia opcional por sesión) y `Asignatura.Categoria`/ventana horaria. La **Etapa 2** sacó al docente del núcleo de generación: `Sesion.DocenteId` es nullable (CR-02) y la disponibilidad docente quedó **degradada** (HC-I02). La **Etapa 3** cerró CR-08: el **grupo/cohorte** es ahora el eje de conflicto (HC-C01: grafo por grupo en Fase 1 + NoOverlap por `(grupo, semana)` en Fase 2) y de optimización (ergonomía por cohorte en Fase 3); el docente quedó **fuera del pipeline** (se asigna después de generar). Un run = una sola cohorte implícita ⇒ todas las sesiones se serializan. La **lógica restante** — HC-CAP (aforo), HC-VH (ventana), SC-PRES (prioridad por categoría), flujos/patrón por sesión, y HU-04 (editar sesión) — es de etapas posteriores. Estado y secuencia en `docs/PLAN_MAESTRO_PresencialFirst.md`.

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
        si MismaCohorte(s1, s2)               // CR-08: eje primario (el docente sale del pipeline)
           O MismoEspacioFijo(s1, s2)
           O ConflictoAlternancia(s1, s2):  // mismo tipo O alguna es SinAlternancia
            G.agregarArista(s1, s2)
    retornar G
```

**Hard constraints procesadas en Fase 1:** HC-C01 (cohorte — eje primario, CR-08), ALT-02/03 (alternancia espacial). El docente quedó fuera del pipeline (CR-08).

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

    // 4. No-cruzar-día: dominio de start = StartsValidos(estructural, sin disponibilidad docente)
    //    HC-I02 degradada (Etapa 2 / CR-08): la disponibilidad docente ya NO restringe el dominio.
    para cada sesión s, semana w:
        modelo.AddLinearExpressionInDomain(start[(s,w)], StartsValidos(s, bloques))

    // 5. HC-C01: NoOverlap por (grupo, semana) — presenciales + virtuales (CR-08).
    //    El grupo no puede estar en dos sesiones a la vez. Cohorte única ⇒ serialización global.
    //    El docente sale del pipeline: ya no hay HC-I01 (docente) ni HC-I03 (máx. horas) aquí.
    para cada grupo g, semana w:
        modelo.AddNoOverlap([interval[(s,w)] para s de g])

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

**Hard constraints procesadas en Fase 2:** HC-C01 (cohorte — eje primario, CR-08), HC-S01, HC-S03, HC-S04 — evaluadas por semana. **Docente fuera de generación (CR-08):** HC-I01 (lo subsume HC-C01) y HC-I03 salieron; HC-I02 degradada (Etapa 2). El docente se asigna después de generar el horario.

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
| Sin solapamiento de docente (HC-I01) | — | fuera de generación (CR-08): lo subsume HC-C01 | — |
| Disponibilidad docente (HC-I02) | — | ~~CP-SAT~~ degradada (CR-08): solo blanda vía SC-06 | — |
| Máx horas docente (HC-I03) | — | fuera de generación (CR-08): docente post-generación | — |
| Sin solapamiento de espacio (HC-S01) | — | CP-SAT ✓ por `(espacio, semana)` | reparación |
| Capacidad espacio (HC-S02) | — | CP-SAT ✓ | reparación |
| Lab → espacio lab (HC-S03) | — | CP-SAT ✓ | reparación |
| Virtual sin espacio (HC-S04) | — | invariante entidad ✓ | — |
| Regla 9 — misma franja A/B (ALT-05) | — | CP-SAT ✓ `start[A]==start[B]` | se restaura tras cruce |
| Sin solapamiento cohorte (HC-C01) | Grafo ✓ | CP-SAT ✓ por semana | reparación |
| Conflicto alternancia (ALT-02/03) | Grafo ✓ | CP-SAT ✓ por semana | reparación |
| Compacidad cohorte (SC-01) | — | — | fitness ✓ (por grupo, CR-08) |
| Compacidad cohorte (SC-02) | — | — | fitness ✓ |
| Uniformidad carga cohorte (SC-06) | — | — | fitness ✓ (por grupo, CR-08) |
| Estabilidad aula (SC-05) | — | — | fitness ✓ |
| Balance carga entre semanas (SC-BAL) | — | — | fitness ✓ (Inc.2) |
