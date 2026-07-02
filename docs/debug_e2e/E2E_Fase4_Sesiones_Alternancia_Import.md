# Debug E2E — Fase 4: Sesiones + Tipos de alternancia + Import

> Auditoría de contrato Frontend ↔ Backend. Ver `docs/debug_e2e/README.md`. Solo diagnóstico.

## Alcance

| Endpoint | BE | FE call-site |
|---|---|---|
| `PATCH /api/sesiones/{id}/docente` | `SesionesController` → `AsignarDocenteSesionService` | `persistencia.service.ts:174-179` |
| `/api/tiposalternancia` GET/POST/PUT/DELETE | `TiposAlternanciaController` → `TipoAlternanciaConfigService` | `persistencia.service.ts:78-90` |
| `POST /api/import/excel` | `ImportController.ImportarExcel` → `ImportExcelStatsDto` | `persistencia.service.ts:197-201` |
| `POST /api/import/curriculum` | `ImportController.ImportCurriculum` ← `CurriculumExcelDto` → `ImportResultDto` | `persistencia.service.ts:193-195`, `asignaturas-tab` L368-380 |
| `GET /api/facultades`, `/api/programas` | `ImportController` | `persistencia.service.ts:36-41` |

## Matriz de contrato — destacados

| Endpoint | FE | BE | Veredicto |
|---|---|---|---|
| asignar docente | envía `{docenteId}`, lee `{sesionId,docenteId,advertencias}` | `AsignarDocenteRequest` + `AsignarDocenteResponse` | ✅ (errores: ver F4-09) |
| tiposalternancia | `TipoAlternanciaConfig{patronBase,esSistema,activo,color,…}` | `TipoAlternanciaConfigDto` idem | ✅ / ⚠ F4-07 |
| import/excel resumen | `ImportExcelStatsDto` (11 campos) | `ImportExcelStatsDto` (12 campos) | ❌ F4-05 (`espaciosActualizados` faltante) |
| import/curriculum | payload PascalCase `{Facultades,Programas,Asignaturas,Docentes}` | `CurriculumExcelDto` | ⚠ F4-04 |
| import AsignaturaDto | envía `categoria`, `grupoNumero` | DTO sin `Categoria` | ❌ F4-01 |

## Hallazgos

### F4-01 — Import descarta `categoria` · **Resuelto** · Silent-drop
- **Síntoma:** `construirPayloadImport` envía `categoria` (`asignaturas-tab.component.ts:377`) pero
  `ImportDtos.AsignaturaDto` no tiene `Categoria` (`ImportDtos.cs:46-58`) → se pierde al crear.
- **Causa raíz:** es la rama "create" del fallo global de categoría (ver F2-01). Como la creación de
  asignaturas va por import (F2-04), esta es la vía por la que la categoría se pierde al dar de alta.
- **Fix aplicado:** `Categoria` agregada a `ImportDtos.AsignaturaDto`; `ImportController.MapAsignaturasDto`
  llama `entidad.EstablecerCategoria(...)` (`ImportController.cs:260-280`). Mismo cambio que F2-01.

### F4-02 — Tipo de espacio: import silencioso vs CRUD estricto · Baja · Enum (inconsistencia interna)
- **Síntoma:** `ImportController.MapEspaciosDto` (`ImportController.cs:230-235`) mapea cualquier tipo
  desconocido a `Salon` en silencio, mientras `EspaciosController.ParseTipo` ya lanza 400 (F1-01).
  Dos entradas, dos comportamientos para el mismo enum.
- **Matiz:** el FE-app no envía espacios por import (`construirPayloadImport` no incluye `Espacios`), así
  que hoy solo afecta a un cliente que llame `/import/curriculum` crudo con espacios.
- **Fix propuesto:** unificar con el switch estricto (incluir "Salón" y rechazar/loggear lo desconocido),
  o documentar la leniencia como intencional para import.
- **Test:** import con `tipo:"salon"` (minúscula) → hoy cae a Salón; decidir.

### F4-03 — Import ignora la disponibilidad del docente · Baja · Semantic
- **Síntoma:** `DocenteImportDto.Disponibilidad` es `object?` y `MapDocentesDto` nunca la lee; fija
  `{Matutino, Vespertino}` (`ImportController.cs:214-215`). Coherente con F1-03 pero por otra vía.
- **Fix propuesto:** decidir junto con F1-03 (¿el import trae disponibilidad real o no?).

### F4-04 — Import curriculum es el único payload en PascalCase · **Resuelto** · Casing
- **Síntoma:** el payload usa claves PascalCase `Facultades/Programas/Asignaturas/Docentes`
  (`asignaturas-tab.component.ts:369-379`); todo el resto del sistema es camelCase. Funciona porque
  System.Text.Json deserializa case-insensitive por defecto, pero es una trampa de mantenimiento.
- **Fix aplicado:** `construirPayloadImport` (`asignaturas-tab.component.ts:368-382`) ahora arma el
  payload en camelCase. Verificado: import sigue funcionando (STJ bindea case-insensitive).

### F4-05 — `ImportExcelStatsDto` del FE omite `espaciosActualizados` · **Resuelto** · Phantom-drop
- **Síntoma:** el BE devuelve 12 contadores incl. `EspaciosActualizados` (`ImportDtos.cs:86-99`,
  `ImportController.cs:88`), pero la interface FE solo declara 11 y **no** incluye `espaciosActualizados`
  (`persistencia.service.ts:204-216`). El conteo llega pero el FE nunca lo lee.
- **Fix aplicado:** `espaciosActualizados: number` agregado a `ImportExcelStatsDto` en
  `persistencia.service.ts:204-217`. El snackbar de resumen sigue mostrando solo el subconjunto
  curado de campos (sin cambios); el campo ahora está tipado y disponible para quien lo necesite.

### F4-06 — Mapeos de grupos del import no son reutilizables por el FE · Baja · Semantic
- **Síntoma:** `ImportResultDto.Grupos` devuelve mappings con claves temporales `"{asigId}:{num}"`
  (`ImportController.cs:309-310`), pero el FE no genera esas claves (envía asignaturas sin id temporal de
  grupo), así que no puede casar los grupos devueltos.
- **Matiz:** inofensivo hoy: tras importar, el FE hace `cargarTodo()` (refetch completo,
  `asignaturas-tab.component.ts:353`) y se re-sincroniza. El mapping de grupos simplemente se ignora.
- **Fix propuesto:** documentar que grupos se resuelven por refetch, o quitar `Grupos` del `ImportResultDto`
  si nadie lo usa.

### F4-07 — `PatronBase` desconocido cae a `SinAlternancia` en silencio · **Resuelto** · Enum (silent-default)
- **Síntoma:** en create/update, un `PatronBase` no parseable se convierte en
  `PatronBaseAlternancia.SinAlternancia` sin avisar (`TipoAlternanciaConfigService.cs:32,44`). Mismo
  patrón que el viejo `ParseTipo` de espacios (que ya se endureció).
- **Fix aplicado:** `TipoAlternanciaConfigService.ParsePatronBase` ahora lanza `ArgumentException`
  para un patrón no reconocido, en vez de defaultear (mismo patrón que `ParseTipo`, F1-01). El
  controller ya capturaba `ArgumentException` → 400 en Create/Update.

### F4-08 — Delete de tipo de sistema · **OK** · —
- `DeleteAsync` lanza 400 para `EsSistema` (`TipoAlternanciaConfigService.cs:54-55`) y 404 si no existe;
  `CreateAsync` fuerza `esSistema:false`. Coincide con REQUISITOS §5. Sin acción.

### F4-09 — Errores de asignar-docente: forma `{error}` no la lee el FE · Media · Error-shape
- **Síntoma:** `SesionesController` devuelve 404/409/400 como `{ error: msg }`
  (`SesionesController.cs:48,53,57`). El FE `persistencia.asignarDocente` no mapea el error inline; el
  util genérico `mensajeErrorHttp` busca `string`/`detail`/`title` pero **no** `error`
  (`http-error.util.ts:6-9`) → el mensaje real (p.ej. el 409 de solape) se pierde y el usuario ve
  "Error desconocido".
- **Fix propuesto:** ver F5-01 (estandarizar envelope o enseñar al util a leer `.error.error`). El 409
  debe mostrarse como bloqueante y las `advertencias` como aviso no bloqueante (REQUISITOS §3).
- **Test:** asignar docente con solape → el mensaje del 409 se muestra textual, no "Error desconocido".

## Decisiones de producto pendientes
- Disponibilidad en import (F4-03) — junto con F1-03.
- ¿Uniformar el envelope de error del import/sesiones (texto plano vs `{error}` vs ModelState)? → Fase 5.
