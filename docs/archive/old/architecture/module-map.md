# Mapa de módulos
**Última actualización:** 2026-05-16

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

Contiene:

**No debe referenciar**: EF Core, EPPlus, OR-Tools, ASP.NET, Angular

Contiene:
---

### `SOEA.Application`
**Ruta**: `src/SOEA.Application/`
**Responsabilidad**: Casos de uso, comandos, consultas y orquestación del pipeline.

Contiene:
- Comandos: `GenerateHorarioCommand`, `IngestExcelCommand`, `PublishHorarioCommand`
- Consultas: `GetHorarioQuery`, `ValidateConstraintsQuery`
- DTOs: `HorarioDto`, `SessionDto`, `CohortDto`
- Orquestador del pipeline: `HorarioOptimizationPipeline` (llama coloreado de grafos → CP → genético)
- Servicio de validación: `ConstraintValidator`

**Depende de**: `SOEA.Domain`
**No debe referenciar**: EF Core, EPPlus, OR-Tools, ASP.NET

---

### `SOEA.Infrastructure.Data`
**Ruta**: `src/SOEA.Infrastructure.Data/`
**Responsabilidad**: Acceso a base de datos usando EF Core.

Contiene:
- `SoeaDbContext` y configuraciones de tipos de entidad
- Implementaciones de repositorios (`HorarioRepository`, `CohortRepository`, etc.)
- Migraciones de base de datos

**Depende de**: `SOEA.Domain`, Entity Framework Core
**Implementa**: `IHorarioRepository` y otras interfaces de repositorio de `SOEA.Domain`

---

## Patrón BaseRepository

Todos los repositorios de SOEA heredan de `BaseRepository<T>`.  
Para agregar un nuevo repositorio:

1. Crear interfaz `I[Entidad]Repository : IRepository<[Entidad]>` en `SOEA.Domain/Interfaces/`
2. Crear clase `[Entidad]Repository : BaseRepository<[Entidad]>, I[Entidad]Repository` en `SOEA.Infrastructure.Data/Repositories/`
3. Registrar en `Program.cs`: `builder.Services.AddScoped<I[Entidad]Repository, [Entidad]Repository>()`
4. Crear `[Entidad]Configuration : IEntityTypeConfiguration<[Entidad]>` en `SOEA.Infrastructure.Data/Configurations/`
5. Aplicar en `SOEABdContext.OnModelCreating`: `modelBuilder.ApplyConfiguration(new [Entidad]Configuration())`

**Ventajas:**
- CRUD base centralizado en `BaseRepository<T>`
- Interfaces genéricas `IRepository<T>` reutilizables
- Métodos específicos de cada entidad se definen solo en repositorios hijo

---

### `SOEA.Infrastructure.Excel`
**Ruta**: `src/SOEA.Infrastructure.Excel/`
**Responsabilidad**: Leer datos institucionales desde archivos Excel usando EPPlus.

Contiene:
- `ExcelReader` — lee datos de asignaturas/cohortes/horas
- `DocenteDisponibilidadReader` — lee matrices de disponibilidad
- `CapacidadEspacioReader` — lee capacidad y tipo de sala
- Mapeadores de filas de Excel a entidades de dominio

**Depende de**: `SOEA.Domain`, EPPlus
**Implementa**: `IExcelIngestionService` de `SOEA.Domain`

---

### `SOEA.Engine.GraphColoring`
**Ruta**: `src/SOEA.Engine.GraphColoring/`
**Responsabilidad**: Fase 1 del pipeline de optimización.

Contiene:
- `ConflictGraphBuilder` — crea el grafo de conflictos de sesiones
- `GraphColoringHorarior` — asigna espacios de tiempo preliminares usando heurísticas de coloreado (por ejemplo, Welsh-Powell)
- Salida: `PreHorario` que se pasa a la Fase 2

**Depende de**: `SOEA.Domain`
**Implementa**: la interfaz `IGraphColoringEngine` de `SOEA.Domain` o `SOEA.Application`

---

### `SOEA.Engine.ConstraintProg`
**Ruta**: `src/SOEA.Engine.ConstraintProg/`
**Responsabilidad**: Fase 2: imponer todas las restricciones duras usando OR-Tools CP-SAT.

Contiene:
- `CpSatHorariorBuilder` — traduce el modelo de dominio a variables y restricciones CP-SAT
- `HardConstraintEncoder` — agrega cada restricción dura de `docs/business-rules/hard-constraints.md` como restricción CP-SAT
- `FeasibleHorarioExtractor` — convierte la solución CP-SAT de vuelta a objetos de dominio

**Depende de**: `SOEA.Domain`, Google OR-Tools
**Implementa**: `IConstraintProgrammingEngine` de `SOEA.Domain` o `SOEA.Application`

---

### `SOEA.Engine.Genetic`
**Ruta**: `src/SOEA.Engine.Genetic/`
**Responsabilidad**: Fase 3: optimizar restricciones blandas usando un algoritmo genético.

Contiene:
- `HorarioChromosome` — codifica un horario completo como cromosoma
- `FitnessEvaluator` — calcula la puntuación ponderada de violaciones de restricciones blandas
- `GeneticHorarioOptimizer` — ejecuta la lógica de selección, cruce, mutación y convergencia

**Depende de**: `SOEA.Domain`
**Implementa**: `IGeneticOptimizationEngine` de `SOEA.Domain` o `SOEA.Application`

---

### `SOEA.API`
**Ruta**: `src/SOEA.API/`
**Responsabilidad**: Punto de entrada HTTP: expone el sistema como API REST.

Contiene:
- Controladores: `HorarioController`, `IngestionController`, `ValidationController`
- Middleware: autenticación JWT, autorización basada en roles
- Modelos de solicitud/respuesta (separados de los DTOs de Application)
- Configuración de OpenAPI / Swagger

**Depende de**: `SOEA.Application`, todos los proyectos de Infrastructure y Engine (para registro de DI)

---

### `SOEA.Tests`
**Ruta**: `test/SOEA.Tests/`
**Responsabilidad**: Suite de pruebas automatizadas (unitarias + integración).

Contiene:
- Pruebas unitarias de dominio (invariantes de entidades, validaciones de restricciones)
- Pruebas unitarias de Application (lógica de casos de uso, orquestación del pipeline)
- Pruebas unitarias de Engine (coloreado de grafos, corrección del modelo CP, aptitud del GA)
- Pruebas de integración (pipeline end-to-end con datos de prueba)

**Depende de**: todos los proyectos `SOEA.*`, xUnit, Moq (o NSubstitute)

---

## Preguntas abiertas

- ¿Cada fase del motor debería tener su propio proyecto de pruebas (por ejemplo, `SOEA.Engine.GraphColoring.Tests`)?
- ¿Los DTOs de Application deberían estar en un proyecto separado (`SOEA.Application.Contracts`) para poder
  compartirlos con el cliente TypeScript del frontend?
