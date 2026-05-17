# Plan de entrega
**Última actualización:** 2026-05-16

## Propósito
Esbozar la hoja de ruta de desarrollo de SOEA: fases, hitos y orden de trabajo recomendado.
Este documento guía la secuencia en la que se le debe pedir a Copilot que cree la estructura e implemente funcionalidades.

## Alcance
Cronograma del proyecto desde la configuración inicial hasta el despliegue piloto.

---

## Fases de desarrollo

### Fase 0 — Configuración del proyecto (Semana 1)
- [x] Inicializar la solución .NET con la estructura de proyectos Clean Architecture
- [x] Crear la estructura de carpetas de documentación (`docs/`)
- [ ] Configurar el workspace de Angular (`frontend/soea-angular/`)
- [ ] Configurar la conexión a la base de datos (EF Core + SQL Server/PostgreSQL)
- [x] Configurar el proyecto de pruebas xUnit

### Fase 1 — Modelo de dominio (Semana 2)
- [ ] Implementar entidades de dominio: `Sesion`, `Cohorte`, `Espacio`, `Docente`, `EspacioDeTiempo`, `Horario`, `Asignatura`
- [ ] Implementar objetos de valor: `AlternanciaType`, `TimeRange`
- [ ] Definir interfaces de dominio: `IScheduleRepository`, `IOptimizationEngine`
- [ ] Escribir pruebas unitarias para invariantes de entidades de dominio

### Fase 2 — Ingesta de datos (Semana 3)
- [ ] Implementar `CurriculumExcelReader` usando EPPlus
- [ ] Implementar `InstructorAvailabilityReader`
- [ ] Implementar `SpaceInventoryReader`
- [ ] Escribir pruebas de integración para los lectores de Excel
- [ ] Configurar el `DbContext` de EF Core y la migración inicial

### Fase 3 — Motor de optimización: coloreado de grafos (Semana 4)
- [ ] Implementar `ConflictGraphBuilder`
- [ ] Implementar `GraphColoringScheduler` (heurística Welsh-Powell)
- [ ] Escribir pruebas unitarias para la construcción del grafo y el coloreado

### Fase 4 — Motor de optimización: CP-SAT (Semanas 5–6)
- [ ] Agregar la dependencia NuGet de OR-Tools
- [ ] Implementar `CpSatSchedulerBuilder`
- [ ] Codificar todas las restricciones duras de `docs/business-rules/hard-constraints.md`
- [ ] Implementar el reporte de infactibilidad
- [ ] Escribir pruebas unitarias para cada restricción codificada

### Fase 5 — Motor de optimización: Algoritmo Genético (Semanas 7–8)
- [ ] Implementar `ScheduleChromosome`
- [ ] Implementar `FitnessEvaluator` con todas las restricciones blandas de `docs/business-rules/soft-constraints.md`
- [ ] Implementar `GeneticScheduleOptimizer` (selección, cruce, mutación, reparación)
- [ ] Escribir pruebas unitarias para la función de aptitud y las operaciones genéticas

### Fase 6 — Capa de Application (Semana 9)
- [ ] Implementar `GenerateScheduleCommand` y su handler
- [ ] Implementar `ScheduleOptimizationPipeline` (orquesta las Fases 1→2→3)
- [ ] Implementar `ConstraintValidator` (validación posterior a la generación)
- [ ] Implementar `IngestExcelCommand` y su handler
- [ ] Escribir pruebas unitarias de la capa de Application

### Fase 7 — Capa de API (Semana 10)
- [ ] Implementar `ScheduleController` (generar, recuperar, publicar)
- [ ] Implementar `IngestionController` (carga de archivos Excel)
- [ ] Agregar autenticación JWT y autorización basada en roles
- [ ] Configurar Swagger/OpenAPI
- [ ] Escribir pruebas de integración de API

### Fase 8 — Frontend (Semanas 11–12)
- [ ] Generar el workspace de Angular con routing y guards basados en roles
- [ ] Admin: formulario de carga de Excel + botón para lanzar la optimización
- [ ] Coordinador: rejilla de revisión del horario + flujo de aprobación
- [ ] Docente/Estudiante: vista de horario personal

### Fase 9 — Validación del piloto (Semanas 13–14)
- [ ] Ejecutar el conjunto de datos del piloto a través del pipeline completo
- [ ] Verificar todos los criterios de aceptación de `docs/testing/acceptance-criteria.md`
- [ ] Recopilar comentarios de los coordinadores
- [ ] Corregir cualquier problema bloqueante
- [ ] Aprobación final

---

## Orden recomendado para pedirle cosas a Copilot

Cuando le pidas a Copilot que genere código, sigue este orden para obtener mejores resultados:

1. Domain entities (use `docs/data/data-dictionary.md`)
2. Domain interfaces (use `docs/architecture/module-map.md`)
3. Infrastructure implementations (use `docs/data/relational-model.md`)
4. Excel readers (use `docs/data/data-dictionary.md`)
5. Phase 1 engine (use `docs/algorithm/phase-1-graph-coloring.md`)
6. Phase 2 engine (use `docs/algorithm/phase-2-constraint-programming.md` + `docs/business-rules/hard-constraints.md`)
7. Phase 3 engine (use `docs/algorithm/phase-3-genetic-algorithm.md` + `docs/business-rules/soft-constraints.md`)
8. Application use cases (use `docs/requirements/SRS.md`)
9. API controllers (use `docs/data/json-output-spec.md`)
10. Angular components (use `docs/requirements/stakeholders.md`)

---

## Preguntas abiertas

- ¿El cronograma de 14 semanas está alineado con el calendario académico del semestre?
- ¿Existen dependencias de infraestructura de TI (configuración del servidor, aprovisionamiento de base de datos) que afecten el cronograma?
