# Status: Audit Fixes (Phase 1 - Critical)

## Start State
- **Date:** 2026-05-18
- **Goal:** Fix critical audit findings that produce wrong scheduling results.
- **Files Touched:**
  - src/SOEA.Application/Features/Horario/GenerarHorarioService.cs
  - src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs
  - src/SOEA.Engine.Genetic/CromosomaHorario.cs
  - src/SOEA.Engine.Genetic/MotorGenetico.cs
  - src/SOEA.Engine.Genetic/OperadoresGeneticos.cs
  - src/SOEA.Engine.Genetic/EvaluadorFitness.cs
  - src/SOEA.Engine.GraphColoring/ConstructorGrafoConflictos.cs
  - src/SOEA.Domain/Entities/Asignatura.cs
  - src/SOEA.API/Controllers/HorarioController.cs

## Current Status
- [x] Step 1: Enforce teacher availability input and map to Docente availability.
- [x] Step 2: Encode HC-I02 (availability) in CP-SAT without dead guards.
- [x] Step 3: Encode HC-I03 (max weekly hours) as CP-SAT constraints.
- [x] Step 4: Add room genes to Phase 3 and write back to Sesion.
- [x] Step 5: Repair room conflicts in GA (espacio + bloque).
- [x] Step 6: Fix alternancia mapping using domain logic and case-insensitive input.
- [x] Step 7: Update SC-01 and SC-09 fitness calculations (hours-aware).
- [x] Step 8: Persist Sesiones with Horario and validate empty Docentes.
- [x] Step 9: Tests for availability, max hours, alternancia, GA room conflicts.

## Progress Notes
- 2026-05-18: Started audit remediation plan and identified critical fixes.
- 2026-05-19: Completed all 9 steps. Steps 4-8 were already implemented in a prior session.
  Steps 1-3: Fixed pre-solve rejection for docentes with zero blocks (GenerarHorarioService),
  removed HC-I02 dead `&& .Any()` guard (MotorConstraintProgramming), and split HC-I03
  condition to separately catch zero-block docentes.
  Step 9: Added 10 unit tests across MotorConstraintProgrammingTests, OperadoresGeneticosTests,
  and ConstructorGrafoConflictosTests. All 135 tests pass.

## Next Immediate Step
Implement teacher availability mapping/validation in GenerarHorarioService and wire HC-I02 in MotorConstraintProgramming.

## Architecture Decisions
- Teacher availability is required input (reject missing/empty).
- Domain Asignatura is the source of truth for session duration/count.
- Phase 3 may reassign rooms with repair/constraints.
- Persist generated Sesiones alongside Horario.

## Related Docs
- docs/business-rules/hard-constraints.md
- docs/business-rules/soft-constraints.md
- docs/business-rules/alternancia.md
- docs/data/data-dictionary.md
- docs/data/json-output-spec.md
