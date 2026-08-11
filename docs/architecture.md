# Arquitectura SOEA

## Diagrama de capas

```
┌──────────────────────────────────────────────────────────────┐
│              Frontend Angular 21                              │
│  /catalogo · /horario · /revisar · /publicar (bloqueado)     │
│  Organizado por journey de 5 pasos, no por entidad — ver     │
│  docs/DESIGN_BRIEF.md. StateService (signals) · Persistencia  │
│  Service · CatalogoService · HorarioApiService → :5066/api   │
└───────────────────────────┬──────────────────────────────────┘
                            │ HTTP REST
                            ▼
┌──────────────────────────────────────────────────────────────┐
│                       SOEA.API (9 controllers)                │
│  AsignaturaController · CriteriosCesionAlternanciaController │
│  DocentesController · EspaciosController · FacultadesController│
│  GruposController · HorarioController · ImportController      │
│  ProgramasController · SesionesController · Program.cs        │
│  (sin autenticación — ningún controlador tiene [Authorize])   │
└──────┬──────────────────┬────────────────────────┬───────────┘
       │[DI]              │[DI]                    │[DI]
       ▼                  ▼                        ▼
┌─────────────┐  ┌─────────────────────────────────────────┐
│Infra.Data   │  │             SOEA.Application             │
│Infra.Excel  │  │  GenerarHorarioService                   │
│             │  │  CreateAsignaturaService (+ CRUD)        │
└──────┬──────┘  └──────┬──────────┬───────────┬───────────┘
       │                │          │           │
       │                ▼          ▼           ▼
       │   ┌──────────────┐ ┌──────────┐ ┌─────────┐
       │   │Engine.Graph  │ │Engine.CP │ │Engine.GA│
       │   │Coloring      │ │(OR-Tools)│ │(Genetic)│
       │   └──────┬───────┘ └────┬─────┘ └────┬────┘
       │          └──────────────┼─────────────┘
       │                         ▼
       └────────────►  ┌─────────────────────────┐
                       │       SOEA.Domain        │
                       │  Entities · Interfaces   │
                       │  Enums · Value Objects   │
                       │  ← sin deps. externas    │
                       └─────────────────────────┘
```

**Regla de oro:** Domain no importa nada externo. Infrastructure e Engines implementan interfaces definidas en Domain.

---

## Mapa de proyectos

| Proyecto | Puede importar | Prohibido importar |
|---|---|---|
| `SOEA.Domain` | *(nada de SOEA)* | todo lo demás |
| `SOEA.Application` | Domain, los 3 Engines | Infrastructure, API |
| `SOEA.Engine.*` | Domain | Infrastructure, Application, API |
| `SOEA.Infrastructure.Data` | Domain, EF Core | Application, Engines, API |
| `SOEA.Infrastructure.Excel` | Domain, EPPlus | Application, Engines, API |
| `SOEA.API` | todos los anteriores | *(nada nuevo)* |
| `SOEA.ConsoleRunner` | Application, Infrastructure, Engines | — |
| `test/SOEA.Tests` | todos los proyectos | — |

---

## Patrón BaseRepository (5 pasos)

Para agregar un nuevo repositorio:

**Paso 1** — Interfaz en `src/SOEA.Domain/Interfaces/`:
```csharp
public interface IEntidadRepositorio : IRepositorio<Entidad>
{
    Task<Entidad?> ObtenerPorCodigoAsync(string codigo);
}
```

**Paso 2** — Repositorio en `src/SOEA.Infrastructure.Data/Repositories/`:
```csharp
public class EntidadRepositorio : BaseRepository<Entidad>, IEntidadRepositorio
{
    public EntidadRepositorio(SOEABdContext context) : base(context) { }

    public async Task<Entidad?> ObtenerPorCodigoAsync(string codigo)
        => await _context.Entidades.FirstOrDefaultAsync(e => e.Codigo == codigo);
}
```

**Paso 3** — Configuración EF en `src/SOEA.Infrastructure.Data/Configurations/`:
```csharp
public class EntidadConfiguration : IEntityTypeConfiguration<Entidad>
{
    public void Configure(EntityTypeBuilder<Entidad> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Codigo).IsRequired().HasMaxLength(20);
    }
}
```

**Paso 4** — Registrar configuración en `SOEABdContext.OnModelCreating`:
```csharp
modelBuilder.ApplyConfiguration(new EntidadConfiguration());
```

**Paso 5** — Registrar en `Program.cs`:
```csharp
builder.Services.AddScoped<IEntidadRepositorio, EntidadRepositorio>();
```

---

## Pipeline de optimización

```
POST /api/horario/generar
         │
         ▼
GenerarHorarioService.EjecutarAsync()
         │
         ├─► [Fase 1] AgendadorColoracionGrafo
         │     Input:  List<Sesion>, List<BloqueTiempo>
         │     Proceso: Welsh-Powell sobre grafo de conflictos (sin cambios)
         │     Output: List<Sesion> con BloqueTiempoId preasignado
         │
         ├─► [Fase 2] MotorConstraintProgramming (OR-Tools CP-SAT, 120 s)
         │     Input:  sesiones coloreadas + bloques + espacios + docentes
         │     Proceso: variables (sesion, semana); HC por semana; regla 9; warm-start
         │     Output: IReadOnlyList<AsignacionSemanal> (2 por sesión, A y B)
         │             OR InfeasibleResult → HTTP 422
         │
         ├─► [Fase 3] MotorGenetico (activa) — Start (semana A) + StartB (semana B) independientes
         │     Input:  sesiones + AsignacionSemanal de Fase 2 (siembra Start/StartB)
         │     Proceso: selección/cruce/mutación/reparación por cohorte (grupo, CR-08);
         │              StartB independiente solo para SinAlternancia (ALT-06); fallback a
         │              Fase 2 si el resultado no es factible
         │     Output: AsignacionSemanal optimizada (fitness SC-01/06/09/BAL + guarda de
         │              capacidad de aulas), más asignación determinista de espacios
         │              (AsignadorEspacios) y reversión de cesiones presencial-first
         │
         └─► Persistencia
               SesionRepositorio.AddRangeAsync(sesiones)
               HorarioRepositorio.AddAsync(horario)
               AsignacionSemanalRepositorio.AddRangeAsync(asignaciones)
               └─► PostgreSQL (tablas Sesiones + Horarios + AsignacionesSemanales)
```

`BloqueTiempo` se genera en memoria por request vía `SOEA.Domain.Services.GrillaInstitucional` (Lun–Vie 06:00–22:00, Sáb 06:00–13:00 — dato bloqueante sin confirmar por Rosa, ver `docs/domain.md`) — no tiene tabla propia en BD.

La respuesta `SesionGeneradaDto` incluye el campo `Semana` (`"A"` / `"B"`); cada sesión lógica produce **dos entradas** en `GenerarHorarioResponse.Sesiones`.

---

## Registro DI (verificado contra `src/SOEA.API/Program.cs`, actualizado 2026-08-07)

> Esta sección estaba desactualizada: listaba 4 servicios CRUD de asignatura separados que nunca existieron como tales (el CRUD real es un único `AsignaturaService`) y omitía 8 de los 13 servicios de Application realmente registrados.

```csharp
// Infraestructura — cada proyecto expone AddX(config)
builder.Services.AddInfrastructureData(builder.Configuration);
builder.Services.AddScoped<ILectorExcel, LectorExcel>();

// Repositorios (7 entidades + UnitOfWork)
builder.Services.AddScoped<IAsignaturaRepositorio, AsignaturaRepository>();
builder.Services.AddScoped<IHorarioRepositorio, HorarioRepositorio>();
builder.Services.AddScoped<ISesionRepositorio, SesionRepositorio>();
builder.Services.AddScoped<IAsignacionSemanalRepositorio, AsignacionSemanalRepositorio>();
builder.Services.AddScoped<IDocenteRepositorio, DocenteRepositorio>();
builder.Services.AddScoped<IEspacioRepositorio, EspacioRepositorio>();
builder.Services.AddScoped<IGrupoRepositorio, GrupoRepositorio>();
builder.Services.AddScoped<IBloqueTiempoRepositorio, BloqueTiempoRepositorio>();
builder.Services.AddScoped<ICriterioCesionAlternanciaRepositorio, CriterioCesionAlternanciaRepositorio>();
builder.Services.AddScoped<IFacultadRepositorio, FacultadRepositorio>();
builder.Services.AddScoped<IProgramaRepositorio, ProgramaRepositorio>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Motores — stateless, sin configuración
builder.Services.AddGraphColoringEngine();
builder.Services.AddConstraintProgEngine();
builder.Services.AddGeneticEngine();

// Application — servicios concretos directos (sin interfaz intermedia)
builder.Services.AddScoped<AsignaturaService>();          // CRUD completo, un solo servicio
builder.Services.AddScoped<CrearSesionManualService>();
builder.Services.AddScoped<DocenteService>();
builder.Services.AddScoped<FusionDocentesService>();
builder.Services.AddScoped<CriterioCesionAlternanciaService>();
builder.Services.AddScoped<GenerarHorarioService>();
builder.Services.AddScoped<ImportarCurriculumService>();
builder.Services.AddScoped<AsignarDocenteSesionService>();
builder.Services.AddScoped<ReacomodarHorarioService>();
```

Nota: `TipoAlternanciaConfig` es una entidad de dominio con configuración EF y migración propia, pero **no tiene servicio de Application ni controlador** — solo la referencia internamente `GenerarHorarioService`. No confundir con `CriterioCesionAlternanciaService`/`CriteriosCesionAlternanciaController`, que sí existen y son la pieza real que gobierna qué cede a alternancia.
