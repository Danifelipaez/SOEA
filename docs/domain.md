# Dominio SOEA

## Entidades principales

> **Actualizado 2026-08-07** contra el código real (`src/SOEA.Domain`), tras el pivote "Grupo como eje" (migraciones `P1_GrupoComoEje`, `P2_ParejaAlternancia`, agosto 2026). Ver `docs/CambioDeRumbo_AsignaturaGrupo_PresencialFirst.md` para el porqué del cambio.

| Entidad | Campos críticos | Notas |
|---|---|---|
| `Asignatura` | `Id`, `Nombre`, `Codigo`, `SesionesTeoriaPresencialSemana`/`HorasTeoriaPresencial`, `SesionesTeoriaVirtualSemana`/`HorasTeoriaVirtual`, `SesionesLaboratorioSemana`/`HorasLaboratorio`, `SesionesLaboratorioSemestre`, `TipoAlternancia`, `ProgramaId`, `Categoria`, `HoraInicioMin?`, `HoraFinMax?`, `EsCandidataAlternancia` | **Ya NO tiene `DocenteId` ni `EspacioFijoId`** (se movieron a `Grupo` — migraciones `Fase2DocenteEnGrupo` 2026-07-17 y `P1_GrupoComoEje` 2026-08-06). Modular: hasta 3 "tracks" independientes (teoría presencial / teoría virtual fija / laboratorio), cada uno con su propio número de sesiones-semana y horas-por-sesión (migración `Etapa6DesglosePorTipoSesion`, 2026-07-09); debe tener al menos un track con sesiones/semana &gt; 0. `HorasPorSesion`/`SesionesPorSemana` sobreviven solo como **alias de solo lectura** del track de teoría presencial (compatibilidad con `LectorExcel`/`ImportarCurriculumService`, no usar en código nuevo). Duración fija en creación; Alternancia derivada de `SesionesLaboratorioSemestre` vs. umbral (`DeterminarAlternancia`, default 8 → TipoA; &gt;8 → TipoB; &lt;8 → SinAlternancia) o fijada manualmente vía `EstablecerAlternancia`. `Categoria` (default `Obligatoria`) y `EsCandidataAlternancia` alimentan el orden de cesión a alternancia (`CriterioCesionAlternancia`, ver abajo) — **activo**, no andamiaje. `HoraInicioMin/HoraFinMax` (nullable) son la ventana horaria por Secretaría Académica (CR-07) y son hard constraint (HC-VH), activa en las 3 fases. |
| `BloqueTiempo` | `DiaDeSemana`, `HoraInicio`, `HoraFin` | Generado en memoria por request (`GrillaInstitucional`, C1 auditoría), con IDs deterministas (hash de día+hora) para que un `BloqueTiempoId` persistido sea reencontrable en runs futuros; Lun–Vie 06:00–22:00, Sáb 06:00–13:00. **Dato bloqueante (CLAUDE.md §4):** el rango real no está confirmado por Rosa; el valor de arriba es el que corre en producción hoy, no una confirmación. |
| `Docente` | `Id`, `Nombre`, `Apellido?`, `Correo?`, `MaximoHorasSemanales`, `Disponibilidad[]` (`FranjaHoraria`), `BloquesDisponibles[]`, `CedulaIdentidad?`, `DisponibilidadUiJson?` | M:N con `BloqueTiempo` vía tabla `DisponibilidadDocente`. **Fuera del pipeline de generación (CR-08):** estos campos solo importan para la edición/reporte posteriores a generar el horario (`PATCH /api/sesiones/{id}/docente`). |
| `Espacio` | `Id`, `Nombre`, `TipoEspacio`, `Capacidad`, `Edificio?`, `Piso?` | `TipoEspacio`: Salon / Laboratorio / Auditorio |
| `Grupo` | `Id`, `Codigo?`, `Nombre`, `AsignaturaId?`, `FacultadId?`, `ProgramaId`, `DocenteId?`, `EstudiantesInscritos`, `TipoAlternancia`, `DisponibilidadUiJson?`, `RequisitosEspacio[]` | **Eje actual del modelo (CR-08/"Grupo como eje").** Ya no es solo la cohorte: desde `Fase2DocenteEnGrupo` (2026-07-17) tiene el `DocenteId` que antes vivía en `Asignatura` (la misma asignatura la dictan docentes distintos en grupos distintos); desde `P1_GrupoComoEje` (2026-08-06) tiene `RequisitosEspacio` (lista de `RequisitoEspacio`, uno por `TipoSesion` — reemplaza a `Asignatura.EspacioFijoId`) y `DisponibilidadUiJson` (JSON crudo por día, fuente de `ObtenerDisponibilidadSemanal()` → `DisponibilidadSemanal`, el dato que arma HC-G01). `EstudiantesInscritos` es la fuente de aforo/capacidad (HC-CAP). |
| `Sesion` | `Id`, `AsignaturaId`, `DocenteId?`, `BloqueTiempoId`, `EspacioId?`, `GrupoId?`, `TipoAlternancia`, `Modalidad`, `Estado`, `DuracionHoras`, `TipoFlujo`, `PatronAlternanciaId?`, `ParejaAlternanciaId?`, `Bloqueada`, `CedidaPorSaturacion`, `MotivoConflicto` | Unidad lógica inmutable. `EspacioId = null` → sesión virtual. **CR-02 (Etapa 2):** `DocenteId` es nullable — el docente es opcional; deja de ser eje de generación. Permanece única; las semanas A/B se materializan en `AsignacionSemanal`. `TipoFlujo` (default `Laboratorio`) distingue laboratorio vs aula/virtual; `PatronAlternanciaId?` es FK de trazabilidad al catálogo `TipoAlternanciaConfig` (null = presencial puro); `Bloqueada` impide que el optimizador altere su alternancia. **`ParejaAlternanciaId?` (nuevo, migración `P2_ParejaAlternancia`, 2026-08-07):** Guid libre (sin FK) que comparten exactamente 2 sesiones normalmente de asignaturas distintas — la "alternancia por parejas" (Tipo C dinámico): una es presencial en semana A y la otra en semana B, compartiendo el mismo bloque y el mismo espacio físico entre semanas (regla HC-ALT, ver abajo). Etiquetado `VERIFICA` en el propio código: la semántica de "alternancia atómica por espacio" está implementada y probada, pero aún no confirmada formalmente con la coordinadora académica. **`CedidaPorSaturacion` (nuevo):** true si esta sesión fue cedida a alternancia o virtualizada por el mecanismo automático de presencial-first (saturación de espacio), no por elección manual del usuario — habilita `RevertirCesion()`, el pase que intenta devolverla a presencial tras la Fase 3 si ya cabe. Todos estos campos están **activos y en uso** por el pipeline (no son andamiaje). |
| `AsignacionSemanal` | `Id`, `SesionId`, `Semana` (`A`/`B`), `BloqueTiempoId`, `EspacioId?`, `Modalidad` | Materialización de una `Sesion` en una semana del ciclo. Cada sesión factible produce **dos** instancias. Invariante: `Modalidad=Virtual → EspacioId=null` (regla 9). |
| `Horario` | `Id`, `Semestre`, `GeneradoEn`, `Estado`, `SesioneIds[]`, `ViolacionesRestriccionesDuras`, `PuntajeFitness` | `Estado` ∈ `{Borrador, Aprobado, Publicado, Archivado}` (`EstadoHorario`, no existen "validado" ni "activo"). `MarcarComoPublicado()` lanza excepción si `ViolacionesRestriccionesDuras > 0`. |
| `Programa` | `Id`, `Nombre`, `FacultadId` | Plan académico |
| `Facultad` | `Id`, `Nombre` | Unidad organizativa |
| `TipoAlternanciaConfig` | `Id`, `Nombre`, `PatronBase`, `SemanasPresenciales`, `Color`, `EsSistema`, `Activo` | Catálogo **fijo** de 3 filas de sistema (TipoA/TipoB/SinAlternancia), sembradas por la migración `CatalogoTiposAlternancia`. **Ya no es editable desde la UI** (no tiene controlador REST) — sobrevive solo como catálogo de IDs estables referenciado por `Sesion.PatronAlternanciaId` para trazabilidad. La decisión de "qué sesión cede a alternancia" ya no pasa por aquí, la toma dinámicamente `CriterioCesionAlternancia`. |
| `CriterioCesionAlternancia` | `Id`, `Criterio` (`CriterioElegibilidadAlternancia`: `Electiva`/`Optativa`/`Elegible`/`MultiplesSesiones`), `Orden`, `Activo` | Lista ordenada y activable (4 filas de sistema, sembradas por `CesionAlternanciaConfigurable` + `AgregarCriteriosOptativaYMultiplesSesiones`) que decide, en orden, qué sesiones son candidatas a ceder presencialidad cuando la demanda satura la capacidad de espacios. `MultiplesSesiones` no otorga elegibilidad por sí sola — solo desempata el orden entre candidatas ya elegibles por otro criterio. Editable vía `GET`/`PATCH /api/criterioscesionalternancia`. |

## Enums relevantes al modelo presencial-first

| Enum | Valores | Uso |
|---|---|---|
| `TipoFlujo` | `Laboratorio`, `AulaVirtual` | Flujo de la sesión; cada flujo se programa por separado (CR-03). Default en creación: `Laboratorio`. |
| `CategoriaAsignatura` | `Obligatoria`, `Optativa`, `Electiva` | Prioridad de presencialidad (CR-05). Default: `Obligatoria` (preserva presencialidad primero). |
| `CriterioElegibilidadAlternancia` | `Electiva`, `Elegible`, `Optativa`, `MultiplesSesiones` | Ver `CriterioCesionAlternancia` arriba. |

> **Ya no es andamiaje.** A diferencia de lo que decía una versión anterior de este documento, estos campos y el mecanismo de cesión dinámica de presencialidad SÍ están implementados y activos en el pipeline (`GenerarHorarioService.AplicarPrioridadPresencial`/`CederSiguienteCandidatoLab`, más el pase de reversión post-Fase 3 en `MotorGenetico`) — ver `docs/algorithms.md`, sección "Presencial-First". Histórico de las 5 etapas de implementación en `docs/PLAN_MAESTRO_PresencialFirst.md` (cerrado 2026-07-11) y del trabajo posterior en `docs/PLAN_MAESTRO_Pendientes.md`.

## Modelo de alternancia y ciclo bi-semanal

El horario producido por SOEA cubre **dos semanas** (Semana A / Semana B), que se repiten a lo largo del semestre.

| Semana | Paridad | TipoA | TipoB | SinAlternancia |
|---|---|---|---|---|
| **A** | impares (1, 3, 5 …) | Presencial (espacio asignado) | Virtual (sin espacio) | Presencial |
| **B** | pares (2, 4, 6 …) | Virtual (sin espacio) | Presencial (espacio asignado) | Presencial |

La modalidad por semana es un **dato derivado fijo** de `TipoAlternancia` — no la elige el solver. Una sesión con `Modalidad=Virtual` intrínseca (asignatura totalmente en línea) es virtual en **ambas** semanas independientemente de la alternancia.

El enum `SemanaAcademica { A, B }` identifica cada semana del ciclo. La entidad `AsignacionSemanal` materializa el par `(Sesion, Semana)` → el resultado visible del pipeline.

| Regla | Descripción |
|---|---|
| ALT-01 | Tipo A + Tipo B pueden compartir espacio/franja — nunca coinciden físicamente (regla A/B) |
| ALT-02 | Dos Tipo A **no** pueden compartir espacio/franja en la misma semana |
| ALT-03 | Dos Tipo B **no** pueden compartir espacio/franja en la misma semana |
| ALT-04 | Sesiones virtuales no consumen capacidad de espacio (`EspacioId = null`) |
| ALT-05 | Para TipoA/TipoB la **franja** es la misma en ambas semanas (regla 9 — la virtual hereda el bloque de la presencial) |
| ALT-06 | `SinAlternancia` = presencial en ambas semanas; puede diferir de franja entre A y B |

Impacto en Fase 1 (`ConstructorGrafoConflictos.TienenConflicto`, verificado en código — corrige una versión anterior de esta nota): hay arista en el grafo de conflictos si **(a)** ambas sesiones comparten el mismo `GrupoId` (eje primario, CR-08), o **(b)** ambas comparten el mismo `EspacioId` — salvo que una sea TipoA y la otra TipoB (excepción ALT-01, nunca coinciden físicamente la misma semana). El `TipoAlternancia` **no** genera arista por sí solo si no hay grupo o espacio en común. La Fase 1 opera sobre sesiones lógicas (sin semana) — no cambia.

**Alternancia por parejas ("Tipo C" dinámico, nuevo — migración `P2_ParejaAlternancia`, 2026-08-07):** además del catálogo fijo TipoA/TipoB/SinAlternancia, el pipeline puede emparejar dinámicamente dos sesiones de teoría (normalmente de asignaturas distintas, con requisito de espacio compatible) bajo saturación de aforo: una se marca TipoA y la otra TipoB, comparten `Sesion.ParejaAlternanciaId`, y quedan forzadas por CP-SAT (regla **HC-ALT**) a compartir el mismo bloque de tiempo y el mismo espacio físico en sus respectivas semanas presenciales. Es la alternativa "conserva más presencialidad" antes de virtualizar por completo una sesión — ver `docs/algorithms.md`, sección Presencial-First.

---

## Restricciones duras (hard constraints)

El motor CP-SAT (Fase 2) las aplica todas **por semana** (A y B por separado). Un horario con violations > 0 no puede publicarse.

### Espacio
| ID | Regla | Evaluación |
|---|---|---|
| HC-S01 | Un espacio no puede alojar dos sesiones presenciales simultáneas en la misma semana | por `(espacio, Semana)`; re-verificada en el validador post-gen (auditoría A1) |
| HC-CAP (ex HC-S02) | El total de estudiantes físicamente presentes no puede exceder la capacidad del espacio | candidatos filtrados en CP-SAT y en `AsignadorEspacios` (Fase 3); re-verificada en el validador post-gen (auditoría A1) — código usa el nombre `HC-CAP` |
| HC-S03 | La sesión debe asignarse a un espacio cuyo tipo cumpla el `RequisitoEspacio` del `Grupo` para ese `TipoSesion` (teoría presencial/virtual/laboratorio) | por sesión, vía `CalculadorEspaciosSesion` (Domain, fuente única compartida por las 3 fases); re-verificada en el validador post-gen (auditoría A1) |
| HC-S04 | Asignaciones virtuales no tienen espacio físico (`EspacioId = null`) | invariante de entidad |
| HC-S05 | **Corregido (2026-08-07): la fuente ya no es `Asignatura.EspacioFijoId` (columna eliminada, migración `P1_GrupoComoEje`)**, sino `Grupo.RequisitosEspacio` — si el requisito de espacio del grupo para ese tipo de sesión fija un `EspacioId` concreto, toda sesión presencial de ese tipo usa ese espacio | único candidato en CP-SAT y en `AsignadorEspacios` (auditoría A1); re-verificada en el validador post-gen |

### Docente
> **CR-08 (Etapa 3): el docente está fuera del pipeline de generación** — se asigna *después* de generar el horario via `PATCH /api/sesiones/{id}/docente` (Etapa 4). Las hard constraints de docente (HC-I01, HC-I03) salieron de la generación; HC-C01 (cohorte) asume el rol de serialización. HC-I02 quedó degradada (Etapa 2). **En edición (Etapa 4):** solape de franja es hard (409); disponibilidad y carga son blandas (advertencias).

| ID | Regla | Evaluación |
|---|---|---|
| HC-I01 | Docente sin dos sesiones en la misma franja. **Fuera de generación (CR-08):** lo subsume HC-C01. **En edición (Etapa 4):** solape → 409 en `PATCH /api/sesiones/{id}/docente`. | `AsignarDocenteSesionService` |
| HC-I02 | Franja dentro de la disponibilidad del docente. **Degradada (Etapa 2):** NO es hard en generación. **En edición (Etapa 4):** bloque fuera de `Docente.BloquesDisponibles` → advertencia (no rechazo). | advertencia |
| HC-I03 | Docente no excede su máximo de horas semanales. **Fuera de generación (CR-08). En edición (Etapa 4):** carga > `MaximoHorasSemanales` → advertencia (no rechazo). | advertencia |

### Tiempo
| ID | Regla | Estado |
|---|---|---|
| HC-T01 | Sesiones dentro del horario institucional (06:00–22:00 L-V; sábado 06:00–13:00 — ver dato bloqueante arriba) | implícito: la grilla canónica (`GrillaInstitucional`) no genera bloques fuera de ese rango |
| HC-T02 | Sesiones de laboratorio no empiezan después de las 19:30 | **no implementada** — sin confirmación de Rosa, de-scope explícito (C1 auditoría) |
| HC-T03 | Bloques de 3 h deben ser horas consecutivas en el mismo día | implícito: `BloquesPlanner.CabeEnDia` no permite spans que crucen día |
| HC-T04 | Sin sesiones en receso del mediodía (12:00–13:00) salvo permiso explícito | **no implementada** — la grilla no modela receso; de-scope explícito (C1 auditoría) |
| HC-T05 | Bloques divididos no se programan en días consecutivos | **no implementada** (`Sesion.EsBloque`/`EstaDividida` son andamiaje sin uso) |

### Cohorte / Grupo
> **CR-08 (Etapa 3): el grupo es el eje de no-solapamiento.** Un run de generación es una sola cohorte implícita (todas las sesiones comparten `GrupoId`), así que HC-C01 las serializa: el grupo no puede estar en dos sesiones a la vez. Se aplica en Fase 1 (arista del grafo por `GrupoId`), Fase 2 (NoOverlap por `(grupo, Semana)` — presenciales + virtuales) y el validador post-generación.

| ID | Regla | Evaluación |
|---|---|---|
| HC-C01 | Una cohorte/grupo no puede tener dos sesiones en la misma franja (presencial o virtual) | por `(grupo, Semana)`; re-verificada en el validador post-gen |
| HC-G01 | Si el grupo declara disponibilidad (Matutino/Vespertino), toda sesión inicia dentro de esa franja | dominio de inicios en las 3 fases (`CalculadorDominioSesion`, auditoría A1); re-verificada en el validador post-gen |
| HC-SEP | Sesiones semanales repetidas del mismo `(grupo, asignatura, TipoSesion)` deben quedar separadas por al menos 2 posiciones de día de la semana | **implementada** (nueva, agosto 2026): CP-SAT vía `AddElement` sobre el día de cada `start`, más re-verificación en el validador post-gen |
| HC-ALT | Toda pareja de sesiones con el mismo `ParejaAlternanciaId` debe tener tipos opuestos (TipoA/TipoB), coincidir de bloque en su semana presencial y compartir el mismo espacio físico entre semanas | **implementada** (nueva, agosto 2026 — "alternancia por parejas"/Tipo C dinámico): CP-SAT + re-verificada en el validador post-gen. Etiquetada `VERIFICA` en el código: implementada y probada, pendiente de confirmación formal con la coordinadora académica |
| HC-C02 | Horas totales programadas deben coincidir con la malla curricular | **no implementada** (C3 auditoría) |

### Asignatura
| ID | Regla |
|---|---|
| ~~HC-SU01~~ | **Obsoleta (C3 auditoría, confirmado por el equipo).** El diseño cambió: TipoA/TipoB dejaron de significar "8+8 inmodificable" y pasaron a ser únicamente **tipos de alternancia semanal** (presencial una semana, virtual la otra — ver §Modelo de alternancia arriba). No hay restricción de bloques de 8h consecutivas; `Sesion.EsBloque`/`EstaDividida` son andamiaje sin uso. |
| HC-SU02 | Asignaturas `SinAlternancia` → sesiones en todas las semanas, no solo alternas |
| HC-VH | Si la asignatura declara `HoraInicioMin`/`HoraFinMax`, toda sesión cae dentro de esa ventana | dominio de inicios en las 3 fases (`CalculadorDominioSesion`, auditoría A1/B3); re-verificada en el validador post-gen |

---

## Restricciones blandas (soft constraints)

Usadas en la función de fitness del algoritmo genético (Fase 3). `fitness = Σ(peso_i × violaciones_i)` — **menor es mejor**.

> **CR-08 (Etapa 3): la ergonomía se mide por cohorte (grupo), no por docente** (el docente está fuera del pipeline). Los objetivos implementados en `EvaluadorFitness` — huecos (SC-01), >N horas seguidas (SC-09), balance entre días de la grilla (SC-06) y balance A/B (SC-BAL) — se evalúan sobre las sesiones del **grupo**. SC-06 reparte la carga del grupo entre los días operativos de la grilla (el grupo no declara disponibilidad).

| ID | Regla | Peso | Estado |
|---|---|---|---|
| SC-01 | Horarios de la cohorte compactos (minimizar huecos inactivos) | 3 | implementada (`EvaluadorFitness.SC01_HuecosOciosos`) |
| SC-02 | Horarios de cohorte compactos (minimizar huecos para estudiantes) | — | **no implementada** — duplica el objetivo de SC-01 |
| SC-03 | Sesiones de la misma cohorte en un día sin más de 1 h de hueco | — | **no implementada** |
| SC-04 | Evitar primera sesión antes de 07:00 o última después de 19:00 | — | **no implementada** |
| SC-05 | Misma aula para misma asignatura/cohorte entre semanas (estabilidad) | — | **no implementada** — el aula no está en el cromosoma, se decide en un pase posterior (`AsignadorEspacios`) sin este objetivo |
| SC-06 | Distribuir la carga de la cohorte uniformemente entre los días de la grilla | 2 | implementada (`EvaluadorFitness.SC06_BalanceEntreDias`) |
| SC-07 | Minimizar número de espacios distintos que usa una cohorte en un día | — | **no implementada** |
| SC-08 | Asignaturas relacionadas (mismo programa/cohorte) en franjas adyacentes | — | **no implementada** |
| SC-09 | Evitar rachas de la cohorte de más de `UmbralHorasSeguidas` (default 6) horas seguidas | 3 (antes documentado 1 — C2 auditoría corrige el default real del motor) | implementada (`EvaluadorFitness.SC09_HorasSeguidas`) |
| SC-BAL | Balancear la carga horaria de la cohorte entre Semana A y Semana B (solo aplica a `SinAlternancia`, vía `StartB`) | 2 | implementada (`EvaluadorFitness.SCBAL_DesbalanceEntreSemanas`) |
| SC-PRES | Penaliza ceder presencialidad de sesiones de alta prioridad, proporcional a categoría/estructura | 4 | implementada pero **informativa desde B2 auditoría**: es constante para el conjunto de sesiones del run (el GA nunca mueve la alternancia) — se reporta aparte (`PenalizacionPresencial`), no suma al fitness |
| (sin ID propio) Guarda de capacidad de aulas (`GuardaCapacidadAulas`) | Penaliza si, en cualquier `(semana, día)`, el número de sesiones presenciales concurrentes excede el número de espacios disponibles | 1000 | implementada (`EvaluadorFitness.GuardaCapacidadAulas`) — peso deliberadamente alto para que el GA nunca prefiera una solución que sature aforo aunque mejore otros términos; no tenía fila propia en versiones anteriores de esta tabla |

Las filas "no implementada" quedaron documentadas en incrementos anteriores como plan, nunca se codificaron en `EvaluadorFitness`; de-scope explícito, no un bug (C3 auditoría).

---

## Esquema de BD (SOEAdb · PostgreSQL)

> Actualizado 2026-08-07 contra `src/SOEA.Infrastructure.Data/Configurations/*.cs` y las 19 migraciones aplicadas (`InitialCreate` → `FixMotivoConflictoColumnMapping`). La versión anterior de este diagrama no incluía `Grupos`, `TiposAlternancia` ni `CriteriosCesionAlternancia`, y la fila de `Asignaturas` listaba columnas ya eliminadas (`DocenteId`, `espacio_fijo_id`).

```
Facultades (1) ──→ (N) Programas ──→ (N) Grupos ──→ (N) Sesiones ←─── Horarios (lógico, por SesioneIds)
                                         ↑  ↑             ↙  ↘
                                    Asignaturas       Docentes  Espacios
                                    (N:1, opcional)   (opcional) (opcional)
                                         ↑                          ↑
                                  TiposAlternancia          AsignacionesSemanales
                                  (catálogo, 3 filas)       (SesionId, Semana A/B)
                                  CriteriosCesionAlternancia
                                  (catálogo, 4 filas)
Docentes ←── DisponibilidadDocente (M:N) ──→ BloqueTiempo (generado en memoria, sin tabla propia)
```

### Tablas

| Tabla | PK | Campos destacados |
|---|---|---|
| `Horarios` | `Id` uuid | `Semestre`, `Estado` (`Borrador`/`Aprobado`/`Publicado`/`Archivado` — no existen "validado" ni "activo"), `ViolacionesRestriccionesDuras`, `PuntajeFitness`, `SesioneIds` (lista lógica, no FK) |
| `Sesiones` | `Id` uuid | `asignatura_id`, `espacio_id` (nullable), `grupo_id` (nullable), `docente_id` (nullable, CR-02), `bloque_tiempo_id`, `alternancia`, `modalidad`, `duracion_horas`, `es_bloque`, `esta_dividida`, `motivo_conflicto` (renombrada a snake_case en `FixMotivoConflictoColumnMapping`, 2026-08-07), `tipo_flujo` (default `'Laboratorio'`), `patron_alternancia_id` (FK nullable → `TiposAlternancia`, SetNull), `pareja_alternancia_id` (nullable, sin FK — nuevo en `P2_ParejaAlternancia`, 2026-08-07), `bloqueada` (default false), `cedida_por_saturacion` (default false, desde `CesionAlternanciaConfigurable`) |
| `AsignacionesSemanales` | `Id` uuid | `sesion_id` FK, `semana` (`"A"`/`"B"`), `bloque_tiempo_id`, `espacio_id` (nullable), `modalidad`. Índice único `(sesion_id, semana)`. |
| `Asignaturas` | `Id` uuid | `Codigo` (UNIQUE con `programa_id`), `sesiones_teoria_presencial_semana`/`horas_teoria_presencial`, `sesiones_teoria_virtual_semana`/`horas_teoria_virtual`, `sesiones_laboratorio_semana`/`horas_laboratorio`, `sesiones_laboratorio_semestre`, `alternancia`, `programa_id`, `categoria` (default `'Obligatoria'`), `hora_inicio_min` (time nullable), `hora_fin_max` (time nullable), `es_candidata_alternancia` (bool). **Ya NO tiene `DocenteId` ni `espacio_fijo_id`** (eliminadas por `Fase2DocenteEnGrupo` y `P1_GrupoComoEje` respectivamente — ambas se movieron a `Grupos`). |
| `Grupos` | `Id` uuid | `codigo` (nullable, único filtrado `WHERE codigo IS NOT NULL`), `nombre`, `asignatura_id` (nullable), `facultad_id` (nullable), `programa_id`, `docente_id` (nullable — **agregada por `Fase2DocenteEnGrupo`**, es el docente que dicta la asignatura para este grupo), `estudiantes_inscritos` (fuente de HC-CAP), `alternancia`, `disponibilidad_ui_json` (nullable, fuente de HC-G01), `requisitos_espacio` (JSON en texto — **agregada por `P1_GrupoComoEje`**, reemplaza a `Asignaturas.espacio_fijo_id`). Ya no tiene `semestre` (columna eliminada por `Fase2DocenteEnGrupo`) ni `disponibilidad` (columna muerta eliminada por `P1_GrupoComoEje`, nunca se persistía realmente). |
| `Docentes` | `Id` uuid | `correo` (opcional, validado si presente), `maximo_horas_semanales`, `cedula_identidad` (nullable), `disponibilidad_ui_json` (nullable) |
| `Espacios` | `Id` uuid | `tipo`, `capacidad`, `edificio` (nullable), `piso` (nullable) |
| `BloqueTiempos` | `Id` uuid | `dia`, `hora_inicio`, `hora_fin` — IDs deterministas (hash día+hora), generados en memoria por request (`GrillaInstitucional`), no persisten entre runs de forma tradicional |
| `DisponibilidadDocente` | (`docente_id`, `bloque_tiempo_id`) | tabla de unión M:N |
| `Programas` | `Id` uuid | `nombre`, `facultad_id` |
| `Facultades` | `Id` uuid | `nombre` |
| `TiposAlternancia` | `Id` uuid | `nombre`, `patron_base`, `semanas_presenciales`, `color` (default `#607d8b`), `es_sistema`, `activo`. Catálogo fijo, 3 filas semilla (TipoA/TipoB/SinAlternancia), sembrado por la migración `CatalogoTiposAlternancia`. Sin controlador REST. |
| `CriteriosCesionAlternancia` | `Id` uuid | `criterio` (`Electiva`/`Optativa`/`Elegible`/`MultiplesSesiones`), `orden`, `activo`. Catálogo fijo, 4 filas semilla, sembrado por `CesionAlternanciaConfigurable` + `AgregarCriteriosOptativaYMultiplesSesiones`. Editable vía `GET`/`PATCH /api/criterioscesionalternancia`. |

`espacio_id = null` en `AsignacionesSemanales` identifica asignaciones virtuales (regla 9). La migración `HorarioBiSemanal` creó la tabla `AsignacionesSemanales` con tres índices: `ix_asignacion_semanal_sesion_id`, `ux_asignacion_semanal_sesion_semana` (único), `ix_asignacion_semanal_espacio_conflicto`. Todas las PK son `uuid` asignadas por la aplicación (`ValueGeneratedNever()`), no autoincrementales.

**19 migraciones aplicadas en orden** (`InitialCreate` 2026-05-12 → `FixMotivoConflictoColumnMapping` 2026-08-07). Ver el detalle completo de cada una en el consolidado del informe de práctica o en `src/SOEA.Infrastructure.Data/Migrations/`.
