# Definición del problema — University Course Timetabling Problem (UCTP)

## Propósito
Definir formalmente el problema de programación de SOEA como una variante de UCTP. Copilot usa este documento
al generar modelos de optimización, codificaciones de restricciones y configuraciones del solucionador.

## Alcance
Definición formal del problema, variables, restricciones y función objetivo.
Los detalles de implementación específicos de cada algoritmo están en los documentos de fase.

---

## ¿Qué es UCTP?

El **University Course Timetabling Problem (UCTP)** es un problema de optimización combinatoria
en el que el objetivo es asignar un conjunto de eventos (sesiones) a un conjunto de periodos de tiempo y salas
de modo que se satisfaga un conjunto de restricciones.

SOEA resuelve una variante específica de UCTP que incluye:
- Modelo de cohorte en alternancia (híbrido)
- Pipeline de optimización de tres fases
- Restricciones duras (deben cumplirse) y restricciones blandas (a optimizar)

---

## Definición formal

### Entrada

Sea:
- **S** = conjunto de sesiones a programar
- **T** = conjunto de espacios de tiempo disponibles (día × hora inicio-fin)
- **R** = conjunto de espacios físicos (salas)
- **I** = conjunto de docentes
- **C** = conjunto de cohortes

Cada sesión `s ∈ S` tiene:
- Un docente requerido `inst(s) ∈ I`
- Una cohorte requerida `coh(s) ∈ C`
- Una duración requerida `dur(s)` en horas
- Un tipo de espacio requerido opcional `type(s)`
- Un tipo de alternancia `alt(s) ∈ {TypeA, TypeB, NonAlternating}`

### Variables de decisión

Para cada sesión `s ∈ S`:
- `t(s) ∈ T` — espacio de tiempo asignado
- `r(s) ∈ R ∪ {null}` — espacio asignado (null para sesiones virtuales)

### Restricciones de factibilidad (duras)

1. **Conflicto de docente**: `∀ s₁, s₂ ∈ S, s₁ ≠ s₂: inst(s₁) = inst(s₂) → t(s₁) ≠ t(s₂)`
2. **Conflicto de cohorte**: `∀ s₁, s₂ ∈ S, s₁ ≠ s₂: coh(s₁) = coh(s₂) → t(s₁) ≠ t(s₂)`
3. **Conflicto de espacio (considerando alternancia)**: `∀ s₁, s₂ ∈ S: r(s₁) = r(s₂) ∧ t(s₁) = t(s₂) → alt(s₁) = alt(s₂) ∨ alt(s₁) = NonAlternating ∨ alt(s₂) = NonAlternating`
4. **Capacidad**: `∀ s ∈ S: r(s) ≠ null → enrolled(coh(s)) ≤ capacity(r(s))`
5. **Disponibilidad**: `∀ s ∈ S: t(s) ∈ available(inst(s))`
6. **Tipo de espacio**: `∀ s ∈ S: type(s) ≠ null → spaceType(r(s)) = type(s)`
7. **Límites de tiempo**: `∀ s ∈ S: startTime(t(s)) ≥ 07:00 ∧ endTime(t(s)) ≤ 21:30`

Consulta `docs/business-rules/hard-constraints.md` para la lista completa.

### Función objetivo (optimización blanda)

Minimizar:
```
F(assignment) = Σᵢ wᵢ × violationCount(SCᵢ)
```

Donde `SCᵢ` son las restricciones blandas y `wᵢ` sus pesos.
Consulta `docs/business-rules/soft-constraints.md` para la lista completa.

---

## ¿Por qué tres fases?

UCTP es NP-hard. Resolverlo directamente con un modelo CP completo para instancias grandes es demasiado lento.
La descomposición en tres fases reduce la complejidad:

1. **Fase 1 — coloreado de grafos**: heurística rápida que produce una asignación inicial plausible (pero posiblemente infactible) modelando los conflictos como un problema de coloreado de grafos. Reduce el espacio de búsqueda para la Fase 2.

2. **Fase 2 — CP-SAT**: solucionador exacto de factibilidad que impone todas las restricciones duras. Toma la asignación de la Fase 1 como punto de partida y encuentra la solución factible más cercana.

3. **Fase 3 — algoritmo genético**: metaheurística que optimiza restricciones blandas a partir de la solución factible de la Fase 2.

---

## Referencias

- Schaerf, A. (1999). "A Survey of Automated Timetabling." *Artificial Intelligence Review*, 13(2), 87–127.
- Tan, J. et al. (2021). "A Survey of the State-of-the-Art of Optimisation Methodologies in School Timetabling Problems." *Expert Systems with Applications*, 165.

---

## Preguntas abiertas

- ¿Cuál es el número esperado de sesiones por semestre para toda la institución (no solo el piloto)?
- ¿Hay sesiones que deban asignarse a espacios de tiempo fijos específicos (sesiones ancladas)?
