# Algoritmos SOEA

## Definición del problema (UCTP)

SOEA resuelve una variante bi-semanal del University Course Timetabling Problem (UCTP): asignar a cada sesión lógica `s` **dos asignaciones** `(t(s,A), r(s,A))` y `(t(s,B), r(s,B))` — una por semana del ciclo de alternancia — de forma que se satisfagan todas las restricciones duras en ambas semanas y se minimice la suma ponderada de violaciones de restricciones blandas. La modalidad por semana (presencial/virtual) es un dato derivado fijo de `TipoAlternancia`, no una variable de decisión. El espacio de búsqueda es combinatorio y NP-completo; se usa un pipeline de 3 fases para hacerlo tratable en la práctica (≤ 200 cohortes).

> **Presencial-First (implementado).** El eje del modelo es **asignatura + grupo + espacio** (no disponibilidad docente). `Sesion.TipoFlujo` distingue laboratorio de teoría (presencial/virtual, 3 tracks); `Sesion.PatronAlternanciaId?`/`Bloqueada` permiten alternancia opcional por sesión; `Asignatura.Categoria` + `HoraInicioMin/HoraFinMax` alimentan SC-PRES y HC-VH. `Sesion.DocenteId` es nullable (CR-02): el docente sale del núcleo de generación y se asigna *después* vía `PATCH /api/sesiones/{id}/docente`. El **grupo/cohorte** es el eje de conflicto (HC-C01: grafo por grupo en Fase 1 + NoOverlap por `(grupo, semana)` en Fase 2) y de optimización (ergonomía por cohorte en Fase 3). Un run = una sola cohorte implícita ⇒ todas las sesiones se serializan. **Implementado:** HC-CAP (aforo), HC-VH (ventana), HC-G01 (franja del grupo), HC-S05 (espacio fijo) — impuestas en CP-SAT (Fase 2), respetadas por el GA (Fase 3, auditoría A1) y re-verificadas en el validador post-generación junto con HC-C01/HC-S01. **HC-SU01 ("8+8" como hard constraint) es obsoleta**: TipoA/TipoB son únicamente tipos de alternancia semanal (presencial una semana, virtual la otra), no un patrón de bloques de 8h — ver `docs/domain.md`. HU-04 (editar sesión) es la única pieza pendiente de este eje. Histórico de etapas en `docs/PLAN_MAESTRO_PresencialFirst.md`.

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

**Auditoría A1/B3:** el dominio de inicios candidatos (`PrimerBloqueDisponible`) ya no es solo "cabe-en-día": usa `CalculadorDominioSesion` (Domain), la misma fuente que CP-SAT y el GA, así que respeta HC-G01 (franja del grupo) y HC-VH (ventana de la asignatura) — el warm-start nunca cae fuera del dominio que Fase 2 va a exigir. Los candidatos se recorren en orden **round-robin por día** (no ascendente puro) para no amontonar todas las sesiones el lunes por la mañana cuando el grafo es completo (cohorte única).

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

**Hard constraints procesadas en Fase 2:** HC-C01 (cohorte — eje primario, CR-08), HC-S01, HC-S03, HC-S04, HC-S05 (espacio fijo), HC-CAP (aforo), HC-G01 (franja del grupo), HC-VH (ventana de la asignatura) — evaluadas por semana. **Docente fuera de generación (CR-08):** HC-I01 (lo subsume HC-C01) y HC-I03 salieron; HC-I02 degradada (Etapa 2). El docente se asigna después de generar el horario.

---

## Fase 3 — Algoritmo genético

> **Estado actual:** la Fase 3 está **activa** en `GenerarHorarioService` y optimiza los objetivos blandos de la cohorte (huecos, &gt; N horas seguidas, balance entre días, balance entre semanas) sobre dos genes de inicio por sesión: `CromosomaHorario.Start` (Semana A) y `StartB` (Semana B). Para TipoA/TipoB, `StartB` se mantiene igual a `Start` por construcción (ALT-05: misma franja en ambas semanas). Para `SinAlternancia`, `StartB` puede diferir de `Start` (ALT-06); la soft constraint SC-BAL penaliza el desbalance de carga horaria entre semanas que esa libertad puede introducir. **SC-PRES es informativo (auditoría B2):** es constante para el conjunto de sesiones del run (el GA nunca mueve la alternancia), así que se reporta aparte (`PenalizacionPresencial`) y ya no suma al fitness — antes inflaba el número sin cambiar ningún ranking.

**Implementación:** `SOEA.Engine.Genetic` · `MotorGenetico`, `CromosomaHorario`, `EvaluadorFitness`, `OperadoresGeneticos`
**Hiperparámetros:** población 50 · generaciones máx 200 · convergencia 30 generaciones sin mejora · selección por torneo (k=5)

**Input:** lista de `AsignacionSemanal` de Fase 2 (pares A/B por sesión; ambas semanas siembran `Start`/`StartB`)
**Output:** lista optimizada de `AsignacionSemanal` (fitness minimizado)

**Auditoría B1 — (μ+λ) con elitismo, no steady-state.** El motor original reemplazaba solo un hijo por generación (≤200 evaluaciones útiles); ahora cada generación produce `TamañoPoblación` hijos, se unen a los padres y sobreviven los mejores `TamañoPoblación` — con la config por defecto (50×200) son ~10.000 evaluaciones, y el elitismo garantiza que el mejor fitness es monótono no-creciente generación a generación:

```pseudocode
función GeneticAlgorithmPhase(horarioFactible):
    poblacion = InicializarPoblacion(horarioFactible, N=50)
    sinMejora = 0
    para generacion = 1 hasta 200:
        hijos = []
        repetir N veces:
            padre1, padre2 = SeleccionTorneo(poblacion, k=5)
            hijo = Crossover(padre1, padre2)
            hijo = Mutar(hijo, tasaMutacion)
            RepararRestriccionesDuras(hijo)   // codiciosa; mantiene feasibilidad
            hijos.agregar(hijo)
        poblacion = MejoresN(poblacion + hijos, N)   // (μ+λ) con elitismo
        si Fitness(MejorDe(poblacion)) < mejorFitness:
            mejorFitness = Fitness(MejorDe(poblacion)); sinMejora = 0
        sino:
            sinMejora++
        si sinMejora >= 30:
            break
    retornar MejorDe(poblacion)

función Fitness(cromosoma):
    score = 0
    para cada SC con peso w en [SC-01, SC-06, SC-09, SC-BAL]:  // SC-PRES es informativo, no suma aquí (B2)
        score += w × ContarViolaciones(cromosoma, SC)
    retornar score   // menor = mejor; 0 = óptimo
```

**Soft constraints procesadas en Fase 3:** SC-01, SC-06, SC-09 y SC-BAL suman al fitness (ver `docs/domain.md` para pesos); SC-PRES se reporta aparte, informativo (B2).

---

## Distribución de responsabilidades entre fases

| Restricción | Fase 1 | Fase 2 | Fase 3 | Validador post-gen |
|---|---|---|---|---|
| Sin solapamiento de docente (HC-I01) | — | fuera de generación (CR-08): lo subsume HC-C01 | — | — |
| Disponibilidad docente (HC-I02) | — | ~~CP-SAT~~ degradada (CR-08): solo blanda vía SC-06 | — | — |
| Máx horas docente (HC-I03) | — | fuera de generación (CR-08): docente post-generación | — | — |
| Sin solapamiento de espacio (HC-S01) | — | CP-SAT ✓ por `(espacio, semana)` | `AsignadorEspacios` ✓ | ✓ |
| Capacidad espacio / aforo (HC-CAP, ex HC-S02) | — | CP-SAT ✓ | `AsignadorEspacios` ✓ (auditoría A1) | ✓ (auditoría A1) |
| Lab → espacio lab (HC-S03) | — | CP-SAT ✓ | `AsignadorEspacios` ✓ | ✓ (auditoría A1) |
| Virtual sin espacio (HC-S04) | — | invariante entidad ✓ | invariante ✓ | — |
| Espacio fijo de la asignatura (HC-S05) | — | CP-SAT ✓ | `AsignadorEspacios` ✓ (auditoría A1) | ✓ (auditoría A1) |
| Franja del grupo (HC-G01) | Dominio ✓ (auditoría A1/B3) | CP-SAT ✓ | dominio de operadores ✓ | ✓ (auditoría A1) |
| Ventana horaria de asignatura (HC-VH) | Dominio ✓ (auditoría A1/B3) | CP-SAT ✓ | dominio de operadores ✓ (auditoría A1) | ✓ (auditoría A1) |
| Sesión fija del horario base (regla 8 / HC-BASE) | — | CP-SAT ✓ (igualdad) | gen congelado ✓ (auditoría A1) | ✓ (auditoría A1) |
| Regla 9 — misma franja A/B (ALT-05) | — | CP-SAT ✓ `start[A]==start[B]` | se restaura tras cruce | — |
| Sin solapamiento cohorte (HC-C01) | Grafo ✓ | CP-SAT ✓ por semana | reparación | ✓ |
| Conflicto alternancia (ALT-02/03) | Grafo ✓ | CP-SAT ✓ por semana | reparación | — |
| Compacidad cohorte (SC-01) | — | — | fitness ✓ (por grupo, CR-08) | — |
| Uniformidad carga cohorte (SC-06) | — | — | fitness ✓ (por grupo, CR-08) | — |
| Balance carga entre semanas (SC-BAL) | — | — | fitness ✓ | — |
| Presencial-first (SC-PRES) | — | — | informativo, no fitness (auditoría B2) | — |

**Auditoría A1:** antes el validador post-generación solo cubría HC-C01/HC-S01, así que si el GA violaba HC-VH/HC-CAP/HC-S03/HC-S05/HC-G01 nada lo detectaba y se podía publicar. Ahora `ValidadorRestriccionesDuras` re-verifica las 7 reglas sobre la salida final (más HC-BASE); si alguna falla, el pipeline hace fallback a la solución de Fase 2 (que sí las cumple todas).
