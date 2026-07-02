# Guía del agente SOEA

Usa esta guía del workspace para cualquier tarea de programación en SOEA. Mantén los cambios pequeños, sigue la arquitectura documentada y enlaza la documentación del proyecto en lugar de repetirla.

## Empezar aquí

- Usa [README.md](README.md) para ver la descripción general del proyecto y la estructura del repositorio.
- Usa [docs/architecture/module-map.md](docs/architecture/module-map.md) para decidir qué proyecto es el dueño de un cambio.
- Usa [docs/architecture/architecture-overview.md](docs/architecture/architecture-overview.md) para entender la estructura del sistema.
- Usa [docs/requirements/glossary.md](docs/requirements/glossary.md) para mantener consistentes los términos del dominio.

## Modos de Ingesta de Información

El proyecto está diseñado para soportar 3 vías principales de entrada de datos:
1. **Frontend en Angular** (Futuro): La vía principal, donde se ingresará materia por materia sin especificar horario, y la disponibilidad de los docentes, para que el algoritmo arme el horario.
2. **Excel de Horario Funcional** (Opción 1): Se lee un Excel con un horario ya construido (Malla completa) para validación o optimización.
3. **Excels Separados** (Opciones 2 y 3): Funciona igual que el frontend pero cargando la información desde 2 archivos: un Excel con las Asignaturas/Carga (sin horas asignadas) y otro Excel con la Disponibilidad de los Docentes.

## Reglas de trabajo

- Mantén la lógica de dominio en `SOEA.Domain`; la orquestación en `SOEA.Application`; las integraciones en el proyecto de infraestructura o motor correspondiente.
- No cruces límites de capa solo para hacer un cambio más fácil. Si una dependencia parece incorrecta, mueve la lógica al proyecto dueño.
- Prefiere las reglas de negocio documentadas frente a suposiciones, especialmente para alternancia, asignación de espacios y restricciones de programación.
- No persistas sesiones virtuales como filas de espacio físico. Las sesiones virtuales se modelan con valores nulos en el espacio.
- Trata `AlternanciaType` como el conjunto canónico del dominio: `TypeA`, `TypeB` y `NonAlternating`.

## Antes de editar código

- Lee primero el documento más relevante:
  - [docs/business-rules/hard-constraints.md](docs/business-rules/hard-constraints.md)
  - [docs/business-rules/soft-constraints.md](docs/business-rules/soft-constraints.md)
  - [docs/business-rules/alternancia.md](docs/business-rules/alternancia.md)
  - [docs/algorithm/problem-definition-uctp.md](docs/algorithm/problem-definition-uctp.md)
  - [docs/data/data-dictionary.md](docs/data/data-dictionary.md)
  - [docs/data/json-output-spec.md](docs/data/json-output-spec.md)

## Validación

- Usa `dotnet build` para comprobar la solución.
- Usa `dotnet test` para el proyecto de pruebas xUnit en `test/SOEA.Tests`.
- Para trabajo de API, ejecuta `dotnet run --project src/SOEA.API/SOEA.API.csproj` cuando sea útil una verificación manual.

## Frontend

- El trabajo de frontend vive en [frontend/soea-angular/README.md](frontend/soea-angular/README.md).
- Mantén los cambios de frontend dentro del workspace de Angular y no los mezcles con proyectos de backend.

## Enfoque de pruebas

- Prioriza pruebas que correspondan a la capa afectada: invariantes del dominio, orquestación de la aplicación, comportamiento del motor o integración de API.
- Revisa [docs/testing/test-plan.md](docs/testing/test-plan.md) y [docs/testing/acceptance-criteria.md](docs/testing/acceptance-criteria.md) cuando un cambio afecte el comportamiento, la validación o una salida visible para el usuario.

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
