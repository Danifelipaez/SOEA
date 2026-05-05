# Scope

## Purpose
Define what SOEA is, what problem it solves, and what it explicitly does not cover.
This is the first document Copilot should use when generating high-level code or answering architectural questions.

## Scope
This document covers the institutional context, problem statement, system objectives, and exclusions.

---

## System Overview

SOEA (Sistema de Optimización de Espacios Académicos) is a university course timetabling system
designed for Colombian higher-education institutions that operate under an **alternancia** (hybrid
in-person/virtual) model.

The core goal is to automatically generate feasible and optimized academic schedules that respect
institutional hard constraints (rooms, capacity, instructor availability) and optimize for soft
preferences (compact schedules, workload balance, classroom stability).

---

## Problem Being Solved

Manually scheduling sessions for hundreds of cohorts across dozens of physical spaces is:
- Time-consuming (currently takes days or weeks per semester)
- Error-prone (conflicts, over-capacity assignments, rule violations)
- Difficult to adapt when changes occur mid-semester

SOEA automates this process using a three-phase optimization pipeline:
1. Graph Coloring — initial slot pre-assignment
2. Constraint Programming (OR-Tools CP-SAT) — feasibility enforcement
3. Genetic Algorithm — soft-constraint optimization

---

## Business Objective

Produce a complete, conflict-free, and optimized timetable for a semester, exportable as JSON and
viewable through a role-based web interface.

---

## In Scope

- Academic session scheduling for undergraduate cohorts
- Alternancia Type A / Type B assignment (see `docs/business-rules/alternancia.md`)
- Physical space assignment (classrooms, labs)
- Instructor availability enforcement
- Excel-based data ingestion (curriculum and availability files)
- Role-based web UI (Admin, Coordinator, Instructor, Student)
- Pilot validation with a subset of programs (defined in `docs/business-rules/pilot-limits.md`)

---

## Out of Scope

- Exam scheduling
- Teacher contract management
- Student enrollment management
- Mobile application
- Real-time room booking
- Integration with external SIS/ERP systems (future phase)

---

## Pilot Context

The initial deployment targets a limited pilot (see `docs/business-rules/pilot-limits.md`) to
validate correctness before institution-wide rollout.

---

## Open Questions

- Which specific programs are included in the pilot?
- Is there a maximum number of cohorts per semester for the first release?
- Are virtual sessions scheduled into physical spaces or excluded entirely?
