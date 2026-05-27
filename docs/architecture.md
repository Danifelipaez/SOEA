# Arquitectura SOEA

## Diagrama de capas

```
┌──────────────────────────────────────────────────────────────┐
│              Frontend Angular 21                              │
│  /ingesta · /horario · /dashboard-admin · /dashboard-dev     │
│  StateService (in-memory) · HorarioApiService → localhost:5066│
└───────────────────────────┬──────────────────────────────────┘
                            │ HTTP REST
                            ▼
┌──────────────────────────────────────────────────────────────┐
│                       SOEA.API                               │
│   AsignaturaController · DocentesController · EspaciosCtrl  │
│   HorarioController · ImportController · Program.cs          │
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
         │     Proceso: Welsh-Powell sobre grafo de conflictos
         │     Output: PreHorario (asignación parcial de bloques)
         │
         ├─► [Fase 2] MotorConstraintProgramming (OR-Tools CP-SAT, 120 s)
         │     Input:  PreHorario + restricciones duras
         │     Proceso: CP-SAT con warm-start de Fase 1
         │     Output: Horario factible  OR  InfeasibleResult → HTTP 422
         │
         └─► [Fase 3] MotorGenetico (200 gen, pop 50, convergencia 30)
               Input:  Horario factible
               Proceso: selección torneo → crossover → mutación → reparación
               Output: Horario optimizado (fitness minimizado)
                     └─► HorarioRepositorio.GuardarAsync() → PostgreSQL
```

`BloqueTiempo` se genera en memoria por request (Lun–Vie 07:00–20:00, Sáb 07:00–14:00) — no tiene tabla propia en BD.

---

## Registro DI (patrón actual en Program.cs)

```csharp
// Infraestructura — cada proyecto expone AddX(config)
builder.Services.AddInfrastructureData(builder.Configuration);

// Motores — stateless, sin configuración
builder.Services.AddGraphColoringEngine();
builder.Services.AddConstraintProgEngine();
builder.Services.AddGeneticEngine();

// Application — servicios concretos directos (sin interfaz intermedia)
builder.Services.AddScoped<GenerarHorarioService>();
builder.Services.AddScoped<CreateAsignaturaService>();
builder.Services.AddScoped<GetAsignaturasService>();
builder.Services.AddScoped<GetAsignaturaByIdService>();
builder.Services.AddScoped<DeleteAsignaturaService>();
```
