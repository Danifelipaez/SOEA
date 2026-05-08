# SOEA — Estructura de Carpetas y Archivos

## Patrón: Vertical Slice Architecture

Este proyecto usa **Vertical Slice** en lugar de CQRS. Cada feature (Asignatura, Docente, Espacio, etc.) es **una rodaja vertical** que atraviesa:

```
API Controller (Asignaturas)
    ↓
Application Service (CreateAsignaturaService)
    ↓
Domain Entity (Asignatura)
    ↓
Infrastructure Repository (AsignaturaRepository)
    ↓
EF Core Configuration (AsignaturaConfiguration)
    ↓
PostgreSQL Table
```

**Ventajas:**
- Cada feature es independiente
- Fácil de entender: todo de una entidad en una carpeta
- Escalable: agregar Docente = copiar patrón de Asignatura
- Minimiza acoplamiento entre features
- Ideal para pilot pequeño

**Regla de oro:** Si trabaja en Asignatura, toca:
- `SOEA.Application/Features/Asignaturas/`
- `SOEA.API/Controllers/AsignaturasController.cs`
- `SOEA.Infrastructure.Data/Repositories/AsignaturaRepository.cs`
- `SOEA.Infrastructure.Data/Configurations/AsignaturaConfiguration.cs`

---

## Frontend — Angular (`soea-angular`)

```
soea-angular/
├── features/
│   ├── admin/
│   │   ├── excel-upload.component.ts
│   │   ├── optimization-panel.component.ts
│   │   ├── schedule-grid.component.ts
│   │   ├── occupancy-chart.component.ts
│   │   ├── infeasibility-report.component.ts
│   │   └── manual-edit.component.ts
│   ├── instructor/
│   │   ├── my-schedule.component.ts
│   │   ├── session-detail.component.ts
│   │   └── availability-form.component.ts
│   └── student/
│       ├── cohort-schedule.component.ts
│       └── my-subjects.component.ts
├── core/                          # auth, guards, interceptors
│   ├── auth.service.ts
│   ├── role.guard.ts
│   ├── auth.interceptor.ts
│   ├── schedule.service.ts
│   └── ingestion.service.ts
└── shared/                        # components, models
    ├── timetable-grid.component.ts
    ├── session-card.component.ts
    ├── schedule.model.ts
    ├── session.model.ts
    ├── loading-spinner.component.ts
    └── alert-banner.component.ts
```

---

## SOEA.API — Vertical Slice Endpoints

Controladores organizados por feature. Cada controlador llama al servicio de su vertical slice.

```
SOEA.API/
├── Controllers/
│   ├── AsignaturasController.cs
│   ├── DocentesController.cs
│   ├── EspaciosController.cs
│   ├── HorariosController.cs
│   └── AuthController.cs
├── Middleware/
│   ├── GlobalExceptionHandlerMiddleware.cs
│   └── RequestLoggingMiddleware.cs
├── Models/                        # Request & Response (compartidos con Application)
│   ├── Asignaturas/
│   │   ├── CreateAsignaturaRequest.cs
│   │   └── AsignaturaResponse.cs
│   ├── Docentes/
│   │   ├── CreateDocenteRequest.cs
│   │   └── DocenteResponse.cs
│   ├── Espacios/
│   │   ├── CreateEspacioRequest.cs
│   │   └── EspacioResponse.cs
│   ├── Horarios/
│   │   ├── GenerateScheduleRequest.cs
│   │   ├── ScheduleResponse.cs
│   │   └── InfeasibilityReportResponse.cs
│   └── Common/
│       ├── ErrorDetail.cs
│       └── PaginationResponse.cs
└── Configuration/
    ├── Program.cs                 # DI registration + Middleware setup
    ├── appsettings.json
    ├── appsettings.Development.json
    └── DependencyInjectionConfig.cs
```

**Flujo endpoint → servicio:**
```
POST /api/asignaturas
  ↓
AsignaturasController.Create()
  ↓
CreateAsignaturaService.ExecuteAsync()
  ↓
IAsignaturaRepository.AddAsync()
  ↓
AsignaturaRepository (EF Core)
  ↓
PostgreSQL
```


---

## SOEA.Application — Vertical Slice

Organización por feature. Cada slice contiene toda la lógica de una entidad: servicios, DTOs, handlers.

```
SOEA.Application/
├── Features/
│   ├── Asignaturas/
│   │   ├── CreateAsignaturaService.cs
│   │   ├── GetAsignaturasService.cs
│   │   ├── Requests/
│   │   │   └── CreateAsignaturaRequest.cs
│   │   └── Responses/
│   │       └── AsignaturaResponse.cs
│   ├── Docentes/
│   │   ├── CreateDocenteService.cs
│   │   ├── GetDocentesService.cs
│   │   ├── Requests/
│   │   │   └── CreateDocenteRequest.cs
│   │   └── Responses/
│   │       └── DocenteResponse.cs
│   ├── Espacios/
│   │   ├── CreateEspacioService.cs
│   │   ├── GetEspaciosService.cs
│   │   ├── Requests/
│   │   │   └── CreateEspacioRequest.cs
│   │   └── Responses/
│   │       └── EspacioResponse.cs
│   └── Horarios/
│       ├── GenerateScheduleService.cs
│       ├── ValidateConstraintsService.cs
│       ├── Requests/
│       │   └── GenerateScheduleRequest.cs
│       └── Responses/
│           ├── ScheduleResponse.cs
│           └── InfeasibilityReportResponse.cs
├── Interfaces/
│   ├── IAsignaturaRepository.cs
│   ├── IDocenteRepository.cs
│   ├── IEspacioRepository.cs
│   └── IHorarioRepository.cs
└── Common/
    └── IRepository.cs             # Base interface
```

**Razón de este patrón:**
- Cada feature vertical: Domain → Application → Infrastructure → API
- DTOs separados (Requests/Responses) por claridad
- Interfaces de repositorio en Application (contrato con Infrastructure)
- Escalable: agregar Sesion es copiar el patrón de Asignaturas


---

## SOEA.Domain — Núcleo Puro

```
SOEA.Domain/
├── Entities/
│   ├── Sesion.cs
│   ├── Espacio.cs
│   ├── Docente.cs
│   ├── FranjaTiempo.cs
│   ├── Horario.cs
│   ├── Asignatura.cs
│   ├── Programa.cs
│   └── DisponibilidadDocente.cs
│   # // Cohorte.cs  (comentado / pendiente)
├── Enums/
│   ├── TipoAlternancia.cs         # TipoA, TipoB, Normal
│   ├── TipoEspacio.cs             # Salon, Lab, Auditorio
│   ├── EstadoSesion.cs            # Pendiente, Asignada, Conflicto
│   └── Modalidad.cs               # Presencial, Virtual
├── Interfaces/
│   ├── IHorarioRepositorio.cs
│   ├── ISesionRepositorio.cs
│   └── IExcelImportador.cs
├── Exceptions/
│   ├── OptimizationInfeasibleException.cs
│   ├── ConstraintViolationException.cs
│   └── InvalidSessionException.cs
└── ValueObjects/
    ├── RangoTiempo.cs
    ├── Capacidad.cs
    ├── RestriccionesPesos.cs
    └── EtiquetaSemestre.cs
```

---

## SOEA.Engine.GraphColoring — Fase 1

```
SOEA.Engine.GraphColoring/
├── ConflictGraphBuilder.cs
├── GraphColoringScheduler.cs      # Welsh-Powell
├── PartialSchedule.cs             # output model
├── IGraphColoringEngine.cs        # interface
├── ConflictNode.cs
├── ConflictEdge.cs
└── ColoringResult.cs
```

---

## SOEA.Engine.ConstraintProg — Fase 2 (OR-Tools CP-SAT)

```
SOEA.Engine.ConstraintProg/
├── CpSatSchedulerBuilder.cs
├── HardConstraintEncoder.cs
├── FeasibleScheduleExtractor.cs
├── FeasibleSchedule.cs            # output model
├── InfeasibleResult.cs
├── IConstraintProgEngine.cs       # interface
├── WarmStartHintProvider.cs
├── InfeasibilityReporter.cs
└── CpSatConfig.cs                 # TimeLimit, parameters
```

---

## SOEA.Engine.Genetic — Fase 3 (Algoritmo Genético)

```
SOEA.Engine.Genetic/
├── ScheduleChromosome.cs
├── FitnessEvaluator.cs
├── GeneticScheduleOptimizer.cs
├── TournamentSelector.cs
├── SinglePointCrossover.cs
├── RandomGeneMutation.cs
├── HardConstraintRepairOperator.cs
├── Population.cs
├── IGeneticEngine.cs              # interface
├── OptimizedSchedule.cs           # output
└── GeneticHyperparameters.cs
```

---

## SOEA.Infrastructure.Data — EF Core + PostgreSQL

DbContext centralizado; Repositories y Configurations por entidad (Vertical Slice).

```
SOEA.Infrastructure.Data/
├── SoEADbContext.cs               # DbContext centralizado
├── Repositories/
│   ├── BaseRepository.cs          # Implementación base
│   ├── AsignaturaRepository.cs
│   ├── DocenteRepository.cs
│   ├── EspacioRepository.cs
│   └── HorarioRepository.cs
├── Configurations/                # EF Fluent API (OnModelCreating)
│   ├── AsignaturaConfiguration.cs
│   ├── DocenteConfiguration.cs
│   ├── EspacioConfiguration.cs
│   ├── HorarioConfiguration.cs
│   ├── SesionConfiguration.cs
│   └── ProgramaConfiguration.cs
└── Migrations/                    # EF Core auto-generated
    ├── 001_InitialCreate.cs
    ├── 002_AddAsignaturas.cs
    └── ...
```

**Por qué esta estructura:**
- `SoEADbContext`: único DbSet centralizado (una fuente de verdad)
- `*Repository`: cada feature tiene su repositorio (sigue Vertical Slice)
- `*Configuration`: mapeo EF por entidad (aislado, fácil de encontrar)
- Migrations: historial limpio, nombrado por cambio


---

## SOEA.Infrastructure.Excel

```
SOEA.Infrastructure.Excel/
├── LectorExcel.cs
├── LectorDisponibilidadDocente.cs
├── LectorInventarioEspacios.cs
├── ExcelRowMapper.cs              # Excel rows → Domain entities
├── ExcelDataValidator.cs
├── IExcelImporter.cs              # implements domain interface
├── VirtualRoomCleaner.cs          # depura salas virtuales de Admisiones
└── InstructorNameNormalizer.cs    # homogeniza usuario/cédula
```

---

## SOEA.Tests — xUnit + Vertical Slice

Tests organizados por capa y feature. Sigue la misma estructura de Vertical Slice.

```
SOEA.Tests/
├── Domain.Tests/
│   ├── Entities/
│   │   ├── AsignaturaEntityTests.cs
│   │   ├── DocenteEntityTests.cs
│   │   ├── EspacioEntityTests.cs
│   │   └── SesionEntityTests.cs
│   └── Enums/
│       ├── TipoAlternanciaTests.cs
│       └── TipoEspacioTests.cs
├── Application.Tests/
│   ├── Features/Asignaturas/
│   │   └── CreateAsignaturaServiceTests.cs
│   ├── Features/Docentes/
│   │   └── CreateDocenteServiceTests.cs
│   └── Features/Espacios/
│       └── CreateEspacioServiceTests.cs
├── Infrastructure.Tests/
│   ├── Repositories/
│   │   ├── AsignaturaRepositoryTests.cs
│   │   ├── DocenteRepositoryTests.cs
│   │   └── EspacioRepositoryTests.cs
│   └── DbContextTests.cs
├── Integration.Tests/
│   ├── Features/Asignaturas/
│   │   ├── CreateAsignaturaIntegrationTests.cs
│   │   └── GetAsignaturasIntegrationTests.cs
│   ├── Features/Docentes/
│   │   └── CreateDocenteIntegrationTests.cs
│   └── EndToEndScheduleTests.cs
└── Common/
    ├── TestFixtures.cs
    ├── DatabaseFixture.cs
    └── SeedTestData.cs
```

**Estructura de tests por capas:**
- **Domain.Tests**: invariantes, validaciones de entidades
- **Application.Tests**: servicios, orquestación
- **Infrastructure.Tests**: repositorio, EF Core
- **Integration.Tests**: API endpoint a BD (flujo completo)

