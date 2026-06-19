# Dominio SOEA

## Entidades principales

| Entidad | Campos críticos | Notas |
|---|---|---|
| `Asignatura` | `Id`, `Nombre`, `Codigo`, `HorasPorSesion`, `SesionesPorSemana`, `TipoAlternancia`, `TipoEspacio`, `DocenteId?` | Duración fija en creación; Alternancia derivada de `sesionesLaboratorioSemestre` vs. umbral (`DeterminarAlternancia`, default 8 → TipoA; &gt;8 → TipoB; &lt;8 → SinAlternancia) o fijada manualmente vía `EstablecerAlternancia` (override de Rosa) |
| `BloqueTiempo` | `DiaDeSemana`, `HoraInicio`, `HoraFin`, `EsSabado` | Generado en memoria por request; Lun–Vie 07:00–20:00, Sáb 07:00–14:00 |
| `Docente` | `Id`, `Nombre`, `Apellido`, `Correo`, `MaximoHorasSemanales`, `Disponibilidad[]` | M:N con FranjasHorarias |
| `Espacio` | `Id`, `Nombre`, `TipoEspacio`, `Capacidad`, `Ubicacion`, `Piso` | `TipoEspacio`: Salon / Laboratorio / Auditorio |
| `Grupo` | `Id`, `Codigo`, `NumeroEstudiantes`, `TipoAlternancia`, `ProgramaId` | Cohorte académica; determina semanas presenciales/virtuales |
| `Sesion` | `Id`, `AsignaturaId`, `DocenteId`, `EspacioId?`, `BloqueId`, `TipoAlternancia`, `Modalidad`, `DuracionHoras` | Unidad lógica inmutable. `EspacioId = null` → sesión virtual. Permanece única; las semanas A/B se materializan en `AsignacionSemanal`. |
| `AsignacionSemanal` | `Id`, `SesionId`, `Semana` (`A`/`B`), `BloqueTiempoId`, `EspacioId?`, `Modalidad` | Materialización de una `Sesion` en una semana del ciclo. Cada sesión factible produce **dos** instancias. Invariante: `Modalidad=Virtual → EspacioId=null` (regla 9). |
| `Horario` | `Id`, `Semestre`, `Estado`, `HardConstraintViolations`, `SoftConstraintFitnessScore` | No publicable si violations > 0; contiene `SesioneIds` lógicas |
| `Programa` | `Id`, `Nombre`, `Codigo`, `FacultadId` | Plan académico |
| `Facultad` | `Id`, `Nombre` | Unidad organizativa |

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
| HC-S01 | Un espacio no puede alojar dos sesiones presenciales simultáneas en la misma semana | por `(espacio, Semana)` |
| HC-S02 | El total de estudiantes físicamente presentes no puede exceder la capacidad del espacio | por `(espacio, Semana)` |
| HC-S03 | Sesión que requiere laboratorio → debe asignarse a espacio tipo Laboratorio | por sesión |
| HC-S04 | Asignaciones virtuales no tienen espacio físico (`EspacioId = null`) | invariante de entidad |

### Docente
| ID | Regla | Evaluación |
|---|---|---|
| HC-I01 | Un docente no puede tener dos sesiones en la misma franja (presenciales + virtuales) | por `(docente, Semana)` |
| HC-I02 | La franja asignada debe estar dentro de la disponibilidad declarada del docente | por `(sesion, Semana)` |
| HC-I03 | El docente no puede exceder su máximo de horas semanales contratadas | por `(docente, Semana)` |

### Tiempo
| ID | Regla |
|---|---|
| HC-T01 | Sesiones dentro del horario institucional (07:00–20:00; sábado hasta 14:00) |
| HC-T02 | Sesiones de laboratorio no empiezan después de las 19:30 |
| HC-T03 | Bloques de 3 h deben ser horas consecutivas en el mismo día |
| HC-T04 | Sin sesiones en receso del mediodía (12:00–13:00) salvo permiso explícito |
| HC-T05 | Bloques divididos no se programan en días consecutivos |

### Cohorte
| ID | Regla |
|---|---|
| HC-C01 | Una cohorte no puede tener dos sesiones en la misma franja |
| HC-C02 | Horas totales programadas deben coincidir con la malla curricular |

### Asignatura
| ID | Regla |
|---|---|
| HC-SU01 | Asignaturas "siempre 8+8" → dos sesiones consecutivas de 8 h (hard constraint) |
| HC-SU02 | Asignaturas `SinAlternancia` → sesiones en todas las semanas, no solo alternas |

---

## Restricciones blandas (soft constraints)

Usadas en la función de fitness del algoritmo genético (Fase 3). `fitness = Σ(peso_i × violaciones_i)` — **menor es mejor**.

| ID | Regla | Peso |
|---|---|---|
| SC-01 | Horarios del docente compactos (minimizar huecos inactivos) | 3 |
| SC-02 | Horarios de cohorte compactos (minimizar huecos para estudiantes) | 3 |
| SC-03 | Sesiones de la misma cohorte en un día sin más de 1 h de hueco | 2 |
| SC-04 | Evitar primera sesión antes de 07:00 o última después de 19:00 | 2 |
| SC-05 | Misma aula para misma asignatura/cohorte entre semanas (estabilidad) | 2 |
| SC-06 | Distribuir carga del docente uniformemente en los días | 2 |
| SC-07 | Minimizar número de espacios distintos que usa una cohorte en un día | 1 |
| SC-08 | Asignaturas relacionadas (mismo programa/cohorte) en franjas adyacentes | 1 |
| SC-09 | Evitar asignar al docente el máximo de horas todos los días | 1 |
| SC-BAL | Balancear la carga horaria del docente entre Semana A y Semana B (Incremento 2; solo aplica a `SinAlternancia`, vía `StartB`) | 2 |

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
| `Sesiones` | `Id` uuid | `Asignatura_id`, `Espacio_id` (nullable), `Docente_id`, `bloque_tiempo_id`, `alternancia`, `modalidad`, `duracion_horas` |
| `AsignacionesSemanales` | `Id` uuid | `sesion_id` FK, `semana` (`"A"`/`"B"`), `bloque_tiempo_id`, `espacio_id` (nullable), `modalidad`. Índice único `(sesion_id, semana)`. |
| `Asignaturas` | `Id` uuid | `Codigo` (UNIQUE), `BloqueSemanales`, `RequiereLab`, `Alternancia`, `ProgramaId`, `DocenteId` |
| `Docentes` | `Id` uuid | `Correo` (UNIQUE), `Maximo_hrs_semanales` |
| `Espacios` | `Id` uuid | `Tipo`, `Capacidad`, `Ubicacion`, `Piso` |
| `FranjasHorarias` | `Id` uuid | `Dia_semana`, `Hora_inicio`, `Hora_fin` |
| `DisponibilidadDocente` | (`Docente_id`, `Franja_id`) | tabla de unión M:N |
| `Programas` | `Id` uuid | `Codigo` (UNIQUE) |
| `Facultades` | `Id` uuid | `Nombre` |

`espacio_id = null` en `AsignacionesSemanales` identifica asignaciones virtuales (regla 9). La migración `HorarioBiSemanal` creó la tabla `AsignacionesSemanales` con tres índices: `ix_asignacion_semanal_sesion_id`, `ux_asignacion_semanal_sesion_semana` (único), `ix_asignacion_semanal_espacio_conflicto`.
