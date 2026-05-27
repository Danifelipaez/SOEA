# Fase 3 — Algoritmo genético
**Última actualización:** 2026-05-16

## Propósito
Describir el algoritmo genético usado en la Fase 3 para optimizar restricciones blandas en el horario
producido por la Fase 2. Copilot usa esto al implementar `SOEA.Engine.Genetic`.

## Alcance
Solo la Fase 3: optimización de restricciones blandas mediante algoritmo genético.
La Fase 1 (coloreado de grafos) y la Fase 2 (CP-SAT) se describen en sus propios documentos.

---

## Objetivo de la Fase 3

Tomar el horario **factible** de la Fase 2 y mejorarlo minimizando la puntuación ponderada de
violaciones de restricciones blandas (aptitud). Las restricciones duras deben permanecer satisfechas en todo momento.

---

## Representación del cromosoma

Un **cromosoma** codifica una asignación completa del horario:

```
chromosome = [ (sessionId₁, timeSlotIndex₁, spaceIndex₁),
               (sessionId₂, timeSlotIndex₂, spaceIndex₂),
               ...
               (sessionIdₙ, timeSlotIndexₙ, spaceIndexₙ) ]
```

Cada gen es una terna de 3 elementos: sesión → espacio de tiempo asignado → espacio asignado.

El horario factible de la Fase 2 es el **cromosoma inicial** (semilla de la primera generación).

---

## Función de aptitud

```
fitness(chromosome) = Σᵢ wᵢ × violationCount(SC_i, chromosome)
```

Donde:
- `SC_i` = restricción blanda i (de `docs/business-rules/soft-constraints.md`)
- `wᵢ` = peso de la restricción i
- `violationCount` = número de violaciones individuales de esa restricción en el horario

**Menor fitness = mejor horario.**

Un cromosoma con fitness = 0 satisface perfectamente todas las restricciones blandas.

---

## Operaciones genéticas

### Selección
- **Selección por torneo**: seleccionar aleatoriamente k cromosomas y conservar el mejor
- Tamaño del torneo k = 5 (configurable)

### Cruce
- **Cruce de un punto** sobre la lista de sesiones
- Ambos descendientes heredan segmentos de genes que preservan restricciones duras
- Después del cruce, verificar que no se violen restricciones duras; si se violan, reparar o descartar

### Mutación
- **Mutación aleatoria de gen**: para una sesión seleccionada al azar, asignar un espacio de tiempo
  o espacio distinto y válido (tomado del conjunto de alternativas seguras respecto a restricciones duras)
- Probabilidad de mutación: 0.05 por gen (configurable)
- La mutación siempre verifica el cumplimiento de restricciones duras antes de aceptar el cambio

### Operador de reparación
Si un cruce o una mutación produce una violación de restricción dura:
1. Intentar reasignar la sesión conflictiva a un espacio de tiempo/espacio alternativo válido
2. Si no existe una alternativa válida, revertir al cromosoma padre para ese gen

---

## Flujo del algoritmo

```
1. Inicializar la población con N copias del horario factible de la Fase 2
  (opcionalmente agregar variantes perturbadas para dar diversidad)
2. Evaluate fitness for all chromosomes
3. Repeat for G generations (or until convergence):
   a. Select parents via tournament selection
   b. Apply crossover to produce offspring
   c. Apply mutation to offspring
   d. Evaluate fitness of offspring
   e. Replace worst chromosomes in population with offspring (if improvement)
4. Return the chromosome with the lowest fitness score
```

### Hiperparámetros (predeterminados — todos configurables)

| Parámetro | Predeterminado |
|---|---|
| Tamaño de población N | 50 |
| Máx. generaciones G | 200 |
| Tamaño del torneo k | 5 |
| Probabilidad de cruce | 0.8 |
| Probabilidad de mutación por gen | 0.05 |
| Umbral de convergencia (sin mejora durante X generaciones) | 30 |

---

## Entradas

- `FeasibleSchedule` de la Fase 2 (cromosoma semilla)
- Definiciones y pesos de restricciones blandas desde la configuración

## Salidas

- `OptimizedSchedule`: el cromosoma con la puntuación de fitness más baja tras G generaciones
- La puntuación de fitness se incluye en la salida para reportes

---

## Objetivo de rendimiento

La Fase 3 debería completarse en menos de 5 minutos para volúmenes de datos del piloto (≤ 200 sesiones).

---

## Preguntas abiertas

- ¿La población inicial debería incluir diversidad más allá de copias de la semilla de la Fase 2?
  (por ejemplo, perturbar intercambiando aleatoriamente sesiones compatibles)
- ¿Los pesos de las restricciones blandas deberían poder configurarse por ejecución desde la UI de Admin?
- ¿Simulated Annealing es una alternativa aceptable al Algoritmo Genético si la convergencia del GA es lenta?
