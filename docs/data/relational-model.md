# Relational Model

## Purpose
Define the physical database tables, columns, primary/foreign keys, and indexes for SOEA.
Copilot uses this when generating EF Core entity configurations and database migrations.

## Scope
All tables in the production database schema.

---

## Tables

> Note: This is an initial draft based on the domain model. Refine after the first EF Core migration.

### `Schedules`
| Column | Type | Constraints |
|---|---|---|
| `Id` | uniqueidentifier | PK |
| `SemesterLabel` | nvarchar(20) | NOT NULL |
| `GeneratedAt` | datetime2 | NOT NULL |
| `Status` | nvarchar(20) | NOT NULL, CHECK IN ('Draft','Published','Archived') |

### `Programs`
| Column | Type | Constraints |
|---|---|---|
| `Id` | uniqueidentifier | PK |
| `Name` | nvarchar(200) | NOT NULL |
| `Code` | nvarchar(20) | NOT NULL, UNIQUE |

### `Cohorts`
| Column | Type | Constraints |
|---|---|---|
| `Id` | uniqueidentifier | PK |
| `Name` | nvarchar(200) | NOT NULL |
| `ProgramId` | uniqueidentifier | FK → Programs.Id |
| `Semester` | int | NOT NULL, CHECK > 0 |
| `EnrolledStudents` | int | NOT NULL, CHECK > 0 |
| `AlternanciaType` | nvarchar(20) | NOT NULL, CHECK IN ('TypeA','TypeB','NonAlternating') |

### `Subjects`
| Column | Type | Constraints |
|---|---|---|
| `Id` | uniqueidentifier | PK |
| `Name` | nvarchar(200) | NOT NULL |
| `Code` | nvarchar(20) | NOT NULL |
| `WeeklyHours` | decimal(4,2) | NOT NULL, CHECK > 0 |
| `RequiresLab` | bit | NOT NULL, DEFAULT 0 |
| `IsNonAlternating` | bit | NOT NULL, DEFAULT 0 |
| `ProgramId` | uniqueidentifier | FK → Programs.Id |

### `Instructors`
| Column | Type | Constraints |
|---|---|---|
| `Id` | uniqueidentifier | PK |
| `FullName` | nvarchar(200) | NOT NULL |
| `Email` | nvarchar(200) | NOT NULL, UNIQUE |
| `MaxWeeklyHours` | decimal(4,2) | NOT NULL, CHECK > 0 |

### `Spaces`
| Column | Type | Constraints |
|---|---|---|
| `Id` | uniqueidentifier | PK |
| `Name` | nvarchar(100) | NOT NULL |
| `Type` | nvarchar(20) | NOT NULL, CHECK IN ('Classroom','Lab','Auditorium','Virtual') |
| `Capacity` | int | NOT NULL, CHECK > 0 |
| `Building` | nvarchar(100) | NULL |
| `Floor` | int | NULL |

### `TimeSlots`
| Column | Type | Constraints |
|---|---|---|
| `Id` | uniqueidentifier | PK |
| `DayOfWeek` | nvarchar(10) | NOT NULL, CHECK IN ('Monday','Tuesday','Wednesday','Thursday','Friday') |
| `StartTime` | time | NOT NULL |
| `EndTime` | time | NOT NULL, CHECK > StartTime |

### `InstructorAvailability`
| Column | Type | Constraints |
|---|---|---|
| `InstructorId` | uniqueidentifier | FK → Instructors.Id |
| `TimeSlotId` | uniqueidentifier | FK → TimeSlots.Id |
| PK | composite | (InstructorId, TimeSlotId) |

### `Sessions`
| Column | Type | Constraints |
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

## Indexes

- `Sessions(CohortId, TimeSlotId)` — conflict detection
- `Sessions(InstructorId, TimeSlotId)` — conflict detection
- `Sessions(SpaceId, TimeSlotId)` — conflict detection
- `Cohorts(ProgramId)` — program-level queries
- `Subjects(ProgramId)` — curriculum queries

---

## Open Questions

- Should SQL Server or PostgreSQL syntax be used for the migration target?
- Should `TimeSlot` rows be pre-seeded (standard institutional slots) or user-defined?
