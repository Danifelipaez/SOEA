# CLAUDE.md

## 1 — Identidad del proyecto

SOEA (Sistema de Optimización de Espacios Académicos) genera horarios semanales universitarios para instituciones con modelo de alternancia (presencial/virtual por semanas alternas). Dado un conjunto de asignaturas, docentes y espacios, ejecuta un pipeline de 3 fases de optimización y retorna un horario factible. La institución objetivo es la Universidad del Magdalena.

**Stack:** ASP.NET Core 10 Web API · PostgreSQL · Angular 21 · Google OR-Tools CP-SAT · Clean Architecture.

## 2 — Reglas de arquitectura (no negociables)

1. Dependencias fluyen: `API → Application → Domain ← Infrastructure / Engines`. Nunca al revés.
2. `SOEA.Domain` no importa nada externo: ni EF Core, ni OR-Tools, ni EPPlus, ni ASP.NET.
3. Toda nueva entidad sigue el orden: entidad en Domain → interfaz `IXRepositorio` en Domain → repositorio en Infrastructure.Data → configuración EF Core → registro en `Program.cs`.
4. Los motores (GraphColoring, ConstraintProg, Genetic) son stateless y solo dependen de Domain.
5. Nunca poner lógica de negocio en controllers ni en repositories.
6. La duración de cada sesión es un dato de entrada fijo — el algoritmo NO la modifica.
7. Las asignaturas Tipo A (8+8) son hard constraint — el algoritmo NO puede alterar su distribución.
8. El horario se genera desde cero en cada ejecución. Un horario base es un conjunto de restricciones de entrada (sesiones con franja/espacio predefinidos) que CP-SAT trata como hard constraints de igualdad — el algoritmo no itera sobre ellas sino que planifica el resto alrededor.
9. Sesión virtual = sincrónica online; se registra con la misma franja que su contraparte presencial. `EspacioId = null` en BD.
10. **Semana A es EL horario** (ALT-05): la franja y el aula de una sesión aplican a todas las semanas del semestre; solo la modalidad puede variar. La Semana B únicamente existe cuando la alternancia se activa, de forma reactiva, tras comprobar que no hay configuración válida por falta de aulas; contiene solo las sesiones emparejadas. Emparejar es el ÚNICO mecanismo que libera capacidad: una sesión que no alterna ocupa su aula en las dos semanas. Fuente única: `ModalidadSemanal`. Se persiste UNA fila `AsignacionSemanal` por sesión; la contraparte virtual se deriva al construir el DTO.

## 3 — Estado actual del proyecto

**Backend**
- [x] Entidades de dominio: `Asignatura`, `BloqueTiempo`, `Docente`, `Espacio`, `Facultad`, `Grupo`, `Horario`, `Programa`, `Sesion`
- [x] Interfaces de repositorio: `IAsignaturaRepositorio`, `IDocenteRepositorio`, `IEspacioRepositorio`, `IGrupoRepositorio`, `IHorarioRepositorio`, `ISesionRepositorio`, `IRepositorio<T>`
- [x] Interfaces de motor: `IMotorColoracionGrafo`, `IMotorConstraintProgramming`, `IMotorGenetico`, `IMotorOptimizacion`
- [x] Enums: `TipoAlternancia`, `TipoEspacio`, `Modalidad`, `DiaDeSemana`, `EstadoHorario`, `EstadoSesion`, `FranjaHoraria`, `TipoRestriccion`, `SemanaAcademica`, `PatronBaseAlternancia`, `TipoFlujo`, `CategoriaAsignatura`
- [x] Value Objects: `CodigoCohorte`, `CodigoEspacio`, `IntervaloTiempo`
- [x] Presencial-First Etapa 1 (datos) + lógica de motor (completada, ya no pendiente): `Sesion.TipoFlujo`/`PatronAlternanciaId?`/`Bloqueada`, `Asignatura.Categoria`/`HoraInicioMin?`/`HoraFinMax?` + migración `EtapaInicialPresencialFirst`. La lógica que los consume (`GenerarHorarioService.AplicarPrioridadPresencial`/`CederSiguienteCandidatoLab`, reversión post-Fase 3) está implementada — ver `docs/algorithms.md` sección Presencial-First
- [x] Presencial-First Etapa 2 (CR-02): `Sesion.DocenteId` nullable (docente opcional) + null-guards en motores/validador + migración `Etapa2DocenteOpcional`. **HC-I02 degradada**: la disponibilidad docente ya no es hard constraint de generación (Fase 2/Fase 3); solo preferencia blanda (SC-06)
- [x] Presencial-First Etapa 3 (CR-08 cerrado): **grupo/cohorte como eje** de conflicto y optimización. Cohorte implícita (un run = un grupo; `GrupoId` sintético por run). Fase 1 arista por `GrupoId`; Fase 2 **HC-C01** NoOverlap por `(grupo, semana)`; Fase 3 ergonomía por cohorte. HC-I01/HC-I03 fuera de generación; **docente fuera del pipeline** (se asigna después de generar). Sin migración (`grupo_id` ya existía). HU-04 (editar sesión) y multi-cohorte → etapas posteriores
- [x] Presencial-First Etapa 4 (CR-02 2º rol): **docente post-generación**. Mutador `Sesion.AsignarDocente(Guid?)` + `AsignarDocenteSesionService` (solape duro → 409; disponibilidad/carga → advertencias) + `PATCH /api/sesiones/{id}/docente` (nuevo `SesionesController`). Sin migración. 224/224 verde.
- [x] `SOEA.Engine.GraphColoring`: `AgendadorColoracionGrafo`, `ConstructorGrafoConflictos`
- [x] `SOEA.Engine.ConstraintProg`: `MotorConstraintProgramming` (OR-Tools CP-SAT, 120 s timeout)
- [x] `SOEA.Engine.Genetic`: `CromosomaHorario`, `EvaluadorFitness`, `MotorGenetico`, `OperadoresGeneticos` (200 gen, pop 50, convergencia 30)
- [x] `SOEA.Infrastructure.Data`: `SOEABdContext`, 9 configuraciones EF, 7 repositorios, **19 migraciones aplicadas** (`InitialCreate` → `FixMotivoConflictoColumnMapping`, 2026-08-07)
- [x] `SOEA.Infrastructure.Excel`: `LectorExcel` (3 modos: curriculum, modo2, disponibilidad)
- [x] `SOEA.Application`: `AsignaturaService` (CRUD completo, un solo servicio), `CrearSesionManualService`, `DocenteService`, `FusionDocentesService`, `CriterioCesionAlternanciaService`, `GenerarHorarioService`, `ImportarCurriculumService`, `AsignarDocenteSesionService`, `ReacomodarHorarioService`
- [x] `SOEA.API`: **9 controllers** (`AsignaturaController`, `CriteriosCesionAlternanciaController`, `DocentesController`, `EspaciosController`, `FacultadesController`, `GruposController`, `HorarioController`, `ImportController`, `ProgramasController`, `SesionesController`) — corrección: no existe `TiposAlternanciaController`; `TipoAlternanciaConfig` no tiene endpoint REST propio
- [x] Tests: arquitectura (NetArchTest), entidades de dominio, motores, value objects (~257 métodos de prueba en 30 archivos)
- [x] Validador post-generación de hard constraints (`ValidadorRestriccionesDuras` en Application, wired en `GenerarHorarioService` paso 4b — fallback a Fase 2 si el GA viola alguna de las **10 reglas** verificadas: HC-C01, HC-S01, HC-ALT, HC-BASE, HC-VH, HC-G01, HC-S03, HC-CAP, HC-S05, HC-SEP)
- [x] "Grupo como eje" completado a nivel de datos (migración `P1_GrupoComoEje`, 2026-08-06): `Grupo` absorbió `DocenteId` (desde `Fase2DocenteEnGrupo`) y `RequisitosEspacio` (reemplaza `Asignatura.EspacioFijoId`, ya eliminado)
- [x] Alternancia por parejas / "Tipo C" dinámico (migración `P2_ParejaAlternancia`, 2026-08-07): `Sesion.ParejaAlternanciaId` + hard constraint HC-ALT en CP-SAT y en el validador
- [ ] `PublicarHorarioService` — impide publicar con violaciones > 0
- [ ] Autenticación JWT y control de acceso por rol — sigue sin implementarse; la aplicación real (backend y frontend) es de un solo operador sin login, no de 4 roles

**Frontend** (`frontend/soea-angular`)
- [x] Rutas actuales, organizadas por journey (rediseño completo, ver `docs/DESIGN_BRIEF.md`): `/catalogo` (paso 1: preparar catálogo), `/horario` (pasos 2–3: generar y ajustar), `/revisar` (paso 4: KPIs), `/publicar` (paso 5, bloqueado). **Las rutas antiguas (`/ingesta`, `/dashboard-admin`, `/dashboard-developer`, `/horario-docente`, `/tipos-alternancia`, `/configuracion-alternancia`) ya no existen** — este archivo las listaba desactualizadas.
- [x] `StateService` (signals reactivos), `PersistenciaService`, `CatalogoService` (hidratación inicial), `HorarioApiService`
- [x] Tabs en `/catalogo`: Asignaturas (con Grupos como fila expandible, no pestaña propia), Docentes, Espacios, Alternancia
- [x] Carga de Excel en `/catalogo`
- [ ] Vista de reporte de conflictos
- [ ] Control de acceso por rol en UI — no aplica: no hay roles ni login en el diseño actual (un solo operador)

## 4 — Datos bloqueantes

El agente NO debe asumir ni inventar estos valores — provienen de Rosa (coordinadora académica):

- Capacidad exacta de cada espacio (laboratorio, salón, auditorio)
- Clasificación de prioridad de cursos (1 / 2 / 3)
- Lista definitiva de asignaturas Tipo A y Tipo B
- Duración fija por asignatura (horas por sesión y sesiones por semana)

## 5 — Convenciones de código

- **Idioma:** clases y archivos en inglés; comentarios y docs en español
- **Enums clave:** `TipoAlternancia { TipoA, TipoB, SinAlternancia }` · `TipoEspacio { Salon, Laboratorio, Auditorio }` · `TipoFlujo { Laboratorio, AulaVirtual }` · `CategoriaAsignatura { Obligatoria, Optativa, Electiva }`
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

PostgreSQL `localhost:5432`, DB `SOEAdb`. Configuraciones EF en `SOEA.Infrastructure.Data/Configurations/`.

**Cadena de conexión (sin credenciales en el repo — P0.1 auditoría):**
- `src/SOEA.API/appsettings.json` tiene `DefaultConnection` vacío.
- En desarrollo la cadena real vive en `src/SOEA.API/appsettings.Development.json` (gitignored). Crear ese archivo localmente con `ConnectionStrings:DefaultConnection`.
- Las migraciones design-time (`SOEABdContextFactory`) resuelven la cadena desde la variable de entorno `SOEA_DESIGN_TIME_DB` o, en su defecto, desde el `appsettings.Development.json` del API.
- En staging/producción usar variables de entorno o user-secrets, nunca un archivo commiteado.

**Motor CP-SAT:** la sección `CpSat` de configuración controla `ExportarModelo` (volcado de `cp_model_debug.txt`, default `false`) y `TimeoutSegundos` (default 120).

`ILectorExcel` expone tres métodos:
- `LeerCurriculumAsync` — columnas detectadas por cabecera (no por posición): Facultad, Programa, Asignatura, Código (opcional), TipoEspacio (opcional), Espacio/Curso/Salón/Aula, Duración/Horas/Reales [h], Día, Hora, Docente, Grupo (opcional, número real de grupo/sección), Final (opcional, hora de fin explícita — si no viene, se deriva de Hora+Duración). Acepta tanto el formato legado (A–J fijo) como el formato real de Rosa (Facultad, Programa, Asignatura, Grupo, Docente, Reales [h], Espacio, Dia, Hora, Final).
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
