# SOEA — Estructura de Carpetas y Archivos

## Patrón: Clean Architecture

Este proyecto usa **Clean Architecture** organizada en capas concéntricas. Las dependencias siempre apuntan hacia adentro:

```
┌─ Capa de Presentación (API)
│  └─ Controllers/
│     └─ AsignaturasController.cs
│
├─ Capa de Aplicación (Application)
│  └─ Features/
│     └─ Asignaturas/
│        └─ CreateAsignaturaService.cs
│
├─ Capa de Dominio (Domain) ← Pura, sin dependencias externas
│  └─ Entities/
│     └─ Asignatura.cs
│
└─ Capa de Infraestructura (Infrastructure) ← Implementa contratos del Domain
   └─ Repositories/
      └─ AsignaturaRepository.cs
      └─ Configurations/
         └─ AsignaturaConfiguration.cs
```

**Flujo de dependencias (siempre hacia adentro):**
```
API → Application → Domain ← Infrastructure
       (usa)        (interfaces)  (implementa)
```

**Ventajas:**
- Aislamiento de reglas de negocio: Domain es independiente de frameworks
- Testabilidad: Application depende de interfaces, no de implementaciones
- Escalabilidad: cambiar EF Core o PostgreSQL no afecta Domain/Application
- Flexibilidad: Infrastructure es intercambiable

**Organización dentro de Application:**
- Features se organizan **por responsabilidad de negocio** (Asignaturas, Docentes, Espacios)
- Cada feature contiene servicios para operaciones relacionadas (Create, Get, Update, Delete)
- Las interfaces de dominio viven en `Interfaces/` (contratos que Infrastructure implementa)

**Regla de oro:** Si trabaja en Asignatura, toca:
- **Presentation (API):** `SOEA.API/Controllers/AsignaturasController.cs`
- **Application:** `SOEA.Application/Features/Asignaturas/` (servicios y DTOs)
- **Domain:** `SOEA.Domain/Entities/Asignatura.cs` (entidades y reglas)
- **Infrastructure:** `SOEA.Infrastructure.Data/Repositories/AsignaturaRepository.cs` + `Configurations/AsignaturaConfiguration.cs`

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

## SOEA.API — Capa de Presentación (Clean Architecture)

Punto de entrada HTTP. Expone endpoints REST que coordinan con Application.
No contiene lógica de negocio: solo valida entrada, delega a servicios, formatea respuesta.

```
SOEA.API/
├── Controllers/
│   ├── AsignaturasController.cs    # Endpoints de Asignatura
│   ├── DocentesController.cs       # Endpoints de Docentes
│   ├── EspaciosController.cs       # Endpoints de Espacios
│   ├── HorariosController.cs       # Endpoints de Horarios
│   └── AuthController.cs           # Autenticación
├── Middleware/
│   ├── GlobalExceptionHandlerMiddleware.cs
│   └── RequestLoggingMiddleware.cs
├── Dtos/                           # Data Transfer Objects (por feature)
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
├── Program.cs                      # Registro de dependencias (DI)
├── appsettings.json
└── appsettings.Development.json
```

**Flujo request-response:**
```
POST /api/asignaturas
  ↓ (API recibe request)
AsignaturasController.Create()
  ↓ (delega a Application)
CreateAsignaturaService.ExecuteAsync()
  ↓ (service usa Domain entity + Repository)
IAsignaturaRepository.AddAsync()
  ↓ (Infrastructure implementa)
AsignaturaRepository (EF Core)
  ↓
PostgreSQL
```

**Responsabilidad:** Validar entrada HTTP, no validar reglas de negocio (eso es Domain).


---

## SOEA.Application — Capa de Aplicación (Clean Architecture)

Orquestación de casos de uso. Coordina Domain entities, Infrastructure repositories e Engines.
**Nunca accede directamente** a EF Core, Excel, HTTP, etc. — depende de interfaces.

```
SOEA.Application/
├── Features/                      # Organización por responsabilidad de negocio
│   ├── Asignaturas/
│   │   ├── CreateAsignaturaService.cs
│   │   ├── GetAsignaturasService.cs
│   │   ├── UpdateAsignaturaService.cs
│   │   ├── DeleteAsignaturaService.cs
│   │   ├── Requests/              # DTOs de entrada
│   │   │   ├── CreateAsignaturaRequest.cs
│   │   │   ├── UpdateAsignaturaRequest.cs
│   │   │   └── GetAsignaturasQuery.cs
│   │   └── Responses/             # DTOs de salida
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
│   ├── Horarios/
│   │   ├── GenerateScheduleService.cs    # Orquesta los 3 Engines
│   │   ├── ValidateConstraintsService.cs
│   │   ├── Requests/
│   │   │   └── GenerateScheduleRequest.cs
│   │   └── Responses/
│   │       ├── ScheduleResponse.cs
│   │       └── InfeasibilityReportResponse.cs
│   └── Excel/
│       ├── ImportScheduleService.cs
│       └── Requests/
│           └── ImportExcelRequest.cs
├── Interfaces/                    # Contratos del Dominio (implementados por Infrastructure)
│   ├── IAsignaturaRepository.cs
│   ├── IDocenteRepository.cs
│   ├── IEspacioRepository.cs
│   ├── IHorarioRepository.cs
│   ├── IExcelImporter.cs
│   └── IRepository.cs             # Base interface
└── Common/
    ├── ApplicationException.cs
    └── Mappings/                  # AutoMapper o manual mapping
        └── MappingProfile.cs
```

**Por qué Features aquí:**
- Domain es **agnóstico a Features**: una Asignatura es lo mismo en cualquier caso de uso
- Application **agrupa servicios** relacionados (todos los servicios de Asignatura van en Asignaturas/)
- Separa concerns: cambiar lógica de Docentes no afecta a Asignaturas

**Regla:** Features en Application = **servicios de aplicación**, no entidades.
- Asignatura entity vive en Domain/Entities/Asignatura.cs (compartida por todos los servicios)
- CreateAsignaturaService, GetAsignaturasService, etc. viven en Application/Features/Asignaturas/


---

## SOEA.Domain — Capa de Dominio (Núcleo Puro de Clean Architecture)

Reglas de negocio independientes de cualquier framework o librería externa.
**Ninguna clase aquí depende de EF Core, ASP.NET, EPPlus, or-tools, etc.**

```
SOEA.Domain/
├── Entities/
│   ├── Sesion.cs          # Entity: representa una clase o actividad
│   ├── Espacio.cs         # Entity: salón, laboratorio, auditorio
│   ├── Docente.cs         # Entity: instructor
│   ├── Asignatura.cs      # Entity: materia del programa
│   ├── Horario.cs         # Entity: horario generado por optimización
│   ├── Programa.cs        # Entity: plan de estudios
│   └── DisponibilidadDocente.cs  # Entity: restricción de disponibilidad
├── ValueObjects/          # Objetos sin identidad, inmutables
│   ├── RangoTiempo.cs     # StartTime + EndTime
│   ├── Capacidad.cs       # cantidad (cantidad mínima/máxima)
│   ├── RestriccionesPesos.cs
│   └── EtiquetaSemestre.cs
├── Enums/
│   ├── TipoAlternancia.cs     # TypeA, TypeB, Normal
│   ├── TipoEspacio.cs         # Salon, Lab, Auditorio
│   ├── EstadoSesion.cs        # Pending, Assigned, Conflict
│   ├── EstadoHorario.cs       # Feasible, Infeasible, Partial
│   ├── Modalidad.cs           # Presencial, Virtual, Hibrido
│   ├── DiaDeSemana.cs         # Monday, Tuesday, ...
│   ├── FranjaHoraria.cs       # Mañana, Tarde, Noche
│   └── TipoRestriccion.cs     # Hard, Soft
├── Interfaces/                # Puertos (implementados por Infrastructure)
│   ├── IAsignaturaRepository.cs
│   ├── IDocenteRepository.cs
│   ├── IEspacioRepository.cs
│   ├── IHorarioRepository.cs
│   ├── IExcelImporter.cs
│   └── IUnitOfWork.cs
├── Exceptions/                # Excepciones de negocio
│   ├── OptimizationInfeasibleException.cs
│   ├── ConstraintViolationException.cs
│   ├── InvalidSessionException.cs
│   └── DomainException.cs (base)
└── Services/                  # Servicios de dominio (orquestación local)
    ├── ScheduleValidator.cs   # Valida reglas de horario
    └── ConstraintEvaluator.cs # Evalúa restricciones
```

**Principios:**
- Todas las reglas de negocio viven aquí (no en Application, no en Infrastructure)
- Si algo requiere validar un Espacio, la lógica de validación va aquí
- Excepciones de dominio heredan de `DomainException`
- Interfaces de repositorio se definen aquí; Infrastructure las implementa

**Ejemplo de límite limpio:**
```csharp
// ✅ BIEN: Validación de negocio en Entity
public class Sesion : AggregateRoot
{
    public void ValidarDuracion()
    {
        if (Duracion > MaximoDuracionSesion)
            throw new InvalidSessionException("Sesion muy larga");
    }
}

// ❌ MAL: Lógica de BD aquí
public class Sesion : AggregateRoot
{
    public void SaveToDatabase() { } // NO
}
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

## SOEA.Infrastructure.Data — Capa de Infraestructura (Clean Architecture)

Implementa los contratos del Dominio. Encapsula EF Core, PostgreSQL, y detalles técnicos.
**Application y Domain nunca conocen esta capa directamente.** Solo ven interfaces.

```
SOEA.Infrastructure.Data/
├── SoEADbContext.cs               # DbContext centralizado (una fuente de verdad)
├── Repositories/
│   ├── BaseRepository.cs          # Implementación base (CRUD común)
│   ├── AsignaturaRepository.cs    # Implementa IAsignaturaRepository
│   ├── DocenteRepository.cs       # Implementa IDocenteRepository
│   ├── EspacioRepository.cs       # Implementa IEspacioRepository
│   └── HorarioRepository.cs       # Implementa IHorarioRepository
├── Configurations/                # EF Fluent API (OnModelCreating)
│   ├── AsignaturaConfiguration.cs
│   ├── DocenteConfiguration.cs
│   ├── EspacioConfiguration.cs
│   ├── HorarioConfiguration.cs
│   ├── SesionConfiguration.cs
│   ├── ProgramaConfiguration.cs
│   └── DisponibilidadDocenteConfiguration.cs
├── Migrations/                    # EF Core auto-generated
│   ├── 001_InitialCreate.cs
│   ├── 002_AddAsignaturas.cs
│   └── ...
└── UnitOfWork.cs                  # Implementa IUnitOfWork (si se usa)
```

**Por qué esta estructura:**
- `SoEADbContext`: mapeo centralizado (una sola fuente de verdad de la BD)
- `BaseRepository`: eliminaoperaciones CRUD repetidas
- `*Repository`: cada repositorio implementa su interfaz de Domain
- `*Configuration`: mapeo por entidad (aislado, fácil de modificar sin afectar otros)
- Migrations: historial limpio de cambios de esquema

**Ejemplo de implementación (inyección de dependencia):**
```csharp
// En Program.cs
services.AddScoped<IAsignaturaRepository, AsignaturaRepository>();
// Application no sabe que AsignaturaRepository existe
// Solo sabe que existe IAsignaturaRepository
```

**Responsabilidad:** Traducir operaciones Domain/Application a llamadas EF Core.


---

## SOEA.Infrastructure.Excel — Capa de Infraestructura (Ingesta de Datos)

Implementa `IExcelImporter` (interfaz definida en Domain). Traduce filas de Excel a entidades de Domain.
Aislado: cambiar EPPlus a ClosedXML o CSV no afecta Application ni Domain.

```
SOEA.Infrastructure.Excel/
├── ExcelDataImporter.cs           # Implementa IExcelImporter
├── Readers/
│   ├── AsignaturasExcelReader.cs
│   ├── DocentesExcelReader.cs
│   ├── EspaciosExcelReader.cs
│   └── DisponibilidadExcelReader.cs
├── Mappers/
│   ├── ExcelRowToEntityMapper.cs   # Excel row → Domain entity
│   └── RowValidator.cs             # Valida filas antes de mapear
├── Processors/
│   ├── VirtualRoomCleaner.cs       # Limpia salas virtuales de Admisiones
│   ├── InstructorNameNormalizer.cs # Homogeniza usuario/cédula
│   └── CapacityCalculator.cs       # Deduce capacidad de espacio
└── Exceptions/
    └── ExcelImportException.cs
```

**Responsabilidad:** Leer Excel, validar formato, traducir a Domain entities.
**No conoce:** Servicios de Application, repositorios, lógica de horario.

---

## SOEA.Tests — Tests por Capa (Clean Architecture)

Cada capa tiene sus pruebas correspondientes. Las pruebas respetan los límites de Clean Architecture.

```
test/SOEA.Tests/
├── Unit/
│   ├── Domain/
│   │   ├── Entities/
│   │   │   ├── AsignaturaEntityTests.cs         # Invariantes: duración, código único
│   │   │   ├── DocenteEntityTests.cs
│   │   │   ├── SesionEntityTests.cs
│   │   │   └── EspacioEntityTests.cs
│   │   ├── ValueObjects/
│   │   │   ├── RangoTiempoTests.cs
│   │   │   └── CapacidadTests.cs
│   │   └── Enums/
│   │       ├── TipoAlternanciaTests.cs
│   │       └── TipoEspacioTests.cs
│   │
│   ├── Application/
│   │   ├── Features/Asignaturas/
│   │   │   ├── CreateAsignaturaServiceTests.cs  # Mock de IAsignaturaRepository
│   │   │   └── GetAsignaturasServiceTests.cs
│   │   ├── Features/Docentes/
│   │   │   └── CreateDocenteServiceTests.cs
│   │   └── Features/Horarios/
│   │       └── GenerateScheduleServiceTests.cs
│   │
│   └── Infrastructure/
│       ├── Repositories/
│       │   ├── AsignaturaRepositoryTests.cs     # In-memory EF Core
│       │   └── DocenteRepositoryTests.cs
│       └── Excel/
│           └── ExcelImporterTests.cs            # Mock de EPPlus
│
├── Integration/
│   ├── Controllers/
│   │   ├── AsignaturasControllerTests.cs        # API → Application → Infrastructure → BD
│   │   ├── DocentesControllerTests.cs
│   │   └── HorariosControllerTests.cs
│   └── Engine/
│       ├── GraphColoringEngineTests.cs
│       ├── ConstraintProgEngineTests.cs
│       └── GeneticEngineTests.cs
│
└── Common/
    ├── TestFixtures.cs                         # Shared test data
    ├── DatabaseFixture.cs                      # In-memory EF Core setup
    └── FakeRepositories.cs                     # Test doubles (mocks/stubs)
```

**Estrategia de testing por capa:**

| Capa | Tipo de test | Aislamiento | Ejemplo |
|------|-------------|-------------|---------|
| **Domain** | Unit | Total (sin dependencias) | `AsignaturaEntity.ValidarDuracion()` |
| **Application** | Unit | Parcial (mock repositories) | `CreateAsignaturaService` con `Mock<IAsignaturaRepository>` |
| **Infrastructure** | Unit | Parcial (in-memory BD) | `AsignaturaRepository` con `DbContext` en memoria |
| **API** | Integration | Completo (API → BD real/fake) | `POST /api/asignaturas` con `DatabaseFixture` |
| **Engines** | Integration | Parcial (entrada/salida) | `GenerateScheduleService` invocando 3 engines |

**Regla de oro:** 
- Tests de Domain no importan nada de Application, Infrastructure, ni Engines
- Tests de Application mockean todas las dependencias de Infrastructure
- Tests de Infrastructure usan EF Core en memoria
- Tests de API usan DatabaseFixture (BD real o in-memory)

---

## APÉNDICE: Clean Architecture vs Vertical Slice

Este documento describe **Clean Architecture**, no Vertical Slice Architecture. Es importante entender la diferencia:

### Clean Architecture (SOEA)
- **Organización:** Por **CAPAS concéntricas** (Domain, Application, Infrastructure, API)
- **Flujo de dependencias:** Siempre hacia adentro: `API → Application → Domain ← Infrastructure`
- **Features en Application:** Agrupan servicios relacionados, pero la entidad vive en Domain (compartida)
- **Testabilidad:** Interfaces en Domain; Application depende de abstracciones, no de implementaciones
- **Ventaja:** Aislamiento extremo del dominio; independencia de frameworks

**Diagrama:**
```
┌─────────────────────────────────┐
│      API (Controllers)          │  ← Capa externa
│  (requests/responses)           │
└────────────┬────────────────────┘
             │ depende
             ▼
┌─────────────────────────────────┐
│   Application (Services)        │  ← Casos de uso
│  (coordina, orquesta)           │
└────────────┬────────────────────┘
             │ depende
             ▼
┌─────────────────────────────────┐
│  Domain (Entities, Interfaces)  │  ← Núcleo puro
│  (reglas de negocio)            │
└────────────▲────────────────────┘
             │ implementa
             │
┌─────────────┴────────────────────┐
│     Infrastructure              │  ← Externa
│  (Repositories, EF Core)        │
└─────────────────────────────────┘
```

### Vertical Slice Architecture (NO es SOEA)
- **Organización:** Por **FEATURES** que cruzan todas las capas
- **Flujo:** Cada slice es independiente: `Feature A: API → Service → Domain → Repository`
- **Features:** Contienen todo: controller, service, entity, repository, DTOs
- **Testabilidad:** Cada feature es un módulo autocontenido
- **Ventaja:** Minimiza cambios entre features; fácil de replicar

**Diagrama:**
```
┌─────────────────────────┐  ┌─────────────────────────┐
│    Feature: Asignatura  │  │    Feature: Docente     │
├─────────────────────────┤  ├─────────────────────────┤
│ Controller              │  │ Controller              │
│ Service                 │  │ Service                 │
│ Entity                  │  │ Entity                  │
│ Repository              │  │ Repository              │
│ DTOs                    │  │ DTOs                    │
└─────────────────────────┘  └─────────────────────────┘
```

### Comparación: SOEA usa Clean Architecture

| Aspecto | Clean Arch (SOEA) | Vertical Slice |
|---------|------------------|-----------------|
| **Entity Asignatura** | Domain/Entities/ (compartida) | Features/Asignatura/Entities/ |
| **Service CreateAsignatura** | Application/Features/Asignaturas/ | Features/Asignatura/Services/ |
| **Repository** | Infrastructure.Data/Repositories/ | Features/Asignatura/Repositories/ |
| **Dependencias** | API → App → Domain ← Infra | Feature es autocontenido |
| **Cambio en Rule** | Afecta solo Domain | Afecta solo la Feature |
| **Compartir lógica** | Fácil (Domain es compartido) | Requiere extracción manual |

### Conclusión

SOEA **es Clean Architecture** porque:
1. ✅ Cada capa tiene responsabilidades claras y separadas
2. ✅ Domain es independiente de frameworks
3. ✅ Las dependencias siempre apuntan hacia adentro
4. ✅ Features en Application no son rodajas verticales, son contenedores lógicos de servicios
5. ✅ Las Entities viven en Domain y son compartidas por todos los servicios

**No es Vertical Slice** porque:
- ❌ Las entities no se replican por feature
- ❌ Los límites son POR CAPAS, no por features
- ❌ Infrastructure no está dentro de Features

