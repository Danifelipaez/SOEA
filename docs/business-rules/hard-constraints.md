# Restricciones duras

## Propósito
Listar todas las reglas que el horario generado debe cumplir sin excepción.
Un horario que viole cualquier restricción dura es inválido y no debe aceptarse como salida.
Copilot usa este documento al implementar modelos de restricciones en `SOEA.Engine.ConstraintProg`
y validadores en `SOEA.Application`.

## Alcance
Todas las reglas de programación no negociables. Las preferencias de optimización pertenecen a `soft-constraints.md`.

---

## Lista de restricciones

### Restricciones de espacio

| ID | Regla |
|---|---|
| HC-S01 | Un espacio no puede alojar dos sesiones en el mismo espacio de tiempo si ambas cohortes son Tipo A, ambas son Tipo B o alguna cohorte es NonAlternating |
| HC-S02 | El total de estudiantes inscritos de las sesiones presentes físicamente en el mismo espacio y espacio de tiempo no debe exceder la capacidad del espacio |
| HC-S03 | Una sesión que requiera un tipo específico de espacio (laboratorio, auditorio) debe asignarse a un espacio de ese tipo |
| HC-S04 | Las sesiones virtuales no deben asignarse a un espacio físico |

### Restricciones de docente

| ID | Regla |
|---|---|
| HC-I01 | Un docente no puede asignarse a dos sesiones en el mismo espacio de tiempo |
| HC-I02 | Una sesión solo debe asignarse a un espacio de tiempo dentro de la disponibilidad declarada del docente |
| HC-I03 | Un docente no puede exceder el máximo de horas semanales contratadas |

### Restricciones de tiempo

| ID | Regla |
|---|---|
| HC-T01 | Las sesiones deben programarse dentro del horario de operación de la institución (07:00–21:30) |
| HC-T02 | Las sesiones de laboratorio no pueden comenzar después de las 19:30 (para terminar como máximo a las 21:30) |
| HC-T03 | Un bloque de 3 horas debe asignarse a horas consecutivas en el mismo día |
| HC-T04 | Ninguna sesión puede programarse durante el receso del mediodía (12:00–13:00) salvo que la institución lo permita explícitamente |
| HC-T05 | Las sesiones que formen un bloque dividido (horas distribuidas en varios días) no deben programarse en días consecutivos |

### Restricciones de cohorte

| ID | Regla |
|---|---|
| HC-C01 | Una cohorte no puede tener dos sesiones en el mismo espacio de tiempo |
| HC-C02 | Las horas totales programadas de una cohorte deben coincidir con las horas definidas por la malla curricular por asignatura y semana |

### Restricciones específicas de asignatura

| ID | Regla |
|---|---|
| HC-SU01 | Las asignaturas marcadas como "siempre 8+8" (por ejemplo, Química Orgánica) deben programarse como dos sesiones consecutivas de 8 horas |
| HC-SU02 | Las asignaturas marcadas como no alternantes deben tener sesiones en todas las semanas, no solo en las semanas alternas |

---

## Ubicación de la lógica de validación

Las restricciones duras se aplican en dos lugares:
1. **Fase 2** (`SOEA.Engine.ConstraintProg`) — el modelo CP-SAT impone todas las HC como restricciones duras
2. **Validador posterior a la generación** (`SOEA.Application`) — verifica la salida antes de devolver el horario

---

## Ejemplos

- Asignar al docente López a dos sesiones el lunes 07:00–09:00 → viola HC-I01
- Programar una sesión de laboratorio a las 20:00 → viola HC-T02
- Asignar 30 estudiantes a un salón con capacidad 25 → viola HC-S02

---

## Preguntas abiertas

- ¿HC-T04 (receso del almuerzo) es configurable por institución o siempre está fijo en 12:00–13:00?
- ¿Cuál es la marca exacta de la malla curricular usada para señalar asignaturas "siempre 8+8" y siempre significa dos sesiones consecutivas de 8 horas?
- ¿Existen límites duros sobre horas consecutivas de docencia para los instructores?
