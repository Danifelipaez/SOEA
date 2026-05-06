# Restricciones blandas

## Propósito
Listar las preferencias de programación que el sistema debe optimizar, pero que puede relajar cuando
no sea posible lograr una solución totalmente óptima. Estas restricciones contribuyen a la función de aptitud
en la Fase 3 (Algoritmo Genético).
Copilot usa esto al implementar la función de aptitud en `SOEA.Engine.Genetic`.

## Alcance
Todas las preferencias de optimización. Las reglas que nunca deben violarse pertenecen a `hard-constraints.md`.

---

## Lista de restricciones

Cada restricción blanda tiene un **peso** (más alto = más importante de cumplir).
Los pesos son configurables y sirven como valores iniciales por defecto.

| ID | Regla | Peso predeterminado |
|---|---|---|
| SC-01 | Los horarios del docente deben ser compactos (minimizar huecos inactivos entre sesiones) | 3 |
| SC-02 | Los horarios de la cohorte deben ser compactos (minimizar huecos inactivos para los estudiantes) | 3 |
| SC-03 | Las sesiones de la misma cohorte en el mismo día no deberían dejar más de 1 hora de hueco | 2 |
| SC-04 | Evitar, cuando sea posible, programar la primera sesión de una cohorte antes de las 07:00 o la última después de las 19:00 | 2 |
| SC-05 | Asignar a la misma cohorte el mismo salón para la misma asignatura en distintas semanas (estabilidad de aula) | 2 |
| SC-06 | Distribuir de forma uniforme la carga del docente a lo largo de los días (evitar concentrarla al inicio o al final) | 2 |
| SC-07 | Minimizar el número de espacios diferentes usados por la misma cohorte en un mismo día | 1 |
| SC-08 | Preferir programar asignaturas relacionadas (mismo programa, misma cohorte) en espacios de tiempo adyacentes | 1 |
| SC-09 | Evitar asignar a los docentes el máximo de horas permitidas todos los días | 1 |

---

## Resumen de la función de aptitud

La puntuación de aptitud de un horario (cromosoma) se calcula como:

```
fitness = Σ (weight_i × violation_count_i)   for all soft constraints SC-01..SC-09
```

Una puntuación de aptitud **más baja** es mejor (menos violaciones ponderadas).
Un horario con fitness = 0 satisface perfectamente todas las restricciones blandas.

El Algoritmo Genético minimiza esta puntuación a lo largo de las generaciones.
Consulta `docs/algorithm/phase-3-genetic-algorithm.md` para más detalles.

---

## Ejemplos

- Una cohorte con sesiones de 07:00–09:00 y 13:00–15:00 sin clase en medio incurre en una
  violación de SC-02 (hueco inactivo de 4 horas).
- Un docente programado durante 6 horas consecutivas el lunes y 0 horas el viernes incurre en una
  violación de SC-06.

---

## Preguntas abiertas

- ¿Los pesos predeterminados anteriores son correctos o la institución debería configurarlos por programa?
- ¿SC-05 (estabilidad de aula) debería elevarse a restricción dura por motivos de accesibilidad?
- ¿SC-04 (horarios preferidos) es configurable por cohorte o a nivel institucional?
