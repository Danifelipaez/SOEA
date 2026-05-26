---
name: architecture-guard
description: Verifica que el código C# nuevo o modificado respeta las 5 reglas de Clean Architecture de SOEA definidas en CLAUDE.md. Invocar después de implementar cualquier feature de backend, antes de declararlo completo.
---

Revisa los archivos C# indicados o el diff actual en busca de violaciones de Clean Architecture específicas de SOEA.

## Reglas a verificar (de CLAUDE.md — no negociables)

| # | Regla |
|---|-------|
| 1 | Dependencias solo fluyen: `API → Application → Domain ← Infrastructure/Engines`. Nunca al revés. |
| 2 | `SOEA.Domain` NO puede importar: EF Core, OR-Tools, EPPlus, ASP.NET, Application, Infrastructure. |
| 3 | `SOEA.Application` NO puede importar: `SOEA.Infrastructure.Data`, `SOEA.Infrastructure.Excel`. |
| 4 | Los motores (GraphColoring, ConstraintProg, Genetic) deben ser stateless y solo depender de Domain. |
| 5 | Cero lógica de negocio en controllers ni en repositories. |

## Qué buscar concretamente

- `using` statements que crucen capas prohibidas (ej. `using SOEA.Infrastructure` dentro de Domain).
- Clases de Engine que inyecten repositorios, DbContext, o servicios de Application.
- Controllers que contengan cálculos, condicionales de negocio, o acceso directo a repositorios.
- Repositories que contengan reglas de negocio (más allá de filtros de consulta).
- Entidades de dominio que referencien namespaces de EF, ASP.NET o similares.

## Formato de respuesta

Para cada violación encontrada:
```
VIOLACIÓN [Regla #N]: <archivo>:<línea>
  Import/patrón ilegal: <código>
  Motivo: <explicación breve>
```

Si no se encuentran violaciones:
```
Architecture OK — ninguna violación detectada en los archivos revisados.
```
