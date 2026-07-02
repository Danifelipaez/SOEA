# Debug E2E — Fase 5: Transversal (error/status, casing, dashboard, auth)

> Auditoría de contrato Frontend ↔ Backend. Ver `docs/debug_e2e/README.md`. Solo diagnóstico.
> Consolida lo que cruza todas las verticales.

## Alcance

Archivos FE: `core/http-error.util.ts`, `horario-api.service.ts` (`manejarError` L243-258),
`environments/environment.ts`, `catalogo.service.ts`, `features/dashboard-*`, `app.routes.ts`.
Archivos BE: `Program.cs` (JSON/CORS), todos los controllers (inventario de status codes), `ImportController` (facultades/programas).

## Hallazgos

### F5-01 — Tres formas de error, dos parsers que no las cubren todas · **Resuelto (parcial)** · Error-shape
- **Síntoma:** el BE emite errores en tres formas distintas:
  - **(a) texto plano** — `BadRequest("mensaje")` en Asignatura (`AsignaturaController.cs:31,84`),
    Grupo, Espacio, Import (`ImportController.cs:48,68`).
  - **(b) ModelState** — `BadRequest(ModelState)` → `{errors:{campo:[...]}}` en generar, sesion-manual,
    sesiones/docente cuando `!ModelState.IsValid`.
  - **(c) objeto `{error: msg}`** — `SesionesController` (404/409/400, `SesionesController.cs:48-57`),
    `HorarioController` sesion-manual y 500 (`HorarioController.cs:109-120`).
- **El FE tiene dos parsers desalineados:**
  - `manejarError` (solo generar, `horario-api.service.ts:243-258`) lee `errors` / `title` / `message`.
  - `mensajeErrorHttp` (util general, `http-error.util.ts:6-9`) lee `string` / `detail` / `title`.
  - **Ninguno lee `.error`** → la forma (c) se pierde: 409 de solape de docente y 422/400 de
    sesión-manual muestran "Error desconocido" en vez del mensaje real.
- **Fix aplicado (mínimo, sin tocar el BE):** `mensajeErrorHttp` (`http-error.util.ts`) ahora lee
  `cuerpo?.error` antes de `detail/title`, cubriendo la forma (c) usada por `SesionesController` y
  `HorarioController`. `manejarError` (usado solo por `/horario/generar`) no se tocó — ya maneja su
  propio caso 400/422 y no usa el envelope `{error}`.
- **Pendiente (no incluido):** unificar los 3 formatos de error del lado del backend a un único
  envelope (`ProblemDetails`) sigue siendo una mejora futura más invasiva, fuera del alcance de este
  fix puntual.

### F5-02 — Casing y enums en el wire · **OK (con 1 excepción)** · Casing
- **Verificado:** `Program.cs:97-100` registra `JsonStringEnumConverter` global → todos los enums viajan
  como **string** (`AsignaturaResponse.Alternancia` → "TipoA", etc.), lo que empareja con las uniones TS
  del FE. Serialización camelCase por defecto (STJ) coincide con todas las lecturas del FE.
- **Excepción:** el payload `POST /import/curriculum` va en PascalCase (F4-04); funciona por binding
  case-insensitive pero rompe la convención. Sin otra acción aquí.

### F5-03 — Inventario de status codes vs manejo en FE · Baja · Status
| Código | Emisor BE | ¿FE lo maneja? |
|---|---|---|
| 400 | validación / ArgumentException (todos) | sí (manejarError + mensajeErrorHttp), salvo forma `{error}` (F5-01) |
| 404 | not-found (CRUD, sesiones) | ✅ mensajeErrorHttp ya lee `{error}` (F5-01) |
| 409 | solape docente (`SesionesController`) | ✅ mensaje extraído (F5-01) |
| 422 | generación infactible (`HorarioController`) | sí (`manejarError` pasa el `GenerarHorarioResponse`) |
| 499 | desconexión del cliente en generar (`HorarioController.cs:79`) | sin manejo — cosmético (cliente ya se fue), no se tocó |

### F5-04 — facultades/programas · **OK** · —
- BE devuelve `{id,nombre}` y `{id,nombre,facultadId}` (anónimos lowercase, `ImportController.cs:98-108`);
  models FE `Facultad`/`Programa` (`models.ts:1-10`) coinciden. Sin acción.

### F5-05 — Dashboard sin endpoint agregador · Decisión (no bug)
- Confirmado: no hay endpoint de agregación; ambos dashboards leen el `StateService` en memoria
  (conteos de asignaturas/docentes/espacios/grupos, último import, última generación). Coincide con
  REQUISITOS §6. **No es inconsistencia**; se documenta para que no se busque un endpoint inexistente.

### F5-06 — Sin histórico de horarios recuperable · Decisión / backlog
- `IHorarioRepositorio` tiene `GetByIdAsync`/`GetBySemestreAsync` pero **ningún controller** los expone;
  `HorarioController` solo tiene `generar` y `sesion-manual`. El último `GenerarHorarioResponse` vive solo
  en memoria del FE. Confirma el hueco #3 de REQUISITOS. **Backlog**, no bug: si se quiere histórico,
  añadir `GET /api/horario/{id}` + `GET /api/horario` reusando el mapeo `MapearSesionDto` existente.

### F5-07 — Autenticación/roles ausentes en ambos lados · Decisión / backlog
- Sin JWT ni middleware de auth en el BE; sin `canActivate`/guards reales en `app.routes.ts` (el grep de
  "Guard" solo pega en métodos `guardar*` de guardado, no en guards de ruta). Coincide con REQUISITOS §7.
  **Backlog conocido**, no inconsistencia de contrato.

## Cierre de cobertura (verificación del método)
- Todos los endpoints con call-site FE (`this.http.*` en `persistencia`/`horario-api`) quedan cubiertos
  en las matrices de las Fases 1–5. Endpoints BE sin consumidor: `POST /api/asignaturas` (F2-04) →
  única fila "dead endpoint".
- Baseline reaparece como esperado: `ParseTipo` (F1-01) y Grupo PUT + ventana (F2-05/F2-01) confirman que
  la auditoría detecta lo ya corregido.

## Prioridad de remediación (global, todas las fases) — estado tras implementación

1. ✅ **Resuelto** — F2-01 + F4-01 (categoría no sobrevive E2E): 3 DTOs + servicios backend, verificado
   en preview con datos reales (edit → PUT 200 → refetch → persiste).
2. ✅ **Resuelto** — F5-01 (mensajes de error 409/`{error}` swallowed): one-liner en `mensajeErrorHttp`.
3. ✅ **Resuelto (parcial)** — F3-02: `categoria` ahora llega al motor de generación. `horaInicioMin`/
   `horaFinMax` (F2-02) sigue pendiente de decisión de producto.
4. ✅ **Resuelto** — F4-07 (PatronBase desconocido), F3-04 (docenteId ""→undefined), F4-04 (camelCase
   import), F4-05 (espaciosActualizados), F3-05 (comentario Semana A/B).
5. **Pendiente — decisión de producto:** F2-06 (dónde vive la traducción de disponibilidad de grupo),
   F3-01 (¿virtualidad declarada por el usuario?), F2-02 (¿ventana horaria editable en la UI?),
   F1-03/F4-03 (disponibilidad docente estructurada vs JSON de UI).
6. **Pendiente — baja prioridad:** F2-03, F4-02/06, F1-02.
7. **Backlog/decisión (sin tocar):** F2-04 (POST muerto), F5-05/06/07 (dashboard/histórico/auth).
8. **Hallazgo nuevo, fuera de alcance:** `PatronBaseAlternancia.cs` vs `SemanaAcademica.cs` — dos
   comentarios de dominio que se contradicen sobre si la semana A es par o impar (ver F3-05 en
   Fase 3). No se corrigió sin confirmar la convención real del motor.
