# Reglas de alternancia

## Propósito
Definir el modelo de programación de alternancia (híbrido) usado por la institución. Este documento es la
fuente autorizada para cualquier código que asigne grupos de estudiantes a semanas presenciales o virtuales.
Copilot usa esto al generar la lógica de asignación de sesiones y las restricciones de programación.

## Alcance
Todas las reglas que gobiernan cómo los grupos alternan entre las modalidades presencial y virtual
a lo largo de las semanas de un semestre.

---

## ¿Qué es la alternancia?

En el modelo de alternancia, los grupos de estudiantes no asisten presencialmente todas las semanas. En su lugar, alternan
entre:
- **Semanas presenciales** — los estudiantes asisten físicamente a su espacio asignado
- **Semanas virtuales** — las sesiones se dictan en línea (no se requiere espacio físico)

Esto significa que dos grupos diferentes pueden compartir el mismo espacio físico en el mismo espacio de tiempo,
siempre que estén en calendarios de alternancia opuestos.

---

## Tipos

### Type A (Tipo A)
- Asiste presencialmente en **semanas impares** (semanas 1, 3, 5, …)
- Asiste virtualmente en **semanas pares** (semanas 2, 4, 6, …)

### Type B (Tipo B)
- Asiste presencialmente en **semanas pares** (semanas 2, 4, 6, …)
- Asiste virtualmente en **semanas impares** (semanas 1, 3, 5, …)

### NonAlternating
- Asiste presencialmente en todas las semanas

---

## Reglas clave

| ID de regla | Regla | Notas |
|---|---|---|
| ALT-01 | Un grupo Tipo A y un grupo Tipo B pueden compartir el mismo espacio en el mismo horario | Nunca están presentes físicamente al mismo tiempo |
| ALT-02 | Dos grupos Tipo A NO pueden compartir el mismo espacio en el mismo horario | Siempre están presentes físicamente en las mismas semanas |
| ALT-03 | Dos grupos Tipo B NO pueden compartir el mismo espacio en el mismo horario | La misma razón que ALT-02 |
| ALT-04 | Las sesiones virtuales no consumen capacidad de espacio físico | No se aplica ninguna restricción de espacio durante las semanas virtuales |
| ALT-05 | Una sesión asignada a un día/hora fija aplica a todas las semanas del semestre | La modalidad cambia, pero el día/hora no |
| ALT-06 | Algunas asignaturas requieren asistencia presencial todas las semanas | Por ejemplo, sesiones de laboratorio |

---

## Datos requeridos por sesión

- `semana_num`: número de la semana en la que ocurre la sesión
- `es_alternancia_virtual`: indica si esa sesión ocurre virtualmente por alternancia
- `modalidad`: `Presencial` o `Virtual`

El tipo de alternancia (`AlternanciaType`) se utiliza como valor canónico del dominio para derivar
`es_alternancia_virtual` según la semana. Para `NonAlternating`, `es_alternancia_virtual` es siempre `false`.

Reglas de derivación explícitas:
- `TypeA`: `es_alternancia_virtual = (semana_num % 2 == 0)`
- `TypeB`: `es_alternancia_virtual = (semana_num % 2 != 0)`
- `NonAlternating`: `es_alternancia_virtual = false`

---

## Ejemplos

- Grupo A-3 (Type A, Ingeniería de Sistemas Sem 3) y Grupo B-7 (Type B, Ingeniería de Sistemas Sem 7)
  pueden compartir Aula 204 a las 07:00–09:00 lunes porque nunca están presentes físicamente en la misma semana.
- Grupo A-3 y Grupo A-5 (ambos Type A) no pueden compartir el mismo espacio — están presenciales en
  las mismas semanas.

---

## Impacto en el grafo de conflictos (Fase 1)

Dos sesiones entran en conflicto en la dimensión de espacio si y solo si:
- Ambos grupos tienen el mismo `AlternanciaType` (ambos A o ambos B), O
- Algún grupo está marcado como `NonAlternating`

Consulta `docs/algorithm/phase-1-graph-coloring.md` para ver cómo esto afecta la construcción de aristas.

---

## Preguntas abiertas

- ¿Un solo grupo puede contener estudiantes con distintos tipos de alternancia?
- ¿Las sesiones virtuales deben persistirse con `Id_espacio = null` o con un espacio virtual?
