# SOEA — Sistema de Optimización de Espacios Académicos

SOEA es un sistema de horario académico universitario (UCTP) diseñado para automatizar y optimizar la programación académica de instituciones colombianas que siguen el modelo de alternancia (híbrido). Combina un backend basado en un **monolito modular con Clean Architecture en .NET** con un frontend en **Angular** y un motor de optimización de tres fases (Graph Coloring → Constraint Programming → Genetic Algorithm).

---

## Pila Tecnológica

| Capa | Tecnología |
|---|---|
| Backend | .NET 10, ASP.NET Core, Clean Architecture |
| ORM / Persistencia | Entity Framework Core, SQL Server / PostgreSQL |
| Ingesta de Excel | EPPlus |
| Motor de optimización | OR-Tools (CP-SAT), coloreado de grafos personalizado, algoritmo genético |
| Frontend | Angular (interfaz basada en roles) |
| Testing | xUnit |

---

## Estructura del Repositorio

```text
/
├── README.md                        ← estás aquí
├── SOEA.sln                         ← archivo de solución de .NET
│
├── docs/                            ← toda la documentación del proyecto (contexto de Copilot)
│   ├── requirements/                ← alcance, stakeholders, glosario, SRS
│   ├── business-rules/              ← alternancia, restricciones duras/blandas, límites del piloto
│   ├── architecture/                ← visión general de arquitectura, mapa de módulos, despliegue
│   ├── data/                        ← diccionario de datos, modelo ER, especificación JSON de salida
│   ├── algorithm/                   ← definición del problema UCTP + 3 fases del algoritmo
│   ├── testing/                     ← plan de pruebas y criterios de aceptación
│   ├── roadmap/                     ← plan de entrega y hitos
│   └── archive/                     ← documentos de referencia / origen
│
├── src/                             ← código fuente del backend (.NET)
│   ├── SOEA.Domain/                 ← entidades, objetos de valor, reglas de dominio
│   ├── SOEA.Application/            ← casos de uso, comandos, consultas, DTOs
│   ├── SOEA.Infrastructure.Data/    ← EF Core, acceso a BD, repositorios
│   ├── SOEA.Infrastructure.Excel/   ← ingesta de Excel con EPPlus
│   ├── SOEA.Engine.GraphColoring/   ← Fase 1: preasignación con coloreado de grafos
│   ├── SOEA.Engine.ConstraintProg/  ← Fase 2: solucionador de factibilidad OR-Tools CP-SAT
│   ├── SOEA.Engine.Genetic/         ← Fase 3: optimizador genético de restricciones blandas
│   └── SOEA.API/                    ← API web ASP.NET Core (controladores, middleware)
│
├── test/                            ← pruebas automatizadas
│   └── SOEA.Tests/                  ← pruebas unitarias e integración (xUnit)
│
└── frontend/                        ← frontend en Angular
    └── soea-angular/                ← espacio de trabajo Angular (interfaz de programación por roles)
```

---

## Archivos de documentación importantes

Empieza aquí cuando trabajes en una nueva funcionalidad o le pidas ayuda a Copilot:

| Tema | Archivo |
|---|---|
| Qué es el sistema y quién lo usa | [`docs/requirements/scope.md`](docs/requirements/scope.md) |
| Vocabulario del dominio | [`docs/requirements/glossary.md`](docs/requirements/glossary.md) |
| Restricciones duras de programación | [`docs/business-rules/hard-constraints.md`](docs/business-rules/hard-constraints.md) |
| Preferencias blandas / de optimización | [`docs/business-rules/soft-constraints.md`](docs/business-rules/soft-constraints.md) |
| Reglas de alternancia (Tipo A / B) | [`docs/business-rules/alternancia.md`](docs/business-rules/alternancia.md) |
| Arquitectura del sistema | [`docs/architecture/architecture-overview.md`](docs/architecture/architecture-overview.md) |
| Responsabilidades de los módulos | [`docs/architecture/module-map.md`](docs/architecture/module-map.md) |
| Problema de optimización (UCTP) | [`docs/algorithm/problem-definition-uctp.md`](docs/algorithm/problem-definition-uctp.md) |
| Campos de datos y significado | [`docs/data/data-dictionary.md`](docs/data/data-dictionary.md) |
| Formato de salida JSON | [`docs/data/json-output-spec.md`](docs/data/json-output-spec.md) |

---

## Responsabilidades de los módulos del backend

### `SOEA.Domain`
Conceptos centrales del negocio sin dependencias externas.
- Entidades: `Sesion`, `Grupo`, `Espacio`, `Docente`, `BloqueTiempo`, `Horario`
- Objetos de valor y enumeraciones: `TipoAlternancia`, `ConstraintWeight`, `EstadoSesion`
- Interfaces de dominio (puertos) implementadas por Infrastructure
- Invariantes de negocio (por ejemplo, una sesión no puede exceder su duración permitida)

### `SOEA.Application`
Capa de orquestación — coordina objetos de dominio e infraestructura.
- Casos de uso / controladores de comandos (por ejemplo, `GenerarHorarioCommand`, `ValidarRestriccionesQuery`)
- DTOs de entrada y salida
- Coordinación del pipeline de optimización (invoca las tres fases del motor en orden)
- Sin dependencia directa de EF Core, Excel ni HTTP

### `SOEA.Infrastructure.Data`
Implementación del acceso a datos.
- `DbContext` de EF Core y configuraciones de entidades
- Implementaciones de repositorios
- Migraciones de base de datos

### `SOEA.Infrastructure.Excel`
Ingesta de Excel mediante EPPlus.
- Lectores para malla curricular, disponibilidad de docentes y datos de espacios
- Mapeadores de filas de Excel a entidades de dominio

### `SOEA.Engine.GraphColoring`
Fase 1 del pipeline de optimización.
- Construye un grafo de conflictos a partir de los datos de las sesiones
- Asigna horarios preliminares usando heurísticas de coloreado de grafos
- Su salida alimenta la Fase 2

### `SOEA.Engine.ConstraintProg`
Fase 2 del pipeline de optimización.
- Usa OR-Tools CP-SAT para imponer todas las restricciones duras
- Devuelve un horario factible (no necesariamente óptimo)
- Su salida alimenta la Fase 3

### `SOEA.Engine.Genetic`
Fase 3 del pipeline de optimización.
- Algoritmo genético para optimizar restricciones blandas
- Cromosoma = asignación completa del horario
- Función de aptitud basada en violaciones ponderadas de restricciones blandas

### `SOEA.API`
Punto de entrada HTTP.
- Controladores ASP.NET Core y endpoints de Minimal API
- Middleware de autenticación y autorización basada en roles
- Modelos de solicitud/respuesta y documentación OpenAPI

---

## Responsabilidades del frontend (`frontend/soea-angular`)

SPA en Angular basada en roles:
- **Administrador**: configura espacios, carga datos de Excel y lanza la optimización
- **Coordinador**: revisa y valida los horarios generados
- **Docente / Estudiante**: ve su horario personal

---

## Cómo probar el proyecto desde la terminal

Puedes verificar y ejecutar el sistema localmente utilizando la CLI de .NET:

1. **Compilar la solución:**
   ```bash
   dotnet build
   ```

2. **Ejecutar las pruebas (xUnit):**
   ```bash
   dotnet test
   ```

3. **Ejecutar la API para verificación manual:**
   ```bash
   dotnet run --project src/SOEA.API/SOEA.API.csproj
   ```

4. **Ejecutar el pipeline de pruebas (ConsoleRunner):**
   ```bash
   dotnet run --project src/SOEA.ConsoleRunner/SOEA.ConsoleRunner.csproj
   ```

---

## Cómo usar este repositorio con Copilot

1. **Abre primero el documento relevante** antes de pedirle a Copilot que genere código.
2. **Referencia el documento en tu prompt**, por ejemplo:
    > "Usando `docs/business-rules/hard-constraints.md`, implementa el validador de restricciones duras en `SOEA.Engine.ConstraintProg`."
3. **Trabaja en pasos pequeños y enfocados**: un caso de uso, una entidad o una restricción a la vez.
4. **Mantén consistente la terminología del dominio** entre la documentación y el código (ver `docs/requirements/glossary.md`).
