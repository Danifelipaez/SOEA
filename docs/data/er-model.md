# Modelo Entidad-Relación

## Propósito
Describir las relaciones entre las entidades principales de SOEA a nivel conceptual.
Copilot usa esto al generar configuraciones de entidades EF Core, migraciones y consultas.

## Alcance
Todas las entidades persistentes y sus asociaciones.

---

## Relaciones entre entidades

```
Program ──< Cohort >──< Session >── TimeSlot
                             │
                             ├── Instructor
                             ├── Space
                             └── Subject ──> Program

Instructor ──< InstructorAvailability (TimeSlot list)

Schedule ──< Session
```

---

## Cardinalidades

| Relación | Cardinalidad | Notas |
|---|---|---|
| Program → Cohort | 1 : muchos | Un programa tiene muchas cohortes |
| Program → Subject | 1 : muchos | Un programa tiene muchas asignaturas en su malla curricular |
| Cohort → Session | 1 : muchos | Una cohorte tiene muchas sesiones por semestre |
| Subject → Session | 1 : muchos | Una asignatura genera una o más sesiones por cohorte |
| Instructor → Session | 1 : muchos | Un docente imparte muchas sesiones |
| Space → Session | 1 : muchos | Un espacio puede alojar muchas sesiones (en distintos espacios de tiempo) |
| TimeSlot → Session | 1 : muchos | Un espacio de tiempo puede asignarse a muchas sesiones (en espacios distintos) |
| Schedule → Session | 1 : muchos | Un horario contiene todas las sesiones de un semestre |
| Instructor → TimeSlot | muchos : muchos | Mediante la tabla de unión `InstructorAvailability` |

---

## Restricciones clave modeladas a nivel de BD

- Una `Session` no puede tener `SpaceId` y `Modality = InPerson` ambos nulos
- Una `Session` con `Modality = Virtual` debe tener `SpaceId = null`
- `TimeSlot.StartTime < TimeSlot.EndTime`
- `Cohort.EnrolledStudents > 0`
- `Subject.WeeklyHours > 0`

---

## Preguntas abiertas

- ¿`InstructorAvailability` debe ser una tabla separada o una columna JSON en `Instructor`?
- ¿Existe una entidad `Program` o el programa es solo un campo de texto en `Cohort` y `Subject`?
