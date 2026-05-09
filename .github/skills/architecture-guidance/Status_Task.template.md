# Status: [TASK_NAME]

## Start State
- **Date:** YYYY-MM-DD HH:MM
- **Goal:** [Clear, one-sentence description of what we're building]
- **Feature/Entity:** [e.g., "Asignatura", "SummerAvailability"]
- **Layers to Touch:** [e.g., Domain, Application, Infrastructure, API]
- **Files to Create/Modify:** 
  - [ ] `src/SOEA.Domain/Entities/[Entity].cs`
  - [ ] `src/SOEA.Domain/Interfaces/I[Entity]Repository.cs`
  - [ ] `src/SOEA.Application/Features/[Feature]/Create[Entity]Service.cs`
  - [ ] `src/SOEA.Infrastructure.Data/Repositories/[Entity]Repository.cs`
  - [ ] `src/SOEA.API/Controllers/[Entity]Controller.cs`

## Current Status

### Layer 1: Domain (SOEA.Domain)
- [ ] Entity created: `Entities/[Entity].cs`
- [ ] Repository interface created: `Interfaces/I[Entity]Repository.cs`
- [ ] Enums/ValueObjects defined (if needed)
- [ ] Domain invariants validated in entity

**Notes:**
- [What was done, decisions made]
- [Any blockers or clarifications needed]

### Layer 2: Application (SOEA.Application)
- [ ] Service created: `Features/[Feature]/Create[Entity]Service.cs`
- [ ] Request DTO: `Features/[Feature]/Requests/Create[Entity]Request.cs`
- [ ] Response DTO: `Features/[Feature]/Responses/[Entity]Response.cs`
- [ ] Service uses Domain entities and IRepository (mocked in tests)

**Notes:**
- [What was done, decisions made]

### Layer 3: Infrastructure (SOEA.Infrastructure.Data)
- [ ] Repository implementation: `Repositories/[Entity]Repository.cs`
- [ ] EF Core configuration: `Configurations/[Entity]Configuration.cs`
- [ ] Migration created (if schema changed)
- [ ] Repository implements I[Entity]Repository interface

**Notes:**
- [What was done, decisions made]

### Layer 4: API (SOEA.API)
- [ ] Controller created: `Controllers/[Entity]Controller.cs`
- [ ] Endpoint(s) added: `POST /api/[entities]`, `GET /api/[entities]/{id}`, etc.
- [ ] Controller routes to Application service (not Infrastructure)
- [ ] Error handling and validation responses

**Notes:**
- [What was done, decisions made]

### Testing
- [ ] Domain tests: `test/SOEA.Tests/Unit/Domain/[Entity]Tests.cs`
- [ ] Application tests: `test/SOEA.Tests/Unit/Application/Features/[Feature]/[Service]Tests.cs`
- [ ] Infrastructure tests: `test/SOEA.Tests/Unit/Infrastructure/Repositories/[Entity]RepositoryTests.cs`
- [ ] Integration tests: `test/SOEA.Tests/Integration/Controllers/[Entity]ControllerTests.cs`

**Notes:**
- [What was tested, test results]

## Progress Notes

| Timestamp | Activity | Decision/Blocker |
|-----------|----------|-----------------|
| 2026-05-08 14:30 | Started | Created progress file |
| [YYYY-MM-DD HH:MM] | [What happened] | [Decision or blocker] |

## Next Immediate Step

[Describe the next action in detail. This should be actionable in < 30 minutes.]

Example: "Create `SummerAvailability.cs` entity in `src/SOEA.Domain/Entities/` with properties: `Id`, `InstructorId`, `StartDate`, `EndDate`, `Reason`. Validate that `EndDate > StartDate` in the entity constructor."

## Architecture Decisions

### Decision 1: [Decision Title]
- **What:** [What was decided]
- **Why:** [Rationale]
- **Alternative Considered:** [Why not this]
- **Impact:** [Files affected]

### Decision 2: [Decision Title]
- **What:** [What was decided]
- **Why:** [Rationale]

## Potential Blockers

- [ ] [Blocker 1] — Resolution: [How to resolve]
- [ ] [Blocker 2] — Resolution: [How to resolve]

## Related Documentation

- [`docs/business-rules/hard-constraints.md`](docs/business-rules/hard-constraints.md)
- [`docs/architecture/SOEA_Estructura_Carpetas.md`](docs/architecture/SOEA_Estructura_Carpetas.md)
- [`docs/requirements/glossary.md`](docs/requirements/glossary.md)

## Session History

### Session 1 (YYYY-MM-DD)
- **Duration:** X minutes
- **Completed:** Layer 1 (Domain)
- **Left off at:** Creating Application service

### Session 2 (YYYY-MM-DD)
- **Duration:** X minutes
- **Completed:** Layers 2-3 (Application, Infrastructure)
- **Left off at:** API controller endpoint

---

**Status:** 🟡 In Progress | 🔴 Blocked | 🟢 Complete
