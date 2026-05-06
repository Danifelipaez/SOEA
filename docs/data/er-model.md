# Modelo Entidad-Relación

## Propósito
Describir las relaciones entre las entidades principales de SOEA a nivel conceptual.
Copilot usa esto al generar configuraciones de entidades EF Core, migraciones y consultas.

## Alcance
Todas las entidades persistentes y sus asociaciones.

> Nota: Los nombres siguen la convención de código en inglés; entre paréntesis se indica el nombre equivalente del diagrama ER en español.

---

## Relaciones entre entidades

```
User (Usuario) ──< Administrator (Administrador)
User (Usuario) ──< Teacher (Docente)
Teacher (Docente) ──< TeacherAvailability (Disponibilidad_Docente)

Administrator (Administrador) ──< Subject (Asignatura)
Administrator (Administrador) ──< StudentGroup (Grupo_de_estudiantes)
Administrator (Administrador) ──< AcademicSpace (Espacio_Academico)
Administrator (Administrador) ──< Session (Sesion)

Subject (Asignatura) ──< StudentGroup (Grupo_de_estudiantes)
Subject (Asignatura) ──< Session (Sesion)
Subject (Asignatura) >──< AcademicSpace (Espacio_Academico) (Utiliza)

StudentGroup (Grupo_de_estudiantes) ──< Session (Sesion)
AcademicSpace (Espacio_Academico) ──< Session (Sesion)
```

---

## Cardinalidades

| Relación | Cardinalidad | Notas |
|---|---|---|
| User → Administrator | 1 : 0..1 | Especialización disjunta: un usuario puede ser administrador |
| User → Teacher | 1 : 0..1 | Especialización disjunta: un usuario puede ser docente |
| Teacher → TeacherAvailability | 1 : muchos | Un docente define varios bloques disponibles |
| Administrator → Subject | 1 : muchos | Un admin crea/gestiona asignaturas |
| Administrator → StudentGroup | 1 : muchos | Un admin registra grupos de estudiantes |
| Administrator → AcademicSpace | 1 : muchos | Un admin administra espacios |
| Administrator → Session | 1 : muchos | Un admin programa sesiones |
| Subject → StudentGroup | 1 : muchos | Una asignatura puede tener varios grupos |
| Subject → Session | 1 : muchos | Una asignatura se programa en varias sesiones |
| Subject ↔ AcademicSpace | muchos : muchos | Relación de uso/requerimientos de espacio |
| StudentGroup → Session | 1 : muchos | Un grupo participa en varias sesiones |
| AcademicSpace → Session | 1 : muchos | Un espacio aloja múltiples sesiones |

---

## Restricciones clave modeladas a nivel de BD

- `Session.StartTime < Session.EndTime`
- `Session.Modality = Virtual` implica `Session.AcademicSpaceId = NULL`
- `StudentGroup.StudentCount > 0`
- `AcademicSpace.Capacity > 0`
- `TeacherAvailability.StartTime < TeacherAvailability.EndTime`

---

## Preguntas abiertas

- ¿La relación "Utiliza" entre Subject y AcademicSpace representa un catálogo fijo o solo requisitos de equipamiento?
- ¿Debe persistirse la especialización de User (Administrator/Teacher) en tablas separadas o con discriminador?
