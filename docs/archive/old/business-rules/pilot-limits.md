# Límites del piloto
**Última actualización:** 2026-05-16

## Propósito
Definir el alcance y las restricciones del despliegue inicial piloto de SOEA.
Este documento evita sobreingeniería en la primera versión y establece límites claros de
aceptación para las pruebas.

## Alcance
Límites del piloto: qué datos, programas y volúmenes se incluyen en la primera ejecución de prueba.

---

## Definición del piloto

El piloto es un despliegue inicial controlado con un conjunto de datos limitado para:
1. Validar el pipeline de optimización de extremo a extremo
2. Verificar el cumplimiento de las restricciones duras
3. Recopilar comentarios de los coordinadores académicos antes del despliegue institucional

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

1. Cero violaciones de restricciones duras en el horario generado
2. La optimización termina en menos de 10 minutos para el volumen de datos del piloto
3. La salida JSON es válida y coincide con la especificación en `docs/data/json-output-spec.md`
4. Al menos 2 coordinadores revisan y aprueban el horario del piloto
5. La puntuación de aptitud de restricciones blandas es ≤ 20% por encima de la puntuación base documentada del piloto

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
