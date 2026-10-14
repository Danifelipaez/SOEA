# Frontend

> `frontend/soea-angular` · Angular 21 standalone + Angular Material · signals · vitest. Verificado el 2026-10-14.

## Usuario y diseño

Un solo operador, la coordinación académica, **sin login ni roles**. Los textos de la UI van dirigidos a personas no técnicas: nada de jerga como "fallback", "CP-SAT" o "hard constraint". Las pantallas están organizadas por el recorrido de trabajo, no por entidad (rediseño de julio de 2026):

| Paso | Ruta | Componente | Qué hace |
|---|---|---|---|
| 1 | `/catalogo` | `IngestaComponent` | Pestañas Asignaturas (los grupos como filas expandibles), Docentes, Espacios y Alternancia (criterios de cesión). Carga de Excel |
| 2–3 | `/horario` | `HorarioComponent` | Generar (diálogo de progreso), ver la grilla por aula, "Virtual" o grupo, editar sesión (`/reacomodar`, docente), agregar clase manual, limpiar horario |
| 4 | `/revisar` | `DashboardAdminComponent` | KPIs de solo lectura: ocupación en horas-aula (88 h por aula y semana), presencialidad, conflictos |
| 5 | `/publicar` | `PublicarComponent` | Bloqueado hasta que exista `PublicarHorarioService` |

`JourneyBarComponent` muestra los pasos y el semestre. Cualquier ruta desconocida redirige a `/catalogo`.

## Estado y datos (`src/app/core`)

| Archivo | Rol |
|---|---|
| `state.service.ts` | Estado global con signals: catálogo, horario vigente, mapas de búsqueda computados, color determinista por asignatura, `semestre` |
| `catalogo.service.ts` | Única forma de cargar el catálogo desde la API al `StateService` (`cargarTodo`), con normalización de claves de día |
| `persistencia.service.ts` | CRUD del catálogo contra la API. Se guarda al pulsar "Aceptar" en cada diálogo; no hay botón de guardar global |
| `horario-api.service.ts` | Contrato y llamadas de `generar`, `actual`, `reacomodar`, `sesion-manual`, docente e importación. Traduce ProblemDetails a mensajes en español |
| `jornada.ts` | Espejo de `GrillaInstitucional` (única fuente en el frontend) |
| `semestre.ts` | Semestre inicial (`SEMESTRE_POR_DEFECTO`); la UI todavía no permite elegirlo |
| `models.ts`, `orden.ts`, `id.util.ts`, `http-error.util.ts` | Modelos, ordenamientos y utilidades |

`environment.ts` / `environment.prod.ts` definen `apiBaseUrl` (local: `http://localhost:5066/api`).

**El generador recibe el catálogo desde el navegador** (no lo relee de la BD): lo que muestra el `StateService` es lo que se agenda.

## Reglas reflejadas en el cliente

`features/horario/horario.reglas.ts` reproduce reglas del backend solo para **avisar** (choque o disponibilidad del docente después de generar, sesiones escalonadas en la grilla). Nunca bloquea una generación: la autoridad es el backend.

## Importación de Excel (dos pasos)

1. `POST /api/import/excel/revisar` → filas crudas + incoherencias (no guarda nada).
2. Si hay incoherencias, `ImportRevisionDialogComponent` permite editar o borrar filas y revalidar (`POST /api/import/filas/revisar`).
3. `POST /api/import/filas` construye y guarda el currículo (422 si queda algún error). `ImportResultadoDialogComponent` muestra el resumen y lo que se asumió por falta de datos.

Errores que bloquean: campo obligatorio vacío, día u hora ilegible, Final ≤ Hora, duración ≠ Final − Hora, misma asignatura con duraciones distintas, fila repetida, grupo con clases que se cruzan. Avisos: choque de aula o de docente, sin día/hora, sin duración.

Columnas detectadas por cabecera (no por posición): Facultad, Programa, Asignatura, Código?, TipoEspacio?, Espacio/Curso/Salón/Aula, Duración/Horas/Reales [h], Día, Hora, Docente, Grupo?, Final?. Acepta el formato legado (A–J) y el real de la coordinación.

## Componentes compartidos (`src/app/shared`)

`disponibilidad-editor` (disponibilidad por día de grupos y docentes), `requisitos-espacio` (requisito de aula por tipo de sesión), `searchable-select`, `confirm-delete-dialog`, `import-revision-dialog`, `import-resultado-dialog`, `journey-bar`.
