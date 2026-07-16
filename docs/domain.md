# Dominio SOEA

## Entidades principales

| Entidad | Campos críticos | Notas |
|---|---|---|
| `Asignatura` | `Id`, `Nombre`, `Codigo`, `HorasPorSesion`, `SesionesPorSemana`, `TipoAlternancia`, `TipoEspacio`, `DocenteId?`, `Categoria`, `HoraInicioMin?`, `HoraFinMax?` | Duración fija en creación; Alternancia derivada de `sesionesLaboratorioSemestre` vs. umbral (`DeterminarAlternancia`, default 8 → TipoA; &gt;8 → TipoB; &lt;8 → SinAlternancia) o fijada manualmente vía `EstablecerAlternancia` (override de Rosa). **Andamiaje presencial-first (Etapa 1):** `Categoria` (CategoriaAsignatura, default `Obligatoria`) rige la prioridad de presencialidad; `HoraInicioMin/HoraFinMax` (nullable) son la ventana horaria por Secretaría Académica (CR-07). La lógica que los consume (SC-PRES, HC-VH) es de etapas posteriores. |
| `BloqueTiempo` | `DiaDeSemana`, `HoraInicio`, `HoraFin`, `EsSabado` | Generado en memoria por request (`GrillaInstitucional`, C1 auditoría); Lun–Vie 06:00–22:00, Sáb 06:00–13:00. **Dato bloqueante (CLAUDE.md §4):** el rango real no está confirmado por Rosa — esta doc, `hard-constraints.md` (archivado) y el código traían 3 valores distintos; el valor de arriba es el que corre en producción hoy, no una confirmación. |
| `Docente` | `Id`, `Nombre`, `Apellido`, `Correo`, `MaximoHorasSemanales`, `Disponibilidad[]` | M:N con FranjasHorarias |
| `Espacio` | `Id`, `Nombre`, `TipoEspacio`, `Capacidad`, `Ubicacion`, `Piso` | `TipoEspacio`: Salon / Laboratorio / Auditorio |
| `Grupo` | `Id`, `Codigo`, `NumeroEstudiantes`, `TipoAlternancia`, `ProgramaId` | Cohorte académica; determina semanas presenciales/virtuales |
| `Sesion` | `Id`, `AsignaturaId`, `DocenteId?`, `EspacioId?`, `GrupoId?`, `BloqueId`, `TipoAlternancia`, `Modalidad`, `DuracionHoras`, `TipoFlujo`, `PatronAlternanciaId?`, `Bloqueada` | Unidad lógica inmutable. `EspacioId = null` → sesión virtual. **CR-02 (Etapa 2):** `DocenteId` es nullable — el docente es opcional (modelo presencial-first); deja de ser eje de generación. Permanece única; las semanas A/B se materializan en `AsignacionSemanal`. **Andamiaje presencial-first (Etapa 1):** `TipoFlujo` (TipoFlujo, default `Laboratorio`) distingue laboratorio vs aula/virtual; `PatronAlternanciaId?` es FK al catálogo `TipoAlternanciaConfig` (null = presencial puro); `Bloqueada` impide que el optimizador altere su alternancia. La lógica de motor que los consume es de etapas posteriores. |
| `AsignacionSemanal` | `Id`, `SesionId`, `Semana` (`A`/`B`), `BloqueTiempoId`, `EspacioId?`, `Modalidad` | Materialización de una `Sesion` en una semana del ciclo. Cada sesión factible produce **dos** instancias. Invariante: `Modalidad=Virtual → EspacioId=null` (regla 9). |
| `Horario` | `Id`, `Semestre`, `Estado`, `HardConstraintViolations`, `SoftConstraintFitnessScore` | No publicable si violations > 0; contiene `SesioneIds` lógicas |
| `Programa` | `Id`, `Nombre`, `Codigo`, `FacultadId` | Plan académico |
| `Facultad` | `Id`, `Nombre` | Unidad organizativa |

## Enums del andamiaje presencial-first (Etapa 1)

| Enum | Valores | Uso |
|---|---|---|
| `TipoFlujo` | `Laboratorio`, `AulaVirtual` | Flujo de la sesión; cada flujo se programa por separado (CR-03). Default en creación: `Laboratorio`. |
| `CategoriaAsignatura` | `Obligatoria`, `Optativa`, `Electiva` | Prioridad de presencialidad (CR-05). Default: `Obligatoria` (preserva presencialidad primero). |

> Estos enums y campos son **andamiaje de datos**: el esquema y las entidades quedan listos, pero ningún motor los lee aún. La lógica (grafo por grupo, HC-CAP, HC-VH, SC-PRES, presencial-first) se implementa en etapas posteriores. Ver `docs/PLAN_MAESTRO_PresencialFirst.md`.

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

Impacto en Fase 1: hay arista en el grafo de conflictos si `TipoAlternancia` es igual en ambas sesiones, O si alguna es `SinAlternancia`. La Fase 1 opera sobre sesiones lógicas (sin semana) — no cambia.

---

## Restricciones duras (hard constraints)

El motor CP-SAT (Fase 2) las aplica todas **por semana** (A y B por separado). Un horario con violations > 0 no puede publicarse.

### Espacio
| ID | Regla | Evaluación |
|---|---|---|
| HC-S01 | Un espacio no puede alojar dos sesiones presenciales simultáneas en la misma semana | por `(espacio, Semana)`; re-verificada en el validador post-gen (auditoría A1) |
| HC-CAP (ex HC-S02) | El total de estudiantes físicamente presentes no puede exceder la capacidad del espacio | candidatos filtrados en CP-SAT y en `AsignadorEspacios` (Fase 3); re-verificada en el validador post-gen (auditoría A1) — código usa el nombre `HC-CAP` |
| HC-S03 | Sesión que requiere laboratorio → debe asignarse a espacio tipo Laboratorio | por sesión; re-verificada en el validador post-gen (auditoría A1) |
| HC-S04 | Asignaciones virtuales no tienen espacio físico (`EspacioId = null`) | invariante de entidad |
| HC-S05 | Si la asignatura tiene espacio fijo (`EspacioFijoId`), toda sesión presencial usa ese espacio | único candidato en CP-SAT y en `AsignadorEspacios` (auditoría A1); re-verificada en el validador post-gen |

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

Las filas "no implementada" quedaron documentadas en incrementos anteriores como plan, nunca se codificaron en `EvaluadorFitness`; de-scope explícito, no un bug (C3 auditoría).

---

## Esquema de BD (SOEAdb · PostgreSQL localhost:5432)

```
Programas (1) ──→ (N) Asignaturas
                          ↓
                      Sesiones ←─── Horarios
                      ↙  ↓  ↘        ↑
                 Docentes  Espacios  AsignacionesSemanales
                      ↑              (SesionId, Semana A/B)
              DisponibilidadDocente
```

### Tablas

| Tabla | PK | Campos destacados |
|---|---|---|
| `Horarios` | `Id` uuid | `Semestre`, `Estado` (borrador/validado/aprobado/activo), `Hard_constraint_violations`, `Soft_constraint_fitness_score` |
| `Sesiones` | `Id` uuid | `Asignatura_id`, `Espacio_id` (nullable), `grupo_id` (nullable), `docente_id` (nullable, CR-02), `bloque_tiempo_id`, `alternancia`, `modalidad`, `duracion_horas`, `tipo_flujo` (default `'Laboratorio'`), `patron_alternancia_id` (FK nullable → `TiposAlternancia`, SetNull), `bloqueada` (default false) |
| `AsignacionesSemanales` | `Id` uuid | `sesion_id` FK, `semana` (`"A"`/`"B"`), `bloque_tiempo_id`, `espacio_id` (nullable), `modalidad`. Índice único `(sesion_id, semana)`. |
| `Asignaturas` | `Id` uuid | `Codigo` (UNIQUE), `BloqueSemanales`, `RequiereLab`, `Alternancia`, `ProgramaId`, `DocenteId`, `espacio_fijo_id` (nullable), `categoria` (default `'Obligatoria'`), `hora_inicio_min` (time nullable), `hora_fin_max` (time nullable) |
| `Docentes` | `Id` uuid | `Correo` (UNIQUE), `Maximo_hrs_semanales` |
| `Espacios` | `Id` uuid | `Tipo`, `Capacidad`, `Ubicacion`, `Piso` |
| `FranjasHorarias` | `Id` uuid | `Dia_semana`, `Hora_inicio`, `Hora_fin` |
| `DisponibilidadDocente` | (`Docente_id`, `Franja_id`) | tabla de unión M:N |
| `Programas` | `Id` uuid | `Codigo` (UNIQUE) |
| `Facultades` | `Id` uuid | `Nombre` |

`espacio_id = null` en `AsignacionesSemanales` identifica asignaciones virtuales (regla 9). La migración `HorarioBiSemanal` creó la tabla `AsignacionesSemanales` con tres índices: `ix_asignacion_semanal_sesion_id`, `ux_asignacion_semanal_sesion_semana` (único), `ix_asignacion_semanal_espacio_conflicto`.
