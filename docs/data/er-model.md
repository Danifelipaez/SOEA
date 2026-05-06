# Modelo Entidad-Relación

## Propósito
Describir las relaciones entre las entidades principales de SOEA a nivel conceptual.
Copilot usa esto al generar configuraciones de entidades EF Core, migraciones y consultas.

## Alcance
Todas las entidades persistentes y sus asociaciones.

---

## Relaciones entre entidades

```
Usuario ──< Administrador
Usuario ──< Docente
Docente ──< Disponibilidad_Docente

Administrador ──< Asignatura
Administrador ──< Grupo_de_estudiantes
Administrador ──< Espacio_Academico
Administrador ──< Sesion

Asignatura ──< Grupo_de_estudiantes
Asignatura ──< Sesion
Asignatura >──< Espacio_Academico (Utiliza)

Grupo_de_estudiantes ──< Sesion
Espacio_Academico ──< Sesion
```

---

## Cardinalidades

| Relación | Cardinalidad | Notas |
|---|---|---|
| Usuario → Administrador | 1 : 0..1 | Especialización disjunta: un usuario puede ser administrador |
| Usuario → Docente | 1 : 0..1 | Especialización disjunta: un usuario puede ser docente |
| Docente → Disponibilidad_Docente | 1 : muchos | Un docente define varios bloques disponibles |
| Administrador → Asignatura | 1 : muchos | Un admin crea/gestiona asignaturas |
| Administrador → Grupo_de_estudiantes | 1 : muchos | Un admin registra grupos de estudiantes |
| Administrador → Espacio_Academico | 1 : muchos | Un admin administra espacios |
| Administrador → Sesion | 1 : muchos | Un admin programa sesiones |
| Asignatura → Grupo_de_estudiantes | 1 : muchos | Una asignatura puede tener varios grupos |
| Asignatura → Sesion | 1 : muchos | Una asignatura se programa en varias sesiones |
| Asignatura ↔ Espacio_Academico | muchos : muchos | Relación de uso/requerimientos de espacio |
| Grupo_de_estudiantes → Sesion | 1 : muchos | Un grupo participa en varias sesiones |
| Espacio_Academico → Sesion | 1 : muchos | Un espacio aloja múltiples sesiones |

---

## Restricciones clave modeladas a nivel de BD

- `Sesion.Hora_inicio < Sesion.Hora_fin`
- `Sesion.Modalidad = Virtual` implica `Sesion.Id_espacio = NULL`
- `Grupo_de_estudiantes.Num_estudiantes > 0`
- `Espacio_Academico.Capacidad > 0`
- `Disponibilidad_Docente.Hora_inicio < Disponibilidad_Docente.Hora_fin`

---

## Preguntas abiertas

- ¿La relación "Utiliza" entre Asignatura y Espacio_Academico representa un catálogo fijo o solo requisitos de equipamiento?
- ¿Debe persistirse la especialización de Usuario (Administrador/Docente) en tablas separadas o con discriminador?
