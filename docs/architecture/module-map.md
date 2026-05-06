# Mapa de módulos

## Propósito
Enumerar cada proyecto/ensamblado de la solución SOEA, describir su responsabilidad única
y definir de qué otros proyectos puede depender. Copilot usa esto al generar nuevas clases,
decidir a qué proyecto pertenece un archivo y hacer respetar los límites de capa.

## Alcance
Todos los proyectos de los directorios `src/` y `test/`.

---

## Reglas de dependencias entre módulos

```
SOEA.API               → SOEA.Application
SOEA.API               → SOEA.Infrastructure.Data
SOEA.API               → SOEA.Infrastructure.Excel
SOEA.Application       → SOEA.Domain
SOEA.Application       → SOEA.Engine.GraphColoring
SOEA.Application       → SOEA.Engine.ConstraintProg
SOEA.Application       → SOEA.Engine.Genetic
SOEA.Infrastructure.*  → SOEA.Domain
SOEA.Engine.*          → SOEA.Domain
SOEA.Domain            → (no dependencies on other SOEA projects)
```

**Nunca permitir**: Domain → Infrastructure, Domain → API, Application → Infrastructure (directamente),
Application → API

---

## Detalle de módulos

### `SOEA.Domain`
**Ruta**: `src/SOEA.Domain/`
**Responsabilidad**: Modelo central de negocio sin dependencias externas.

Contains:
- Entidades: `src/SOEA.Domain/Entities/` (`Sesion`, `Session`, `Cohort`, `Space`, `Instructor`, `TimeSlot`, `Schedule`, `Subject`)
- Objetos de valor: `AlternanciaType`, `TimeRange`, `Capacity`
- Enumeraciones: `SpaceType`, `SessionStatus`, `ConstraintSeverity`
- Interfaces de dominio (puertos): `src/SOEA.Domain/Interfaces/` (`IScheduleRepository`, `IOptimizationEngine`, `IExcelIngestionService`)
- Excepciones de dominio: `ConstraintViolationException`, `InvalidSessionException`

**No debe referenciar**: EF Core, EPPlus, OR-Tools, ASP.NET, Angular

---

### `SOEA.Application`
**Ruta**: `src/SOEA.Application/`
**Responsabilidad**: Casos de uso, comandos, consultas y orquestación del pipeline.

Contains:
- Comandos: `GenerateScheduleCommand`, `IngestExcelCommand`, `PublishScheduleCommand`
- Consultas: `GetScheduleQuery`, `ValidateConstraintsQuery`
- DTOs: `ScheduleDto`, `SessionDto`, `CohortDto`
- Orquestador del pipeline: `ScheduleOptimizationPipeline` (llama Graph Coloring → CP → Genetic)
- Servicio de validación: `ConstraintValidator`

**Depende de**: `SOEA.Domain`
**No debe referenciar**: EF Core, EPPlus, OR-Tools, ASP.NET

---

### `SOEA.Infrastructure.Data`
**Ruta**: `src/SOEA.Infrastructure.Data/`
**Responsabilidad**: Acceso a base de datos usando EF Core.

Contains:
- `SoeaDbContext` y configuraciones de tipos de entidad
- Implementaciones de repositorios (`ScheduleRepository`, `CohortRepository`, etc.)
- Migraciones de base de datos

**Depende de**: `SOEA.Domain`, Entity Framework Core
**Implementa**: `IScheduleRepository` y otras interfaces de repositorio de `SOEA.Domain`

---

### `SOEA.Infrastructure.Excel`
**Ruta**: `src/SOEA.Infrastructure.Excel/`
**Responsabilidad**: Leer datos institucionales desde archivos Excel usando EPPlus.

Contains:
- `CurriculumExcelReader` — lee datos de asignaturas/cohortes/horas
- `InstructorAvailabilityReader` — lee matrices de disponibilidad
- `SpaceInventoryReader` — lee capacidad y tipo de sala
- Mapeadores de filas de Excel a entidades de dominio

**Depende de**: `SOEA.Domain`, EPPlus
**Implementa**: `IExcelIngestionService` de `SOEA.Domain`

---

### `SOEA.Engine.GraphColoring`
**Ruta**: `src/SOEA.Engine.GraphColoring/`
**Responsabilidad**: Fase 1 del pipeline de optimización.

Contains:
- `ConflictGraphBuilder` — crea el grafo de conflictos de sesiones
- `GraphColoringScheduler` — asigna espacios de tiempo preliminares usando heurísticas de coloreado (por ejemplo, Welsh-Powell)
- Salida: `PartialSchedule` que se pasa a la Fase 2

**Depende de**: `SOEA.Domain`
**Implementa**: la interfaz `IGraphColoringEngine` de `SOEA.Domain` o `SOEA.Application`

---

### `SOEA.Engine.ConstraintProg`
**Ruta**: `src/SOEA.Engine.ConstraintProg/`
**Responsabilidad**: Fase 2: imponer todas las restricciones duras usando OR-Tools CP-SAT.

Contains:
- `CpSatSchedulerBuilder` — traduce el modelo de dominio a variables y restricciones CP-SAT
- `HardConstraintEncoder` — agrega cada restricción dura de `docs/business-rules/hard-constraints.md` como restricción CP-SAT
- `FeasibleScheduleExtractor` — convierte la solución CP-SAT de vuelta a objetos de dominio

**Depende de**: `SOEA.Domain`, Google OR-Tools
**Implementa**: `IConstraintProgrammingEngine` de `SOEA.Domain` o `SOEA.Application`

---

### `SOEA.Engine.Genetic`
**Ruta**: `src/SOEA.Engine.Genetic/`
**Responsabilidad**: Fase 3: optimizar restricciones blandas usando un algoritmo genético.

Contains:
- `ScheduleChromosome` — codifica un horario completo como cromosoma
- `FitnessEvaluator` — calcula la puntuación ponderada de violaciones de restricciones blandas
- `GeneticScheduleOptimizer` — ejecuta la lógica de selección, cruce, mutación y convergencia

**Depende de**: `SOEA.Domain`
**Implementa**: `IGeneticOptimizationEngine` de `SOEA.Domain` o `SOEA.Application`

---

### `SOEA.API`
**Ruta**: `src/SOEA.API/`
**Responsabilidad**: Punto de entrada HTTP: expone el sistema como API REST.

Contains:
- Controladores: `ScheduleController`, `IngestionController`, `ValidationController`
- Middleware: autenticación JWT, autorización basada en roles
- Modelos de solicitud/respuesta (separados de los DTOs de Application)
- Configuración de OpenAPI / Swagger

**Depende de**: `SOEA.Application`, todos los proyectos de Infrastructure y Engine (para registro de DI)

---

### `SOEA.Tests`
**Ruta**: `test/SOEA.Tests/`
**Responsabilidad**: Suite de pruebas automatizadas (unitarias + integración).

Contains:
- Pruebas unitarias de dominio (invariantes de entidades, validaciones de restricciones)
- Pruebas unitarias de Application (lógica de casos de uso, orquestación del pipeline)
- Pruebas unitarias de Engine (graph coloring, corrección del modelo CP, aptitud del GA)
- Pruebas de integración (pipeline end-to-end con datos de prueba)

**Depende de**: todos los proyectos `SOEA.*`, xUnit, Moq (o NSubstitute)

---

## Preguntas abiertas

- ¿Cada fase del motor debería tener su propio proyecto de pruebas (por ejemplo, `SOEA.Engine.GraphColoring.Tests`)?
- ¿Los DTOs de Application deberían estar en un proyecto separado (`SOEA.Application.Contracts`) para poder
  compartirlos con el cliente TypeScript del frontend?
