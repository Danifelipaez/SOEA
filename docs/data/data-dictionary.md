# Data Dictionary

## Purpose
Define every data field in the SOEA domain model: name, type, description, required/optional status,
and allowed values. Copilot uses this as the authoritative source when generating entity classes,
database schemas, Excel readers, and API DTOs.

## Scope
All persistent domain entities. Derived/computed fields are noted as such.

---

## Session

The core schedulable unit: one occurrence of a subject for a cohort.

| Field | Type | Required | Description | Allowed Values / Constraints |
|---|---|---|---|---|
| `Id` | GUID | Yes | Unique identifier | Auto-generated |
| `SubjectId` | GUID | Yes | Reference to Subject | Must exist in Subject table |
| `CohortId` | GUID | Yes | Reference to Cohort | Must exist in Cohort table |
| `InstructorId` | GUID | Yes | Reference to Instructor | Must exist in Instructor table |
| `SpaceId` | GUID | No | Assigned Space (null if virtual) | Must exist in Space table |
| `TimeSlotId` | GUID | Yes | Assigned time slot | Must exist in TimeSlot table |
| `AlternanciaType` | Enum | Yes | Cohort alternancia type | `TypeA`, `TypeB`, `NonAlternating` |
| `Modality` | Enum | Yes | Presencial or virtual | `InPerson`, `Virtual` |
| `Status` | Enum | Yes | Scheduling status | `Pending`, `Assigned`, `Conflict` |
| `DurationHours` | decimal | Yes | Session duration in hours | > 0, ≤ 8 |
| `IsBlock` | bool | Yes | Whether this is a contiguous block session | — |
| `IsSplitBlock` | bool | Yes | Whether hours are split across multiple days | Cannot be true if IsBlock is true |

---

## Cohort

A group of students enrolled in the same program and semester.

| Field | Type | Required | Description | Allowed Values |
|---|---|---|---|---|
| `Id` | GUID | Yes | Unique identifier | Auto-generated |
| `Name` | string | Yes | Cohort display name | E.g., "Systems Engineering — Sem 3" |
| `ProgramId` | GUID | Yes | Academic program | Must exist in Program table |
| `Semester` | int | Yes | Academic semester number | 1–10 |
| `EnrolledStudents` | int | Yes | Number of enrolled students | > 0 |
| `AlternanciaType` | Enum | Yes | Type A, Type B, or non-alternating | `TypeA`, `TypeB`, `NonAlternating` |

---

## Space

A physical or virtual location for sessions.

| Field | Type | Required | Description | Allowed Values |
|---|---|---|---|---|
| `Id` | GUID | Yes | Unique identifier | Auto-generated |
| `Name` | string | Yes | Room name or code | E.g., "Aula 204", "Lab Química" |
| `Type` | Enum | Yes | Space type | `Classroom`, `Lab`, `Auditorium`, `Virtual` |
| `Capacity` | int | Yes | Maximum occupancy | > 0 |
| `Building` | string | No | Building name or code | — |
| `Floor` | int | No | Floor number | — |

---

## Instructor

A person who delivers sessions.

| Field | Type | Required | Description | Allowed Values |
|---|---|---|---|---|
| `Id` | GUID | Yes | Unique identifier | Auto-generated |
| `FullName` | string | Yes | Full name | — |
| `Email` | string | Yes | Institutional email | Valid email format |
| `MaxWeeklyHours` | decimal | Yes | Maximum contracted teaching hours per week | > 0 |
| `Availability` | list of TimeSlot | Yes | Available time blocks | See TimeSlot |

---

## TimeSlot

A discrete schedulable time block.

| Field | Type | Required | Description | Allowed Values |
|---|---|---|---|---|
| `Id` | GUID | Yes | Unique identifier | Auto-generated |
| `DayOfWeek` | Enum | Yes | Day of the week | `Monday`–`Friday` |
| `StartTime` | TimeOnly | Yes | Start time | 07:00–21:30 |
| `EndTime` | TimeOnly | Yes | End time | > StartTime, ≤ 21:30 |

---

## Subject

An academic course or subject from the curriculum.

| Field | Type | Required | Description | Allowed Values |
|---|---|---|---|---|
| `Id` | GUID | Yes | Unique identifier | Auto-generated |
| `Name` | string | Yes | Subject name | — |
| `Code` | string | Yes | Institutional course code | Alphanumeric |
| `WeeklyHours` | decimal | Yes | Hours per week per cohort | > 0 |
| `RequiresLab` | bool | Yes | Whether sessions must be in a lab | — |
| `IsNonAlternating` | bool | Yes | Whether sessions occur every week (not alternating) | — |
| `ProgramId` | GUID | Yes | Academic program this subject belongs to | — |

---

## Schedule

The complete timetable output for one semester.

| Field | Type | Required | Description |
|---|---|---|---|
| `Id` | GUID | Yes | Unique identifier |
| `SemesterLabel` | string | Yes | E.g., "2025-1" |
| `GeneratedAt` | DateTime | Yes | Timestamp of generation |
| `Status` | Enum | Yes | `Draft`, `Published`, `Archived` |
| `Sessions` | list of Session | Yes | All assigned sessions |
| `HardConstraintViolations` | int | Computed | Must be 0 for a valid schedule |
| `SoftConstraintFitnessScore` | decimal | Computed | Lower is better; from Phase 3 |

---

## Open Questions

- Should `Instructor.Availability` be stored as a separate join table or embedded as JSON?
- Is `Subject.WeeklyHours` the same as `Session.DurationHours × sessions per week`?
- Are there subjects with variable hours that differ by cohort?
