# Requisitos del nuevo frontend — SOEA

Rebuild del frontend con Claude Code. Este documento define funcionalidades, pantallas, endpoints y contenido de los popups de edición. Diseño, layout y tipografía quedan a criterio de Claude Code.

Backend de referencia: 8 controllers en `src/SOEA.API/Controllers`. Todos los endpoints listados abajo fueron verificados contra el código actual (controllers + DTOs + services), no inferidos. La sección final ["Inconsistencias y huecos detectados"](#inconsistencias-y-huecos-detectados-en-el-backend-actual) documenta los casos donde el backend no hace lo que un DTO sugiere.

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
| PATCH | `/api/asignaturas/{id}/alternancia` | `{ "alternancia": "TipoA" \| "TipoB" \| "SinAlternancia" }` | 204 | Override manual — acción rápida sin abrir el form completo |

**Popup "Crear/Editar asignatura":**
- Nombre* (texto)
- Codigo* (texto)
- HorasPorSesion* (número)
- SesionesPorSemana* (número)
- SesionesLaboratorioSemestre* (número)
- ProgramaId* (select, desde `GET /api/programas`)
- Solo en edición: Alternancia (select TipoA/TipoB/SinAlternancia, override manual), DocenteId (select opcional), EspacioFijoId (select opcional, desde espacios)

**Popup/acción rápida "Cambiar alternancia":** select TipoA/TipoB/SinAlternancia sobre una fila de la lista → `PATCH .../alternancia`.

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
| PUT | `/api/grupos/{id}` | `GrupoDto` | `GrupoDto` | Persiste Nombre, Codigo, ProgramaId, Semestre, EstudiantesInscritos, AsignaturaId, FacultadId, DisponibilidadUiJson |
| DELETE | `/api/grupos/{id}` | — | 204 | 404 si no existe |

**Popup "Crear/Editar grupo":**
- AsignaturaId* (select)
- ProgramaId* (select)
- FacultadId (select opcional)
- Nombre* (texto)
- Codigo (texto opcional)
- Semestre* (número)
- EstudiantesInscritos* (número)
- Disponibilidad: mismo selector por día que Docente (§1.2)

### 1.5 Importación

| Método | Ruta | Body | Respuesta | Notas |
|---|---|---|---|---|
| POST | `/api/import/excel` | `multipart/form-data`, campo `archivo` (.xlsx/.xls) | `ImportExcelStatsDto` | Detecta el modo (Curriculum/Modo2/Disponibilidad) vía `ILectorExcel` |
| POST | `/api/import/curriculum` | `CurriculumExcelDto` (con IDs temporales string) | `ImportResultDto` (mapeo tempId→realId) | Ruta alterna: cliente ya parseó y arma el JSON con IDs temporales |
| GET | `/api/facultades` | — | `{ id, nombre }[]` | |
| GET | `/api/programas` | — | `{ id, nombre, facultadId }[]` | |

`ImportExcelStatsDto`: FacultadesCreadas, ProgramasCreados, DocentesCreados, DocentesActualizados, EspaciosCreados, EspaciosActualizados, AsignaturasCreadas, AsignaturasActualizadas, GruposCreados, SesionesPersistidas, AsignaturasSinDocente, Advertencias[] (todos números excepto Advertencias).

**Popup "Importar Excel":** selector de archivo (.xlsx/.xls) + botón importar → al terminar, muestra `ImportExcelStatsDto` como resumen (contadores creados/actualizados + lista de advertencias).

## 2. Generación de horario

| Método | Ruta | Body | Respuesta | Notas |
|---|---|---|---|---|
| POST | `/api/horario/generar` | `GenerarHorarioRequest` | `GenerarHorarioResponse` (200) | 422 si no hay solución factible; 400 si faltan Asignaturas, Docentes o Espacios |
| POST | `/api/horario/sesion-manual` | `CrearSesionManualRequest` | `SesionGeneradaDto[]` (201) | Crea sesión sin re-ejecutar el pipeline; 400 datos inválidos, 422 si viola hard constraint (HC-I01/HC-S01/HC-S05) |

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

| Método | Ruta | Body | Respuesta | Notas |
|---|---|---|---|---|
| GET | `/api/tiposalternancia` | — | `TipoAlternanciaConfigDto[]` | Ordenado: sistema primero, luego alfabético |
| POST | `/api/tiposalternancia` | `TipoAlternanciaConfigDto` | `TipoAlternanciaConfigDto` (201) | |
| PUT | `/api/tiposalternancia/{id}` | `TipoAlternanciaConfigDto` | `TipoAlternanciaConfigDto` | 404 si no existe |
| DELETE | `/api/tiposalternancia/{id}` | — | 204 | 400 si `EsSistema=true`, 404 si no existe |

`TipoAlternanciaConfigDto`: Id, Nombre, PatronBase ("PresencialEnSemanaA" \| "PresencialEnSemanaB" \| "SinAlternancia"), SemanasPresenciales (número), Color (string hex, default `#607d8b`), EsSistema (bool, true para TipoA/TipoB/SinAlternancia), Activo (bool).

**Popup "Crear/Editar tipo de alternancia":**
- Nombre* (texto)
- PatronBase* (select: PresencialEnSemanaA / PresencialEnSemanaB / SinAlternancia)
- SemanasPresenciales* (número)
- Color (color picker)
- Activo (toggle)
- EsSistema: solo lectura (badge "Sistema"); ocultar/deshabilitar el botón eliminar cuando es `true` (el backend igual lo rechaza con 400).

## 6. Dashboard

Sin endpoint agregador propio — se nutre de datos ya cubiertos por las otras pantallas (conteos de asignaturas/docentes/espacios/grupos, último `ImportExcelStatsDto`, última `GenerarHorarioResponse` en memoria de sesión). Ver hueco §3: no hay histórico de horarios recuperable vía API.

## 7. Autenticación / roles

Login y guard de rutas por rol (Admin / Coordinador / Docente / Estudiante). Sin endpoints todavía — depende de que el backend implemente JWT (pendiente). Puede desarrollarse en paralelo con mocks.

## Explícitamente fuera de alcance

- Vista de horario por docente (descartada).

## Inconsistencias y huecos detectados en el backend actual

1. ✅ **Resuelto.** `PUT /api/grupos/{id}` ahora persiste también Codigo, ProgramaId y Semestre (`Grupo.ActualizarCodigo/ActualizarPrograma/ActualizarSemestre` + wiring en `GruposController.Update`). Los 3 campos pueden editarse sin restricción.
2. **`Asignatura.Categoria`, `HoraInicioMin`, `HoraFinMax` no están expuestos en el CRUD.** Existen en el dominio (Etapa 1 Presencial-First, ver `CLAUDE.md`) y se usan en `POST /api/horario/generar` vía `AsignaturaDto.Categoria/HoraInicioMin/HoraFinMax`, pero `CreateAsignaturaRequest`/`UpdateAsignaturaRequest` no los incluyen. Si el popup de asignatura necesita editarlos, hay que ampliar esos DTOs en el backend primero.
3. **No existe endpoint para listar/recuperar horarios generados.** `HorarioController` solo expone `POST generar` y `POST sesion-manual`. El dashboard y cualquier "histórico de horarios" dependen de que el frontend conserve en memoria el último `GenerarHorarioResponse`; no hay persistencia recuperable vía API.
4. **No existe `PublicarHorarioService` ni su endpoint.** Ver §4.
5. **`EspacioDto.Tipo` espera literalmente `"Salón"` (con tilde), `"Laboratorio"` o `"Auditorio"`.** El select del popup debe usar esos 3 strings exactos, no los nombres del enum de dominio (`Salon` sin tilde). ✅ Ya no cae en un default silencioso: `ParseTipo` ahora rechaza cualquier otro valor con 400 en vez de guardarlo como `"Salón"` sin avisar.
6. **`DocenteUiDto.Disponibilidad` y `GrupoDto.DisponibilidadUiJson` son JSON libre** — el backend no valida su forma, solo la guarda/reconstruye tal cual. El shape real que `DocenteService` produce y espera es el documentado en §1.2; se recomienda que el popup de disponibilidad de Grupo genere el mismo shape por consistencia, aunque el backend no lo exige.
7. **La disponibilidad de Grupo tiene dos representaciones distintas según el endpoint:** `GrupoDto.DisponibilidadUiJson` (JSON libre por día, usado en el CRUD de Ingesta) vs `GenerarHorarioRequest.GrupoDto.Disponibilidad` (`List<string>` con valores `"Matutino"`/`"Vespertino"`, usado al generar). El frontend es responsable de traducir una a la otra; el backend no lo hace.
