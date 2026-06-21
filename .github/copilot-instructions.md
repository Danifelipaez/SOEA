# GitHub Copilot Instructions — SOEA

Fuente principal de contexto: `CLAUDE.md` en la raíz del proyecto. Este archivo contiene el estado implementado y las reglas no negociables.

## Reglas de arquitectura (resumen)

1. Dependencias: `API → Application → Domain ← Infrastructure / Engines`
2. `SOEA.Domain` = sin dependencias externas (no EF Core, no OR-Tools, no EPPlus)
3. Motores (GraphColoring, ConstraintProg, Genetic) = stateless, solo dependen de Domain
4. Lógica de negocio solo en Domain y Application — nunca en controllers ni repositories
5. La duración de sesión es fija como dato de entrada; el algoritmo no la modifica
6. Las asignaturas Tipo A (8+8) son hard constraint inamovible
7. Horario se genera desde cero cada vez — sin iterar sobre horario previo
8. Sesión virtual: `EspacioId = null`; misma franja que su contraparte presencial

## Patrón para implementar un nuevo feature

Orden obligatorio al agregar una nueva entidad o funcionalidad:

1. **Domain** — crear entidad en `src/SOEA.Domain/Entities/` e interfaz `IXRepositorio` en `src/SOEA.Domain/Interfaces/`
2. **Infrastructure.Data** — crear repositorio en `Repositories/`, configuración EF en `Configurations/`, aplicar en `SOEABdContext.OnModelCreating`
3. **Application** — crear servicio de caso de uso en `Features/<Nombre>/`
4. **API** — crear controller en `src/SOEA.API/Controllers/` y registrar el servicio en `Program.cs`
5. **Tests** — agregar pruebas unitarias en `test/SOEA.Tests/`

## Lo que NUNCA debes hacer

1. Hacer que Domain importe `Microsoft.EntityFrameworkCore`, `Google.OrTools` o `OfficeOpenXml`
2. Inyectar un repositorio directamente en un controller (va siempre por Application)
3. Agregar lógica de cálculo de fitness, conflictos o alternancia en un repositorio
4. Llamar a un motor de optimización desde un controller o desde Infrastructure
5. Modificar `BloqueTiempo` desde un motor — son de solo lectura durante la optimización

## Enums y convenciones de código

```csharp
// Namespace: SOEA.Domain.Enums
TipoAlternancia { TipoA, TipoB, SinAlternancia }
TipoEspacio     { Salon, Laboratorio, Auditorio }
Modalidad       { Presencial, Virtual, Hibrida }
EstadoHorario   { Borrador, Validado, Aprobado, Activo }
```

- Clases y archivos: inglés. Comentarios y documentación: español.
- Tests: xUnit + NSubstitute. Datos de prueba en `TestData/`.
