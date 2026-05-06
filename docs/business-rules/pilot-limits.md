# Límites del piloto

## Propósito
Definir el alcance y las restricciones del despliegue inicial piloto de SOEA.
Este documento evita sobreingeniería en la primera versión y establece límites claros de
aceptación para las pruebas.

## Alcance
Límites del piloto: qué datos, programas y volúmenes se incluyen en la primera ejecución de prueba.

---

## Definición del piloto

El piloto es un despliegue inicial controlado con un conjunto de datos limitado para:
1. Validate the optimization pipeline end-to-end
2. Verify hard constraint compliance
3. Collect feedback from Academic Coordinators before institution-wide rollout

---

## Alcance del piloto (por confirmar con el experto del dominio)

| Parámetro | Límite del piloto | Notas |
|---|---|---|
| Programas académicos | Por definir (p. ej., 2–3 programas) | Se sugiere Ingeniería de Sistemas + otro programa |
| Cohortes | ≤ 20 cohortes | Entre todos los programas incluidos |
| Docentes | ≤ 30 docentes | Solo quienes impartan asignaturas del programa piloto |
| Espacios | ≤ 15 espacios | Aulas y laboratorios asignados a los programas piloto |
| Asignaturas por cohorte | Según la malla curricular real | Sin simplificación |
| Semestre | 1 semestre completo | Primavera o otoño: por definir |

---

## Criterios de aceptación del piloto

1. Zero hard constraint violations in the generated schedule
2. Optimization runs in under 10 minutes for pilot data volume
3. JSON output is valid and matches the spec in `docs/data/json-output-spec.md`
4. At least 2 coordinators review and approve the pilot schedule
5. Soft-constraint fitness score is ≤ 20% above the documented pilot baseline score

---

## Riesgos conocidos

- Los datos de entrada (archivos Excel) pueden tener inconsistencias que deben detectarse durante la ingesta
- Los datos de disponibilidad de docentes pueden estar incompletos en la primera ejecución
- Las asignaciones de tipo de alternancia pueden no ser uniformes entre cohortes

---

## Preguntas abiertas

- ¿Qué programas están confirmados para el piloto?
- ¿Quiénes son los 2 coordinadores designados para validar la salida del piloto?
- ¿A qué semestre apunta el piloto?
