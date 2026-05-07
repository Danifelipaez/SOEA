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
┌──────────────────────────────────────────────────────────────────────────────────────────┐
│                        Capa de Presentación — Frontend Angular                           │
│                                                                                          │
│  ┌───────────────────────────┐  ┌───────────────────────────┐  ┌──────────────────────┐  │
│  │       Módulo Admin        │  │     Módulo Profesor       │  │   Módulo Estudiante  │  │
│  │  espacios, ocupación      │  │  vista personalizada de   │  │  materias inscritas  │  │
│  │  global, edición manual   │  │  clases asignadas         │  │  docente, horario,   │  │
│  │  de horarios              │  │                           │  │  lugar               │  │
│  └───────────────────────────┘  └───────────────────────────┘  └──────────────────────┘  │
└─────────────────────────────────────────┬────────────────────────────────────────────────┘
                                          │ HTTP REST
                                          ▼
┌──────────────────────────────────────────────────────────────────────────────────────────┐
│                                      SOEA.API                                            │
│              Controllers  ·  Endpoints REST  ·  Program.cs (registro de servicios)       │
└──────┬───────────────────────────────────┬────────────────────────────────────────┬──────┘
       │ [DI]                              │                                   [DI] │
       │ AddScoped<ISessionRepository,     │                   AddScoped<IExcelImporter,
       │   SessionRepository>()            │                         ExcelImporter>()
       │                                   ▼                                        │
       │          ┌────────────────────────────────────────────────────────────┐    │
       │          │                   SOEA.Application                         │    │
       │          │  Orquestación · Casos de uso                               │    │
       │          │  Solo conoce interfaces, nunca implementaciones concretas  │    │
       │          └───────────┬─────────────────────┬──────────────┬───────────┘    │
       │                      │                     │              │                │
       │                      ▼                     ▼              ▼                │__________
       │  ┌────────────────────────┐  ┌────────────────────────┐  ┌────────────────────────┐  │
       │  │  SOEA.Engine           │  │  SOEA.Engine           │  │  SOEA.Engine           │  │
       │  │  .GraphColoring        │  │  .ConstraintProg       │  │  .Genetic              │  │
       │  │                        │  │                        │  │                        │  │
       │  │  Fase 1                │  │  Fase 2                │  │  Fase 3                │  │
       │  │  Conflictos docentes   │  │  Hard constraints      │  │  Soft constraints      │  │
       │  │  Coloración de grafos  ├─►│  Constraint            ├─►│  Algoritmo genético    │  │
       │  │  (determinístico)      │  │  Programming           │  │  Mutación +            │  │
       │  │  Sin deps. externas    │  │  OR-Tools (Google)     │  │  hibridación           │  │
       │  └──────────┬─────────────┘  └──────────┬─────────────┘  └──────────┬─────────────┘  │
       │             └────────────────────────────┼──────────────────────────┘                │_
       │                                          │                                            │
       │                                          ▼                                            │
       │  ┌────────────────────────────────────────────────────────────────────────────────┐   │
       │  │                         SOEA.Domain — Núcleo Puro                              │   │
       │  │                                                                                │   │
       │  │   Entidades · Reglas de negocio · Interfaces del dominio                       │   │
       │  │   (ISessionRepository, IExcelImporter, IHorarioRepository, etc.)               │   │
       │  │                                                                                │   │
       │  │   ✗  Sin dependencias externas. Ninguna flecha sale hacia afuera.             │   │
       │  └─────────────────────┬──────────────────────────────────┬───────────────────────┘   │
       │                        │  implementa interfaces            │  implementa interfaces   │
       │                        ▼                                   ▼                          │
       └───────►┌───────────────────────────────┐  ┌───────────────────────────────┐◄──────────┘
                │  SOEA.Infrastructure.Data     │  │  SOEA.Infrastructure.Excel    │
                │                               │  │                               │
                │  Implementa:                  │  │  Implementa IExcelImporter    │
                │  ISessionRepository           │  │                               │
                │  IHorarioRepository, etc.     │  │  Parsea el Excel de química   │
                │                               │  │  EPPlus · ClosedXML           │
                │  Entity Framework Core        │  │                               │
                │  PostgreSQL                   │  └───────────────────────────────┘
                └───────────────────────────────┘

                ┌────────────────────────────────────────────┐
                │               SOEA.Tests                   │- - - referencia para pruebas - - -►  SOEA.Application
                │                                            │                                      SOEA.Engine.*
                │  xUnit · Pruebas unitarias e integración   │
                │  por motor algorítmico                     │
                └────────────────────────────────────────────┘
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
Entrada Excel → [Ingesta] → Modelo de dominio → [Fase 1: coloreado de grafos]
  → Horario parcial → [Fase 2: CP-SAT] → Horario factible
  → [Fase 3: algoritmo genético] → Horario optimizado → salida JSON
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
