# JSON Output Specification

## Purpose
Define the exact structure of the JSON document that SOEA produces as its schedule output.
Copilot uses this when generating serialization code, API response models, and frontend
data-binding types.

## Scope
The canonical JSON output of the optimization pipeline (FR-05 from `docs/requirements/SRS.md`).

---

## Top-Level Structure

```json
{
  "scheduleId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "semesterLabel": "2025-1",
  "generatedAt": "2025-03-01T10:30:00Z",
  "status": "Published",
  "summary": {
    "totalSessions": 180,
    "hardConstraintViolations": 0,
    "softConstraintFitnessScore": 12.5
  },
  "sessions": [ ... ]
}
```

---

## Session Object

```json
{
  "sessionId": "a1b2c3d4-...",
  "subject": {
    "id": "...",
    "name": "Algoritmos y Programación",
    "code": "ICSW-301"
  },
  "cohort": {
    "id": "...",
    "name": "Systems Engineering — Sem 3",
    "alternanciaType": "TypeA",
    "enrolledStudents": 28
  },
  "instructor": {
    "id": "...",
    "fullName": "Dra. Ana López",
    "email": "ana.lopez@university.edu.co"
  },
  "space": {
    "id": "...",
    "name": "Aula 204",
    "type": "Classroom",
    "capacity": 35
  },
  "timeSlot": {
    "dayOfWeek": "Monday",
    "startTime": "07:00",
    "endTime": "09:00"
  },
  "modality": "InPerson",
  "durationHours": 2.0,
  "isBlock": false,
  "isSplitBlock": false
}
```

---

## Field Descriptions

| Field | Type | Description |
|---|---|---|
| `scheduleId` | string (UUID) | Unique identifier for this schedule version |
| `semesterLabel` | string | E.g., "2025-1" or "2025-2" |
| `generatedAt` | string (ISO 8601) | UTC timestamp of generation |
| `status` | string | `Draft`, `Published`, or `Archived` |
| `summary.totalSessions` | int | Count of all sessions in the output |
| `summary.hardConstraintViolations` | int | Must be 0 in a valid published schedule |
| `summary.softConstraintFitnessScore` | decimal | Weighted soft-violation score (lower = better) |
| `session.space` | object or null | Null when modality is "Virtual" |
| `session.modality` | string | `InPerson` or `Virtual` |
| `session.timeSlot.dayOfWeek` | string | `Monday`, `Tuesday`, `Wednesday`, `Thursday`, `Friday` |
| `session.timeSlot.startTime` | string | `HH:mm` format (24-hour) |
| `session.timeSlot.endTime` | string | `HH:mm` format (24-hour) |

---

## Example: Minimal Valid Output (2 sessions)

```json
{
  "scheduleId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "semesterLabel": "2025-1",
  "generatedAt": "2025-03-01T10:30:00Z",
  "status": "Published",
  "summary": {
    "totalSessions": 2,
    "hardConstraintViolations": 0,
    "softConstraintFitnessScore": 0.0
  },
  "sessions": [
    {
      "sessionId": "a1b2c3d4-0001-0001-0001-000000000001",
      "subject": { "id": "b1b2c3d4-1111-1111-1111-111111111111", "name": "Cálculo I", "code": "MAT-101" },
      "cohort": { "id": "c1c2c3d4-2222-2222-2222-222222222222", "name": "Engineering — Sem 1", "alternanciaType": "TypeA", "enrolledStudents": 30 },
      "instructor": { "id": "d1d2d3d4-3333-3333-3333-333333333333", "fullName": "Prof. Carlos Ruiz", "email": "c.ruiz@uni.edu.co" },
      "space": { "id": "e1e2e3e4-4444-4444-4444-444444444444", "name": "Aula 101", "type": "Classroom", "capacity": 40 },
      "timeSlot": { "dayOfWeek": "Monday", "startTime": "07:00", "endTime": "09:00" },
      "modality": "InPerson",
      "durationHours": 2.0,
      "isBlock": false,
      "isSplitBlock": false
    },
    {
      "sessionId": "a1b2c3d4-0001-0001-0001-000000000002",
      "subject": { "id": "b1b2c3d4-1111-1111-1111-111111111112", "name": "Química Orgánica", "code": "QUI-201" },
      "cohort": { "id": "c1c2c3d4-2222-2222-2222-222222222223", "name": "Chemical Eng — Sem 2", "alternanciaType": "TypeB", "enrolledStudents": 22 },
      "instructor": { "id": "d1d2d3d4-3333-3333-3333-333333333334", "fullName": "Dra. María Torres", "email": "m.torres@uni.edu.co" },
      "space": null,
      "timeSlot": { "dayOfWeek": "Tuesday", "startTime": "09:00", "endTime": "11:00" },
      "modality": "Virtual",
      "durationHours": 2.0,
      "isBlock": false,
      "isSplitBlock": false
    }
  ]
}
```

---

## Downstream Usage

- **Frontend Angular app**: consumes this JSON to render schedule grids
- **Coordinator review UI**: uses `summary.hardConstraintViolations` to flag invalid drafts
- **Audit trail**: the full JSON is stored in the database per schedule version

---

## Open Questions

- Should the output include a `violations` array listing each individual constraint violation?
- Should time slots use ISO 8601 duration notation or the HH:mm strings above?
- Is there a need for a per-cohort or per-instructor filtered output endpoint?
