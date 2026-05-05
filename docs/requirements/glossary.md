# Glossary

## Purpose
Define every domain term used in SOEA code, docs, and UI so that all contributors (and Copilot)
use consistent language. When in doubt, refer to this file before naming a class, variable, or endpoint.

## Scope
All domain-specific terms used across backend, frontend, and documentation.

---

## Terms

| Term | Definition |
|---|---|
| **Session** | A scheduled teaching event: one subject + one instructor + one cohort + one time slot + optional space |
| **Cohort** | A group of students enrolled in the same academic program and semester (e.g., "Systems Engineering — Semester 3") |
| **Space** | A physical location where an in-person session can take place (classroom, lab, auditorium) |
| **Espacio** | Spanish equivalent of Space; used in the original institutional documents |
| **Instructor** | A person assigned to deliver a session (professor, lecturer, teaching assistant) |
| **Time Slot** | A discrete schedulable unit: day + start time + end time (e.g., Monday 07:00–09:00) |
| **Schedule** | The complete assignment of all sessions to time slots and spaces for a semester |
| **Alternancia** | Hybrid teaching model: cohorts alternate between in-person (presencial) and virtual weeks |
| **Type A (Tipo A)** | Alternancia cohort that attends in-person on odd weeks and virtually on even weeks |
| **Type B (Tipo B)** | Alternancia cohort that attends in-person on even weeks and virtually on odd weeks |
| **Hard Constraint** | A rule that must never be violated in any valid schedule (see `hard-constraints.md`) |
| **Soft Constraint** | A preference that should be optimized but may be relaxed (see `soft-constraints.md`) |
| **Conflict** | Two sessions that cannot share the same time slot (same instructor, same space, or same cohort) |
| **Conflict Graph** | A graph where nodes are sessions and edges represent conflicts; used in Phase 1 |
| **Chromosome** | A complete schedule encoding used in the Genetic Algorithm (Phase 3) |
| **Fitness** | A numeric score measuring how well a chromosome satisfies soft constraints |
| **OR-Tools** | Google's open-source optimization library; used for Constraint Programming in Phase 2 |
| **CP-SAT** | The constraint programming solver inside OR-Tools used in Phase 2 |
| **EPPlus** | .NET library for reading/writing Excel files; used for data ingestion |
| **EF Core** | Entity Framework Core; the ORM used for database access |
| **UCTP** | University Course Timetabling Problem; the formal combinatorial optimization problem SOEA solves |
| **Pilot** | Limited initial deployment covering a subset of programs to validate the system before full rollout |
| **Block** | A session that spans multiple consecutive hours (e.g., a 3-hour lab block on a single day) |
| **Split Block** | A session whose hours are distributed across multiple days |

---

## Naming Conventions for Code

- Entity classes: singular noun (`Session`, `Cohort`, `Space`)
- Collections: plural noun (`Sessions`, `Cohorts`, `Spaces`)
- Use English for all code identifiers; Spanish only for user-facing labels and doc titles
- Use `AlternanciaType` enum with values `TypeA`, `TypeB`, and `NonAlternating`

---

## Open Questions

- Is "Espacio" always a physical room, or can it also be a virtual meeting link?
- Should "virtual sessions" be assigned to a Space entity or excluded from space constraints?
