# Stakeholders

## Purpose
Identify who uses SOEA, what role they play, and what they are responsible for validating.
Copilot uses this document when generating role-based authorization logic, UI views, and access control.

## Scope
All human actors who interact with the system directly or whose constraints the system must respect.

---

## Roles

### Admin
- **Who**: Scheduling office staff or IT administrator
- **Responsibilities**:
  - Upload Excel files (curriculum, spaces, instructor availability)
  - Configure system parameters (semester dates, pilot scope)
  - Trigger the optimization pipeline
  - Manage user accounts
- **Validates**: Data integrity, system configuration, final schedule publication

### Academic Coordinator
- **Who**: Faculty coordinator or academic director per program
- **Responsibilities**:
  - Review generated schedules for their program
  - Flag constraint violations or business-rule exceptions
  - Approve or request re-optimization
- **Validates**: Schedule correctness for assigned programs

### Instructor
- **Who**: University professor or lecturer
- **Responsibilities**:
  - View their personal teaching timetable
  - Report availability conflicts
- **Validates**: Their own session assignments

### Student
- **Who**: Enrolled undergraduate student
- **Responsibilities**:
  - View their cohort timetable
- **Validates**: N/A (read-only role)

---

## Indirect Stakeholders

| Stakeholder | Interest |
|---|---|
| University management | Efficient use of physical spaces |
| IT department | System deployment and data security |
| Accreditation body | Schedule compliance with academic regulations |

---

## Open Questions

- Does the institution need a "Department Head" role separate from Coordinator?
- Should Instructors be able to block availability through the UI, or only via uploaded Excel?
