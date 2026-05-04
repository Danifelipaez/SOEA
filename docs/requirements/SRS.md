# Software Requirements Specification (SRS)

## Purpose
High-level functional and non-functional requirements for SOEA.
This document is the authoritative source for what the system must do.
Copilot uses this when generating use cases, API endpoints, and test cases.

## Scope
All requirements for the backend optimization engine, data ingestion, API, and frontend.

---

## Functional Requirements

### FR-01 — Excel Data Ingestion
The system shall accept Excel files containing:
- Curriculum data (subjects, hours per week, cohorts)
- Instructor availability (time blocks per instructor per day)
- Space inventory (capacity, type, equipment)

### FR-02 — Schedule Generation
The system shall produce a complete timetable for one semester given the input data,
using a three-phase pipeline (Graph Coloring → CP → Genetic Algorithm).

### FR-03 — Hard Constraint Enforcement
The generated schedule shall not violate any hard constraint defined in
`docs/business-rules/hard-constraints.md`.

### FR-04 — Soft Constraint Optimization
The system shall optimize soft constraints (see `docs/business-rules/soft-constraints.md`)
through the Genetic Algorithm phase.

### FR-05 — JSON Export
The system shall export the final schedule as a structured JSON document
(format defined in `docs/data/json-output-spec.md`).

### FR-06 — Role-Based Web UI
The system shall provide a web interface with distinct views and permissions for:
Admin, Coordinator, Instructor, Student (see `docs/requirements/stakeholders.md`).

### FR-07 — Schedule Validation Report
The system shall produce a validation report listing any remaining soft-constraint
violations after optimization, with a severity score.

### FR-08 — Alternancia Support
The system shall assign sessions according to alternancia rules (Type A / Type B),
as defined in `docs/business-rules/alternancia.md`.

---

## Non-Functional Requirements

### NFR-01 — Performance
The optimization pipeline shall complete within 10 minutes for a standard semester load
(up to 200 cohorts, 50 instructors, 30 spaces).

### NFR-02 — Correctness
All hard constraints must be satisfied (zero violations) in the final output.

### NFR-03 — Usability
Non-technical users (Coordinators) must be able to review and understand the generated
schedule without training beyond a 30-minute onboarding session.

### NFR-04 — Maintainability
The codebase shall follow Clean Architecture conventions as described in
`docs/architecture/architecture-overview.md`.

### NFR-05 — Security
Role-based access control shall prevent users from accessing data outside their scope.

---

## Open Questions

- What is the exact maximum number of sessions per semester for sizing performance targets?
- Should schedule regeneration be possible mid-semester (partial re-optimization)?
