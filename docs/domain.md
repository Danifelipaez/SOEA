# Dominio

> Verificado contra `src/SOEA.Domain` el 2026-10-14. Las reglas por ID (HC-*, SC-*, ALT-*) son las que usa el código.

## Glosario

| Término | Significado |
|---|---|
| **Asignatura** | Materia del plan de estudios. Define cuántas sesiones y de cuántas horas tiene cada *track* |
| **Grupo** | Sección/cohorte de una asignatura (estudiantes, docente, disponibilidad, requisitos de aula). **Eje del modelo** |
| **Sesión** | Una clase semanal de un grupo. Unidad que se agenda; su duración es un dato fijo |
| **Track** | Teoría presencial, teoría virtual o laboratorio (`TipoSesion`) |
| **Semana A / B** | Semanas impares / pares del semestre. La A es EL horario; la B solo existe para sesiones emparejadas |
| **Alternancia** | Una sesión presencial en una semana y virtual en la otra. `TipoA` = presencial en A, `TipoB` = presencial en B |
| **Pareja de alternancia** | Dos sesiones que comparten bloque y aula, turnándose la presencialidad (`ParejaAlternanciaId`) |
| **Cesión** | Emparejar sesiones cuando no caben por falta de aulas (activación reactiva de la Semana B) |
| **Horario base / sesión fija** | Sesión con franja/aula predefinida (importada o manual) que el generador respeta como igualdad |
| **Sesión virtual** | Sincrónica online, misma franja que su contraparte; `EspacioId = null` |

## Jerarquía (obligatoria desde M19)

```
Facultad 1─N Programa 1─N Asignatura 1─N Grupo 1─N Sesion 1─N AsignacionSemanal N─1 Horario
                                            │          │                    │
                                       Docente?    Docente?            Espacio? · BloqueTiempo
```

`Sesion.GrupoId` y `Grupo.AsignaturaId` son NOT NULL; FK compuesta `(grupo_id, asignatura_id)` garantiza que la sesión y su grupo pertenecen a la misma asignatura.

## Entidades

| Entidad | Campos clave | Notas |
|---|---|---|
| `Asignatura` | `Codigo` (único por programa), `ProgramaId`, por track `Sesiones…Semana` + `Horas…`, `SesionesLaboratorioSemestre`, `Alternancia`, `Categoria`, `HoraInicioMin?`/`HoraFinMax?`, `EsCandidataAlternancia` | Al menos un track con sesiones > 0. `Alternancia` se deriva de `SesionesLaboratorioSemestre` contra un umbral (= umbral → TipoA, > → TipoB, < → SinAlternancia) o se fija a mano. La ventana horaria es HC-VH |
| `Grupo` | `AsignaturaId`, `Codigo?`, `Nombre`, `DocenteId?`, `EstudiantesInscritos`, `Alternancia`, `DisponibilidadUiJson?`, `RequisitosEspacio[]` | `EstudiantesInscritos` → aforo (HC-CAP). `DisponibilidadUiJson` → HC-G01: **un día sin entrada = cerrado** (en docentes = abierto). `RequisitoEspacio(TipoSesion, EspacioId?, TipoEspacio?, Sesiones)` → HC-S03/HC-S05 |
| `Sesion` | `GrupoId`, `AsignaturaId`, `DocenteId?`, `BloqueTiempoId`, `EspacioId?`, `DuracionHoras`, `Alternancia`, `Modalidad`, `TipoFlujo`, `ParejaAlternanciaId?`, `CedidaPorSaturacion`, `Bloqueada`, `PatronAlternanciaId?`, `Estado`, `MotivoConflicto` | Teoría siempre nace `SinAlternancia`; laboratorio hereda la alternancia declarada. `CedidaPorSaturacion` marca las emparejadas por el generador (reversibles). `EsBloque`/`EstaDividida` no se usan |
| `AsignacionSemanal` | `SesionId`, `HorarioId`, `Semana`, `BloqueTiempoId`, `EspacioId?`, `Modalidad` | **Una fila por sesión**, en su semana presencial (A para lo que no alterna = todas las semanas). La contraparte virtual se deriva al construir el DTO |
| `Horario` | `Semestre`, `GeneradoEn`, `Estado`, `SesioneIds`, `ViolacionesRestriccionesDuras`, `PuntajeFitness` | `EstadoHorario`: Borrador/Aprobado/Publicado/Archivado. `MarcarComoPublicado()` falla si hay violaciones. Varios semestres conviven; regenerar reemplaza solo el mismo semestre |
| `Docente` | `Nombre`, `Apellido`, `Correo`, `MaximoHorasSemanales`, `Disponibilidad`, `BloquesDisponibles`, `CedulaIdentidad?`, `DisponibilidadUiJson?` | **Fuera de la generación.** Solo produce avisos al asignar/editar |
| `Espacio` | `Nombre`, `Tipo` (Salon/Laboratorio/Auditorio), `Capacidad`, `Edificio?`, `Piso?` | |
| `BloqueTiempo` | `Dia`, `HoraInicio`, `HoraFin` | Bloques de 1 h generados por `GrillaInstitucional` (88), sembrados en BD con Id determinista |
| `Facultad`, `Programa` | `Nombre` (+ `FacultadId`) | Borrar con hijos → 409 |
| `CriterioCesionAlternancia` | `Criterio`, `Orden`, `Activo` | 4 filas semilla: Electiva, Optativa, Elegible, MultiplesSesiones (este último solo desempata). Editable por API |
| `TipoAlternanciaConfig` | `Nombre`, `PatronBase`, `SemanasPresenciales`, `Color`, `EsSistema`, `Activo` | Catálogo fijo de 3 filas (TipoA/TipoB/SinAlternancia), solo trazabilidad; sin API |

Value objects: `DisponibilidadSemanal` (parseo único de la disponibilidad por día), `RequisitoEspacio`, `NormalizadorTexto` (comparación sin tildes/mayúsculas).

## Alternancia (ALT-05: la Semana A es EL horario)

| Regla | Contenido |
|---|---|
| ALT-01 | Un TipoA y un TipoB pueden compartir aula y franja (nunca coinciden físicamente) |
| ALT-02/03 | Dos TipoA (o dos TipoB) no pueden compartir aula y franja |
| ALT-04 | Lo virtual no consume aula |
| ALT-05 | Franja y aula valen para todas las semanas; solo cambia la modalidad |
| ALT-06 | Una sesión que no alterna ocupa su aula en las dos semanas: no libera capacidad |

Emparejar es el **único** mecanismo que libera capacidad. Fuente única: `ModalidadSemanal`. Detalle del emparejamiento en [algorithms.md](algorithms.md#semana-b-reactiva).

## Restricciones duras

Una violación impide publicar. "Validador" = `ValidadorRestriccionesDuras`, que revisa la salida final.

| ID | Regla | Dónde se impone |
|---|---|---|
| HC-C01 | Un grupo no tiene dos sesiones a la vez (presencial o virtual) | Fase 1 (arista), Fase 2 (NoOverlap por grupo), GA, validador |
| HC-S01 | Un aula no aloja dos sesiones presenciales a la vez en la misma semana | Fase 2, asignador de aulas, validador |
| HC-CAP | Inscritos ≤ capacidad del aula | Candidatos de aula, validador |
| HC-S03 | El aula cumple el tipo pedido por el requisito del grupo (default por `TipoSesion`) | Candidatos de aula, validador |
| HC-S04 | Virtual ⇒ sin aula | Invariante de entidad |
| HC-S05 | Si el requisito fija un aula concreta, se usa esa | Candidatos de aula, validador |
| HC-G01 | La sesión cae dentro de la disponibilidad del grupo | Dominio de inicios (3 fases), validador |
| HC-VH | La sesión cae dentro de `[HoraInicioMin, HoraFinMax]` de la asignatura | Dominio de inicios, validador |
| HC-SEP | ≥ 2 sesiones semanales del mismo (grupo, asignatura, tipo) quedan separadas al menos 2 días. Una sesión fija cuenta frente a sus hermanas libres; dos fijas entre sí no se comparan | Fase 2, GA, validador |
| HC-ALT | Una pareja tiene tipos opuestos, mismo bloque y misma aula | Fase 2, GA, validador |
| HC-BASE | Las sesiones fijas conservan su franja y aula | Fase 2 (igualdad), GA (gen congelado), validador |
| HC-T01/T03 | Dentro de la jornada; sesiones multi-hora no cruzan de día | Implícito en la grilla y `BloquesPlanner` |

**Docente (fuera de la generación):** HC-I01 (choque de docente) es duro solo al editar (`PATCH /sesiones/{id}/docente` → 409; `/reacomodar` rechaza). HC-I02 (disponibilidad) y HC-I03 (máximo de horas) son solo avisos.

**No implementadas por decisión:** HC-T02 (labs antes de 19:30), HC-T04 (receso de mediodía), HC-T05, HC-C02 (horas = malla). HC-SU01 ("8+8") es obsoleta: TipoA/TipoB solo indican la semana presencial.

## Restricciones blandas (fitness del GA, menor es mejor)

Se miden por grupo. Pesos por defecto en `GenerarHorarioRequest.Configuracion`.

| ID | Objetivo | Peso |
|---|---|---|
| SC-01 | Minimizar huecos del grupo en el día | 3 (`PesoErgo`) |
| SC-06 | Repartir la carga del grupo entre los días | 2 (`PesoTiempos`) |
| SC-09 | Evitar más de 6 h seguidas | 3 (`PesoMaxHorasSeguidas`) |
| Guarda de aulas | Sesiones presenciales concurrentes ≤ aulas disponibles | 1000 (fijo) |
| SC-PRES | Penalización por ceder presencialidad | 4, **informativo**: se reporta aparte, no suma |

`PesoBalanceSemanas` (SC-BAL) sigue en el contrato pero no afecta: con un gen por sesión no hay desbalance posible.

## Esquema de BD (PostgreSQL, snake_case, PK `uuid` asignada por la app)

Tablas: `Facultades`, `Programas`, `Asignaturas`, `Grupos`, `Sesiones`, `AsignacionesSemanales`, `Horarios`, `Docentes`, `DisponibilidadDocente` (M:N docente–bloque), `Espacios`, `BloqueTiempos`, `TiposAlternancia`, `CriteriosCesionAlternancia`.

- JSON en texto: `grupos.requisitos_espacio`, `*.disponibilidad_ui_json`, `horarios.sesion_ids` (lista lógica sin FK).
- Índice único `ux_asignacion_semanal_espacio_conflicto (horario_id, espacio_id, semana, bloque_tiempo_id)`. No ve el choque entre una sesión que no alterna (fila en A, aula ocupada también en B) y un TipoB (fila en B); esa garantía la dan CP-SAT y el validador (HC-S01).
- FKs: todas las relaciones de la jerarquía; `asignaciones_semanales.sesion_id` en cascada, el resto Restrict.
- 28 migraciones (`InitialCreate` → `M19_JerarquiaSesionGrupoAsignatura`). Las de datos sanean huérfanos antes de crear la FK. Se aplican solas al arrancar el API.
