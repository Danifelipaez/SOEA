# Estado del Proyecto SOEA y Próximo Paso

**Actualizado:** 11 de mayo de 2026  
**Etapa actual:** Fase 1 - Modelo de Dominio (PARCIALMENTE COMPLETADA)

---

## Estado Resumido

El proyecto SOEA se encuentra en desarrollo con una **estructura de Clean Architecture establecida** pero con la mayoría de capas aún incompletas. Las entidades de dominio tienen esqueletos parciales definidos, pero falta la mayoría de la lógica de negocio, interfaces de dominio, y todas las capas de infraestructura.

---

## ✅ Completado

### Estructura de Proyectos
- [x] Solución .NET 10 inicializada con estructura Clean Architecture
- [x] Todos los proyectos creados (Domain, Application, 3 Engines, 2 Infrastructure, API, Tests)
- [x] Referencias entre proyectos configuradas correctamente según el mapa de módulos
- [x] Estructura de carpetas de documentación completa
- [x] Proyecto de pruebas xUnit preparado

### Entidades de Dominio (Esqueletos)
- [x] `Asignatura.cs` - Constructor con validaciones básicas
- [x] `Sesion.cs` - Estructura inicial
- [x] `Docente.cs` - Estructura inicial
- [x] `Espacio.cs` - Estructura inicial
- [x] `Horario.cs` - Estructura inicial

### Enumeraciones
- [x] `TipoAlternancia.cs` - TypeA, TypeB, NonAlternating
- [x] `DiaDeSemana.cs`
- [x] `EstadoHorario.cs`
- [x] `EstadoSesion.cs`
- [x] `FranjaHoraria.cs`
- [x] `Modalidad.cs`
- [x] `TipoEspacio.cs`
- [x] `TipoRestriccion.cs`

---

## ❌ No Completado

### Fase 1 - Modelo de Dominio
- [ ] **Entidades de dominio** - Solo esqueletos; falta:
  - Invariantes de negocio completas
  - Lógica de validación robusta
  - Propiedades navegacionales y relaciones
  - Métodos de comportamiento
  - Objetos de valor (`TimeSlop`, `CohortCode`, `SpaceCode`) (Completado)

- [x] **Interfaces de dominio (puertos)**
  - [x] `IScheduleRepository`
  - [x] `IOptimizationEngine`
  - [ ] Otras interfaces necesarias

- [ ] **Pruebas unitarias del dominio**
  - [ ] Tests para invariantes de `Asignatura`
  - [ ] Tests para invariantes de `Sesion`
  - [ ] Tests para reglas de alternancia

### Fase 2 - Ingesta de Datos
- [ ] `SOEA.Infrastructure.Data` - No creado aún (solo esqueleto)
- [ ] `SOEA.Infrastructure.Excel` - Solo `Class1.cs`
  - [ ] `CurriculumExcelReader`
  - [ ] `InstructorAvailabilityReader`
  - [ ] `SpaceInventoryReader`
  - [ ] Mapeadores Excel → Dominio
  - [ ] Validaciones de ingesta

- [ ] **DbContext de EF Core**
  - [ ] `SoeaDbContext`
  - [ ] Configuraciones de tipo de entidad
  - [ ] Migraciones

### Fase 3, 4, 5 - Motores de Optimización
- [ ] `SOEA.Engine.GraphColoring` - Solo `Class1.cs`
  - [ ] `ConflictGraphBuilder`
  - [ ] `GraphColoringScheduler` (Welsh-Powell)

- [ ] `SOEA.Engine.ConstraintProg` - Solo `Class1.cs`
  - [ ] OR-Tools NuGet dependency
  - [ ] `CpSatSchedulerBuilder`
  - [ ] Codificación de restricciones duras

- [ ] `SOEA.Engine.Genetic` - Solo `Class1.cs`
  - [ ] `ScheduleChromosome`
  - [ ] `FitnessEvaluator`
  - [ ] `GeneticScheduleOptimizer`

### Fase 6 - Application
- [ ] `SOEA.Application` - Solo esqueletos
  - [ ] `GenerateScheduleCommand` + Handler
  - [ ] `ScheduleOptimizationPipeline`
  - [ ] `ConstraintValidator`
  - [ ] `IngestExcelCommand` + Handler
  - [ ] DTOs (`HorarioDto`, `SessionDto`, etc.)

### Fase 7 - API
- [ ] `SOEA.API` - Solo controlador skeleton
  - [ ] `ScheduleController`
  - [ ] `IngestionController`
  - [ ] JWT + Autorización basada en roles
  - [ ] Swagger/OpenAPI

### Fase 8 - Frontend
- [ ] `frontend/soea-angular/` - No inicializado

---

## 📊 Resumen de Progreso

| Fase | Tarea | % Completado | Prioridad |
|------|-------|-------------|-----------|
| 0 | Configuración | 90% | - |
| 1 | Modelo de Dominio | 60% | 🔴 **CRÍTICA** |
| 2 | Ingesta de Datos | 0% | 🔴 **CRÍTICA** |
| 3-5 | Motores | 0% | 🟡 **ALTA** |
| 6 | Application | 5% | 🟡 **ALTA** |
| 7 | API | 5% | 🟡 **ALTA** |
| 8 | Frontend | 0% | 🟡 **MEDIA** |
| 9 | Validación Piloto | 0% | 🟡 **MEDIA** |

**Estimado Total**: ~20% del proyecto

---

## 🎯 Siguiente Paso Inmediato

### **COMPLETAR FASE 1: Modelo de Dominio (Entidades + Interfaces)**

**Por qué:** Las entidades de dominio son la base sobre la que se construye todo lo demás. Sin ellas bien definidas, los cambios posteriores serán caóticos.

### Orden de trabajo recomendado:

1. **Enriquecer y validar entidades**
   - Leer [`docs/data/data-dictionary.md`](docs/data/data-dictionary.md) para cada entidad
   - Leer [`docs/business-rules/hard-constraints.md`](docs/business-rules/hard-constraints.md)
   - Leer [`docs/business-rules/alternancia.md`](docs/business-rules/alternancia.md)
   - Completar constructores, propiedades navegacionales y métodos de comportamiento

2. **Crear objetos de valor**
   - `TimeRange` - Rango horario con validaciones
   - `CohortCode` - Código de grupo con formato y unicidad
   - `SpaceCode` - Código de espacio

3. **Definir interfaces de dominio (puertos)**
   - `IScheduleRepository` 
   - `IOptimizationEngine`

4. **Escribir pruebas unitarias**
   - Tests para cada invariante de entidad
   - Tests para objetos de valor

**Artefactos entregables:**
- ✅ `SOEA.Domain/Entities/*` - Enriquecidas con lógica
- ✅ `SOEA.Domain/ValueObjects/*` - Nuevos
- ✅ `SOEA.Domain/Interfaces/*` - Nuevos
- ✅ `test/SOEA.Tests/Domain/*` - Nuevos tests

**Esfuerzo estimado:** 3-4 días

---

## Documentación Crítica a Consultar (en orden)

1. [`docs/requirements/glossary.md`](docs/requirements/glossary.md) - Vocabulario del dominio
2. [`docs/data/data-dictionary.md`](docs/data/data-dictionary.md) - Campos y significados
3. [`docs/business-rules/hard-constraints.md`](docs/business-rules/hard-constraints.md) - Restricciones que MUST not break
4. [`docs/business-rules/alternancia.md`](docs/business-rules/alternancia.md) - Lógica de alternancia
5. [`docs/architecture/module-map.md`](docs/architecture/module-map.md) - Límites de capa

---

## Bloqueadores Actuales

**Ninguno** — El proyecto está en buen estado para iniciar la Fase 1 completa.

---

## Notas

- El proyecto sigue Clean Architecture de manera consistente
- Las enumeraciones están bien definidas
- Los .csproj tienen las dependencias correctas
- No hay dependencias externas aún instaladas (necesitaremos EPPlus, OR-Tools, EF Core en fases posteriores)
