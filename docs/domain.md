# Dominio SOEA

## Entidades principales

| Entidad | Campos críticos | Notas |
|---|---|---|
| `Asignatura` | `Id`, `Nombre`, `Codigo`, `HorasPorSesion`, `SesionesPorSemana`, `TipoAlternancia`, `TipoEspacio`, `DocenteId?` | Duración fija en creación; Alternancia auto-derivada del nombre |
| `BloqueTiempo` | `DiaDeSemana`, `HoraInicio`, `HoraFin`, `EsSabado` | Generado en memoria por request; Lun–Vie 07:00–20:00, Sáb 07:00–14:00 |
| `Docente` | `Id`, `Nombre`, `Apellido`, `Correo`, `MaximoHorasSemanales`, `Disponibilidad[]` | M:N con FranjasHorarias |
| `Espacio` | `Id`, `Nombre`, `TipoEspacio`, `Capacidad`, `Ubicacion`, `Piso` | `TipoEspacio`: Salon / Laboratorio / Auditorio |
| `Grupo` | `Id`, `Codigo`, `NumeroEstudiantes`, `TipoAlternancia`, `ProgramaId` | Cohorte académica; determina semanas presenciales/virtuales |
| `Sesion` | `Id`, `AsignaturaId`, `DocenteId`, `EspacioId?`, `BloqueId`, `TipoAlternancia`, `Modalidad` | `EspacioId = null` → sesión virtual |
| `Horario` | `Id`, `Semestre`, `Estado`, `HardConstraintViolations`, `SoftConstraintFitnessScore` | No publicable si violations > 0 |
| `Programa` | `Id`, `Nombre`, `Codigo`, `FacultadId` | Plan académico |
| `Facultad` | `Id`, `Nombre` | Unidad organizativa |

## Modelo de alternancia

Tipo A — presencial en semanas **impares** (1, 3, 5 …), virtual en pares.
Tipo B — presencial en semanas **pares** (2, 4, 6 …), virtual en impares.
SinAlternancia — presencial **todas** las semanas.

| Regla | Descripción |
|---|---|
| ALT-01 | Tipo A + Tipo B pueden compartir espacio/franja — nunca coinciden físicamente |
| ALT-02 | Dos Tipo A **no** pueden compartir espacio/franja |
| ALT-03 | Dos Tipo B **no** pueden compartir espacio/franja |
| ALT-04 | Sesiones virtuales no consumen capacidad de espacio |
| ALT-05 | Franja asignada aplica a todo el semestre; la modalidad cambia, el bloque no |
| ALT-06 | `SinAlternancia` = siempre presencial; va marcado en la malla curricular |

Impacto en Fase 1: hay arista en el grafo de conflictos si `TipoAlternancia` es igual en ambas sesiones, O si alguna es `SinAlternancia`.

---

## Restricciones duras (hard constraints)

El motor CP-SAT (Fase 2) las aplica todas. Un horario con violations > 0 no puede publicarse.

### Espacio
| ID | Regla |
|---|---|
| HC-S01 | Un espacio no puede alojar dos sesiones simultáneas del mismo tipo de alternancia (o si alguna es NonAlternating) |
| HC-S02 | El total de estudiantes físicamente presentes no puede exceder la capacidad del espacio |
| HC-S03 | Sesión que requiere laboratorio → debe asignarse a espacio tipo Laboratorio |
| HC-S04 | Sesiones virtuales no se asignan a espacio físico (`EspacioId = null`) |

### Docente
| ID | Regla |
|---|---|
| HC-I01 | Un docente no puede tener dos sesiones en la misma franja |
| HC-I02 | La franja asignada debe estar dentro de la disponibilidad declarada del docente |
| HC-I03 | El docente no puede exceder su máximo de horas semanales contratadas |

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

---

## Esquema de BD (SOEAdb · PostgreSQL localhost:5432)

```
Programas (1) ──→ (N) Asignaturas
                          ↓
                      Sesiones ←─── Horarios
                      ↙  ↓  ↘
                 Docentes  Espacios  FranjasHorarias
                      ↑
              DisponibilidadDocente
                      ↓
               FranjasHorarias
```

### Tablas

| Tabla | PK | Campos destacados |
|---|---|---|
| `Horarios` | `Id` uuid | `Semestre`, `Estado` (borrador/validado/aprobado/activo), `Hard_constraint_violations`, `Soft_constraint_fitness_score` |
| `Sesiones` | `Id` uuid | `Horario_id`, `Asignatura_id`, `Espacio_id` (nullable), `Docente_id`, `Franja_id`, `Tipo_alternancia`, `Modalidad`, `Duracion_horas` |
| `Asignaturas` | `Id` uuid | `Codigo` (UNIQUE), `BloqueSemanales`, `RequiereLab`, `Alternancia`, `ProgramaId`, `DocenteId` |
| `Docentes` | `Id` uuid | `Correo` (UNIQUE), `Maximo_hrs_semanales` |
| `Espacios` | `Id` uuid | `Tipo`, `Capacidad`, `Ubicacion`, `Piso` |
| `FranjasHorarias` | `Id` uuid | `Dia_semana`, `Hora_inicio`, `Hora_fin` |
| `DisponibilidadDocente` | (`Docente_id`, `Franja_id`) | tabla de unión M:N |
| `Programas` | `Id` uuid | `Codigo` (UNIQUE) |
| `Facultades` | `Id` uuid | `Nombre` |

`Espacio_id = null` identifica sesiones virtuales. Los nombres de tabla usan comillas dobles en PostgreSQL (`"Sesiones"`).
