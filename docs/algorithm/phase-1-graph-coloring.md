# Fase 1 — coloreado de grafos

## Propósito
Describir el enfoque de coloreado de grafos usado en la primera fase del pipeline de optimización de SOEA.
Copilot usa esto al implementar `SOEA.Engine.GraphColoring`.

## Alcance
Solo la Fase 1: construcción del grafo de conflictos y preasignación inicial de espacios de tiempo.
La Fase 2 (factibilidad CP) y la Fase 3 (optimización genética) se describen en sus propios documentos.

---

## Objetivo de la Fase 1

Producir una asignación inicial rápida y plausible (pero no necesariamente perfecta en restricciones) de sesiones
a espacios de tiempo. Esta semilla de **warm start** reduce el espacio de búsqueda para la Fase 2 (CP-SAT).

La Fase 1 no asigna salas: solo espacios de tiempo. La asignación de salas se completa en la Fase 2.

---

## El grafo de conflictos

A **grafo de conflictos** `G = (V, E)` se construye donde:
- Cada **nodo** `v ∈ V` representa una sesión que debe programarse
- Una **arista** `(v₁, v₂) ∈ E` significa que las sesiones `v₁` y `v₂` no pueden asignarse al mismo espacio de tiempo

### Reglas de arista

Dos sesiones entran en conflicto (se conectan por una arista) si CUALQUIERA de las siguientes condiciones es verdadera:

| Condición | Razón |
|---|---|
| Mismo docente | Un docente no puede impartir dos sesiones simultáneamente |
| Misma cohorte | Una cohorte no puede asistir a dos sesiones simultáneamente |
| Mismo tipo de alternancia O cohorte no alternante involucrada | Ambas cohortes ocuparían físicamente un espacio al mismo tiempo |
| Misma cohorte Y dependencia secuencial (restricción split block) | Restricción de días consecutivos de HC-T05 |

### Lógica de aristas por alternancia

- TypeA + TypeA compartiendo un espacio → arista de conflicto (ambas están físicamente presentes en las mismas semanas)
- TypeB + TypeB compartiendo un espacio → arista de conflicto
- TypeA + TypeB compartiendo un espacio → **no** hay arista de conflicto (nunca están físicamente presentes al mismo tiempo)
- Cualquier sesión que involucre SinAlternancia → arista de conflicto por compartición de espacio

---

## Algoritmo de coloreado de grafos

**Colores** = espacios de tiempo disponibles en `T`

Objetivo: asignar un color (espacio de tiempo) a cada nodo (sesión) de modo que ningún par de nodos adyacentes comparta
el mismo color.

### Algoritmo recomendado: heurística Welsh-Powell

1. Ordenar los nodos por **grado** (número de aristas) en orden descendente
2. Asignar a cada nodo, en orden, el color disponible con número más bajo, omitiendo los colores usados por los vecinos
3. Si no hay color disponible, marcar la sesión como sin colorear para que la Fase 2 la resuelva o la rechace explícitamente

Esta es una heurística codiciosa: es rápida, pero puede no minimizar los colores ni satisfacer todas las restricciones.
Si no existe un color disponible, la sesión se marca como sin colorear para que la Fase 2 la resuelva o la rechace explícitamente.
Su salida se refina en la Fase 2.

---

## Entradas

- Lista completa de sesiones `S` con su docente, cohorte y tipo de alternancia
- Conjunto de espacios de tiempo disponibles `T`

## Salidas

- `PartialSchedule`: un mapeo `session → timeSlot` para todas las sesiones
- Las sesiones que no puedan colorearse (si las hay) se marcan con estado `Conflict`

---

## Integración con la Fase 2

La salida `PartialSchedule` se pasa a `SOEA.Engine.ConstraintProg` como indicio de warm start.
La Fase 2 puede reasignar espacios de tiempo para sesiones conflictivas o infactibles.

---

## Objetivo de rendimiento

La Fase 1 debería completarse en menos de 5 segundos para hasta 500 sesiones.

---

## Preguntas abiertas

- ¿El coloreado de grafos debería usar un algoritmo DSatur en lugar de Welsh-Powell para minimizar mejor el número cromático?
- ¿Las restricciones blandas (por ejemplo, preferir horarios de mañana para ciertas cohortes) deberían incorporarse como indicios en la Fase 1?
