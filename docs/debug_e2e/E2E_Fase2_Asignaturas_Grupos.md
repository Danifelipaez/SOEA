# Debug E2E — Fase 2: Asignaturas + Grupos (la fase más densa)

> Auditoría de contrato Frontend ↔ Backend. Ver `docs/debug_e2e/README.md`. Solo diagnóstico.

## Alcance

| Endpoint | BE | FE call-site |
|---|---|---|
| `/api/asignaturas` GET | `AsignaturasController.GetAll` → `AsignaturaResponse` | `persistencia` L143-145, `mapAsignatura` L132-147 |
| `/api/asignaturas/{id}` PUT | `AsignaturaService.UpdateAsync` ← `UpdateAsignaturaRequest` | `persistencia.actualizarAsignatura` L147-161 |
| `/api/asignaturas/{id}/alternancia` PATCH | `UpdateAlternanciaAsync` | `persistencia` L163-165 |
| `/api/asignaturas/{id}` DELETE | `DeleteAsync` | `persistencia` L167-169 |
| `/api/asignaturas` POST | `CreateAsync` ← `CreateAsignaturaRequest` | **ninguno** (ver F2-04) |
| `/api/grupos` GET/POST/PUT/DELETE, `por-asignatura` | `GruposController` (DTO inline) | `persistencia` L115-139, `mapGrupo` L149-161 |

## Matriz de contrato — Asignatura

| Campo (dominio) | FE model `Asignatura` | GET `AsignaturaResponse` | PUT `UpdateAsignaturaRequest` | POST import `AsignaturaDto` | Veredicto |
|---|---|---|---|---|---|
| Nombre/Codigo/HorasPorSesion/SesionesPorSemana/SesionesLaboratorioSemestre/ProgramaId | ✓ | ✓ | ✓ | ✓ | ✅ |
| Alternancia | `'TipoA'…` | ✓ (enum→string) | ✓ opcional | ✓ | ✅ (JsonStringEnumConverter) |
| DocenteId / EspacioFijoId | ✓ | ✓ | ✓ | DocenteId ✓ / EspacioFijoId ✗ | ⚠ |
| **Categoria** | ✓ (`categoria?`) | **✗** | **✗** | **✗** | ❌ F2-01 |
| **HoraInicioMin / HoraFinMax** | ✗ (ni en el model) | ✗ | ✗ | ✗ | ❌ F2-02 |
| **grupoNumero** | ✓ (`grupoNumero?`) | ✗ | ✗ | ✓ (solo entrada) | ❌ F2-03 |

## Hallazgos

### F2-01 — `categoria` no sobrevive en NINGÚN camino E2E · **Resuelto** · Phantom-read + Silent-drop
- **Síntoma:** la categoría (Obligatoria/Optativa/Electiva, prioridad presencial SC-PRES) que el
  usuario fija en la UI nunca se persiste ni se recupera.
- **Causa raíz (triple):**
  - GET: `AsignaturaResponse` no tiene `Categoria` (`AsignaturaResponse.cs:6-31`) → `mapAsignatura`
    lee `a.categoria` (`catalogo.service.ts:141`) que **siempre es `undefined`**.
  - PUT: `UpdateAsignaturaRequest` no tiene `Categoria` (`UpdateAsignaturaRequest.cs:5-22`) aunque el
    FE la envía (`persistencia.service.ts:156`) → **se descarta** (el dominio `ActualizarDatos` la
    conserva pero nunca la recibe).
  - Create (import): `ImportDtos.AsignaturaDto` no tiene `Categoria` (`ImportDtos.cs:46-58`) aunque el
    FE la envía (`asignaturas-tab.component.ts:377`) → **se descarta** (ver F4-01).
- **Fix aplicado (causa raíz, un cambio coordinado):**
  1. `AsignaturaResponse`: `Categoria` agregada en `FromEntity`.
  2. `UpdateAsignaturaRequest.Categoria?` agregado; `AsignaturaService.UpdateAsync` la pasa a
     `ActualizarDatos(...categoria:...)`.
  3. `ImportDtos.AsignaturaDto.Categoria` agregado; `ImportController.MapAsignaturasDto` llama
     `entidad.EstablecerCategoria(...)`.
- **Verificado en preview (datos reales):** se editó "Química General" (CBASIC_BIOL_QGEN_AlexC) de
  Obligatoria → Electiva vía el popup, se guardó (`PUT /api/asignaturas/{id}` → 200), y tras un
  refetch completo (`GET /api/asignaturas`) la fila sigue mostrando "Electiva". 221/221 tests
  backend verdes, `npm run build` limpio, sin errores de consola.
- **Pendiente (no cubierto por este fix):** el desbloqueo de F3-02 solo alcanza a `categoria`; la
  ventana horaria (F2-02) sigue sin DTO/UI — sigue siendo una decisión de producto separada.

### F2-02 — Ventana horaria (`HoraInicioMin/HoraFinMax`, CR-07/HC-VH) inalcanzable desde la UI · Media · Missing
- **Síntoma:** el dominio (`Asignatura.cs:50-51`) y la generación (`GenerarHorarioService.cs:72-76`)
  soportan una ventana horaria dura por asignatura, pero ningún DTO de CRUD ni el model FE la exponen,
  así que no hay forma de fijarla desde la aplicación.
- **Causa raíz:** ausente en `AsignaturaResponse`, `Update/CreateAsignaturaRequest`, `ImportDtos.AsignaturaDto`
  y `models.ts Asignatura`.
- **Fix propuesto:** decidir producto primero (¿lo edita Secretaría en esta UI?). Si sí: agregar
  `horaInicioMin/horaFinMax` ("HH:mm") a los 3 DTOs + model + `mapAsignatura` + builder de generar
  (F3-02). Si no: documentar como fuera de alcance de la UI. (Coincide con gap #2 de REQUISITOS.)
- **Test:** PUT con ventana → GET la refleja → generar la respeta (sesión fuera de rango = infactible).

### F2-03 — `grupoNumero` es phantom-read en GET · Baja · Phantom-read
- **Síntoma:** `mapAsignatura` lee `a.grupoNumero` (`catalogo.service.ts:144`) pero la entidad
  `Asignatura` no tiene ese campo y `AsignaturaResponse` no lo devuelve → siempre `undefined`.
- **Matiz:** `grupoNumero` sí es entrada válida del import (`ImportDtos.AsignaturaDto.GrupoNumero`,
  usado para nombrar grupos en `MapGruposDeAsignaturas` `ImportController.cs:301-310`). Es entrada,
  no salida.
- **Fix propuesto:** eliminar el read fantasma en `mapAsignatura`, o persistir el número de grupo si
  se necesita mostrarlo. Bajo impacto.
- **Test:** GET asignatura importada con `grupoNumero:2` → hoy `undefined`; decidir comportamiento.

### F2-04 — `POST /api/asignaturas` es endpoint muerto · Media · Dead endpoint
- **Síntoma:** el FE nunca llama `POST /api/asignaturas`; las asignaturas nuevas se crean vía
  `POST /import/curriculum` (`asignaturas-tab.component.ts:337-343`). Solo existe `GET` en el FE
  (`persistencia.service.ts:143`, único uso de la ruta).
- **Causa raíz:** `AsignaturasController.CreateAsignatura` + `CreateAsignaturaRequest` +
  `AsignaturaService.CreateAsync` sin consumidor.
- **Riesgo:** dos caminos de creación divergentes; el vivo (import) descarta `categoria` (F2-01/F4-01)
  y no acepta `espacioFijoId`. Un dev que "arregle" `CreateAsignaturaRequest` no cambia nada porque
  nadie lo llama.
- **Fix propuesto:** o (a) borrar el POST + `CreateAsignaturaRequest` (menos superficie), o (b)
  convertirlo en el camino real de creación individual y entonces alinearlo con categoría/ventana.
- **Test:** grep de la ruta en el FE = 0 POST; decidir borrar o cablear.

### F2-05 — Grupo PUT persiste Codigo/ProgramaId/Semestre · **Resuelto (baseline)** · —
- Corregido en sesión previa (`Grupo.ActualizarCodigo/ActualizarPrograma/ActualizarSemestre` +
  wiring `GruposController.cs:112-118`). El FE ya los envía en POST y PUT
  (`persistencia.service.ts:119-135`). **Acción:** confirmar que el popup de grupo ya no deshabilita
  esos 3 campos.

### F2-06 — Grupo: dos representaciones de disponibilidad, traducción solo en el FE · Media · Semantic
- **Síntoma:** el CRUD usa `DisponibilidadUiJson` (JSON opaco por día); la generación usa
  `Disponibilidad: string[]` con "Matutino"/"Vespertino". La conversión vive **solo** en el FE
  (`horario-api.service.ts:211-235`, `franjasDeGrupo`).
- **Causa raíz:** el `GrupoDto` de generar (`GenerarHorarioRequest.cs:129-143`) transporta ambos, pero
  solo `Disponibilidad` alimenta HC-G01 (`GenerarHorarioService.cs:606`). El BE no deriva una de la otra.
- **Riesgo del heurístico:** "Nocturno"→Vespertino; "específica" parte en 13:00; cubrir ambas franjas
  → `[]` (interpretado como "sin restricción"). Si los presets de la UI no calzan con el `includes()`
  de substrings, la franja se mapea mal en silencio.
- **Fix propuesto:** mover la traducción al BE (una sola fuente), o congelar los presets de la UI y
  cubrirlos con un test del heurístico.
- **Test:** grupo "solo mañanas" → `franjasDeGrupo` = `["Matutino"]`; "todo el día" → `[]`;
  "nocturno" → `["Vespertino"]`.

### F2-07 — Grupo generar omite programaId/semestre · Info
- El `GrupoDto` de generar no lleva programaId/semestre; correcto: el eje del grupo en el pipeline es
  asignatura + disponibilidad. No es bug.

## Decisiones de producto pendientes
- Categoría y ventana horaria: ¿editables en esta UI? (desbloquea F2-01 alcance completo y F2-02).
- `POST /api/asignaturas`: ¿borrar o volverlo el camino de creación individual? (F2-04).
- Traducción de disponibilidad de grupo: ¿FE o BE? (F2-06).
