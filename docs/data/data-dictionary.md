# Diccionario de datos

## Propósito
Definir cada campo de datos del modelo de dominio de SOEA: nombre, tipo, descripción, si es obligatorio/opcional
y valores permitidos. Copilot usa esto como fuente autorizada al generar clases de entidades,
esquemas de base de datos, lectores de Excel y DTOs de API.

## Alcance
Todas las entidades persistentes del dominio. Los campos derivados o calculados se indican como tales.

> Nota: Los nombres siguen la convención de código en inglés; entre paréntesis se indica el nombre equivalente del diagrama ER en español.

---

## User (Usuario)

Entidad base para cuentas humanas del sistema.

| Field | Type | Required | Description | Constraints |
|---|---|---|---|---|
| `UserId` | UUID | Yes | Identificador único (Id_usuario) | Generado automáticamente |
| `Name` | string | Yes | Nombre completo visible | — |
| `Email` | string | Yes | Correo institucional | Formato válido, único |

---

## Administrator (Administrador)

Especialización de User para gestión académica.

| Field | Type | Required | Description | Constraints |
|---|---|---|---|---|
| `UserId` | UUID | Yes | PK/FK a User (Id_usuario) | Debe existir en `User.UserId` |
| `AdminType` | enum | Yes | Rol administrativo (Tipo_admin) | `Program`, `Admissions`, `RedEdu` |

---

## Teacher (Docente)

Especialización de User para docencia.

| Field | Type | Required | Description | Constraints |
|---|---|---|---|---|
| `UserId` | UUID | Yes | PK/FK a User (Id_usuario) | Debe existir en `User.UserId` |
| `EmploymentType` | string | Yes | Tipo de vinculación (Tipo_vinculacion) | Catálogo institucional |

---

## TeacherAvailability (Disponibilidad_Docente)

Bloques de tiempo disponibles por docente.

| Field | Type | Required | Description | Constraints |
|---|---|---|---|---|
| `AvailabilityId` | UUID | Yes | Identificador único (Id_disponibilidad) | Generado automáticamente |
| `TeacherId` | UUID | Yes | FK a Teacher (Id_usuario) | Debe existir en `Teacher.UserId` |
| `DayOfWeek` | enum | Yes | Día de la semana (Dia_semana) | `Monday`–`Friday` |
| `StartTime` | TimeOnly | Yes | Hora de inicio (Hora_inicio) | 07:00–21:30 |
| `EndTime` | TimeOnly | Yes | Hora de fin (Hora_fin) | > `StartTime`, ≤ 21:30 |

---

## Subject (Asignatura)

Curso académico de la malla curricular.

| Field | Type | Required | Description | Constraints |
|---|---|---|---|---|
| `SubjectId` | UUID | Yes | Identificador único (Id_asignatura) | Generado automáticamente |
| `Name` | string | Yes | Nombre de la asignatura | — |
| `AcademicProgram` | string | Yes | Programa académico (Prog_academico) | Catálogo institucional |
| `EquipmentRequirements` | string | No | Equipamiento requerido (Req_equipamiento) | Lista separada por comas o catálogo |
| `ClassType` | enum | Yes | Tipo de clase (Tipo_de_clase) | `Lecture`, `Lab`, `Practice` |
| `DurationHours` | decimal | Yes | Duración de la sesión (Duracion) | > 0, ≤ 8 |
| `LabWeeks` | int | No | Semanas en laboratorio (Num_semanas_en_lab) | ≥ 0 |
| `CreatedByAdminId` | UUID | Yes | FK a Administrator (Creado_por_id_admin) | Debe existir en `Administrator.UserId` |

---

## StudentGroup (Grupo_de_estudiantes)

Grupo de estudiantes asociado a una asignatura.

| Field | Type | Required | Description | Constraints |
|---|---|---|---|---|
| `StudentGroupId` | UUID | Yes | Identificador único (Id_grupo) | Generado automáticamente |
| `AcademicProgram` | string | Yes | Programa académico (Prog_academico) | Catálogo institucional |
| `CohortLabel` | string | Yes | Cohorte o semestre (Cohorte) | Ejemplo: "2025-1 / Sem 3" |
| `StudentCount` | int | Yes | Número de estudiantes (Num_estudiantes) | > 0 |
| `SubjectId` | UUID | Yes | FK a Subject (Asignatura) | Debe existir en `Subject.SubjectId` |
| `CreatedByAdminId` | UUID | Yes | FK a Administrator (Creado_por_id_admin) | Debe existir en `Administrator.UserId` |

---

## AcademicSpace (Espacio_Academico)

Ubicación física o recurso virtual para dictar sesiones.

| Field | Type | Required | Description | Constraints |
|---|---|---|---|---|
| `AcademicSpaceId` | UUID | Yes | Identificador único (Id_espacio) | Generado automáticamente |
| `Name` | string | Yes | Nombre o código del espacio | Ejemplo: "Aula 204" |
| `BuildingBlock` | string | No | Bloque/edificio (Bloque) | — |
| `SpaceType` | enum | Yes | Tipo de espacio (Tipo) | `Classroom`, `Lab`, `Auditorium` |
| `Capacity` | int | Yes | Ocupación máxima (Capacidad) | > 0 |
| `Equipment` | string | No | Equipamiento disponible (Equipamiento) | Lista separada por comas o catálogo |
| `IsVirtual` | bool | Yes | Indica si el espacio es virtual (Es_virtual) | — |
| `ReservedByAdminId` | UUID | No | FK a Administrator (Reservado_por_id_admin) | Debe existir en `Administrator.UserId` |

---

## Session (Sesion)

Unidad programable: una ocurrencia de clase en un día y hora específicos.

| Field | Type | Required | Description | Constraints |
|---|---|---|---|---|
| `SessionId` | UUID | Yes | Identificador único (Id_sesion) | Generado automáticamente |
| `StudentGroupId` | UUID | Yes | FK a StudentGroup (Id_grupo) | Debe existir en `StudentGroup.StudentGroupId` |
| `AcademicSpaceId` | UUID | No | FK a AcademicSpace (Id_espacio) | Null cuando `Modality = Virtual` |
| `TeacherId` | UUID | Yes | FK a Teacher (Id_docente) | Debe existir en `Teacher.UserId` |
| `DayOfWeek` | enum | Yes | Día de la semana (Dia_semana) | `Monday`–`Friday` |
| `StartTime` | TimeOnly | Yes | Hora de inicio (Hora_inicio) | 07:00–21:30 |
| `EndTime` | TimeOnly | Yes | Hora de fin (Hora_fin) | > `StartTime`, ≤ 21:30 |
| `Modality` | enum | Yes | Modalidad (Modalidad) | `InPerson`, `Virtual` |
| `WeekNumber` | int | Yes | Número de semana (Semana_num) | ≥ 1 |
| `IsVirtualAlternation` | bool | Yes | Alternancia virtual (Es_alternancia_virtual) | — |
| `CreatedByAdminId` | UUID | Yes | FK a Administrator (Creado_por_id_admin) | Debe existir en `Administrator.UserId` |

---

## Preguntas abiertas

- ¿`EquipmentRequirements` y `Equipment` deben normalizarse a una tabla de catálogo?
- ¿`EmploymentType` y `ClassType` se deben restringir con listas institucionales rígidas?
- ¿Las sesiones virtuales deben persistir siempre con `AcademicSpaceId = null`, aun si existe un `AcademicSpace` virtual?
