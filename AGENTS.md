# Guía del agente SOEA

Guía para cualquier agente de programación en SOEA. Las reglas completas y los comandos están en [CLAUDE.md](CLAUDE.md); la documentación técnica, en [docs/README.md](docs/README.md). Mantén los cambios pequeños y enlaza la documentación en lugar de repetirla.

## Dónde mirar

- Qué proyecto es dueño de un cambio, API y configuración: [docs/architecture.md](docs/architecture.md)
- Términos, entidades y reglas HC/SC/ALT: [docs/domain.md](docs/domain.md)
- Pipeline de generación: [docs/algorithms.md](docs/algorithms.md)
- Frontend e importación de Excel: [docs/frontend.md](docs/frontend.md)
- Pruebas, migraciones y despliegue: [docs/operacion.md](docs/operacion.md)

## Reglas de trabajo

- Lógica de dominio en `SOEA.Domain`, orquestación en `SOEA.Application`, integraciones en su proyecto de infraestructura o motor. No cruces capas para facilitar un cambio.
- El grupo es el eje del modelo; el docente queda fuera de la generación y solo produce avisos.
- Las sesiones virtuales no ocupan aula (`EspacioId = null`). La Semana A es el horario; la B solo existe para sesiones emparejadas por falta de aulas.
- `TipoAlternancia` canónico: `TipoA`, `TipoB`, `SinAlternancia`.
- No inventes datos institucionales (capacidades, duraciones, listas Tipo A/B): ver los datos bloqueantes en CLAUDE.md.
- Si cambias un comportamiento documentado, actualiza el doc correspondiente en el mismo cambio.

## Validación

- `dotnet build SOEA.sln` y `dotnet test SOEA.sln` (las pruebas de PostgreSQL necesitan `SOEA_TEST_DB`).
- Frontend: `npm test` y `npm run build` en `frontend/soea-angular`.

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
