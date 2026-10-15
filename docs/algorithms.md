# Algoritmos

> Verificado contra el código el 2026-10-14. Reglas HC/SC/ALT en [domain.md](domain.md).

## Problema

Variante del University Course Timetabling Problem con alternancia: dar a cada sesión **una** franja y **un** aula válidas para todo el semestre (ALT-05), cumpliendo todas las restricciones duras y minimizando las blandas. La modalidad por semana no es variable de decisión: se deriva de la alternancia. El horario se genera desde cero en cada corrida (las sesiones fijas entran como igualdades).

## Pipeline (`GenerarHorarioService.EjecutarAsync`)

```
POST /api/horario/generar  (catálogo enviado por el frontend + semestre + configuración GA)
 1. Mapear DTOs → entidades. Cada grupo aporta sus sesiones con su propio GrupoId (multi-grupo real).
    Las sesiones fijas reemplazan a una sesión del mismo grupo/asignatura/tipo.
 2. Fase 1  Coloración de grafos  → bloque inicial por sesión (warm-start)
 3. Fase 2  CP-SAT                → horario factible  (si falta aula: bucle de Semana B)
 4. Fase 3  Algoritmo genético    → mejora blandas; asignación exacta de aulas; reversión de cesiones
 5. Validador de 10 reglas duras  → si el GA violó algo, se usa la solución de Fase 2
 6. Persistir en UNA transacción: borra sesiones/asignaciones de corridas previas del mismo
    semestre, guarda Sesiones + Horario + AsignacionesSemanales
```

- Plazo total: 200 s (`PlazoTotalPorDefecto`). Si vence antes de Fase 2 → 422 `Timeout`; si vence en Fase 3 → se publica la solución de Fase 2.
- Una generación a la vez (429 si hay otra en curso). Parámetros del GA validados por rango (400).
- Infactible → 422 con `motivoInfactibilidad` ∈ {`Espacio`, `Capacidad`, `VentanaHoraria`, `FranjaGrupo`, `Datos`, `Timeout`, `Otro`} y un mensaje que nombra lo que no cupo.

## Fase 1 — Coloración (Welsh-Powell)

`SOEA.Engine.GraphColoring` (`ConstructorGrafoConflictos`, `AgendadorColoracionGrafo`).

- Arista si dos sesiones comparten **grupo**, o comparten aula fija y la ocupan alguna semana en común (`ModalidadSemanal.CompartenSemanaDeEspacio`; una TipoA y una TipoB no chocan, ALT-01).
- Orden por grado descendente; a cada sesión se le da el primer inicio de su dominio (`CalculadorDominioSesion`: HC-G01 + HC-VH) libre de vecinos, recorriendo los días en round-robin para no amontonar el lunes.

## Fase 2 — CP-SAT (OR-Tools)

`SOEA.Engine.ConstraintProg` (`MotorConstraintProgramming`). Timeout por solve: `CpSat:TimeoutSegundos` (120 s).

- **Variables:** un inicio por sesión (ALT-05 es estructural) con dominio = `CalculadorDominioSesion`; para cada (sesión presencial, semana en que ocupa aula) un literal por aula candidata (`CalculadorEspaciosSesion`: HC-S03, HC-S05, HC-CAP) con `AddExactlyOne`.
- **Restricciones:** NoOverlap por grupo (HC-C01, independiente de la semana); NoOverlap de intervalos opcionales por (aula, semana) (HC-S01); `AddElement` sobre el día para HC-SEP (una sesión fija cuenta frente a sus hermanas libres; dos fijas entre sí no se comparan); igualdad de bloque y aula en parejas (HC-ALT); igualdad en sesiones fijas (HC-BASE). Hints desde Fase 1.
- **Restricción redundante:** un `Cumulative` por (semana, clase de aula) — laboratorios vs. salones/auditorios — sobre las sesiones cuyas aulas candidatas son todas de esa clase, con capacidad = número de aulas de la clase. No cambia las soluciones; acelera la prueba de "no alcanzan las aulas", que es el INFEASIBLE que repite cada vuelta del bucle de Semana B.
- **Parámetros:** `max_time_in_seconds`, `random_seed` y `num_workers` (el antiguo `num_search_workers` está obsoleto en OR-Tools).
- **Pre-chequeos** antes de construir el modelo (demanda vs. capacidad por clase de aula, datos incoherentes) → infactibilidad explicada sin resolver.
- **Clasificación:** ante un INFEASIBLE sin explicación, reintenta sin restricciones de aula (`ClasificarInfactibilidadEspacio`); si así es factible, el motivo es `Espacio`. Opcionalmente (`SweepGrupos`) reintenta excluyendo cada grupo para nombrar al culpable. El diagnóstico por grupo (grupos contradictorios en solitario + barrido) solo corre en el primer intento (`diagnosticarPorGrupo`): en las vueltas del bucle de Semana B se descartaría y costaría hasta `SweepGruposMaximo` solves por vuelta. Si la generación falla dentro del bucle, la respuesta reutiliza los grupos del primer diagnóstico.

## Semana B reactiva

Solo se activa si Fase 2 devuelve `Espacio`:

1. `CederSiguientePareja` elige dos candidatas y las empareja (una TipoA, otra TipoB, mismo `ParejaAlternanciaId`, `CedidaPorSaturacion = true`).
2. **Candidata:** presencial, sin alternancia, no fija ni bloqueada, alcanzada por algún criterio activo de `CriterioCesionAlternancia` (Electiva → Optativa → Elegible, en el orden configurado; `MultiplesSesiones` solo desempata).
3. **Pareja válida** (`EvaluadorParejaAlternancia`): grupos y asignaturas distintos, misma duración, requisito de aula compatible y, sobre todo, **franja común y aula común** según las mismas fuentes que usa CP-SAT.
4. Se reintenta Fase 1 + Fase 2. Si la pareja provoca otra causa de infactibilidad (p. ej. HC-SEP), se deshace y se prueba otra.
5. Si no quedan parejas posibles, la generación **falla** nombrando qué no cupo. Nunca se virtualiza una sesión en silencio.

## Fase 3 — Algoritmo genético

`SOEA.Engine.Genetic` (`MotorGenetico`, `CromosomaHorario`, `EvaluadorFitness`, `OperadoresGeneticos`).

- Cromosoma: un gen `Start` por sesión (válido para ambas semanas). Genes de sesiones fijas congelados.
- (μ+λ) con elitismo: población 50, hasta 200 generaciones, corte tras 30 sin mejora, torneo k=5. Cada hijo pasa por reparación (HC-C01, HC-SEP, HC-ALT, dominio). Rangos aceptados: población 10–200, generaciones 1–1000.
- Fitness: SC-01, SC-06, SC-09 y guarda de aulas (ver [domain.md](domain.md#restricciones-blandas-fitness-del-ga-menor-es-mejor)).
- Al converger: si queda un solape de grupo → solución de Fase 2. Luego `AsignadorEspaciosExactoCpSat` (sub-modelo CP-SAT, inicios fijos, solo variables de aula, 10 s) asigna las aulas definitivas.
- **Reversión de cesiones:** en orden inverso, cada pareja cedida se intenta devolver a presencial; se acepta si el asignador exacto aún encuentra aula para toda la demanda.

## Validador post-generación

`ValidadorRestriccionesDuras.Validar` revisa 10 reglas sobre las asignaciones finales: HC-C01, HC-S01, HC-ALT, HC-BASE, HC-VH, HC-G01, HC-S03, HC-CAP, HC-S05, HC-SEP. Si el GA violó alguna, se restaura el estado previo a Fase 3 y se usa la solución de Fase 2. Una asignación que apunta a sesión/bloque inexistente se reporta como `DATOS`. `ValidarDocentes` (HC-I01) se usa solo en edición.

## Operaciones sobre un horario ya generado

| Endpoint | Servicio | Comportamiento |
|---|---|---|
| `POST /horario/reacomodar` | `ReacomodarHorarioService` | Mueve una sesión a (día, hora, aula). Recalcula solo la editada y las que ahora chocan (mismo grupo o aula); el resto conserva exactamente su asignación. Rechaza si queda choque de docente |
| `POST /horario/sesion-manual` | `CrearSesionManualService` | Agrega una sesión fija al horario vigente, validando solapes |
| `PATCH /sesiones/{id}/docente` | `AsignarDocenteSesionService` | Solape de docente → 409; fuera de disponibilidad o sobre el máximo de horas → avisos |
| `GET` / `DELETE /horario/actual` | `GenerarHorarioService` | Recupera el horario vigente del semestre / borra sus sesiones |

## Responsabilidad por fase

| Regla | Fase 1 | Fase 2 | Fase 3 | Validador |
|---|---|---|---|---|
| HC-C01 grupo | arista | NoOverlap | reparación | ✓ |
| HC-S01 aula | — | NoOverlap (aula, semana) | asignador exacto | ✓ |
| HC-S03 / S05 / CAP | — | candidatos | asignador exacto | ✓ |
| HC-G01 / VH | dominio | dominio | dominio | ✓ |
| HC-SEP | — | `AddElement` (fijas como obstáculo) | reparación (fijas como obstáculo) | ✓ (ídem) |
| HC-ALT | — | igualdad | reparación | ✓ |
| HC-BASE | — | igualdad | gen congelado | ✓ |
| ALT-05 | — | un inicio por sesión | un gen por sesión | — |
| SC-01 / 06 / 09, guarda | — | — | fitness | — |
