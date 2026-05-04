# Entity-Relationship Model

## Purpose
Describe the relationships between the main SOEA entities at a conceptual level.
Copilot uses this when generating EF Core entity configurations, migrations, and queries.

## Scope
All persistent entities and their associations.

---

## Entity Relationships

```
Program ──< Cohort >──< Session >── TimeSlot
                             │
                             ├── Instructor
                             ├── Space
                             └── Subject ──> Program

Instructor ──< InstructorAvailability (TimeSlot list)

Schedule ──< Session
```

---

## Cardinalities

| Relationship | Cardinality | Notes |
|---|---|---|
| Program → Cohort | 1 : many | One program has many cohorts |
| Program → Subject | 1 : many | One program has many subjects in its curriculum |
| Cohort → Session | 1 : many | One cohort has many sessions per semester |
| Subject → Session | 1 : many | One subject generates one or more sessions per cohort |
| Instructor → Session | 1 : many | One instructor teaches many sessions |
| Space → Session | 1 : many | One space can host many sessions (across different time slots) |
| TimeSlot → Session | 1 : many | One time slot can be assigned to many sessions (different spaces) |
| Schedule → Session | 1 : many | One schedule contains all sessions for a semester |
| Instructor → TimeSlot | many : many | Via `InstructorAvailability` join table |

---

## Key Constraints Modeled at DB Level

- A `Session` cannot have `SpaceId` and `Modality = InPerson` both null
- A `Session` with `Modality = Virtual` should have `SpaceId = null`
- `TimeSlot.StartTime < TimeSlot.EndTime`
- `Cohort.EnrolledStudents > 0`
- `Subject.WeeklyHours > 0`

---

## Open Questions

- Should `InstructorAvailability` be a separate table or a JSON column on `Instructor`?
- Is there a `Program` entity or is the program just a string field on `Cohort` and `Subject`?
