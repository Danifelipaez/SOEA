# Graph Report - soea-angular  (2026-06-02)

## Corpus Check
- 29 files · ~12,772 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 293 nodes · 410 edges · 22 communities (13 shown, 9 thin omitted)
- Extraction: 100% EXTRACTED · 0% INFERRED · 0% AMBIGUOUS
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `083efce7`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- [[_COMMUNITY_Community 0|Community 0]]
- [[_COMMUNITY_Community 1|Community 1]]
- [[_COMMUNITY_Community 2|Community 2]]
- [[_COMMUNITY_Community 3|Community 3]]
- [[_COMMUNITY_Community 4|Community 4]]
- [[_COMMUNITY_Community 5|Community 5]]
- [[_COMMUNITY_Community 6|Community 6]]
- [[_COMMUNITY_Community 7|Community 7]]
- [[_COMMUNITY_Community 8|Community 8]]
- [[_COMMUNITY_Community 9|Community 9]]
- [[_COMMUNITY_Community 10|Community 10]]
- [[_COMMUNITY_Community 11|Community 11]]
- [[_COMMUNITY_Community 12|Community 12]]
- [[_COMMUNITY_Community 13|Community 13]]
- [[_COMMUNITY_Community 14|Community 14]]
- [[_COMMUNITY_Community 15|Community 15]]
- [[_COMMUNITY_Community 16|Community 16]]
- [[_COMMUNITY_Community 17|Community 17]]
- [[_COMMUNITY_Community 18|Community 18]]

## God Nodes (most connected - your core abstractions)
1. `StateService` - 32 edges
2. `HorarioComponent` - 24 edges
3. `PersistenciaService` - 19 edges
4. `Espacio` - 15 edges
5. `Docente` - 15 edges
6. `Asignatura` - 14 edges
7. `Sesion` - 13 edges
8. `AsignaturasTabComponent` - 13 edges
9. `Programa` - 10 edges
10. `Facultad` - 8 edges

## Surprising Connections (you probably didn't know these)
- `GenerarHorarioResponse` --references--> `Sesion`  [EXTRACTED]
  src/app/core/horario-api.service.ts → src/app/core/models.ts
- `AsignaturaDialogComponent` --references--> `Programa`  [EXTRACTED]
  src/app/features/ingesta/asignaturas-tab/asignaturas-tab.component.ts → src/app/core/models.ts
- `MergedSesion` --references--> `Sesion`  [EXTRACTED]
  src/app/features/horario/horario.component.ts → src/app/core/models.ts

## Communities (22 total, 9 thin omitted)

### Community 0 - "Community 0"
Cohesion: 0.09
Nodes (14): all, f, Asignatura, CONFIGURACION_DEFECTO, ConfiguracionAlgoritmo, Facultad, Programa, ImportExcelStatsDto (+6 more)

### Community 1 - "Community 1"
Cohesion: 0.05
Nodes (39): build, serve, test, builder, configurations, defaultConfiguration, options, cli (+31 more)

### Community 2 - "Community 2"
Cohesion: 0.08
Nodes (16): AsignaturaApiDto, ConfiguracionAlgoritmoApiDto, DisponibilidadDiaDto, DocenteApiDto, EspacioApiDto, GenerarHorarioRequest, GenerarHorarioResponse, HorarioApiService (+8 more)

### Community 3 - "Community 3"
Cohesion: 0.08
Nodes (8): Docente, all, DocenteDialogComponent, DocentesTabComponent, f, GuardadoResultado, GuardadoResultadoDialogComponent, IngestaComponent

### Community 4 - "Community 4"
Cohesion: 0.09
Nodes (8): Espacio, PersistenciaService, all, EspacioDialogComponent, EspaciosTabComponent, f, GuardadoResultado, GuardadoResultadoDialogComponent

### Community 6 - "Community 6"
Cohesion: 0.10
Nodes (19): devDependencies, @angular/build, @angular/cli, @angular/compiler-cli, jsdom, prettier, typescript, vitest (+11 more)

### Community 7 - "Community 7"
Cohesion: 0.12
Nodes (17): dependencies, @angular/animations, @angular/cdk, @angular/common, @angular/compiler, @angular/core, @angular/forms, @angular/material (+9 more)

### Community 9 - "Community 9"
Cohesion: 0.27
Nodes (5): AppComponent, appConfig, routes, compiled, fixture

### Community 10 - "Community 10"
Cohesion: 0.24
Nodes (8): DashboardAdminComponent, data, espacios, horasAsignadas, labels, sesDoc, sesiones, slots

### Community 11 - "Community 11"
Cohesion: 0.20
Nodes (9): code:text (soea-angular/), code:bash (cd frontend && ng new soea-angular --routing --style=scss), Documentos relacionados, Estructura prevista (pendiente de generar), Integración con la API, Primeros pasos, Propósito, Resumen (+1 more)

## Knowledge Gaps
- **106 isolated node(s):** `$schema`, `version`, `packageManager`, `analytics`, `newProjectRoot` (+101 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **9 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `StateService` connect `Community 0` to `Community 10`, `Community 2`, `Community 3`, `Community 4`?**
  _High betweenness centrality (0.097) - this node is a cross-community bridge._
- **Why does `HorarioComponent` connect `Community 5` to `Community 0`, `Community 2`, `Community 3`?**
  _High betweenness centrality (0.060) - this node is a cross-community bridge._
- **Why does `PersistenciaService` connect `Community 4` to `Community 0`, `Community 2`, `Community 3`?**
  _High betweenness centrality (0.045) - this node is a cross-community bridge._
- **What connects `$schema`, `version`, `packageManager` to the rest of the system?**
  _106 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.08658536585365853 - nodes in this community are weakly interconnected._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.052564102564102565 - nodes in this community are weakly interconnected._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.08374384236453201 - nodes in this community are weakly interconnected._