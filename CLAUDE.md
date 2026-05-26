# CLAUDE.md

## 1 — Identidad del proyecto

SOEA (Sistema de Optimización de Espacios Académicos) genera horarios semanales universitarios para instituciones con modelo de alternancia (presencial/virtual por semanas alternas). Dado un conjunto de asignaturas, docentes y espacios, ejecuta un pipeline de 3 fases de optimización y retorna un horario factible. El piloto objetivo son los laboratorios de química de la Universidad del Magdalena.

**Stack:** ASP.NET Core 10 Web API · PostgreSQL · Angular 21 · Google OR-Tools CP-SAT · Clean Architecture.

## 2 — Reglas de arquitectura (no negociables)

1. Dependencias fluyen: `API → Application → Domain ← Infrastructure / Engines`. Nunca al revés.
2. `SOEA.Domain` no importa nada externo: ni EF Core, ni OR-Tools, ni EPPlus, ni ASP.NET.
3. Toda nueva entidad sigue el orden: entidad en Domain → interfaz `IXRepositorio` en Domain → repositorio en Infrastructure.Data → configuración EF Core → registro en `Program.cs`.
4. Los motores (GraphColoring, ConstraintProg, Genetic) son stateless y solo dependen de Domain.
5. Nunca poner lógica de negocio en controllers ni en repositories.
6. La duración de cada sesión es un dato de entrada fijo — el algoritmo NO la modifica.
7. Las asignaturas Tipo A (8+8) son hard constraint — el algoritmo NO puede alterar su distribución.
8. El horario se genera desde cero en cada ejecución — nunca se itera sobre un horario existente.
9. Sesión virtual = sincrónica online; se registra con la misma franja que su contraparte presencial. `EspacioId = null` en BD.

## 3 — Estado actual del proyecto

**Backend**
- [x] Entidades de dominio: `Asignatura`, `BloqueTiempo`, `Docente`, `Espacio`, `Facultad`, `Grupo`, `Horario`, `Programa`, `Sesion`
- [x] Interfaces de repositorio: `IAsignaturaRepositorio`, `IDocenteRepositorio`, `IEspacioRepositorio`, `IGrupoRepositorio`, `IHorarioRepositorio`, `ISesionRepositorio`, `IRepositorio<T>`
- [x] Interfaces de motor: `IMotorColoracionGrafo`, `IMotorConstraintProgramming`, `IMotorGenetico`, `IMotorOptimizacion`
- [x] Enums: `TipoAlternancia`, `TipoEspacio`, `Modalidad`, `DiaDeSemana`, `EstadoHorario`, `EstadoSesion`, `FranjaHoraria`, `TipoRestriccion`
- [x] Value Objects: `CodigoCohorte`, `CodigoEspacio`, `IntervaloTiempo`
- [x] `SOEA.Engine.GraphColoring`: `AgendadorColoracionGrafo`, `ConstructorGrafoConflictos`
- [x] `SOEA.Engine.ConstraintProg`: `MotorConstraintProgramming` (OR-Tools CP-SAT, 120 s timeout)
- [x] `SOEA.Engine.Genetic`: `CromosomaHorario`, `EvaluadorFitness`, `MotorGenetico`, `OperadoresGeneticos` (200 gen, pop 50, convergencia 30)
- [x] `SOEA.Infrastructure.Data`: `SOEABdContext`, 9 configuraciones EF, 7 repositorios, 5 migraciones aplicadas
- [x] `SOEA.Infrastructure.Excel`: `LectorExcel` (3 modos: curriculum, modo2, disponibilidad)
- [x] `SOEA.Application`: `GenerarHorarioService`, CRUD completo de asignaturas
- [x] `SOEA.API`: 5 controllers (`AsignaturaController`, `DocentesController`, `EspaciosController`, `HorarioController`, `ImportController`)
- [x] Tests: arquitectura (NetArchTest), entidades de dominio, motores, value objects
- [ ] Validador post-generación de hard constraints (Application layer)
- [ ] `PublicarHorarioService` — impide publicar con violaciones > 0
- [ ] Autenticación JWT y control de acceso por rol (Admin / Coordinador / Docente / Estudiante)

**Frontend** (`frontend/soea-angular`)
- [x] Rutas: `/ingesta`, `/horario`, `/dashboard-admin`, `/dashboard-developer`
- [x] `StateService` (estado global en memoria), `HorarioApiService`, `PersistenciaService`
- [x] Tabs en `/ingesta`: Asignaturas, Docentes, Espacios
- [ ] Carga de Excel en pestaña Ingesta
- [ ] Vista de reporte de conflictos
- [ ] Control de acceso por rol en UI

## 4 — Datos bloqueantes

El agente NO debe asumir ni inventar estos valores — provienen de Rosa (coordinadora académica):

- Capacidad exacta de cada laboratorio de química
- Clasificación de prioridad de cursos (1 / 2 / 3)
- Lista definitiva de asignaturas Tipo A y Tipo B
- Duración fija por asignatura (horas por sesión y sesiones por semana)

## 5 — Convenciones de código

- **Idioma:** clases y archivos en inglés; comentarios y docs en español
- **Enums clave:** `TipoAlternancia { TipoA, TipoB, SinAlternancia }` · `TipoEspacio { Salon, Laboratorio, Auditorio }`
- **Tests:** xUnit · NSubstitute para mocks · datos de prueba en `TestData/`
- **DI:** cada proyecto de infraestructura/motor expone `AddX()`. Application registra servicios concretos con `AddScoped<ConcreteService>()` — sin interfaz adicional.
- **Docs:** `docs/architecture.md`, `docs/domain.md`, `docs/algorithms.md`

## Comandos

```powershell
# Backend (raíz de la solución)
dotnet build SOEA.sln
dotnet run --project src/SOEA.API          # API → http://localhost:5066
dotnet test SOEA.sln
dotnet test --filter "FullyQualifiedName~Architecture"
dotnet test --filter "DisplayName~BloqueTiempo"

# Migraciones (desde src/SOEA.Infrastructure.Data)
dotnet ef migrations add <Nombre> --startup-project ../SOEA.API
dotnet ef database update --startup-project ../SOEA.API

# Frontend (desde frontend/soea-angular)
npm install
npm start        # → http://localhost:4200
npm run build
npm test         # vitest
```

## Base de datos

PostgreSQL `localhost:5432`, DB `SOEAdb`. Connection string en `src/SOEA.API/appsettings.json` y en `SOEABdContextFactory` para design-time migrations. Configuraciones EF en `SOEA.Infrastructure.Data/Configurations/`.

`ILectorExcel` expone tres métodos:
- `LeerCurriculumAsync` — cols A–J: Facultad, Programa, Asignatura, Código, TipoEspacio, Espacio, Duración, Día, Hora, Docente.
- `LeerAsignaturasModo2Async` — cols A–H (sin Día/Hora).
- `LeerDisponibilidadDocentesAsync` — cols: Docente, Correo, MaxHoras, Días, Franjas.

## Architecture tests

`test/SOEA.Tests/Architecture/ArchitectureTests.cs` usa NetArchTest para verificar:
- Domain no depende de Application, Infrastructure ni API.
- Application no depende de Infrastructure.
- Infrastructure no depende de API.
- Repositorios residen en `SOEA.Infrastructure.Data.Repositories`.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
