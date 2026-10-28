# Frontend

> `frontend/soea-angular` · Angular 21 standalone + Angular Material · signals · vitest. Verificado el 2026-10-14.

## Usuario y diseño

Un solo operador, la coordinación académica, **sin login ni roles**. Los textos de la UI van dirigidos a personas no técnicas: nada de jerga como "fallback", "CP-SAT" o "hard constraint". Las pantallas están organizadas por el recorrido de trabajo, no por entidad (rediseño de julio de 2026):

| Paso | Ruta | Componente | Qué hace |
|---|---|---|---|
| 1 | `/catalogo` | `IngestaComponent` | Pestañas Asignaturas (los grupos como filas expandibles), Docentes, Espacios y Alternancia (asignaturas que pueden alternar, qué clases pueden alternar por tipo de espacio —todas, solo laboratorios o solo salones y auditorios— con aviso si la combinación no deja ninguna pareja, y prioridad de criterios). Carga de Excel |
| 2–3 | `/horario` | `HorarioComponent` | Generar (diálogo de progreso), ver la grilla por aula, "Virtual" o grupo, editar sesión (`/reacomodar`, docente), limpiar horario <br>**Avisos persistentes** sobre la grilla: si generar falla (haya o no horario guardado) `GeneracionFallidaComponent` lista los grupos en conflicto (o, si no alcanzan las aulas a ciertas horas, cada hora con sus clases y el programa de cada grupo) y enlaza a `/catalogo?estado=incompletas`, que abre el catálogo con el filtro «Con datos incompletos o en conflicto» (un grupo deja de marcarse en conflicto en cuanto se edita él o su asignatura; si sigue sin caber, lo marca «datos incompletos»); si el horario guardado tiene clases fuera de la disponibilidad de su grupo, un aviso «desactualizado» ofrece volver a generar; **antes de generar**, «⚠ Así no se puede generar el horario» lista las horas donde la disponibilidad obliga a más clases a coincidir que aulas hay (`POST /horario/choques-aulas`, consultado al entrar y cada vez que cambia el catálogo, las clases fijas o el tipo de espacio de alternancia) <br>**Filtro por facetas** («Ver solo»): un buscador agrega chips de facultad, programa, asignatura, grupo o docente (uno u otro dentro de un tipo, todos entre tipos); cada opción dice cuántas clases deja a la vista y no se ofrecen las que vaciarían la cuadrícula; «Atenuar las demás» las pinta en gris en vez de ocultarlas <br>**Ajustes del horario** (panel lateral, cerrado por defecto: lo abre su botón de la barra o una prueba; un clic en la cuadrícula lo cierra salvo con una prueba abierta). Sección *Probar sin guardar*: clases fijas que se pueden apagar sin borrarlas; «📌 Fijar una clase» (o «📌 Fijar para pruebas» al editar una clase) deja elegir una clase en la cuadrícula —las demás pierden contraste, Esc o un clic fuera cancela— y la fija donde está (con su pareja de alternancia: el servidor la rearma solo si las dos quedan fijas; «Fijar para pruebas» no deja fijar fuera de la jornada ni de la disponibilidad del grupo, y una clase movida pierde su pareja); «🧪 Probar sin guardar» genera con `soloPrueba` y muestra el resultado sin tocar el horario guardado (clic en una clase = fijarla; «Probar otra vez» repite la prueba con las clases fijas actuales). Una prueba sin solución muestra su motivo en un aviso propio y no cambia el aviso de la última generación real ni marca grupos en conflicto; «Guardar este horario» lo guarda tal cual; «Guardar como punto de partida» guarda el horario actual para que las próximas generaciones respeten sus clases. Sección *Cambios al horario guardado*: «＋ Sesión manual» y «✂ Liberar espacio del horario», que saca la clase elegida (y su pareja de alternancia) con `DELETE /horario/{id}/sesiones/{sesionId}`. **Pendientes de ubicar** no se guardan: `clasesPendientes` las deduce (lo que el catálogo pide por grupo y tipo menos lo que está en el horario); cada una se ubica con «Elegir hora» (sesión manual ya en su grupo y tipo, con aula libre propuesta y bloqueo si el grupo no está disponible o ya tiene clase) o todas con «🔎 Buscar lugar para las pendientes», una prueba que manda todas las demás clases como fijas para que el generador solo complete lo que falta. Clases fijas y tipo de espacio de alternancia viven en `localStorage` de este navegador <br>Con parejas de alternancia aparece el selector **Horario completo / Semana siguiente**: el horario completo muestra todo y marca con ⇄ la clase que alterna e indica qué asignatura usa ese espacio la próxima semana; la semana siguiente muestra solo las parejas que alternan (el resto vacío, porque es el mismo horario) |
| 4 | `/revisar` | `DashboardAdminComponent` | Mismo aviso de generación fallida que `/horario`; bloque **Conflictos** del horario guardado (fuera de la disponibilidad del grupo, espacio ocupado, clases sin ubicar, avisos de docente) con enlace a Horario; KPIs: ocupación en horas-aula (88 h por aula y semana), presencialidad, carga docente |
| 5 | `/publicar` | `PublicarComponent` | Bloqueado hasta que exista `PublicarHorarioService` |

`JourneyBarComponent` muestra los pasos y el semestre. Cualquier ruta desconocida redirige a `/catalogo`.

## Estado y datos (`src/app/core`)

| Archivo | Rol |
|---|---|
| `state.service.ts` | Estado global con signals: catálogo (cada lista se guarda en orden alfabético, así todo listado sale ordenado), horario vigente, prueba, clases fijas, filtros de /horario, mapas de búsqueda computados, color determinista por asignatura, `semestre` |
| `catalogo.service.ts` | Única forma de cargar el catálogo desde la API al `StateService` (`cargarTodo`), con normalización de claves de día |
| `persistencia.service.ts` | CRUD del catálogo contra la API. Se guarda al pulsar "Aceptar" en cada diálogo; no hay botón de guardar global |
| `horario-api.service.ts` | Contrato y llamadas de `generar`, `actual`, `reacomodar`, `sesion-manual`, liberar sesión, docente e importación. Traduce ProblemDetails a mensajes en español |
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
