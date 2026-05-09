# Skill: architecture-guidance

Workspace-scoped skill for SOEA development. Validates Clean Architecture compliance and maintains progress tracking to prevent hallucinations across sessions.

## Files in This Skill

- **SKILL.md** — Main skill definition (workflow, checklist, anti-patterns)
- **Status_Task.template.md** — Template for progress tracking files

## How to Use

### Invoke the Skill

In VS Code Chat, mention this skill when:
1. **Planning the next step:** "What should I create next for the Asignatura feature?"
2. **Validating layer structure:** "Is this code in the right layer?"
3. **Checking dependencies:** "Does this cross Clean Architecture boundaries?"
4. **Resuming work:** "I left off creating the repository; what's next?"

The skill will:
- Load relevant architecture documentation
- Validate folder structure and layer ownership
- Check dependency directions
- Create or update a `Status_Task.md` file for progress tracking

### Create a Progress File

For any multi-step feature, create a `Status_Task.md` at the project root:

```bash
cp .github/skills/architecture-guidance/Status_Task.template.md Status_FeatureName.md
```

Then fill in:
1. **Start State** — what you're building and why
2. **Current Status** — checklist of layers to implement
3. **Progress Notes** — timestamp each action
4. **Next Immediate Step** — what to do right now
5. **Architecture Decisions** — why you chose X over Y

### Update After Each Layer

After creating an entity, service, or controller:
- ✅ Check off the completed layer
- 📝 Add a note with the timestamp and what was done
- 🎯 Update "Next Immediate Step"

This prevents the agent from losing context or suggesting duplicates on follow-up prompts.

---

## Why This Skill Exists

**Problem:** Across sessions, the agent can lose context about:
- Which layers have been implemented
- What folder structure is correct for SOEA
- Whether dependencies cross boundaries
- What the next step should be

**Solution:** This skill ensures:
1. **Architecture validation** — files go in the right layer every time
2. **Progress persistence** — Status_Task.md tracks what's done
3. **Decision documentation** — why each layer was implemented a certain way
4. **Clear next steps** — no ambiguity about what to do next

---

## Example Workflow

**Session 1:**
```
User: "Add a summer availability constraint for instructors"

Agent (using this skill):
1. Loads SOEA_Estructura_Carpetas.md
2. Checks module-map.md → confirms where it belongs
3. Creates Status_SummerAvailability.md
4. Proposes Domain entity
5. User implements it
6. Updates Status_SummerAvailability.md with completion
```

**Session 2 (days later):**
```
User: "Continue the summer availability feature"

Agent (using this skill):
1. Finds Status_SummerAvailability.md
2. Reads "Domain ✅ complete; Application layer next"
3. Proposes Application service
4. Never duplicates Domain work; no hallucinations
```

---

## Anti-Patterns This Prevents

❌ **"Let's create the entity in both Domain and Application"**
→ Status_Task.md ensures it's only in one place

❌ **"I'll have the controller call the database"**
→ Skill workflow enforces API → App → Domain ← Infrastructure

❌ **"What layer did we implement last?"**
→ Status_Task.md maintains a clear checklist

❌ **"Let me guess the next step"**
→ "Next Immediate Step" field always has the answer

---

## Integration with SOEA Documentation

This skill references and enforces:
- [`docs/architecture/SOEA_Estructura_Carpetas.md`](../../docs/architecture/SOEA_Estructura_Carpetas.md) — folder structure
- [`docs/architecture/module-map.md`](../../docs/architecture/module-map.md) — layer ownership
- [`docs/business-rules/hard-constraints.md`](../../docs/business-rules/hard-constraints.md) — constraints reference
- [`docs/requirements/glossary.md`](../../docs/requirements/glossary.md) — domain terms

All decisions are justified against these docs.

---

## Troubleshooting

**"The skill doesn't load in chat"**
- Confirm VS Code recognizes `.github/skills/architecture-guidance/SKILL.md`
- Reload VS Code if needed

**"I'm not seeing the Status_Task.md file suggestion"**
- Create it manually from the template: `cp .github/skills/architecture-guidance/Status_Task.template.md Status_FeatureName.md`
- The agent will find it on the next prompt

**"The agent is hallucinating about layers again"**
- Update Status_Task.md with the current status
- Include "Next Immediate Step" with explicit detail
- In your next prompt, mention "Use the Status_Task.md file for context"
