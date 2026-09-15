# Plan de entrega SOEA — auditoría 2026-09-14

**Alcance.** Backend .NET 10 (~13 k líneas), frontend Angular 21 (~7,5 k), esquema PostgreSQL (13 tablas, 23 migraciones).

**Método.** Lectura de servicios, motores, repositorios, controllers y componentes, más ejecución real:

- `dotnet test` → 452/452 verdes · `vitest` → 110/110 verdes · `ng build` → OK con 3 avisos.
- API y front levantados contra **`SOEAdb_audit`**, una copia de la BD local (`SOEAdb` no se tocó).
- Pruebas con curl, psql, navegador y dos Excel generados para el import.

Etiquetas de evidencia: **[vivo]** = reproducido contra la app corriendo · **[código]** = por lectura, sin reproducir.

## Estado (2026-09-14)

| Ítem | Estado |
|---|---|
| 0.1 Commit del working tree | ✅ `282ca0b` |
| 0.2 Tests de integración contra Postgres | ✅ `test/SOEA.Tests/Integracion/` — API real (`WebApplicationFactory`) sobre una BD desechable del Postgres local (no hay Docker; `SOEA_TEST_DB` para CI). Sin servidor se omiten. |
| 0.3 Consulta de huérfanos en producción | ⏳ Pendiente: requiere acceso a la BD de prod |
| P0-1 … P0-5 | ✅ Corregidos; cada uno con test de integración que falló antes del fix (P0-4, además, con test unitario determinista) |

Cómo quedó cada bloqueante:

- **P0-1** M14 sanea antes de crear las FK: borra sesiones fuera de todo horario y las que no tienen asignatura o bloque, alinea `Sesion.bloque_tiempo_id` con su asignación y pone en NULL las referencias opcionales rotas. Verificado sobre una copia de la BD local real: el API arranca y quedan 0 huérfanos. `Migrate()` al arrancar sigue ahí (OPS-1, Fase 5).
- **P0-2** El import ya no persiste las filas día/hora del Excel como sesiones (nadie las leía); solo alimentan el requisito de aula del grupo. Desaparece el `EspacioId` temporal.
- **P0-3** La sesión fija exige un grupo del request, ocupa el lugar de una sesión del mismo grupo/asignatura/tipo y siempre recibe id nuevo. En el front, "Sesión fija" usa el mismo diálogo que "Crear sesión" (grupo, tipo y duración de la asignatura; docente opcional).
- **P0-4** La sesión manual valida contra las asignaciones reales del horario con `ValidadorRestriccionesDuras` (más HC-I01); ahora también cubre HC-C01, HC-S03, HC-CAP, HC-VH y HC-G01. La generación alinea `Sesion.BloqueTiempoId` con la asignación final.
- **P0-5** La sesión manual exige `horarioId` y se agrega a `Horario.SesioneIds`; las que no pertenecen al horario vigente ya no bloquean.

## Veredicto

Los tests en verde no cubren lo que un revisor senior va a probar primero. Hay **5 bloqueantes reproducidos en vivo** y comparten dos causas raíz:

1. **La BD no garantiza integridad.** FKs y uniques incompletos, JSON en columnas `text`, y los mismos datos guardados en `Sesiones` y `AsignacionesSemanales`, que ya divergen.
2. **Las restricciones duras están reimplementadas en 6 sitios** con modelos de tiempo distintos.

Los tests usan EF InMemory y fakes, que no aplican FKs ni índices únicos: por eso nada de esto aparece en CI.

## Orden y esfuerzo

| Fase | Qué resuelve | Esfuerzo |
|---|---|---|
| 0 | Punto de partida y red de seguridad | ½ día |
| 1 | Bloqueantes (P0) | 1–2 días |
| 2 | Base de datos en 3FN con integridad real | 2–3 días |
| 3 | Contratos y fugas de lógica | 2 días |
| 4 | Lógica duplicada y sobre-ingeniería | 1–2 días |
| 5 | Rendimiento, seguridad y pulido | 1 día |

---

## Fase 0 — Antes de tocar código

- **0.1** Commitear el working tree actual (≈100 archivos modificados sin commit en `Developing`) para revisar cada fix con un diff limpio.
- **0.2** Tests de integración contra Postgres real (Testcontainers + `WebApplicationFactory`) que reproduzcan en rojo P0-1 … P0-5.
- **0.3** Correr la consulta de huérfanos del anexo en **producción** antes de desplegar M14 (ver P0-1).

## Fase 1 — Bloqueantes

### P0-1 · El API no arranca en una BD con datos huérfanos [vivo]
`Program.cs:142` (`Database.Migrate()` al arrancar) + `M14_ClavesAjenasSesionesYGrupos`.

- **Qué pasa.** Al arrancar aplica M14 y falla con `23503 FK_Grupos_Facultades_facultad_id`: el proceso termina sin responder. Huérfanos medidos en la BD local: `Grupos.facultad_id` 78/78, `Sesiones.espacio_id` 79/81, `Sesiones.asignatura_id` y `grupo_id` 3/81. En Azure sería un crash-loop si prod tiene lo mismo.
- **Arreglo.** Migración de datos previa a M14 (facultad_id huérfana → NULL, espacio_id huérfano → NULL, borrar sesiones sin asignatura/grupo). Sacar `Migrate()` del arranque y aplicar migraciones como paso de despliegue (`dotnet ef migrations bundle`).

### P0-2 · Importar un Excel con columna "Espacio" falla completo con 409 [vivo]
`ImportarCurriculumService.cs:472-476`, `LectorExcel.cs:239`.

- **Qué pasa.** El lector crea cada Espacio con `Guid.NewGuid()` y la sesión predefinida guarda ese id temporal; el servicio remapea facultad, programa, docente, asignatura y grupo, pero **no el espacio**. Con M14 el import responde "Conflicto de datos" y revierte toda la transacción. Sin M14 persistía el id temporal: es la causa raíz de los 79 `espacio_id` huérfanos.
- **Arreglo.** `espacioIdMap` temporal→real igual que los demás, y test de integración con un `.xlsx` real.

### P0-3 · Generar con horario base siempre falla con 409 [vivo]
`GenerarHorarioService.cs:563`, `horario-api.service.ts:162`, `horario.component.ts:1285,1302`.

- **Qué pasa.** Cada sesión fija recibe `grupoId: Guid.NewGuid()`, que no existe en `Grupos` → FK de M14 → 409 al persistir. La misma petición sin horario base da 200. Además el diálogo marca toda sesión fija presencial como Laboratorio y exige docente.
- **Arreglo.** El diálogo pide grupo y tipo de sesión (ya tiene las listas); el backend exige un GrupoId válido; docente opcional.

### P0-4 · Doble reserva de aula al crear una sesión manual [vivo]
`CrearSesionManualService.cs:120, 147-151, 230`.

- **Qué pasa.** Los chequeos comparan `Sesion.BloqueTiempoId`, que guarda el bloque de Fase 1 (pista inicial), no el final. Medido: en **78/78** sesiones generadas `Sesiones.bloque_tiempo_id ≠ AsignacionesSemanales.bloque_tiempo_id`. Una sesión manual 1 h después de otra en la misma aula → **201 y un solape real en BD**. Con el mismo inicio exacto la frena el índice único, pero con un 409 "Conflicto de datos" que no explica nada. HC-I01 (docente) y HC-SEP tienen el mismo defecto.
- **Arreglo inmediato.** Validar contra `AsignacionesSemanales` pasando por `ValidadorRestriccionesDuras`. **Arreglo de fondo:** DB-1.

### P0-5 · Sesiones fantasma: bloquean pero no se ven [vivo]
`ImportarCurriculumService.cs:438-481`, `CrearSesionManualService.cs:243-248`.

- **Qué pasa.** La BD de prueba tenía 156 sesiones para un horario de 78: 78 importadas sin asignación ni horario. Un docente sin clases visibles el jueves 06:00 recibe 409 HC-I01 contra "INTRODUCCION QUIMICA (jueves 06:00)", que no aparece en ninguna pantalla. Una sesión manual creada con 201 **desaparece al recargar** (`GET /horario/actual` no la trae). El generador y `/reacomodar` no las ven, así que pueden chocar con ellas; se acumulan sin límite.
- **Arreglo.** Definir su ciclo de vida: la sesión manual pertenece al horario vigente (ver DB-2); las filas del Excel se convierten en horario base o no se persisten; script para limpiar las existentes.

## Fase 2 — Base de datos en 3FN con integridad real

Prod ya tiene datos: cada cambio va con migración de datos probada primero sobre una copia.

| ID | Sev. | Hallazgo | Propuesta |
|---|---|---|---|
| DB-1 | Alta | **Datos duplicados entre `Sesiones` y `AsignacionesSemanales`** (relación 1:1 desde M9): bloque, espacio y modalidad en ambas, y ya divergen 78/78 [vivo]. `Sesiones.espacio_id` significa dos cosas: aula fija exigida (HC-S05) y aula asignada (la escriben `ReacomodarHorarioService.cs:151,204` y la creación manual). | `Sesion` = demanda (asignatura, grupo, tipo, duración, alternancia, pareja, docente). `AsignacionSemanal` = asignación (`sesion_id` UNIQUE FK CASCADE, bloque, espacio, semana). El aula fija vive solo en el requisito del grupo. |
| DB-2 | Alta | `Horarios.sesion_ids` es un array JSON en `text`: viola 1FN, no tiene FK y se filtra en memoria cargando todas las corridas (`AsignarDocenteSesionService.cs:113`). | `Sesiones.horario_id` FK; archivar o borrar corridas superadas. |
| DB-3 | Alta | Dependencias transitivas en `Grupos`: `facultad_id` depende de `programa_id`, y `programa_id` de `asignatura_id`. Pueden contradecirse (el 100 % de `facultad_id` estaba roto). | Quitar ambas columnas (se obtienen por join); `asignatura_id` NOT NULL (el controller ya lo exige). |
| DB-4 | Media | `Sesiones.patron_alternancia_id` es función de `alternancia`. `TiposAlternancia` guarda color/semanas/activo que nadie lee. `Grupos.alternancia` (78/78 `SinAlternancia`), `Asignaturas.alternancia` y `sesiones_laboratorio_semestre` no las usa el pipeline actual. | Enum en `Sesion`; eliminar catálogo y columnas. |
| DB-5 | Media | Disponibilidad docente en 3 formatos: `Docentes.disponibilidad` (vale `[0]`, nadie la lee), `disponibilidad_ui_json` (la llena la UI, ninguna regla la usa) y `DisponibilidadDocente` (solo Excel). `Grupos` guarda disponibilidad y requisitos como JSON en `text`. | Tablas `GrupoDisponibilidad(grupo_id, dia, desde, hasta)` y `GrupoRequisitoEspacio(grupo_id, tipo_sesion, espacio_id FK, tipo_espacio)`; una sola disponibilidad docente. |
| DB-6 | Alta | FKs que siguen faltando tras M14 [vivo]: `Asignaturas.programa_id` (POST con programa inexistente → 201), `Programas.facultad_id` (DELETE de facultad con programas → 204 y programa huérfano), `AsignacionesSemanales.sesion_id/bloque_tiempo_id/espacio_id`, espacio dentro del JSON de requisitos. | Agregar las FK con migración de datos. |
| DB-7 | Media | Unicidad y CHECKs [vivo]: acepta facultades "FAC AUDIT" y "fac audit"; acepta criterio con orden 99. | UNIQUE en `Facultades(lower(nombre))`, `Programas(facultad_id, lower(nombre))`, `Espacios(lower(nombre))`, `Grupos(asignatura_id, nombre)`, `BloqueTiempos(dia, hora_inicio)`, `CriteriosCesionAlternancia(orden)`. CHECK capacidad>0, estudiantes>0, duración en (0, 8], hora_inicio<hora_fin, enums en `text` con CHECK IN. |
| DB-8 | Baja | 24/24 docentes con correo sintético `@soea.local` para cumplir un NOT NULL UNIQUE, con dos fórmulas distintas (`DocenteService.cs:68` y `NormalizadorTexto.CorreoSintetico`). | `correo` NULL + índice único parcial. |
| DB-9 | Baja | Columnas muertas: `Sesiones.es_bloque`, `esta_dividida` (siempre false), `motivo_conflicto` (siempre ""), `bloqueada` (nunca se activa), `estado`; `Horarios.estado` (nunca Publicado), `hard_constraint_violations` (siempre 0). Nombres mezclados (`generated_at` en inglés, tabla `BloqueTiempos`, columnas PascalCase en `DisponibilidadDocente`, propiedad `SesioneIds`). | Eliminar y normalizar nombres en la misma tanda de migraciones. |
| DB-10 | Media | Datos inventados, contra CLAUDE.md §4: capacidad 30 y 30 estudiantes por defecto en el import (`LectorExcel.cs:239,280`, `ImportarCurriculumService.cs:387`, `ImportController.cs:292`) y en el front. BD de prueba: 78/78 grupos y 2/2 espacios con 30. | NULL = "sin dato" y la generación lo reporta por nombre. |

## Fase 3 — Contratos y fugas de lógica

- **L-1 · Alta · [código]** El generador usa el catálogo que envía el navegador, no la BD. `POST /generar` recibe asignaturas, espacios, grupos y docentes completos (`GenerarHorarioRequest.cs:7-36`): capacidades o tipos pueden no coincidir con lo guardado, ids inválidos se descartan en silencio y `Docentes` solo alimenta un log (`GenerarHorarioService.cs:87`). → Request = `{ semestre, configuracion?, horarioBaseId? }` y el servicio lee de repositorios; desaparecen DTOs y mapeos en ambos lados.
- **L-2 · Alta · [vivo]** El aviso de disponibilidad del docente nunca aparece: docente disponible miércoles 08–10, asignado viernes 20:00 → `advertencias: []`. `GetByIdAsync` (`FindAsync`) no carga `BloquesDisponibles` (`BaseRepository.cs:34`, `AsignarDocenteSesionService.cs:63,206`) y además usa el bloque obsoleto.
- **L-3 · Alta · [vivo]** Los grupos importados se ven "incompletos" y editarlos borra su disponibilidad. `LectorExcel.cs:382` guarda claves `"Martes"`; el front lee `"martes"` (`asignaturas-tab.component.ts:212`, `disponibilidad-editor`) y con `defaultNoDisponible=true` al guardar deja todos los días cerrados → infactible por franja de grupo.
- **L-4 · Media · [código]** Contrato roto del import: el backend devuelve `gruposSinDocente` (`ImportDtos.cs:103`), el front lee `asignaturasSinDocente` (`persistencia.service.ts:267`, `import-resultado-dialog.component.ts:27`). El aviso "Sin docente" nunca se muestra.
- **L-5 · Media · [código]** El front pisa los pesos del motor: `CONFIGURACION_DEFECTO.pesoAlm = 1` (`models.ts:111`) se envía siempre como `PesoMaxHorasSeguidas` (default del backend: 3); `umbralConvergencia` fijo en 30 (`horario-api.service.ts:197`). La semilla está documentada de tres formas contradictorias.
- **L-6 · Media · [vivo]** Jornada institucional con 4 valores: backend sábado hasta 14:00 (`GrillaInstitucional.cs:30`); front hasta 13:00 (`horario.component.ts:99,283,560`); mensaje "06:00–20:00 L-V" (`CrearSesionManualService.cs:68`); KPI de ocupación con 16×6 h (`dashboard-admin.component.ts:161`). Se generó una sesión sábado 12:00–14:00 que el front no deja editar, y /revisar muestra 87 % de ocupación cuando el backend calcula 95 %. → Exponer la grilla desde el API.
- **L-7 · Baja · [código]** El front exige docentes para generar (`horario.component.ts:671`) aunque el backend los sacó del pipeline (`HorarioController.cs:65`).
- **L-8 · Baja · [código]** Semestre `"2026-1"` e "Ing. Sistemas" escritos a mano (`catalogo.service.ts:212`, `horario-api.service.ts:158,246`, `horario.component.ts:678`, `journey-bar.component.ts:42`).
- **L-9 · Media · [vivo]** Dos generaciones simultáneas del mismo semestre → una 200 y la otra 409 "Conflicto de datos". → `pg_advisory_xact_lock` por semestre y mensaje claro.
- **L-10 · Alta · [código]** Tiempo de respuesta sin techo real: el presupuesto de 5 min del bucle de cesión (`GenerarHorarioService.cs:227`) se revisa entre iteraciones, y cada iteración puede costar un solve (120 s) + una relajación + hasta 20 solves de diagnóstico (SweepGrupos está activo en prod), todo dentro de un HTTP síncrono que Azure corta a los 230 s. Sin límite de concurrencia; CP-SAT usa todos los núcleos. → Diagnóstico solo al final, un deadline único vía `CancellationToken`, `SemaphoreSlim(1)`.
- **L-11 · Media · [vivo]** Errores con 4 formatos: ProblemDetails (GET asignatura 404), texto plano (DELETE docente 404, POST generar 400), ProblemDetails sin detalle (GET grupo 404) y `{ error }` (DELETE grupo 409). Un enum inválido responde en inglés con tipos internos (``System.Nullable`1[SOEA.Domain.Enums.CategoriaAsignatura]``). Las violaciones de FK/índice salen como "Conflicto de datos" genérico. → `Problem()` en todos los controllers, `InvalidModelStateResponseFactory` en español, mapear nombre de constraint → mensaje.
- **L-12 · Baja · [vivo]** Tipo de espacio con dos grafías: `POST /espacios` con `"Salon"` → 400 (exige `"Salón"`), mientras los requisitos usan `"Salon"`. `JsonStringEnumConverter` ya está registrado: usar el nombre del enum y dejar la etiqueta al front.
- **L-13 · Baja · [vivo]** Mensajes de `/reacomodar` pensados para otra operación: mover una sola sesión responde "marque más asignaturas como candidatas a alternancia"; el tope del sábado (14:00) dice "sin cruzar la medianoche".
- **L-14 · Baja · [vivo]** NG0955 en consola: `alternancia-tab.component.ts:42` hace `track grupo.programa` y hay dos programas "INGENIERIA PESQUERA"; `track c.texto` y `track w` en horario.component también pueden repetir.

## Fase 4 — Lógica duplicada y sobre-ingeniería

### Duplicación
- **DUP-1 · Alta** Restricciones duras implementadas 6 veces con modelos de tiempo distintos: CP-SAT, `ValidadorRestriccionesDuras`, reparación del GA, `CrearSesionManualService` (índice de bloque), `AsignarDocenteSesionService` (TimeOnly), `ReacomodarHorarioService.Solapa`, y el front (`seSolapanHorarios`, `espaciosPermitidosPara`). Las divergencias son P0-4 y L-2. → Las operaciones puntuales arman el conjunto de asignaciones y llaman al validador; el front muestra el error del servidor.
- **DUP-2 · Media** Días de la semana: 5 parsers en backend (`GenerarHorarioService.DiaToString`, `CrearSesionManualService.MapearDia`, `DisponibilidadSemanal.ParseDia`, `LectorExcel.TryParseDia`, `DocenteService.DiaKey`) y 7 arreglos en el front (4 solo en `horario.component.ts`), más 4 copias de la lista de franjas.
- **DUP-3 · Baja** Tipo de espacio parseado en 5 sitios (`GenerarHorarioService` ×2, `EspaciosController`, `ImportController`, `LectorExcel`) + conversiones Salon/Salón en 2 componentes.
- **DUP-4 · Baja** `Math.Max(1, (int)Math.Ceiling(s.DuracionHoras))` en 12+ lugares → propiedad `Sesion.DuracionBloques`. `diffHoras` ×3 en el front.
- **DUP-5 · Baja** Índices por grupo (estudiantes, requisitos, bloques permitidos) reconstruidos en CP-SAT, GA, asignador de aulas, coloración, cesión y reacomodar.
- **DUP-6 · Media** Clases homónimas `GrupoDto`, `RequisitoEspacioDto`, `EspacioDto`, `AsignaturaDto` en `API.Controllers` y en `Application.Requests`, con tipos distintos (Guid vs string).
- **DUP-7 · Media** `CatalogoService` (front) con 6 funciones `switch (tipo)` y cuerpos crear/actualizar repetidos en `PersistenciaService`. → Un descriptor por entidad (url, signal, mapper).
- **DUP-8 · Media** Franjas "Matutino (06:00–12:00)"… definidas en `DocenteService`, `DisponibilidadSemanal`, `disponibilidad-editor` y `docentes-tab`. "Horario de oficina" existe en el front pero el backend no lo reconoce y lo trata como día completo (`DisponibilidadSemanal.cs:164`).

### Sobre-ingeniería y código muerto
- **SOB-1 · Media** `POST /api/import/curriculum` + ~250 líneas de mapeo en el controller (`ImportController.cs:98-342`): el front nunca lo llama y viola la regla 5 (lógica en controllers).
- **SOB-2 · Media** Sin llamadores: `LectorExcel.ParsearBloqueDisponibilidad`, `DisponibilidadSemanal.ComoFranjasCoarse`, `Sesion.EstablecerFlujo/EstablecerPatronAlternancia/Bloquear/Desbloquear/VirtualizarSesion` (la heurística que la usaba se eliminó), `Horario.MarcarComoPublicado/ActualizarFitnessScore`, `Docente.ActualizarDisponibilidad/ActualizarMaximoHoras`, constructor y `ActualizarDatos` "legado" de `Asignatura`, enum `FranjaHoraria`, SC-PRES "informativo", carpeta `src/SOEA.ConsoleRunner` (solo bin/obj, fuera de la solución), `SOEA.sln` + `SOEA.slnx` duplicadas.
- **SOB-3 · Media** `ng2-charts` + `chart.js` registrados en `app.config.ts` sin ningún gráfico en las plantillas: peso muerto en el bundle inicial (505,9 kB > presupuesto de 500 kB).
- **SOB-4 · Baja** Dependencias opcionales en constructores "para no romper tests" (`CrearSesionManualService.cs:25-34`, `AsignarDocenteSesionService.cs:27-37`): ramas degradadas en producción solo por los fakes. `IDocenteRepositorio` inyectado y sin uso en `ReacomodarHorarioService`.
- **SOB-5 · Media** Repositorios que hacen `SaveChanges` en cada Add/Update (`BaseRepository.cs:27-45`) mezclados con `UnitOfWork` + transacción manual. → Repos sin `SaveChanges`, uno por caso de uso.
- **SOB-6 · Baja** La Fase 1 (coloración) solo produce una pista y es la fuente del bloque obsoleto de P0-4; CP-SAT resolvió las 78 sesiones en milisegundos. Medir y quitarla si no aporta.
- **SOB-7 · Media** Comentarios de bitácora de auditoría ("M14 auditoría", "PERF4", "Bug:") de 5–15 líneas en casi cada método; `GenerarHorarioService` tiene 1 177 líneas. → Dejar el "por qué" y mover la historia a git/ADR.
- **SOB-8 · Baja** Parámetros del algoritmo genético (población, mutación, cruce) expuestos al coordinador en /horario; horarios base en `localStorage` (por navegador) e import/export JSON "solo vista local" que se desincroniza del servidor.

## Fase 5 — Rendimiento, seguridad y pulido

- **PERF-1 · Info · [código]** Fugas de memoria: **no encontré fugas reales** (observables HTTP que completan, diálogos de un solo uso, `valueChanges` de formularios propios del componente, DbContext scoped, sin cachés estáticas). Lo que sí crece sin límite son **datos**: sesiones importadas/manuales, filas de `Horarios` y bases en `localStorage`.
- **PERF-2 · Media · [código]** Lecturas de tabla completa por request: `AsignarDocenteSesionService` (Sesiones + Horarios + Bloques en cada PATCH), `CrearSesionManualService` (Sesiones), `GruposController.Delete` (Sesiones), `EspacioService.DeleteAsync` (Grupos), `FusionDocentesService` (Grupos + un UPDATE por fila), import con `GetById` + `Exists` + `SaveChanges` por sesión (`ImportarCurriculumService.cs:461-480`).
- **PERF-3 · Media · [vivo]** Cada entrada a /catalogo u /horario vuelve a descargar el catálogo completo (8 requests, incluida la reconstrucción del horario en el servidor), y otra vez tras cada borrado. `GET /horario/actual` responde 404 cuando no hay horario y deja un error en consola. → Cargar una vez, refrescar por mutación, responder 204.
- **PERF-4 · Baja · [vivo]** Front sin `OnPush` y con métodos en plantillas (`getMergedCellSesiones` ×3 por celda, `JSON.parse` por fila en asignaturas-tab). Medido: 0,9 ms por ciclo de detección con 40 celdas; hoy no duele.
- **PERF-5 · Baja · [vivo]** Build: presupuesto inicial excedido (505,9 kB), estilos de `horario.component` 4,73 kB > 4 kB, `TitleCasePipe` importado sin uso (NG8113).
- **SEC-1 · Alta · [vivo]** Paquetes con vulnerabilidad alta (NU1903): `System.Security.Cryptography.Xml` 9.0.3 (vía EPPlus 8.0.1) y `Microsoft.OpenApi` 2.0.0.
- **OPS-1 · Media · [código]** `Program.cs`: `Migrate()` al arrancar, `UseHttpsRedirection` con perfil solo http, `UseAuthorization` sin autenticación, `/api/health` no comprueba la BD, coma final en `appsettings.json`.
- **DOC-1 · Media · [código]** Documentación desfasada: CLAUDE.md (dice 19 migraciones, "9 controllers" y lista 10, `ILectorExcel` con 3 métodos y existe 1, ~257 tests y hay 452), `src/SOEA.API/CLAUDE.md` (bloques "en memoria", cadena de conexión hardcodeada), comentarios "catálogo fijo de 2 filas" (son 4), `Status_Task.md` de mayo, `docs/auditoria_viernes.md` apunta a `audit/` vacío, `.claude/settings.json.bak` versionado.
- **UX-1 · Baja · [vivo]** Todas las filas del catálogo en rojo por "datos incompletos" (ruido que tapa lo importante); `window.prompt`/`confirm` en /horario; mezcla de `*ngIf` y `@if`.

## Listo para entregar cuando…

- La consulta de huérfanos del anexo devuelve 0 en local y en prod.
- Hay tests de integración verdes contra Postgres para: importar Excel con espacios, generar con y sin horario base, sesión manual con solape parcial, reacomodar, y asignar docente fuera de disponibilidad (con aviso).
- `dotnet build` sin NU1903; `ng build` dentro de presupuesto y sin NG8113; consola del navegador sin errores ni NG0955.
- Todos los errores salen como ProblemDetails en español.
- CLAUDE.md refleja el estado real.

## Anexo — cómo se reprodujo

```bash
createdb -T SOEAdb SOEAdb_audit   # copia; el API se apuntó con ConnectionStrings__DefaultConnection
```

Saneamiento aplicado **solo en la copia** para poder arrancar: 78 `Grupos.facultad_id` → NULL, 79 `Sesiones.espacio_id` → NULL, 3 sesiones y 2 asignaciones huérfanas borradas.

Consulta de huérfanos (correr en prod antes de M14):

```sql
select 'grupos.facultad', count(*) from "Grupos" g where facultad_id is not null and not exists(select 1 from "Facultades" x where x.id=g.facultad_id)
union all select 'sesiones.asignatura', count(*) from "Sesiones" s where not exists(select 1 from "Asignaturas" x where x.id=s.asignatura_id)
union all select 'sesiones.grupo', count(*) from "Sesiones" s where grupo_id is not null and not exists(select 1 from "Grupos" x where x.id=s.grupo_id)
union all select 'sesiones.espacio', count(*) from "Sesiones" s where espacio_id is not null and not exists(select 1 from "Espacios" x where x.id=s.espacio_id)
union all select 'sesiones.docente', count(*) from "Sesiones" s where docente_id is not null and not exists(select 1 from "Docentes" x where x.id=s.docente_id)
union all select 'asignaturas.programa', count(*) from "Asignaturas" a where not exists(select 1 from "Programas" x where x.id=a.programa_id)
union all select 'programas.facultad', count(*) from "Programas" p where not exists(select 1 from "Facultades" x where x.id=p.facultad_id)
union all select 'sesiones_fantasma', count(*) from "Sesiones" s where not exists(select 1 from "AsignacionesSemanales" a where a.sesion_id=s.id)
union all select 'grupos.programa (M14 no lo sanea)', count(*) from "Grupos" g where not exists(select 1 from "Programas" x where x.id=g.programa_id)
union all select 'sesiones_fuera_de_horario (M14 las borra)', count(*) from "Sesiones" s where not exists(select 1 from "Horarios" h, jsonb_array_elements_text(h.sesion_ids::jsonb) e where e = s.id::text);
```

Borrar la copia cuando ya no sirva: `dropdb SOEAdb_audit`.
