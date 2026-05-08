# Modelo relacional

## Propósito
Definir las tablas físicas de la base de datos, columnas, claves primarias/foráneas e índices de SOEA.
Copilot usa esto al generar configuraciones de entidades EF Core y migraciones de base de datos.

## Alcance
Todas las tablas del esquema de base de datos de producción.

---

## Tablas

> Nota: Este es un borrador inicial basado en el modelo de dominio. Debe refinarse después de la primera migración de EF Core.

### `Schedules`
| Columna | Tipo | Restricciones |
|---|---|---|
| `Id` | uniqueidentifier | PK |
| `SemesterLabel` | nvarchar(20) | NOT NULL |
| `GeneratedAt` | datetime2 | NOT NULL |
| `Status` | nvarchar(20) | NOT NULL, CHECK IN ('Draft','Published','Archived') |

### `Programs`
| Columna | Tipo | Restricciones |
|---|---|---|
| `Id` | uniqueidentifier | PK |
| `Name` | nvarchar(200) | NOT NULL |
| `Code` | nvarchar(20) | NOT NULL, UNIQUE |

### `Cohorts`
| Columna | Tipo | Restricciones |
|---|---|---|
| `Id` | uniqueidentifier | PK |
| `Name` | nvarchar(200) | NOT NULL |
| `ProgramId` | uniqueidentifier | FK → Programs.Id |
| `Semester` | int | NOT NULL, CHECK > 0 |
| `EnrolledStudents` | int | NOT NULL, CHECK > 0 |
| `AlternanciaType` | nvarchar(20) | NOT NULL, CHECK IN ('TypeA','TypeB','NonAlternating') |

### `Subjects`
| Columna | Tipo | Restricciones |
|---|---|---|
| `Id` | uniqueidentifier | PK |
| `Name` | nvarchar(200) | NOT NULL |
| `Code` | nvarchar(20) | NOT NULL |
| `WeeklyHours` | decimal(4,2) | NOT NULL, CHECK > 0 |
| `RequiresLab` | bit | NOT NULL, DEFAULT 0 |
| `IsNonAlternating` | bit | NOT NULL, DEFAULT 0 |
| `ProgramId` | uniqueidentifier | FK → Programs.Id |

### `Instructors`
| Columna | Tipo | Restricciones |
|---|---|---|
| `Id` | uniqueidentifier | PK |
| `FullName` | nvarchar(200) | NOT NULL |
| `Email` | nvarchar(200) | NOT NULL, UNIQUE |
| `MaxWeeklyHours` | decimal(4,2) | NOT NULL, CHECK > 0 |

### `Spaces`
| Columna | Tipo | Restricciones |
|---|---|---|
| `Id` | uniqueidentifier | PK |
| `Name` | nvarchar(100) | NOT NULL |
| `Type` | nvarchar(20) | NOT NULL, CHECK IN ('Classroom','Lab','Auditorium') |
| `Capacity` | int | NOT NULL, CHECK > 0 |
| `Building` | nvarchar(100) | NULL |
| `Floor` | int | NULL |

Las sesiones virtuales se representan con `Session.SpaceId = NULL`; no requieren una fila persistida de `Space`.

### `TimeSlots`
| Columna | Tipo | Restricciones |
|---|---|---|
| `Id` | uniqueidentifier | PK |
| `DayOfWeek` | nvarchar(10) | NOT NULL, CHECK IN ('Monday','Tuesday','Wednesday','Thursday','Friday') |
| `StartTime` | time | NOT NULL |
| `EndTime` | time | NOT NULL, CHECK > StartTime |

### `InstructorAvailability`
| Columna | Tipo | Restricciones |
|---|---|---|
| `InstructorId` | uniqueidentifier | FK → Instructors.Id |
| `TimeSlotId` | uniqueidentifier | FK → TimeSlots.Id |
| PK | composite | (InstructorId, TimeSlotId) |

### `Sessions`
| Columna | Tipo | Restricciones |
|---|---|---|
| `Id` | uniqueidentifier | PK |
| `ScheduleId` | uniqueidentifier | FK → Schedules.Id |
| `SubjectId` | uniqueidentifier | FK → Subjects.Id |
| `CohortId` | uniqueidentifier | FK → Cohorts.Id |
| `InstructorId` | uniqueidentifier | FK → Instructors.Id |
| `SpaceId` | uniqueidentifier | NULL, FK → Spaces.Id |
| `TimeSlotId` | uniqueidentifier | FK → TimeSlots.Id |
| `Modality` | nvarchar(20) | NOT NULL, CHECK IN ('InPerson','Virtual') |
| `Status` | nvarchar(20) | NOT NULL |
| `DurationHours` | decimal(4,2) | NOT NULL, CHECK > 0 |
| `IsBlock` | bit | NOT NULL |
| `IsSplitBlock` | bit | NOT NULL |

---

## Índices

- `Sessions(CohortId, TimeSlotId)` — detección de conflictos
- `Sessions(InstructorId, TimeSlotId)` — detección de conflictos
- `Sessions(SpaceId, TimeSlotId)` — detección de conflictos
- `Cohorts(ProgramId)` — consultas por programa
- `Subjects(ProgramId)` — consultas de malla curricular

---

## Preguntas abiertas

- ¿Debe usarse sintaxis de SQL Server o PostgreSQL para el destino de la migración?
- ¿Las filas de `TimeSlot` deben precargarse (espacios institucionales estándar) o ser definidas por el usuario?
