# Especificación de salida JSON

## Propósito
Definir la estructura exacta del documento JSON que SOEA produce como salida de horario.
Copilot usa esto al generar código de serialización, modelos de respuesta de API y tipos
de enlace de datos del frontend.

## Alcance
La salida JSON canónica del pipeline de optimización (FR-05 en `docs/requirements/SRS.md`).

> Nota: Los nombres siguen la convención de código en inglés; equivalencias en español se describen en `data-dictionary.md`.

---

## Estructura de nivel superior

```json
{
  "scheduleId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "period": "2025-1",
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

## Objeto Session

```json
{
  "sessionId": "a1b2c3d4-...",
  "studentGroupId": "c1c2c3d4-...",
  "teacherId": "d1d2c3d4-...",
  "academicSpaceId": "e1e2c3d4-...",
  "dayOfWeek": "Monday",
  "startTime": "07:00",
  "endTime": "09:00",
  "modality": "InPerson",
  "weekNumber": 3,
  "isVirtualAlternation": false,
  "createdByAdminId": "f1f2f3f4-...",
  "studentGroup": {
    "studentGroupId": "c1c2c3d4-...",
    "academicProgram": "Ingeniería de Sistemas",
    "cohortLabel": "Sem 3",
    "studentCount": 28,
    "subject": {
      "subjectId": "b1b2c3d4-...",
      "name": "Algoritmos y Programación",
      "classType": "Lecture"
    }
  },
  "teacher": {
    "teacherId": "d1d2c3d4-...",
    "name": "Dra. Ana López",
    "email": "ana.lopez@university.edu.co",
    "employmentType": "Planta"
  },
  "academicSpace": {
    "academicSpaceId": "e1e2c3d4-...",
    "name": "Aula 204",
    "buildingBlock": "B",
    "spaceType": "Classroom",
    "capacity": 35,
    "equipment": "Proyector",
    "isVirtual": false
  }
}
```

> `academicSpaceId` será `null` cuando `modality = "Virtual"`; el campo `academicSpace` puede omitirse o ser `null`.

---

## Descripción de campos

| Field | Type | Description |
|---|---|---|
| `scheduleId` | string (UUID) | Identificador único de esta versión del horario |
| `period` | string | Por ejemplo, "2025-1" o "2025-2" |
| `generatedAt` | string (ISO 8601) | Marca temporal UTC de generación |
| `status` | string | `Draft`, `Published` o `Archived` |
| `summary.totalSessions` | int | Cantidad total de sesiones en la salida |
| `summary.hardConstraintViolations` | int | Debe ser 0 en un horario publicado válido |
| `summary.softConstraintFitnessScore` | decimal | Puntuación ponderada de violaciones blandas (más bajo = mejor) |
| `session.modality` | string | `InPerson` o `Virtual` |
| `session.dayOfWeek` | string | `Monday`, `Tuesday`, `Wednesday`, `Thursday`, `Friday` |
| `session.startTime` | string | Formato `HH:mm` (24 horas) |
| `session.endTime` | string | Formato `HH:mm` (24 horas) |
| `session.weekNumber` | int | Número de semana del semestre |
| `session.isVirtualAlternation` | bool | Indica alternancia virtual en esa semana |

---

## Ejemplo: salida válida mínima (2 sesiones)

```json
{
  "scheduleId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "period": "2025-1",
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
      "studentGroupId": "c1c2c3d4-2222-2222-2222-222222222222",
      "teacherId": "d1d2c3d4-3333-3333-3333-333333333333",
      "academicSpaceId": "e1e2c3d4-4444-4444-4444-444444444444",
      "dayOfWeek": "Monday",
      "startTime": "07:00",
      "endTime": "09:00",
      "modality": "InPerson",
      "weekNumber": 1,
      "isVirtualAlternation": false,
      "createdByAdminId": "f1f2f3f4-5555-5555-5555-555555555555",
      "studentGroup": {
        "studentGroupId": "c1c2c3d4-2222-2222-2222-222222222222",
        "academicProgram": "Ingeniería",
        "cohortLabel": "Sem 1",
        "studentCount": 30,
        "subject": { "subjectId": "b1b2c3d4-1111-1111-1111-111111111111", "name": "Cálculo I", "classType": "Lecture" }
      },
      "teacher": { "teacherId": "d1d2c3d4-3333-3333-3333-333333333333", "name": "Prof. Carlos Ruiz", "email": "c.ruiz@uni.edu.co", "employmentType": "Catedra" },
      "academicSpace": { "academicSpaceId": "e1e2c3d4-4444-4444-4444-444444444444", "name": "Aula 101", "buildingBlock": "A", "spaceType": "Classroom", "capacity": 40, "equipment": "Proyector", "isVirtual": false }
    },
    {
      "sessionId": "a1b2c3d4-0001-0001-0001-000000000002",
      "studentGroupId": "c1c2c3d4-2222-2222-2222-222222222223",
      "teacherId": "d1d2c3d4-3333-3333-3333-333333333334",
      "academicSpaceId": null,
      "dayOfWeek": "Tuesday",
      "startTime": "09:00",
      "endTime": "11:00",
      "modality": "Virtual",
      "weekNumber": 2,
      "isVirtualAlternation": true,
      "createdByAdminId": "f1f2f3f4-5555-5555-5555-555555555555",
      "studentGroup": {
        "studentGroupId": "c1c2c3d4-2222-2222-2222-222222222223",
        "academicProgram": "Ingeniería",
        "cohortLabel": "Sem 2",
        "studentCount": 22,
        "subject": { "subjectId": "b1b2c3d4-1111-1111-1111-111111111112", "name": "Química Orgánica", "classType": "Lab" }
      },
      "teacher": { "teacherId": "d1d2c3d4-3333-3333-3333-333333333334", "name": "Dra. María Torres", "email": "m.torres@uni.edu.co", "employmentType": "Planta" },
      "academicSpace": null
    }
  ]
}
```

---

## Uso posterior

- **Aplicación frontend Angular**: consume este JSON para renderizar rejillas de horarios
- **UI de revisión del coordinador**: usa `summary.hardConstraintViolations` para marcar borradores inválidos
- **Trazabilidad de auditoría**: el JSON completo se almacena en la base de datos por cada versión del horario

---

## Preguntas abiertas

- ¿El frontend necesita que `studentGroup`, `teacher` y `academicSpace` sean siempre objetos completos o basta con IDs?
- ¿Se debe incluir un arreglo de violaciones con detalle por sesión?
- ¿La semana se representa con `weekNumber` numérico o con fechas reales del calendario?
