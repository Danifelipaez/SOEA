# Especificación de salida JSON

## Propósito
Definir la estructura exacta del documento JSON que SOEA produce como salida de horario.
Copilot usa esto al generar código de serialización, modelos de respuesta de API y tipos
de enlace de datos del frontend.

## Alcance
La salida JSON canónica del pipeline de optimización (FR-05 en `docs/requirements/SRS.md`).

---

## Estructura de nivel superior

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

## Objeto Sesión

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

## Descripción de campos

| Campo | Tipo | Descripción |
|---|---|---|
| `scheduleId` | string (UUID) | Identificador único de esta versión del horario |
| `semesterLabel` | string | Por ejemplo, "2025-1" o "2025-2" |
| `generatedAt` | string (ISO 8601) | Marca temporal UTC de generación |
| `status` | string | `Draft`, `Published` o `Archived` |
| `summary.totalSessions` | int | Cantidad total de sesiones en la salida |
| `summary.hardConstraintViolations` | int | Debe ser 0 en un horario publicado válido |
| `summary.softConstraintFitnessScore` | decimal | Puntuación ponderada de violaciones blandas (más bajo = mejor) |
| `session.space` | object or null | Null cuando la modalidad es "Virtual" |
| `session.modality` | string | `InPerson` o `Virtual` |
| `session.timeSlot.dayOfWeek` | string | `Monday`, `Tuesday`, `Wednesday`, `Thursday`, `Friday` |
| `session.timeSlot.startTime` | string | Formato `HH:mm` (24 horas) |
| `session.timeSlot.endTime` | string | Formato `HH:mm` (24 horas) |

---

## Ejemplo: salida válida mínima (2 sesiones)

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

## Uso posterior

- **Aplicación frontend Angular**: consume este JSON para renderizar rejillas de horarios
- **UI de revisión del coordinador**: usa `summary.hardConstraintViolations` para marcar borradores inválidos
- **Trazabilidad de auditoría**: el JSON completo se almacena en la base de datos por cada versión del horario

---

## Preguntas abiertas

- ¿La salida debe incluir un arreglo `violations` que liste cada violación individual de restricción?
- ¿Los espacios de tiempo deben usar notación de duración ISO 8601 o las cadenas HH:mm anteriores?
- ¿Hace falta un endpoint de salida filtrado por cohorte o por docente?
