# Reglas de alternancia

## Propósito
Definir el modelo de programación de alternancia (híbrido) usado por la institución. Este documento es la
fuente autorizada para cualquier código que asigne cohortes a semanas presenciales o virtuales.
Copilot usa esto al generar la lógica de asignación de sesiones y las restricciones de programación de cohortes.

## Alcance
Todas las reglas que gobiernan cómo las cohortes alternan entre las modalidades presencial y virtual
a lo largo de las semanas de un semestre.

---

## ¿Qué es la alternancia?

En el modelo de alternancia, las cohortes no asisten presencialmente todas las semanas. En su lugar, alternan
entre:
- **Semanas presenciales** — los estudiantes asisten físicamente a su espacio asignado
- **Semanas virtuales** — las sesiones se dictan en línea (no se requiere espacio físico)

Esto significa que dos cohortes diferentes pueden compartir el mismo espacio físico en el mismo espacio de tiempo,
siempre que estén en calendarios de alternancia opuestos.

---

## Tipos

### Tipo A
- Asiste presencialmente en **semanas impares** (semanas 1, 3, 5, …)
- Asiste virtualmente en **semanas pares** (semanas 2, 4, 6, …)

### Tipo B
- Asiste presencialmente en **semanas pares** (semanas 2, 4, 6, …)
- Asiste virtualmente en **semanas impares** (semanas 1, 3, 5, …)

---

## Reglas clave

| ID de regla | Regla | Notas |
|---|---|---|
| ALT-01 | Una cohorte Tipo A y una cohorte Tipo B pueden compartir el mismo espacio en el mismo espacio de tiempo | Nunca están presentes físicamente al mismo tiempo |
| ALT-02 | Dos cohortes Tipo A NO pueden compartir el mismo espacio en el mismo espacio de tiempo | Siempre están presentes físicamente en las mismas semanas |
| ALT-03 | Dos cohortes Tipo B NO pueden compartir el mismo espacio en el mismo espacio de tiempo | La misma razón que ALT-02 |
| ALT-04 | Las sesiones virtuales no consumen capacidad de espacio físico | No se aplica ninguna restricción de espacio durante las semanas virtuales |
| ALT-05 | Una sesión asignada a un día/hora fija aplica a todas las semanas del semestre | La modalidad (presencial/virtual) cambia, pero el espacio de tiempo no |
| ALT-06 | Algunas asignaturas requieren asistencia presencial todas las semanas (no alternantes) | Por ejemplo, las sesiones de laboratorio: deben marcarse en los datos de la malla curricular |

---

## Sesiones fijas vs. flexibles

- **Sesiones fijas**: el espacio de tiempo se asigna una vez y permanece durante todo el semestre (la mayoría de las sesiones)
- **Sesiones flexibles**: pueden reprogramarse por semana (raro: solo con aprobación institucional explícita)

Actualmente SOEA solo admite programación de sesiones fijas.

---

## Ejemplos

- La cohorte A-3 (Tipo A, Ingeniería de Sistemas Sem 3) y la cohorte B-7 (Tipo B, Ingeniería de Sistemas Sem 7)
  pueden compartir el salón 204 el lunes de 07:00–09:00 porque nunca están ambas presenciales en la misma semana.
- La cohorte A-3 y la cohorte A-5 (ambas Tipo A) no pueden compartir el mismo espacio: están presenciales en
  las mismas semanas.

---

## Impacto en el grafo de conflictos (Fase 1)

Dos sesiones entran en conflicto en la dimensión de espacio si y solo si:
- Ambas cohortes tienen el mismo `AlternanciaType` (ambas A o ambas B), O
- Alguna de las cohortes está marcada como no alternante (siempre presencial)

Consulta `docs/algorithm/phase-1-graph-coloring.md` para ver cómo esto afecta la construcción de aristas.

---

## Preguntas abiertas

- ¿Una sola cohorte puede contener estudiantes con distintos tipos de alternancia?
- ¿Cómo se registran las sesiones virtuales en el JSON de salida: se listan como registros separados de sesión?
