# Plan de pruebas
**Última actualización:** 2026-05-16

## Propósito
Definir la estrategia de pruebas para SOEA: qué tipos de pruebas existen, qué cubren y cómo
ejecutarlas. Copilot usa esto al generar archivos de prueba y datos de prueba.

## Alcance
Todas las pruebas automatizadas en `test/SOEA.Tests/`. Las pruebas manuales/de aceptación están en `acceptance-criteria.md`.

---

## Niveles de prueba

### Pruebas unitarias

Cubren clases individuales de forma aislada con todas las dependencias simuladas.

| Área | Clase objetivo | Qué probar |
|---|---|---|
| Domain | `Sesion` | Invariantes de entidad (duration > 0, una sesión virtual no tiene espacio) |
| Domain | `EspacioDeTiempo` | Validación de StartTime < EndTime |
| Domain | `Cohorte` | Asignación de TipoAlternancia |
| Application | `ConstraintValidator` | Lógica de detección de restricciones duras |
| Application | `ScheduleOptimizationPipeline` | Llamadas de orquestación de fases (motores simulados) |
| Fase 1 | `ConflictGraphBuilder` | Construcción de aristas para cada tipo de conflicto |
| Fase 1 | `GraphColoringScheduler` | El coloreado no produce nodos adyacentes con el mismo color |
| Fase 2 | `HardConstraintEncoder` | Cada HC se mapea al tipo correcto de restricción CP-SAT |
| Fase 3 | `FitnessEvaluator` | Fitness = 0 para un horario sin violaciones blandas |
| Fase 3 | `GeneticScheduleOptimizer` | El fitness mejora o se mantiene entre generaciones |

### Pruebas de integración

Prueban la interacción entre capas usando datos reales (en memoria o de prueba).

| Prueba | Qué verificar |
|---|---|
| Pipeline end-to-end (conjunto pequeño) | El pipeline completo (Fase 1→2→3) produce un horario con cero violaciones duras |
| Ingesta de Excel | Leer un archivo de Excel de muestra produce las entidades de dominio correctas |
| Endpoint API: POST /schedule/generate | Devuelve 200 con un JSON válido que coincide con `json-output-spec.md` |
| Ida y vuelta en base de datos | Un horario guardado puede cargarse y coincide con el original |

### Pruebas específicas de restricciones

Pruebas que validan directamente cada restricción dura de `hard-constraints.md`.

| Restricción | Escenario de prueba |
|---|---|
| HC-I01 | Dos sesiones con el mismo docente en el mismo espacio de tiempo → debe marcarse |
| HC-S02 | 30 estudiantes asignados a un espacio con capacidad 25 → debe marcarse |
| HC-T02 | Sesión de laboratorio que inicia a las 20:00 → debe marcarse |
| HC-T05 | Sesiones split-block en días consecutivos → debe marcarse |
| HC-S04 | Sesión virtual con un espacio físico asignado → debe marcarse |

---

## Datos de prueba

- Conjunto de prueba pequeño: 5 cohortes, 3 docentes, 5 espacios, 20 sesiones
- Conjunto de caso extremo: sesión que no puede programarse (todos los espacios del docente ocupados)
- Conjunto de alternancia: mezcla de cohortes Tipo A, Tipo B y SinAlternancia

Los archivos de datos de prueba deben colocarse en `test/SOEA.Tests/TestData/`.

---

## Ejecución de pruebas

```bash
dotnet test test/SOEA.Tests/SOEA.Tests.csproj
```

---

## Objetivo de cobertura de pruebas

- Capa de dominio: 90% de cobertura de líneas
- Capa de Application: 80% de cobertura de líneas
- Capas de Engine: 75% de cobertura de líneas (rutas complejas del algoritmo)
- Capas de Infrastructure: 60% de cobertura de líneas (enfoque en pruebas de integración)

---

## Preguntas abiertas

- ¿Cada fase del motor debería tener su propio proyecto de pruebas (por ejemplo, `SOEA.Engine.GraphColoring.Tests`)?
- ¿Los datos de prueba deberían incrustarse como objetos C# o cargarse desde archivos JSON/Excel?
