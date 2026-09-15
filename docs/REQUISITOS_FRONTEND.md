# Requisitos del nuevo frontend — SOEA

Rebuild del frontend con Claude Code. Este documento define funcionalidades, pantallas, endpoints y contenido de los popups de edición. Diseño, layout y tipografía quedan a criterio de Claude Code.

> **Nota de actualización (2026-08-07):** el frontend actual ya pasó por el rediseño descrito en `docs/DESIGN_BRIEF.md`/`docs/MAPEO_FLUJOS_FRONTEND.md` — las pantallas reales están organizadas por journey (`/catalogo`, `/horario`, `/revisar`, `/publicar`), no por pestañas CRUD independientes por entidad como sugiere la organización de este documento (p. ej. "Grupos" ya no es una pestaña propia: vive como fila expandible dentro de la pestaña Asignaturas). El **contrato de API/campos** por sección documentado abajo se revisó y corrigió contra el código actual; para la **organización de pantallas real**, usar `docs/MAPEO_FLUJOS_FRONTEND.md` como fuente de verdad.

Backend de referencia: **9 controllers** (`AsignaturaController`, `CriteriosCesionAlternanciaController`, `DocentesController`, `EspaciosController`, `FacultadesController`, `GruposController`, `HorarioController`, `ImportController`, `ProgramasController`, `SesionesController`) en `src/SOEA.API/Controllers` — no 8. Todos los endpoints listados abajo fueron re-verificados contra el código actual (controllers + DTOs + services) el 2026-08-07. La sección final ["Inconsistencias y huecos detectados"](#inconsistencias-y-huecos-detectados-en-el-backend-actual) documenta los casos donde el backend no hace lo que un DTO sugiere.

`*` = campo obligatorio.

## 1. Ingesta de datos

### 1.1 Asignaturas

| Método | Ruta | Body | Respuesta | Notas |
|---|---|---|---|---|
| GET | `/api/asignaturas` | — | `AsignaturaResponse[]` | |
| GET | `/api/asignaturas/{id}` | — | `AsignaturaResponse` | 404 si no existe |
| POST | `/api/asignaturas` | `CreateAsignaturaRequest` | `AsignaturaResponse` (201) | Alternancia se infiere automáticamente por umbral de `SesionesLaboratorioSemestre` |
| PUT | `/api/asignaturas/{id}` | `UpdateAsignaturaRequest` | `AsignaturaResponse` | 404 si no existe, 400 si datos inválidos |
| DELETE | `/api/asignaturas/{id}` | — | 204 | 404 si no existe |
| PATCH | `/api/asignaturas/{id}/elegibilidad-alternancia` | `{ "elegible": bool }` | 204 | **Corregido (2026-08-07): no es "cambiar alternancia".** No existe ningún endpoint `PATCH .../alternancia`; este marca la asignatura como candidata a ceder presencialidad bajo saturación de espacio (`Asignatura.EsCandidataAlternancia`, alimenta `CriterioCesionAlternancia`). La alternancia (TipoA/TipoB/SinAlternancia) en sí se edita con el `PUT` normal, es un campo más de `UpdateAsignaturaRequest`. |

**Popup "Crear/Editar asignatura"** (corregido 2026-08-07 — el modelo real tiene 3 tracks independientes, no uno solo):
- Nombre* (texto)
- Codigo* (texto)
- ProgramaId* (select, desde `GET /api/programas`)
- Al menos uno de estos 3 tracks debe tener sesiones/semana &gt; 0:
  - Teoría presencial: SesionesTeoriaPresencialSemana (número), HorasTeoriaPresencial (número, obligatorio si el track tiene sesiones)
  - Teoría virtual (fija, sin alternancia): SesionesTeoriaVirtualSemana (número), HorasTeoriaVirtual (número, obligatorio si el track tiene sesiones)
  - Laboratorio (único track sujeto a alternancia TipoA/TipoB): SesionesLaboratorioSemana (número), HorasLaboratorio (número, obligatorio si el track tiene sesiones)
- SesionesLaboratorioSemestre* (número) — alimenta únicamente la inferencia automática de Alternancia (`DeterminarAlternancia`, umbral por defecto 8)
- Categoria (select: Obligatoria/Optativa/Electiva, default Obligatoria) — **ya expuesto en el CRUD**, ver hueco §2 corregido abajo
- Solo en edición: Alternancia (select TipoA/TipoB/SinAlternancia, override manual del valor inferido). **`DocenteId` y `EspacioFijoId` ya NO existen en esta entidad** (se movieron a Grupo, ver §1.4) — no incluir esos campos en el popup.

**Nota:** el popup "Cambiar alternancia" descrito en versiones anteriores de este documento (select rápido sobre una fila → `PATCH .../alternancia`) no tiene endpoint real que lo respalde; usar el `PUT` completo, o el nuevo toggle de elegibilidad (`PATCH .../elegibilidad-alternancia`) si lo que se quiere es marcar/desmarcar candidatura a ceder alternancia.

### 1.2 Docentes

| Método | Ruta | Body | Respuesta | Notas |
|---|---|---|---|---|
| GET | `/api/docentes` | — | `DocenteUiDto[]` | |
| POST | `/api/docentes` | `DocenteUiDto` | `DocenteUiDto` (201) | |
| PUT | `/api/docentes/{id}` | `DocenteUiDto` | `DocenteUiDto` | 404 si no existe |
| DELETE | `/api/docentes/{id}` | — | 204 | 404 si no existe |
| GET | `/api/docentes/duplicados` | — | `DocenteUiDto[][]` | Grupos de posibles duplicados (variantes de nombre) |
| POST | `/api/docentes/fusionar` | `{ canonicoId, duplicadosIds[] }` | resultado de fusión | Reasigna asignaturas del duplicado al canónico y elimina el duplicado |

**Popup "Crear/Editar docente":**
- Nombre* (texto)
- Cedula (texto)
- MaxHoras* (número)
- Disponibilidad: selector por día (lunes a sábado), cada día con 3 estados posibles (shape verificado en `DocenteService`):
  - **No disponible** → `{ noDisponible: true }`
  - **Franja general** → `{ noDisponible: false, tipo: "Franja general", franjaGeneral: <preset> }`, con `<preset>` uno de: "Todo el día (06:00–22:00)", "Horario de oficina (06:00–18:00)", "Matutino (06:00–12:00)", "Vespertino (12:00–18:00)", "Nocturno (18:00–22:00)"
  - **Franja específica** → `{ noDisponible: false, tipo: "Franja específica", desde: "HH:mm", hasta: "HH:mm" }`

**Popup "Revisar duplicados de docentes":** lista de grupos candidatos (`GET duplicados`); por grupo, elegir el registro canónico y cuáles se fusionan en él → `POST fusionar`.

### 1.3 Espacios

| Método | Ruta | Body | Respuesta | Notas |
|---|---|---|---|---|
| GET | `/api/espacios` | — | `EspacioDto[]` | |
| POST | `/api/espacios` | `EspacioDto` | `EspacioDto` (201) | |
| PUT | `/api/espacios/{id}` | `EspacioDto` | `EspacioDto` | 404 si no existe |
| DELETE | `/api/espacios/{id}` | — | 204 | 404 si no existe |

**Popup "Crear/Editar espacio":**
- Nombre* (texto)
- Tipo* (select: **"Salón"**, "Laboratorio", "Auditorio" — strings literales exactos, ver huecos §5)
- Capacidad* (número)
- Edificio (texto opcional)
- Piso (número opcional)

### 1.4 Grupos

| Método | Ruta | Body | Respuesta | Notas |
|---|---|---|---|---|
| GET | `/api/grupos` | — | `GrupoDto[]` | |
| GET | `/api/grupos/{id}` | — | `GrupoDto` | 404 si no existe |
| GET | `/api/grupos/por-asignatura/{asignaturaId}` | — | `GrupoDto[]` | |
| POST | `/api/grupos` | `GrupoDto` | `GrupoDto` (201) | AsignaturaId obligatorio (400 si falta o no existe) |
| PUT | `/api/grupos/{id}` | `GrupoDto` | `GrupoDto` | **Corregido (2026-08-07): ya NO existe `Semestre`** (columna eliminada de `Grupo`, migración `Fase2DocenteEnGrupo`). Persiste Nombre, Codigo, ProgramaId, EstudiantesInscritos, AsignaturaId, FacultadId, DocenteId, DisponibilidadUiJson, RequisitosEspacio |
| DELETE | `/api/grupos/{id}` | — | 204 | 404 si no existe |

**Popup "Crear/Editar grupo"** (corregido 2026-08-07):
- AsignaturaId* (select)
- ProgramaId* (select)
- FacultadId (select opcional)
- Nombre* (texto)
- Codigo (texto opcional)
- ~~Semestre~~ (**eliminado del modelo — no incluir en el popup**)
- EstudiantesInscritos* (número) — fuente de aforo/capacidad (HC-CAP)
- DocenteId (select opcional) — **nuevo campo (migración `Fase2DocenteEnGrupo`)**: el docente que dicta la asignatura para este grupo específico (la misma asignatura puede tener docentes distintos en grupos distintos); la generación automática lo ignora, solo se usa para asignación/edición posterior
- RequisitosEspacio (lista, opcional) — **nuevo campo (migración `P1_GrupoComoEje`)**: uno por tipo de sesión (Teoría presencial / Teoría virtual / Laboratorio), cada uno con un espacio específico o un tipo de espacio de respaldo; reemplaza al antiguo `Asignatura.EspacioFijoId`
- Disponibilidad: mismo selector por día que Docente (§1.2)

### 1.5 Importación

| Método | Ruta | Body | Respuesta | Notas |
|---|---|---|---|---|
| POST | `/api/import/excel` | `multipart/form-data`, campo `archivo` (.xlsx/.xls) | `ImportExcelStatsDto` | Detecta el modo (Curriculum/Modo2/Disponibilidad) vía `ILectorExcel` |
| POST | `/api/import/curriculum` | `CurriculumExcelDto` (con IDs temporales string) | `ImportResultDto` (mapeo tempId→realId) | Ruta alterna: cliente ya parseó y arma el JSON con IDs temporales |
| GET | `/api/facultades` | — | `{ id, nombre }[]` | |
| GET | `/api/programas` | — | `{ id, nombre, facultadId }[]` | |

`ImportExcelStatsDto`: FacultadesCreadas, ProgramasCreados, DocentesCreados, DocentesActualizados, EspaciosCreados, EspaciosActualizados, AsignaturasCreadas, AsignaturasActualizadas, GruposCreados, AsignaturasSinDocente, Advertencias[] (todos números excepto Advertencias).

**Popup "Importar Excel":** selector de archivo (.xlsx/.xls) + botón importar → al terminar, muestra `ImportExcelStatsDto` como resumen (contadores creados/actualizados + lista de advertencias).

## 2. Generación de horario

| Método | Ruta | Body | Respuesta | Notas |
|---|---|---|---|---|
| POST | `/api/horario/generar` | `GenerarHorarioRequest` | `GenerarHorarioResponse` (200) | 422 si no hay solución factible; 400 si faltan Asignaturas, Docentes o Espacios |
| POST | `/api/horario/sesion-manual` | `CrearSesionManualRequest` | `SesionGeneradaDto[]` (201) | Crea sesión sin re-ejecutar el pipeline; 400 datos inválidos, 422 si viola hard constraint (HC-I01/HC-S01/HC-S05) |
| POST | `/api/horario/reacomodar` | `ReacomodarHorarioRequest` | `ReacomodarHorarioResponse` (200) | **Agregado (2026-08-07) — faltaba en versiones anteriores de este documento.** Mueve una sesión ya generada (día/hora/espacio/semana) sin re-ejecutar el pipeline completo; es el endpoint real detrás del flujo "Editar una sesión existente" descrito en `docs/MAPEO_FLUJOS_FRONTEND.md` §3. 404 sesión inexistente, 400 datos inválidos, 422 si el nuevo bloque viola una hard constraint |

`GenerarHorarioRequest`: Semestre (string, fijo `"2026-1"`), Asignaturas[], Docentes[], Espacios[], Grupos[] (estos 4 son el estado ya cargado en Ingesta), Configuracion (opcional), SesionesFijas[] (opcional, horario base).

`GenerarHorarioResponse`: HorarioId, Semestre, EsFactible (bool), PuntajeFitness, Generaciones, MensajeError? (si no factible), Logs[], Sesiones[] (`SesionGeneradaDto`).

`SesionGeneradaDto` (lo que pinta el grid): Id, AsignaturaId, DocenteId, EspacioId?, EspacioIdHogar? (lab de origen aunque la fila sea virtual), Dia, HoraInicio, HoraFin, DuracionHoras, Alternancia (TipoA/TipoB/SinAlternancia), Virtual (bool), Semana ("A"/"B").

**Popup "Parámetros avanzados de generación"** (opcional/colapsable, no bloquea el flujo — todo tiene default):
- TamañoPoblacion (número, default 50)
- MaxGeneraciones (número, default 200)
- ProbabilidadMutacion (número 0–1, default 0.05)
- ProbabilidadCruce (número 0–1, default 0.80)
- UmbralConvergencia (número, default 30)
- PesoErgo (número, default 3)
- PesoTiempos (número, default 2)
- PesoMaxHorasSeguidas (número, default 3; antes "PesoAlmuerzo" — pondera SC-09, rachas de >6h seguidas, no almuerzo)
- PesoBalanceSemanas (número, default 2)
- PesoPresencialFirst (número, default 4; informativo desde B2 — ya no afecta el ranking del GA)
- Semilla (número entero opcional; null = producción no reproducible, fija = determinista)

**Popup "Agregar sesión fija (horario base)"** — antes de generar, para fijar restricciones de igualdad que CP-SAT no mueve:
- AsignaturaId* (select)
- DocenteId* (select)
- EspacioId (select, opcional si Virtual)
- Dia* (select lunes–sábado)
- HoraInicio* (hora)
- HoraFin* (hora)
- DuracionHoras* (número)
- Alternancia (select opcional)
- Virtual (checkbox)

**Popup "Crear sesión manual"** (sobre un horario ya generado, sin re-optimizar):
- AsignaturaId* (select)
- DocenteId* (select)
- EspacioId (select, opcional para fila virtual)
- Dia* (select: lunes, martes, miercoles, jueves, viernes, sabado)
- HoraInicio* (hora, "HH:mm")
- DuracionHoras* (número)
- Alternancia* (select: TipoA/TipoB/SinAlternancia)
- Si el backend responde 422, mostrar el mensaje de la hard constraint violada como error bloqueante (no cerrar el popup).

**Vista de horario resultante:** grid semanal pintando `SesionGeneradaDto[]`. Si `EsFactible=false`, mostrar `MensajeError` y `Logs[]` en vez del grid.

## 3. Asignación de docente post-generación

| Método | Ruta | Body | Respuesta | Notas |
|---|---|---|---|---|
| PATCH | `/api/sesiones/{id}/docente` | `{ "docenteId": guid \| null }` | `AsignarDocenteResponse` (200) | `null` desasigna. 404 sesión inexistente, 409 solape duro de horario del docente, 400 datos inválidos |

`AsignarDocenteResponse`: SesionId, DocenteId? (null si se desasignó), Advertencias[] (soft — disponibilidad/carga horaria; la asignación se persiste igual).

**Popup "Asignar docente a sesión":**
- DocenteId (select, con opción "Sin asignar" que envía `null`)
- Al guardar: si `Advertencias[]` viene con datos, mostrarlas como aviso no bloqueante (ya se guardó). Si el backend devuelve 409, mostrar error bloqueante y no cerrar el popup.

## 4. Publicación

Sin endpoint todavía (`PublicarHorarioService` pendiente en backend, ver `CLAUDE.md`). La acción de publicar debe quedar bloqueada/deshabilitada en la UI hasta que ese servicio exista. No hay contrato que documentar aún.

## 5. Configuración de alternancia

> **Corregido por completo (2026-08-07).** El CRUD de `/api/tiposalternancia` descrito en versiones anteriores de esta sección **no existe en el backend** — se verificó por grep en todo `src/` y no hay ningún `TiposAlternanciaController.cs` ni servicio de Application que lo respalde. `TipoAlternanciaConfig` (la entidad) sí existe, con configuración EF y migración (`CatalogoTiposAlternancia`), pero es un catálogo fijo de 3 filas de sistema (TipoA/TipoB/SinAlternancia) **no editable desde la UI** — no construir ningún popup de creación/edición contra él. Lo que sí existe y es editable es el catálogo de **criterios de cesión a alternancia**, documentado abajo.

| Método | Ruta | Body | Respuesta | Notas |
|---|---|---|---|---|
| GET | `/api/criterioscesionalternancia` | — | `CriterioCesionAlternanciaDto[]` | Lista ordenada de los 4 criterios de sistema (Electiva, Optativa, Elegible, MultiplesSesiones) |
| PATCH | `/api/criterioscesionalternancia/{id}` | `{ orden?, activo? }` | lista actualizada | Reordena y/o activa/desactiva un criterio |

`CriterioCesionAlternanciaDto`: Id, Criterio ("Electiva" \| "Optativa" \| "Elegible" \| "MultiplesSesiones"), Orden (número, posición en la lista — 1 = se intenta primero), Activo (bool).

**Vista "Orden de cesión a alternancia"** (lista reordenable, no un CRUD de creación — los 4 criterios son fijos):
- Lista arrastrable (drag-and-drop) de los 4 criterios en su Orden actual → `PATCH` por cada cambio de posición
- Toggle Activo/Inactivo por fila → `PATCH { activo }`
- Nota para el usuario: `MultiplesSesiones` nunca otorga elegibilidad por sí solo, solo desempata el orden entre candidatas ya elegibles por otro criterio activo — explicar esto en la UI (p. ej. tooltip) para que la coordinadora no espere que activar solo ese criterio ceda ninguna sesión.

## 6. Dashboard

Sin endpoint agregador propio — se nutre de datos ya cubiertos por las otras pantallas (conteos de asignaturas/docentes/espacios/grupos, último `ImportExcelStatsDto`, última `GenerarHorarioResponse` en memoria de sesión). Ver hueco §3: no hay histórico de horarios recuperable vía API.

## 7. Autenticación / roles

Login y guard de rutas por rol (Admin / Coordinador / Docente / Estudiante). Sin endpoints todavía — depende de que el backend implemente JWT (pendiente). Puede desarrollarse en paralelo con mocks.

## Explícitamente fuera de alcance

- Vista de horario por docente (descartada).

## Inconsistencias y huecos detectados en el backend actual

1. ✅ **Resuelto.** `PUT /api/grupos/{id}` ahora persiste también Codigo, ProgramaId y Semestre (`Grupo.ActualizarCodigo/ActualizarPrograma/ActualizarSemestre` + wiring en `GruposController.Update`). Los 3 campos pueden editarse sin restricción. **Nota 2026-08-07: `Semestre` ya no existe como campo de `Grupo`** (se eliminó en una migración posterior a esta resolución) — ver §1.4 corregida arriba.
2. ✅ **Parcialmente resuelto (2026-08-07).** `Asignatura.Categoria` **ya está expuesto** en `CreateAsignaturaRequest`/`UpdateAsignaturaRequest` — se puede editar desde el popup normal (ver §1.1 corregida). `HoraInicioMin`/`HoraFinMax` **siguen sin exponerse** en esos DTOs (solo existen en `AsignaturaDto` de generación de horario); si el popup de asignatura necesita editarlos, hay que ampliar `CreateAsignaturaRequest`/`UpdateAsignaturaRequest` en el backend primero.
3. **No existe endpoint para listar/recuperar horarios generados.** `HorarioController` solo expone `POST generar`, `POST sesion-manual` y (desde 2026-08) `POST reacomodar`. El dashboard y cualquier "histórico de horarios" dependen de que el frontend conserve en memoria el último `GenerarHorarioResponse`; no hay persistencia recuperable vía API. Sigue sin resolver.
4. **No existe `PublicarHorarioService` ni su endpoint.** Ver §4. Sigue sin resolver.
5. **`EspacioDto.Tipo` espera literalmente `"Salón"` (con tilde), `"Laboratorio"` o `"Auditorio"`.** El select del popup debe usar esos 3 strings exactos, no los nombres del enum de dominio (`Salon` sin tilde). ✅ Ya no cae en un default silencioso: `ParseTipo` ahora rechaza cualquier otro valor con 400 en vez de guardarlo como `"Salón"` sin avisar.
6. **`DocenteUiDto.Disponibilidad` y `GrupoDto.DisponibilidadUiJson` son JSON libre** — el backend no valida su forma, solo la guarda/reconstruye tal cual. El shape real que `DocenteService` produce y espera es el documentado en §1.2; se recomienda que el popup de disponibilidad de Grupo genere el mismo shape por consistencia, aunque el backend no lo exige.
7. ✅ **Resuelto (2026-08-07).** La antigua doble representación de disponibilidad de Grupo (`GrupoDto.DisponibilidadUiJson` JSON libre vs. `GenerarHorarioRequest.GrupoDto.Disponibilidad` como `List<string>`) ya no existe: ambos flujos usan hoy el mismo campo `DisponibilidadUiJson`. **Advertencia nueva, no cubierta por esta corrección:** el mapeo real del frontend a `GenerarHorarioRequest` (`HorarioApiService.generarHorario()`) actualmente NO envía `RequisitosEspacio` de cada Grupo al generar — ese dato solo se usa en los diálogos de edición/creación manual de sesión, no llega al solver CP-SAT durante la generación automática. Si el requisito de espacio por grupo debe respetarse en la generación, hay que agregarlo al payload.
8. **`Facultades`/`Programas` tienen CRUD completo (POST/PUT/DELETE), no solo GET.** Este documento en versiones anteriores solo mencionaba `GET /api/facultades` y `GET /api/programas` en §1.5 — el frontend real ya usa el CRUD completo para gestionar ambos catálogos.
9. **Posible bug de nombre de campo, no verificado en runtime:** el frontend (`HorarioApiService`) parece enviar la clave `pesoAlmuerzo` en `ConfiguracionAlgoritmoApiDto`, mientras que el backend espera `pesoMaxHorasSeguidas` (renombrado explícitamente en un comentario del propio `GenerarHorarioRequest.cs` tras una auditoría). Si esto no se corrigió, el peso de "rachas de horas seguidas" configurado por el usuario en el popup de parámetros avanzados (§2) se ignora silenciosamente y el backend usa su default (3). Verificar en código antes de reportarlo como bug confirmado.
