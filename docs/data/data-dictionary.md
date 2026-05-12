# Diccionario de datos

## Propósito
Definir cada campo de datos del modelo de dominio de SOEA: nombre, tipo, descripción, si es obligatorio/opcional
y valores permitidos. Copilot usa esto como fuente autorizada al generar clases de entidades,
esquemas de base de datos, lectores de Excel y DTOs de API.

## Alcance
Todas las entidades persistentes del dominio. Los campos derivados o calculados se indican como tales.

---

## Sesión

La unidad programable central: una ocurrencia de una asignatura para una cohorte.

| Campo | Tipo | Requerido | Descripción | Valores permitidos / Restricciones |
|---|---|---|---|---|
| `Id` | GUID | Sí | Identificador único | Generado automáticamente |
| `SubjectId` | GUID | Sí | Referencia a Subject | Debe existir en la tabla Subject |
| `GrupoId` | GUID | No | Referencia a Grupo (null para sesiones de todo el programa) | Debe existir en la tabla Grupo, o null |
| `InstructorId` | GUID | Sí | Referencia a Instructor | Debe existir en la tabla Instructor |
| `SpaceId` | GUID | No | Espacio asignado (null si es virtual) | Debe existir en la tabla Space |
| `TimeSlotId` | GUID | Sí | Espacio de tiempo asignado | Debe existir en la tabla TimeSlot |
| `AlternanciaType` | Enum | Sí | Tipo de alternancia de la cohorte | `TypeA`, `TypeB`, `NonAlternating` |
| `Modality` | Enum | Sí | Presencial o virtual | `InPerson`, `Virtual` |
| `Status` | Enum | Sí | Estado de programación | `Pending`, `Assigned`, `Conflict` |
| `DurationHours` | decimal | Sí | Duración de la sesión en horas | > 0, ≤ 8 |
| `IsBlock` | bool | Sí | Indica si es una sesión en bloque continuo | — |
| `IsSplitBlock` | bool | Sí | Indica si las horas están divididas en varios días | No puede ser true si IsBlock es true |

---

## Grupo

Agrupación opcional de estudiantes para programación de sesiones (piloto de química).
Las sesiones pueden asignarse a un Grupo específico o a todo el programa (GrupoId = null).

| Campo | Tipo | Requerido | Descripción | Valores permitidos |
|---|---|---|---|---|
| `Id` | GUID | Sí | Identificador único | Generado automáticamente |
| `Name` | string | Sí | Nombre visible del grupo | Por ejemplo, "Química Lab A", "Grupo 1" |
| `ProgramId` | GUID | Sí | Programa académico | Debe existir en la tabla Program |
| `Semester` | int | Sí | Número de semestre académico | 1–10 |
| `EnrolledStudents` | int | Sí | Número de estudiantes inscritos | > 0 |
| `AlternanciaType` | Enum | Sí | Tipo A, Tipo B o no alternante | `TypeA`, `TypeB`, `NonAlternating` |

---

## Espacio

Ubicación física para las sesiones.

| Campo | Tipo | Requerido | Descripción | Valores permitidos |
|---|---|---|---|---|
| `Id` | GUID | Sí | Identificador único | Generado automáticamente |
| `Name` | string | Sí | Nombre o código del espacio | Por ejemplo, "Aula 204", "Lab Química" |
| `Type` | Enum | Sí | Tipo de espacio | `Classroom`, `Lab`, `Auditorium` |
| `Capacity` | int | Sí | Ocupación máxima | > 0 |
| `Building` | string | No | Nombre o código del edificio | — |
| `Floor` | int | No | Número del piso | — |

---

## Docente

Persona que imparte sesiones.

| Campo | Tipo | Requerido | Descripción | Valores permitidos |
|---|---|---|---|---|
| `Id` | GUID | Sí | Identificador único | Generado automáticamente |
| `FullName` | string | Sí | Nombre completo | — |
| `Email` | string | Sí | Correo institucional | Formato de correo válido |
| `MaxWeeklyHours` | decimal | Sí | Máximo de horas de docencia contratadas por semana | > 0 |
| `Availability` | list of TimeSlot | Sí | Espacios de tiempo disponibles | Ver TimeSlot |

---

## Espacio de tiempo

Bloque de tiempo discreto programable.

| Campo | Tipo | Requerido | Descripción | Valores permitidos |
|---|---|---|---|---|
| `Id` | GUID | Sí | Identificador único | Generado automáticamente |
| `DayOfWeek` | Enum | Sí | Día de la semana | `Monday`–`Friday` |
| `StartTime` | TimeOnly | Sí | Hora de inicio | 07:00–21:30 |
| `EndTime` | TimeOnly | Sí | Hora de fin | > StartTime, ≤ 21:30 |

---

## Asignatura

Una asignatura o curso académico de la malla curricular.

| Campo | Tipo | Requerido | Descripción | Valores permitidos |
|---|---|---|---|---|
| `Id` | GUID | Sí | Identificador único | Generado automáticamente |
| `Name` | string | Sí | Nombre de la asignatura | — |
| `Code` | string | Sí | Código institucional de la asignatura | Alfanumérico |
| `WeeklyHours` | decimal | Sí | Horas por semana por cohorte | > 0 |
| `RequiresLab` | bool | Sí | Indica si las sesiones deben ser en un laboratorio | — |
| `IsNonAlternating` | bool | Sí | Indica si las sesiones ocurren todas las semanas (no alternan) | — |
| `ProgramId` | GUID | Sí | Programa académico al que pertenece la asignatura | — |

---

## Horario

La salida completa del horario para un semestre.

| Campo | Tipo | Requerido | Descripción |
|---|---|---|---|
| `Id` | GUID | Sí | Identificador único |
| `Semestre` | string | Sí | Por ejemplo, "2025-1" |
| `GeneratedAt` | DateTime | Sí | Marca temporal de generación |
| `Estado` | Enum | Sí | `Draft`, `Published`, `Archived` |
| `sesiones` | list of Session | Sí | Todas las sesiones asignadas |
| `HardConstraintViolations` | int | Calculado | Debe ser 0 para un horario válido |
| `SoftConstraintFitnessScore` | decimal | Calculado | Más bajo es mejor; proviene de la Fase 3 |

---

## Preguntas abiertas

- ¿`Instructor.Availability` debe almacenarse como una tabla de unión separada o incrustada como JSON?
- ¿`Subject.WeeklyHours` es lo mismo que `Session.DurationHours × sesiones por semana`?
- ¿Existen asignaturas con horas variables que cambian según la cohorte?
