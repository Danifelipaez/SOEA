# Visión general de la arquitectura

## Propósito
Describir la arquitectura de alto nivel de SOEA para que Copilot y los colaboradores puedan
entender cómo interactúan las capas, qué tecnologías se usan y dónde ubicar nuevo código.

## Alcance
Arquitectura del backend, frontend, base de datos y puntos de integración. Los detalles de
implementación de bajo nivel pertenecen a la documentación específica de cada módulo.

---

## Estilo de arquitectura

SOEA usa **Clean Architecture** organizada como un **monolito modular .NET**.

- Una sola unidad desplegable (un único proceso de API)
- Límites internos impuestos por la separación de proyectos/ensamblados
- Sin microservicios: la simplicidad es intencional para un sistema a escala piloto
- Las dependencias fluyen hacia adentro: `WebApi → Application → Domain` (Infrastructure implementa las interfaces del dominio)

---

## Diagrama de capas

```
┌─────────────────────────────────────────────────────────┐
│                     SOEA.API                            │
│         (controladores ASP.NET Core, middleware)       │
└────────────────────────┬────────────────────────────────┘
                         │ calls
┌────────────────────────▼────────────────────────────────┐
│                 SOEA.Application                        │
│   (casos de uso, comandos, consultas, orquestación del pipeline)│
└───────┬────────────────┬────────────────────────────────┘
        │ domain         │ calls engines + infra (via interfaces)
        ▼                ▼
┌───────────────┐   ┌──────────────────────────────────────┐
│ SOEA.Domain   │   │ Capas de Infrastructure + Engine     │
│ (entidades,   │   │ ┌──────────────────────────────────┐ │
│  objetos de   │   │ │ SOEA.Infrastructure.Data (EF Core)│ │
│  valor,       │   │ │ SOEA.Infrastructure.Excel (EPPlus)│ │
│  interfaces)  │   │ │ SOEA.Engine.GraphColoring         │ │
└───────────────┘   │ │ SOEA.Engine.GraphColoring         │ │
                    │ │ SOEA.Engine.ConstraintProg        │ │
                    │ │ SOEA.Engine.Genetic               │ │
                    │ └──────────────────────────────────┘ │
                    └──────────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────┐
│          SQL Server / PostgreSQL database               │
└─────────────────────────────────────────────────────────┘
```

---

## Elecciones tecnológicas

| Aspecto | Tecnología | Justificación |
|---|---|---|
| Framework backend | ASP.NET Core (.NET 10) | Maduro, multiplataforma, con ecosistema robusto |
| ORM | Entity Framework Core | Reduce boilerplate y ofrece buen soporte para LINQ |
| Base de datos | SQL Server o PostgreSQL | El modelo relacional encaja con los datos de horarios |
| Ingesta de Excel | EPPlus | Nativo de .NET, sin dependencia de Office |
| Solucionador de restricciones | OR-Tools CP-SAT | Gratuito, probado y apto para problemas combinatorios grandes |
| Algoritmo genético | Implementación personalizada | Se ajusta al diseño de cromosoma y aptitud específico de SOEA |
| Frontend | Angular | Basado en TypeScript y con buena arquitectura de componentes para UI de horarios |
| Pruebas | xUnit | Marco de pruebas estándar de .NET |

---

## Pipeline de optimización

```
Excel Input → [Ingestion] → Domain Model → [Phase 1: Graph Coloring]
    → Horario Parcial → [Phase 2: CP-SAT] → Horario Factible
    → [Phase 3: Genetic Algorithm] → Horario Optimizado → JSON Output
```

Cada fase se implementa en su propio proyecto dentro de `src/`:
- `SOEA.Engine.GraphColoring` — Fase 1
- `SOEA.Engine.ConstraintProg` — Fase 2
- `SOEA.Engine.Genetic` — Fase 3

La capa Application orquesta el pipeline sin conocer los detalles de implementación.

---

## Decisiones de diseño clave

1. **Monolito sobre microservicios** — simplifica el despliegue para un equipo de TI universitario
2. **Proyectos de motor separados** — cada fase del algoritmo se puede probar de forma independiente
3. **Dependencia basada en interfaces** — Application llama a interfaces de motor; las implementaciones
  pueden intercambiarse (por ejemplo, reemplazar Genetic por Simulated Annealing en el futuro)
4. **JSON como salida canónica** — el horario se serializa a JSON para exportación,
  consumo del frontend y trazabilidad de auditoría

---

## Preguntas abiertas

- ¿Debería OR-Tools encapsularse en su propio proyecto (`SOEA.Engine.ConstraintProg`) o fusionarse
  dentro de `SOEA.Infrastructure`?
- ¿Se prefiere PostgreSQL sobre SQL Server para el entorno de producción?
