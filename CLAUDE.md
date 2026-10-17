# CLAUDE.md

## 1 — Identidad del proyecto

SOEA (Sistema de Optimización de Espacios Académicos) genera horarios semanales universitarios con alternancia presencial/virtual por semanas para la Universidad del Magdalena. A partir de asignaturas, grupos, docentes y espacios, un pipeline de 3 fases (coloración de grafos → CP-SAT → algoritmo genético) produce un horario que cumple todas las restricciones duras.

**Stack:** ASP.NET Core 10 Web API · PostgreSQL · Angular 21 · Google OR-Tools CP-SAT · Clean Architecture.

**Documentación** (`docs/`, verificada contra el código el 2026-10-14): [README](docs/README.md) · [architecture](docs/architecture.md) · [domain](docs/domain.md) (entidades, reglas HC/SC/ALT, esquema) · [algorithms](docs/algorithms.md) · [frontend](docs/frontend.md) · [operacion](docs/operacion.md) (tests, migraciones, CI/CD, Azure) · [pendientes](docs/pendientes.md). Si cambias el comportamiento que describe un doc, actualízalo en el mismo cambio.

## 2 — Reglas de arquitectura y dominio (no negociables)

1. Dependencias: `API → Application → Domain ← Infrastructure / Engines`. Nunca al revés (lo verifican `ArchitectureTests`).
2. `SOEA.Domain` no importa nada externo: ni EF Core, ni OR-Tools, ni EPPlus, ni ASP.NET.
3. Nueva entidad: entidad en Domain → `IXRepositorio` en Domain → repositorio en Infrastructure.Data → configuración EF → registro en `Program.cs` → migración (skill `/add-entity`).
4. Los motores (GraphColoring, ConstraintProg, Genetic) son stateless y solo dependen de Domain. Las reglas compartidas viven en `SOEA.Domain/Services` (`CalculadorDominioSesion`, `CalculadorEspaciosSesion`, `ModalidadSemanal`…): no dupliques una regla en un motor.
5. Nunca lógica de negocio en controllers ni en repositories.
6. La duración de cada sesión es un dato de entrada fijo: el algoritmo NO la modifica.
7. **El grupo es el eje.** Jerarquía obligatoria Sesión → Grupo → Asignatura → Programa → Facultad. Cada grupo tiene su propio `GrupoId` y el no-solape (HC-C01), la disponibilidad (HC-G01) y la ergonomía se aplican por grupo. **El docente está fuera de la generación**: se asigna después y solo genera avisos (el choque de docente es duro únicamente al editar).
8. El horario se genera desde cero en cada ejecución. Un horario base (sesiones con franja/aula predefinidas) son restricciones de igualdad en CP-SAT: se planifica el resto alrededor.
9. Sesión virtual = sincrónica online, con la misma franja que su contraparte presencial. `EspacioId = null`.
10. **La Semana A es EL horario** (ALT-05): franja y aula valen para todo el semestre; solo puede variar la modalidad. La Semana B solo existe cuando, tras comprobar que no hay configuración válida por falta de aulas, el generador empareja sesiones. Emparejar es el ÚNICO mecanismo que libera capacidad; una sesión que no alterna ocupa su aula en las dos semanas. Nunca se virtualiza una sesión en silencio: si no cabe, la generación falla explicando por qué. Fuente única: `ModalidadSemanal`. Se persiste UNA fila `AsignacionSemanal` por sesión; la contraparte virtual se deriva al construir el DTO.
11. TipoA/TipoB solo indican en qué semana es presencial una sesión. La antigua regla "8+8 inmodificable" (HC-SU01) es obsoleta.

## 3 — Estado actual (2026-10-14)

**Hecho:** dominio completo (13 entidades, 28 migraciones hasta `M19_JerarquiaSesionGrupoAsignatura`, aplicadas solas al arrancar el API); 3 motores; validador post-generación de 10 reglas duras con fallback a Fase 2; Semana B reactiva por parejas; reacomodo mínimo al editar; sesión manual; asignación de docente posterior; importación de Excel en dos pasos con revisión de filas; fusión de docentes duplicados; 10 controllers; frontend por recorrido (`/catalogo`, `/horario`, `/revisar`, `/publicar` bloqueado); CI en GitHub Actions y despliegue automático a Azure al hacer push a `main`. Pruebas: ~500 backend (20 contra PostgreSQL real) y ~150 frontend.

**Falta** (detalle en [docs/pendientes.md](docs/pendientes.md)):
- `PublicarHorarioService` (impedir publicar con violaciones > 0) y la vista de reporte de conflictos.
- **La API está expuesta sin autenticación.** No hay login ni roles (un solo operador). Hasta decidir cómo restringir el acceso (lista de IP o Easy Auth) y activar `httpsOnly`, no tratarla como un servicio protegido.

## 4 — Datos bloqueantes

No asumir ni inventar estos valores; los define Rosa (coordinadora académica):

- Capacidad exacta de cada espacio (laboratorio, salón, auditorio).
- Clasificación de prioridad de cursos (1 / 2 / 3).
- Lista definitiva de asignaturas Tipo A y Tipo B.
- Duración fija por asignatura (horas por sesión y sesiones por semana).
- Rango de la jornada (hoy L–V 06:00–22:00, sábado 06:00–14:00) y la semántica de HC-ALT (marcada `VERIFICA`).

## 5 — Convenciones

- **Idioma:** identificadores en español o inglés según el código vecino (dominio en español); comentarios, docs y textos de UI en español.
- **Textos de UI:** los usuarios son coordinadores no técnicos; nada de jerga ("fallback", "CP-SAT", "hard constraint").
- **Enums clave:** `TipoAlternancia { TipoA, TipoB, SinAlternancia }` · `TipoEspacio { Salon, Laboratorio, Auditorio }` · `TipoSesion { TeoriaPresencial, TeoriaVirtual, Laboratorio }` · `TipoFlujo { Laboratorio, AulaVirtual }` · `CategoriaAsignatura { Obligatoria, Optativa, Electiva }`.
- **Errores de API:** lanzar excepciones que `GlobalExceptionHandler` traduce a ProblemDetails en español; no devolver `BadRequest("…")` desde controllers.
- **Tests:** xUnit con dobles a mano en `test/SOEA.Tests/Fakes/RepositorioFakes.cs` (no NSubstitute, no `TestData/`); PostgreSQL real en `test/SOEA.Tests/Integracion`. Frontend con vitest.
- **DI:** cada proyecto de infraestructura/motor expone `AddX()`. Application registra servicios concretos con `AddScoped<ConcreteService>()`, sin interfaz.
- **Migraciones:** nombre `M<n>_<Descripción>`; si agregan una FK, sanean antes los huérfanos.

## Comandos

```powershell
# Backend (raíz de la solución)
dotnet build SOEA.sln
dotnet run --project src/SOEA.API          # API → http://localhost:5066
dotnet test SOEA.sln                        # las pruebas de Postgres se omiten sin SOEA_TEST_DB; con ella
                                           # (servidor + credenciales; crea y borra BDs soea_it_*, nunca toca SOEAdb)
                                           # deben dar "Omitido: 0", y fallan si el servidor no responde.
dotnet test --filter "FullyQualifiedName~Architecture"

# Migraciones (desde src/SOEA.Infrastructure.Data)
dotnet ef migrations add <Nombre> --startup-project ../SOEA.API
dotnet ef database update --startup-project ../SOEA.API

# Frontend (desde frontend/soea-angular)
npm install
npm start        # → http://localhost:4200
npm run build
npm test         # vitest
```

## Base de datos y configuración

- PostgreSQL `localhost:5432`, BD `SOEAdb`. Configuraciones EF en `src/SOEA.Infrastructure.Data/Configurations/`.
- Sin credenciales en el repo: `appsettings.json` tiene `DefaultConnection` vacío; en desarrollo la cadena vive en `src/SOEA.API/appsettings.Development.json` (gitignored). Las migraciones design-time la toman de `SOEA_DESIGN_TIME_DB` o de ese archivo. En producción, variables de entorno de la App Service.
- Sección `CpSat`: `TimeoutSegundos` (120), `ClasificarInfactibilidadEspacio` (true), `SweepGrupos` (false; `true` en prod), `ExportarModelo` (false). Detalle en [docs/architecture.md](docs/architecture.md#configuración-appsettings--variables-de-entorno).
- Importación de Excel (dos pasos, columnas por cabecera, errores vs. avisos): [docs/frontend.md](docs/frontend.md#importación-de-excel-dos-pasos).

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).

<!-- code-review-graph MCP tools -->
## MCP Tools: code-review-graph

**IMPORTANT: This project has a knowledge graph. ALWAYS use the
code-review-graph MCP tools BEFORE using Grep/Glob/Read to explore
the codebase.** The graph is faster, cheaper (fewer tokens), and gives
you structural context (callers, dependents, test coverage) that file
scanning cannot.

### When to use graph tools FIRST

- **Exploring code**: `semantic_search_nodes` or `query_graph` instead of Grep
- **Understanding impact**: `get_impact_radius` instead of manually tracing imports
- **Code review**: `detect_changes` + `get_review_context` instead of reading entire files
- **Finding relationships**: `query_graph` with callers_of/callees_of/imports_of/tests_for
- **Architecture questions**: `get_architecture_overview` + `list_communities`

Fall back to Grep/Glob/Read **only** when the graph doesn't cover what you need.

### Key Tools

| Tool | Use when |
| ------ | ---------- |
| `detect_changes` | Reviewing code changes — gives risk-scored analysis |
| `get_review_context` | Need source snippets for review — token-efficient |
| `get_impact_radius` | Understanding blast radius of a change |
| `get_affected_flows` | Finding which execution paths are impacted |
| `query_graph` | Tracing callers, callees, imports, tests, dependencies |
| `semantic_search_nodes` | Finding functions/classes by name or keyword |
| `get_architecture_overview` | Understanding high-level codebase structure |
| `refactor_tool` | Planning renames, finding dead code |

### Workflow

1. The graph auto-updates on file changes (via hooks).
2. Use `detect_changes` for code review.
3. Use `get_affected_flows` to understand impact.
4. Use `query_graph` pattern="tests_for" to check coverage.
