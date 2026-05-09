---
name: architecture-guidance
description: "Use when: planning next steps in SOEA development, validating folder/layer structure, or ensuring Clean Architecture boundaries are respected. Automatically creates and maintains a Status_Task.md file to prevent hallucinations and track progress."
---

# SOEA Architecture Guidance & Progress Tracking

## When to Use This Skill

✅ **Invoke when:**
- Planning the next development step (controllers, services, entities, repositories)
- Validating that files are created in the correct layer (API, Application, Domain, Infrastructure)
- Checking if dependencies cross Clean Architecture boundaries
- Needing to resume work on an incomplete feature
- Asking "what's the next step?" — the skill will create/update a progress file

❌ **Don't use for:**
- General C# questions
- Database schema changes (that's part of the workflow, not a prerequisite)
- Frontend Angular work (separate workspace)

---

## Workflow

### Step 1: Understand the Change Request

Extract from the user's request:
- **What**: What feature, entity, or service is being added/modified?
- **Why**: What business rule or requirement drives it?
- **Where**: Which layer(s) need to change? (Domain, Application, Infrastructure, API)

### Step 2: Load Architecture Context

Before proposing any code:
1. Read [`docs/architecture/SOEA_Estructura_Carpetas.md`](../../docs/architecture/SOEA_Estructura_Carpetas.md) for folder organization
2. Read [`docs/architecture/module-map.md`](../../docs/architecture/module-map.md) to identify the owning project
3. Read [`docs/business-rules/hard-constraints.md`](../../docs/business-rules/hard-constraints.md), [`soft-constraints.md`](../../docs/business-rules/soft-constraints.md), or [`alternancia.md`](../../docs/business-rules/alternancia.md) if the change involves constraints

### Step 3: Validate Clean Architecture Compliance

Before writing code, confirm:

| Aspect | ✅ Correct | ❌ Violation |
|--------|-----------|------------|
| **Layer Ownership** | Domain = reglas; App = orquestación; Infra = detalles | Service touches EF Core directly; Controller has SQL |
| **Dependency Flow** | API → App → Domain ← Infra | App imports Infrastructure; Domain imports Framework |
| **Entity Placement** | Shared entities in `Domain/Entities/`; services in `Application/Features/` | Entities duplicated per feature; entities in services |
| **Repository Usage** | Infrastructure implements `IRepository` (defined in Domain) | Domain or App defines repositories; Infrastructure invents interfaces |
| **Persistence** | Only Infrastructure knows about EF Core, DbContext, Migrations | Application or Domain mentions `DbContext`, `.SaveAsync()` |

### Step 4: Create/Update Progress File

If this is a multi-step task, create `Status_Task.md` in the root with:

```markdown
# Status: [TASK_NAME]

## Start State
- **Date:** YYYY-MM-DD
- **Goal:** [What we're building]
- **Files Touched:** [List of files to create/modify]

## Current Status
- [ ] Step 1: [e.g., Create Entity in Domain]
- [ ] Step 2: [e.g., Create Repository Interface in Domain]
- [ ] Step 3: [e.g., Implement Repository in Infrastructure]
- [ ] Step 4: [e.g., Create Application Service]
- [ ] Step 5: [e.g., Add Controller Endpoint]
- [ ] Step 6: [e.g., Write Tests]

## Progress Notes
- [Timestamp]: What happened, decisions made
- [Timestamp]: Blockers or clarifications needed

## Next Immediate Step
[What to do right now]

## Architecture Decisions
- [Decision 1]: [Rationale]
- [Decision 2]: [Rationale]

## Related Docs
- [`docs/business-rules/...`](docs/business-rules/...)
- [`docs/architecture/module-map.md`](docs/architecture/module-map.md)
```

### Step 5: Propose Changes Layer by Layer

In order of dependency (Domain first, then outward):

1. **Domain layer** (SOEA.Domain)
   - New entity, enum, value object, or interface
   - Validate: No external dependencies (no EF, no ASP.NET, no EPPlus)

2. **Application layer** (SOEA.Application)
   - New service in `Features/[Feature]/`
   - DTOs (Requests, Responses)
   - Validate: Depends on Domain entities and interfaces only, not Infrastructure

3. **Infrastructure layer** (SOEA.Infrastructure.Data)
   - Repository implementation
   - EF Core configuration
   - Validate: Implements interfaces from Domain; isolated from Application

4. **API layer** (SOEA.API)
   - Controller endpoint
   - Middleware (if needed)
   - Validate: Routes to Application service, not Infrastructure

### Step 6: Validate and Close

After implementing each layer:
- ✅ Confirm no circular dependencies
- ✅ Confirm tests can mock boundaries correctly
- ✅ Update `Status_Task.md` with completion date and blockers
- ✅ If work spans multiple sessions, keep the file for reference

---

## Example: Adding a New Constraint Feature

**User request:** "I need to add a new hard constraint for instructor availability in summer"

### Workflow Execution

**Step 1:** Extract
- **What**: Summer availability constraint
- **Why**: Instructors have limited availability in summer months
- **Where**: Domain (new entity), Application (new validation service), Infrastructure (new column/config), API (new endpoint)

**Step 2:** Load context
- Read `docs/business-rules/hard-constraints.md` → understand existing constraints structure
- Read `docs/architecture/module-map.md` → identify which project owns "Constraints"

**Step 3:** Validate Clean Architecture
- ❌ Don't: Create a `SummerAvailabilityService` in API and have it import EF Core
- ✅ Do: Entity in Domain, service in Application, repository in Infrastructure, endpoint in API

**Step 4:** Create progress file
```markdown
# Status: Summer Availability Constraint

## Start State
- **Date:** 2026-05-08
- **Goal:** Add summer availability constraint for instructors
- **Files:** See "Current Status"

## Current Status
- [ ] Create `SummerAvailability` entity in Domain/Entities/
- [ ] Create `ISummerAvailabilityRepository` interface in Domain/Interfaces/
- [ ] Implement `SummerAvailabilityRepository` in Infrastructure/Repositories/
- [ ] Create `CreateSummerAvailabilityService` in Application/Features/SummerAvailability/
- [ ] Add controller endpoint POST /api/summer-availability
- [ ] Add tests

## Next Immediate Step
Create `SummerAvailability` entity in `Domain/Entities/SummerAvailability.cs`
```

**Step 5:** Propose changes
- Domain: New `SummerAvailability.cs` entity
- Application: `CreateSummerAvailabilityService.cs` in `Features/SummerAvailability/`
- Infrastructure: `SummerAvailabilityRepository.cs` + `SummerAvailabilityConfiguration.cs`
- API: `SummerAvailabilityController.cs`

**Step 6:** Update status file after each layer

---

## Anti-Patterns (What NOT to Do)

❌ **"Let me just add a quick property to the DbContext"**
→ ✅ Add the property to the Domain entity first, then update DbContext configuration

❌ **"I'll have the controller call the repository directly"**
→ ✅ Route through an Application service

❌ **"I'll put the validation logic in the API controller"**
→ ✅ Put validation in Domain entity; controller just delegates

❌ **"I'll skip the progress file this is quick"**
→ ✅ Create it anyway — prevents hallucinations on follow-up sessions

---

## Quick Checklist

Before asking "what's next?":
- [ ] Progress file exists and is up-to-date
- [ ] Latest completed step is checked
- [ ] "Next Immediate Step" is clear and actionable
- [ ] No half-implemented layers (e.g., Domain entity without repository)

---

## Related Documentation

- [SOEA Folder Structure](../../docs/architecture/SOEA_Estructura_Carpetas.md)
- [Module Map](../../docs/architecture/module-map.md)
- [Architecture Overview](../../docs/architecture/architecture-overview.md)
- [Hard Constraints](../../docs/business-rules/hard-constraints.md)
- [Soft Constraints](../../docs/business-rules/soft-constraints.md)
- [Alternancia Rules](../../docs/business-rules/alternancia.md)
