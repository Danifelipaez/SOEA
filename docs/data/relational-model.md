# Modelo relacional

## Propósito
Definir las tablas físicas de la base de datos, columnas, claves primarias/foráneas e índices de SOEA.
Copilot usa esto al generar configuraciones de entidades EF Core y migraciones de base de datos.

## Alcance
Todas las tablas del esquema de base de datos de producción.

> Nota: Los nombres siguen la convención de código en inglés; equivalencias en español se describen en `data-dictionary.md`.

---

## Tablas

> Nota: Este es un borrador inicial basado en el modelo de dominio. Debe refinarse después de la primera migración de EF Core.

### `Users`
| Column | Type | Constraints |
|---|---|---|
| `UserId` | uniqueidentifier | PK |
| `Name` | nvarchar(200) | NOT NULL |
| `Email` | nvarchar(200) | NOT NULL, UNIQUE |

### `Administrators`
| Column | Type | Constraints |
|---|---|---|
| `UserId` | uniqueidentifier | PK, FK → Users.UserId |
| `AdminType` | nvarchar(30) | NOT NULL, CHECK IN ('Program','Admissions','RedEdu') |

### `Teachers`
| Column | Type | Constraints |
|---|---|---|
| `UserId` | uniqueidentifier | PK, FK → Users.UserId |
| `EmploymentType` | nvarchar(50) | NOT NULL |

### `TeacherAvailability`
| Column | Type | Constraints |
|---|---|---|
| `AvailabilityId` | uniqueidentifier | PK |
| `TeacherId` | uniqueidentifier | FK → Teachers.UserId |
| `DayOfWeek` | nvarchar(10) | NOT NULL, CHECK IN ('Monday','Tuesday','Wednesday','Thursday','Friday') |
| `StartTime` | time | NOT NULL |
| `EndTime` | time | NOT NULL, CHECK > StartTime |

### `Subjects`
| Column | Type | Constraints |
|---|---|---|
| `SubjectId` | uniqueidentifier | PK |
| `Name` | nvarchar(200) | NOT NULL |
| `AcademicProgram` | nvarchar(200) | NOT NULL |
| `EquipmentRequirements` | nvarchar(200) | NULL |
| `ClassType` | nvarchar(30) | NOT NULL |
| `DurationHours` | decimal(4,2) | NOT NULL, CHECK > 0 |
| `LabWeeks` | int | NULL, CHECK >= 0 |
| `CreatedByAdminId` | uniqueidentifier | FK → Administrators.UserId |

### `StudentGroups`
| Column | Type | Constraints |
|---|---|---|
| `StudentGroupId` | uniqueidentifier | PK |
| `AcademicProgram` | nvarchar(200) | NOT NULL |
| `CohortLabel` | nvarchar(50) | NOT NULL |
| `StudentCount` | int | NOT NULL, CHECK > 0 |
| `SubjectId` | uniqueidentifier | FK → Subjects.SubjectId |
| `CreatedByAdminId` | uniqueidentifier | FK → Administrators.UserId |

### `AcademicSpaces`
| Column | Type | Constraints |
|---|---|---|
| `AcademicSpaceId` | uniqueidentifier | PK |
| `Name` | nvarchar(100) | NOT NULL |
| `BuildingBlock` | nvarchar(50) | NULL |
| `SpaceType` | nvarchar(30) | NOT NULL |
| `Capacity` | int | NOT NULL, CHECK > 0 |
| `Equipment` | nvarchar(200) | NULL |
| `IsVirtual` | bit | NOT NULL, DEFAULT 0 |
| `ReservedByAdminId` | uniqueidentifier | NULL, FK → Administrators.UserId |

### `SubjectAcademicSpaces`
| Column | Type | Constraints |
|---|---|---|
| `SubjectId` | uniqueidentifier | FK → Subjects.SubjectId |
| `AcademicSpaceId` | uniqueidentifier | FK → AcademicSpaces.AcademicSpaceId |
| PK | composite | (`SubjectId`, `AcademicSpaceId`) |

### `Sessions`
| Column | Type | Constraints |
|---|---|---|
| `SessionId` | uniqueidentifier | PK |
| `StudentGroupId` | uniqueidentifier | FK → StudentGroups.StudentGroupId |
| `AcademicSpaceId` | uniqueidentifier | NULL, FK → AcademicSpaces.AcademicSpaceId |
| `TeacherId` | uniqueidentifier | FK → Teachers.UserId |
| `DayOfWeek` | nvarchar(10) | NOT NULL, CHECK IN ('Monday','Tuesday','Wednesday','Thursday','Friday') |
| `StartTime` | time | NOT NULL |
| `EndTime` | time | NOT NULL, CHECK > StartTime |
| `Modality` | nvarchar(20) | NOT NULL, CHECK IN ('InPerson','Virtual') |
| `WeekNumber` | int | NOT NULL, CHECK > 0 |
| `IsVirtualAlternation` | bit | NOT NULL |
| `CreatedByAdminId` | uniqueidentifier | FK → Administrators.UserId |

Las sesiones virtuales se representan con `Sessions.AcademicSpaceId = NULL` y `Modality = 'Virtual'`.

---

## Índices

- `Sessions(StudentGroupId, DayOfWeek, StartTime)` — detección de conflictos por grupo
- `Sessions(TeacherId, DayOfWeek, StartTime)` — detección de conflictos por docente
- `Sessions(AcademicSpaceId, DayOfWeek, StartTime)` — detección de conflictos por espacio
- `StudentGroups(SubjectId)` — consultas por asignatura
- `Subjects(AcademicProgram)` — consultas por programa

---

## Preguntas abiertas

- ¿Se requiere historizar cambios en `Sessions` (versionado de horarios)?
- ¿`SubjectAcademicSpaces` representa requisitos de equipamiento o restricciones duras de asignación?
