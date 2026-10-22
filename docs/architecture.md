# Arquitectura

> Verificado contra el código el 2026-10-14.

## Capas

```
Angular 21 (frontend/soea-angular)  ──HTTP──►  SOEA.API (10 controllers, sin auth)
                                                   │ DI
                     ┌─────────────────────────────┼───────────────────────────┐
                     ▼                             ▼                           ▼
        Infrastructure.Data / .Excel      SOEA.Application        Engine.GraphColoring · .ConstraintProg · .Genetic
                     │                             │                           │
                     └──────────────►  SOEA.Domain (sin dependencias externas)  ◄┘
```

| Proyecto | Puede importar | Contenido |
|---|---|---|
| `SOEA.Domain` | nada | Entidades, enums, value objects, interfaces de repositorio y de motor, servicios de dominio puros |
| `SOEA.Application` | Domain, Engines | Servicios de caso de uso (`Features/*`), `ValidadorRestriccionesDuras` |
| `SOEA.Engine.*` | Domain | Motores stateless de las 3 fases |
| `SOEA.Infrastructure.Data` | Domain, EF Core | `SOEABdContext`, configuraciones, repositorios, migraciones |
| `SOEA.Infrastructure.Excel` | Domain, EPPlus | `LectorExcel` |
| `SOEA.API` | todo | Controllers, `Program.cs`, `GlobalExceptionHandler` |
| `test/SOEA.Tests` | todo | xUnit + NetArchTest |

`ArchitectureTests` hace cumplir: Domain no depende de nada de SOEA, Application no depende de Infrastructure, Infrastructure no depende de API, y los repositorios viven en `SOEA.Infrastructure.Data.Repositories`.

## Servicios de dominio (`SOEA.Domain/Services`)

Fuente única compartida por las tres fases y el validador:

| Servicio | Responsabilidad |
|---|---|
| `GrillaInstitucional` | Bloques de 1 h: L–V 06:00–22:00, sábado 06:00–14:00 (88 bloques). IDs deterministas por día+hora |
| `CalculadorDominioSesion` | Inicios válidos de una sesión (cabe en el día + HC-G01 + HC-VH); `BloquesPermitidosPorGrupo` arma el índice HC-G01 de todos los grupos |
| `CalculadorEspaciosSesion` | Aulas candidatas (HC-S03 tipo, HC-S05 aula fija), `RequisitoDe` (requisito del grupo para el tipo de sesión) y `CumpleAforo` (HC-CAP) |
| `ModalidadSemanal` | Modalidad por semana y semanas en que una sesión ocupa aula (fuente de ALT-05) |
| `EvaluadorParejaAlternancia` | Decide si dos sesiones pueden emparejarse (Semana B) |
| `BloquesPlanner`, `ReglasSesion` | Encaje de sesiones multi-hora y reglas de sesión compartidas |
| `DetectorDocentesDuplicados` | Detección de docentes repetidos para la fusión |

`Sesion.DuracionBloques` (= `ceil(DuracionHoras)`, mínimo 1) es la única conversión de horas a bloques de la grilla. Ningún motor ni servicio debe recalcularla, ni volver a armar por su cuenta el índice HC-G01, el requisito de espacio o el filtro de aforo.

## API REST (`src/SOEA.API/Controllers`)

| Recurso | Endpoints |
|---|---|
| `api/asignaturas` | CRUD · `PATCH {id}/elegibilidad-alternancia` |
| `api/grupos` | CRUD · `GET por-asignatura/{asignaturaId}` |
| `api/docentes` | CRUD · `GET duplicados` · `POST fusionar` |
| `api/espacios`, `api/facultades`, `api/programas` | CRUD |
| `api/criterioscesionalternancia` | `GET` · `PATCH {id}` |
| `api/horario` | `GET actual` · `DELETE actual` · `POST generar` · `POST sesion-manual` · `DELETE {horarioId}/sesiones/{sesionId}` · `POST reacomodar` |
| `api/import` | `POST excel/revisar` · `POST filas/revisar` · `POST filas` |
| `api/sesiones` | `PATCH {id}/docente` |

- Errores: siempre `application/problem+json` con `detail` en español (`GlobalExceptionHandler`). 422 = infactible (cuerpo con `motivoInfactibilidad`), 409 = conflicto de datos, 429 = ya hay una generación en curso.
- `POST /horario/generar` tiene un limitador de concurrencia global (1, sin cola) y un plazo total de 200 s.
- No hay autenticación (ver [pendientes.md](pendientes.md)).

## Servicios de Application

Registrados como concretos (`AddScoped<X>()`, sin interfaz): `AsignaturaService`, `GrupoService`, `DocenteService`, `FusionDocentesService`, `EspacioService`, `FacultadService`, `ProgramaService`, `CriterioCesionAlternanciaService`, `GenerarHorarioService`, `CrearSesionManualService`, `ReacomodarHorarioService`, `AsignarDocenteSesionService`, `ImportarCurriculumService`, `SesionCascadeService` (borrado en cascada de sesiones al borrar catálogo).

## Cómo agregar una entidad

1. Entidad en `SOEA.Domain/Entities` (hereda `EntidadBase`, Id `Guid` asignado por la app).
2. `IXRepositorio : IRepositorio<X>` en `SOEA.Domain/Interfaces`.
3. Repositorio en `Infrastructure.Data/Repositories` (hereda `BaseRepository<X>`).
4. `IEntityTypeConfiguration<X>` en `Infrastructure.Data/Configurations` (columnas snake_case) y `DbSet` en `SOEABdContext`.
5. `AddScoped<IXRepositorio, XRepositorio>()` en `Program.cs`.
6. Migración (`dotnet ef migrations add …`, ver [operacion.md](operacion.md)).

La skill `/add-entity` automatiza estos pasos.

## Configuración (`appsettings` / variables de entorno)

| Sección | Clave | Default | Uso |
|---|---|---|---|
| `ConnectionStrings` | `DefaultConnection` | vacío | Solo en `appsettings.Development.json` (gitignored) o variables de entorno |
| `CpSat` | `TimeoutSegundos` | 120 | Por solve de Fase 2 |
| `CpSat` | `NumWorkers` / `RandomSeed` | 0 / 1 | Paralelismo y reproducibilidad |
| `CpSat` | `ClasificarInfactibilidadEspacio` | true | Relaja aulas para saber si la infactibilidad es por espacio (activa la Semana B) |
| `CpSat` | `SweepGrupos` / `SweepGruposMaximo` | false / 20 | Diagnóstico: reintenta sin cada grupo para nombrar al culpable. En prod está en `true` |
| `CpSat` | `ExportarModelo` | false | Vuelca `cp_model_debug.txt` |
| `AllowedOrigins` | (array) | `http://localhost:4200` | CORS, política `AllowFrontend`; en prod, la URL del Static Web App |
