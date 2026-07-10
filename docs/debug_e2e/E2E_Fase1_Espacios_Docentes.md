# Debug E2E — Fase 1: Espacios + Docentes

> Auditoría de contrato Frontend ↔ Backend. Método y convenciones: ver
> `docs/debug_e2e/README.md` y el plan aprobado. Solo diagnóstico; la remediación es aparte.

## Alcance

| Endpoint | BE | FE call-site |
|---|---|---|
| `/api/espacios` GET/POST/PUT/DELETE | `EspaciosController` (DTO inline) | `persistencia.service.ts` L95-111 |
| `/api/docentes` GET/POST/PUT/DELETE | `DocentesController` → `DocenteService` | `persistencia.service.ts` L46-62 |
| `/api/docentes/duplicados` GET | `DocentesController` → `FusionDocentesService` | `persistencia.service.ts` L65-67 |
| `/api/docentes/fusionar` POST | idem | `persistencia.service.ts` L70-73 |

Archivos: `EspaciosController.cs`, `Espacio.cs`, `DocentesController.cs`, `DocenteService.cs`,
`DocenteUiDto.cs`, `FusionDocentesService.cs`, `Docente.cs`; FE `persistencia.service.ts`,
`catalogo.service.ts` (`mapEspacio` L121-130, `mapDocente` L111-119), `models.ts` (L12-27).

## Matriz de contrato

| Endpoint | Campos FE envía / lee | Campos BE acepta / devuelve | Veredicto |
|---|---|---|---|
| Espacio POST/PUT | `{id,nombre,tipo,capacidad,edificio,piso}` | `EspacioDto` idem; `tipo` ∈ {"Salón","Laboratorio","Auditorio"} | ✅ OK (tipo tipado en FE) |
| Espacio GET | lee `{id,nombre,capacidad,tipo,edificio,piso}` | mapea enum→string ("Salón"/…) | ✅ OK |
| Docente POST/PUT | `{id,nombre,cedula,maxHoras,disponibilidad}` | `DocenteUiDto` idem (`disponibilidad`=JsonElement?) | ⚠ passthrough opaco |
| Docente GET | lee idem | devuelve idem (disponibilidad reconstruida) | ⚠ ver F1-03 |
| fusionar POST | envía `{canonicoId,duplicadosIds}`, lee `{canonicoId,docentesEliminados,asignaturasReasignadas}` | `FusionarDocentesRequest` + `FusionResultado(CanonicoId,DocentesEliminados,AsignaturasReasignadas)` | ✅ OK |

## Hallazgos

### F1-01 — Espacio.Tipo round-trip · **Resuelto (baseline)** · Enum/type
- **Síntoma:** antes cualquier `tipo` no reconocido caía a "Salón" sin aviso (gap #5 REQUISITOS).
- **Estado:** `EspaciosController.ParseTipo` (`EspaciosController.cs:80-87`) ya rechaza con 400
  valores inválidos e incluye el caso `"Salón"`. El FE tipa `Espacio.tipo` como unión
  `'Laboratorio'|'Salón'|'Auditorio'` (`models.ts:16`), así que solo emite strings válidos.
- **Acción:** ninguna. Confirma que el método detecta lo ya corregido. Verificar que
  `espacios-tab` use el `<select>` con esos 3 strings exactos.

### F1-02 — Disponibilidad de docente es JSON opaco sin validación · Media · Semantic
- **Síntoma:** el BE guarda `DocenteUiDto.Disponibilidad` verbatim (`DocenteService.cs:66,77`
  via `GetRawText()`) y lo re-emite igual (`BuildDisponibilidad` L104-110). No valida forma.
- **Causa raíz:** FE `models.ts:26` tipa `disponibilidad: any`; el shape real (por día,
  `{noDisponible,tipo,franjaGeneral,desde,hasta}`) solo está documentado, no forzado en ningún lado.
  Los presets de "Franja general" deben coincidir **exactamente** con las etiquetas del BE
  (`DocenteService.cs:41-45`: "Todo el día (06:00–22:00)", "Horario de oficina (06:00–18:00)",
  "Matutino (06:00–12:00)", "Vespertino (12:00–18:00)", "Nocturno (18:00–22:00)").
- **Impacto:** hoy bajo (la disponibilidad docente dejó de ser hard constraint de generación,
  CR-02/HC-I02). Pero la Fase 3 (generar) sí parsea ese shape por día → un preset mal escrito
  desde la UI se ignora silenciosamente allí.
- **Fix propuesto:** documentar el shape canónico como interface TS compartida
  (`DisponibilidadDia`) y usarla en el popup de docente; opcionalmente validar en `DocenteService`.
- **Test:** POST docente con cada preset → GET devuelve el mismo `franjaGeneral` literal.

### F1-03 — Create/Update de docente ignora la disponibilidad estructurada · Media · Semantic
- **Síntoma:** `DocenteService.CreateAsync`/`UpdateAsync` (`DocenteService.cs:58-66, 76-77`)
  **hardcodean** `BloquesDisponibles = {Matutino, Vespertino}` y solo guardan el JSON crudo en
  `DisponibilidadUiJson`. La disponibilidad real por franjas del dominio queda fija, sin importar
  lo que envíe la UI.
- **Impacto:** la representación estructurada (`Docente.BloquesDisponibles`) siempre dice
  "todo Matutino+Vespertino"; la restricción real vive solo en el JSON opaco. Bajo hoy (docente
  fuera del pipeline), pero es una fuente de verdad divergente latente.
- **Fix propuesto:** derivar `BloquesDisponibles` desde el JSON al persistir, o eliminar el campo
  estructurado si ya no se usa. Decidir en conjunto con F1-02.
- **Test:** crear docente "solo lunes matutino" → `BloquesDisponibles` refleja esa restricción.

### F1-04 — Correo sintetizado no forma parte del contrato · Info
- `DocenteService` genera `{id}@soea.local` (`DocenteService.cs:62`); `DocenteUiDto` no expone
  correo y el FE no lo pide. No es inconsistencia; se anota para que nadie asuma que el correo es real.

## Decisiones de producto pendientes
- ¿Se conserva `BloquesDisponibles` como fuente estructurada (F1-03) o se declara el JSON de UI
  como única fuente? Afecta si la disponibilidad docente volverá a ser restricción de generación.
