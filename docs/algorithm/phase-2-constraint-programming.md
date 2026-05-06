# Fase 2 — Programación por restricciones (CP-SAT)

## Propósito
Describir cómo SOEA usa Google OR-Tools CP-SAT para imponer todas las restricciones duras y producir
un horario factible. Copilot usa esto al implementar `SOEA.Engine.ConstraintProg`.

## Alcance
Solo la Fase 2: imposición de restricciones duras y resolución de factibilidad.
La Fase 1 (warm start de Graph Coloring) y la Fase 3 (optimización genética) están en sus propios documentos.

---

## Objetivo de la Fase 2

Tomar el `PartialSchedule` de warm start de la Fase 1 y encontrar una asignación **totalmente factible** —
una que no viole ninguna restricción dura — usando OR-Tools CP-SAT.

La Fase 2 asigna tanto **espacios de tiempo** como **espacios** a todas las sesiones.

---

## Tecnología

- **Biblioteca**: solucionador Google OR-Tools CP-SAT (paquete NuGet `Google.OrTools`)
- **Tipo de modelo**: Programación por restricciones (CP), no Programación Lineal (LP)
- **Tiempo límite del solucionador**: configurable (predeterminado 600 segundos)

---

## Resumen del modelo CP

### Variables

For each session `s`:
- `timeVar(s)`: integer variable ∈ {0, ..., |T|-1} — index into the time slot array
- `spaceVar(s)`: integer variable ∈ {0, ..., |R|-1, null_index} — index into the space array

### Restricciones añadidas al modelo

Each hard constraint from `docs/business-rules/hard-constraints.md` is encoded as follows:

| Restricción dura | Codificación CP-SAT |
|---|---|
| HC-I01: Conflicto de docente | Para cada par de sesiones con el mismo docente: `timeVar(s₁) ≠ timeVar(s₂)` |
| HC-C01: Conflicto de cohorte | Para cada par de sesiones con la misma cohorte: `timeVar(s₁) ≠ timeVar(s₂)` |
| HC-S01: Conflicto de espacio (mismo tipo de alternancia) | Para cada par con el mismo tipo de alternancia y ambas presenciales: `¬(timeVar(s₁) = timeVar(s₂) ∧ spaceVar(s₁) = spaceVar(s₂))` |
| HC-S02: Capacidad | `enrolledStudents(coh(s)) ≤ capacity(r)` — se impone restringiendo el dominio de `spaceVar(s)` a espacios válidos |
| HC-I02: Disponibilidad del docente | `timeVar(s) ∈ domain(available(inst(s)))` |
| HC-S03: Tipo de espacio | `spaceVar(s) ∈ domain(spacesOfType(type(s)))` |
| HC-T01–T02: Límites de tiempo | El dominio de la variable se restringe solo a espacios de tiempo válidos |
| HC-T03: Bloque de 3h = consecutivo | Las sesiones en bloque se enlazan mediante restricciones auxiliares `AllDifferent` / de orden |

### Warm Start (indicio)

La asignación de la Fase 1 se pasa a CP-SAT como un **indicio de solución**:
```
solver.AddHint(timeVar(s), phase1TimeIndex(s))
```
Esto ayuda a CP-SAT a encontrar una solución factible más rápido.

---

## Entradas

- `PartialSchedule` from Phase 1 (time slot hints)
- Full domain model: sessions, instructors, cohorts, spaces, time slots

## Salidas

- `FeasibleSchedule`: una asignación completa de sesiones a espacios de tiempo y espacios
- Todas las restricciones duras satisfechas (cero violaciones)
- Si no existe una solución factible dentro del tiempo límite, la Fase 2 devuelve un `InfeasibleResult`

---

## Manejo de infactibilidad

Si CP-SAT no puede encontrar una solución factible:
1. Registrar qué restricciones son las más probables causantes de la infactibilidad (usando la API de supuestos de CP-SAT)
2. Devolver un error a la capa Application con un informe de conflicto de restricciones
3. La capa Application lo muestra al usuario como un error de validación antes de ejecutar la Fase 3

---

## Objetivo de rendimiento

La Fase 2 debería encontrar una solución factible en menos de 120 segundos para volúmenes de datos del piloto.
El límite configurable de tiempo es de 600 segundos.

---

## Preguntas abiertas

- ¿CP-SAT también debería optimizar un objetivo básico (por ejemplo, minimizar dispersión) o solo imponer factibilidad?
- ¿La Fase 2 debería omitir las sesiones que la Fase 1 marcó como no resolubles y reportarlas por separado?
