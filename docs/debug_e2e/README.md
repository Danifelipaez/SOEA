# Auditoría E2E Frontend ↔ Backend — Índice

Búsqueda segmentada de inconsistencias de contrato entre el frontend Angular y el backend
ASP.NET Core. Cada fase acota un subconjunto del contrato (endpoint + DTO request + DTO response
+ entidad de dominio + call-site FE + mapeo FE) y entrega un plan de debug. Plan aprobado:
`~/.claude/plans/haz-un-plan-para-jaunty-hamming.md`.

## Método (común a todas las fases)

Por cada endpoint se comparan 4 contratos y se clasifica cada mismatch:

- **Silent-drop** — el FE envía un campo que el DTO del BE no tiene → se pierde.
- **Phantom-read** — el FE lee un campo que el BE nunca devuelve → siempre `undefined`.
- **Enum/type mismatch** — mismo campo, dominios distintos.
- **Missing/dead endpoint** — el FE llama algo inexistente, o el BE expone algo que nadie llama.
- **Error-shape mismatch** — el FE espera una forma de error distinta a la emitida.
- **Semantic divergence** — mismo campo, distinta representación/significado.

Archivos listados vía CLI `graphify` 0.8.44 + grafo `code-review-graph` (196 archivos, C#/TS).

## Fases

| Fase | Doc | Hallazgos clave |
|---|---|---|
| 1 | [Espacios + Docentes](E2E_Fase1_Espacios_Docentes.md) | disponibilidad JSON opaca; `BloquesDisponibles` hardcodeado |
| 2 | [Asignaturas + Grupos](E2E_Fase2_Asignaturas_Grupos.md) | **categoría no sobrevive (F2-01)**; ventana inalcanzable; POST muerto; doble disponibilidad de grupo |
| 3 | [Generación de horario](E2E_Fase3_GeneracionHorario.md) | `esVirtual` hardcoded; categoría/ventana no llegan al motor; paridad de Semana documentada al revés |
| 4 | [Sesiones + Alternancia + Import](E2E_Fase4_Sesiones_Alternancia_Import.md) | import descarta categoría (F4-01); `espaciosActualizados` faltante; errores `{error}` no leídos |
| 5 | [Transversal](E2E_Fase5_Transversal.md) | **envelope de error inconsistente (F5-01)**; casing/enums OK; dashboard/histórico/auth = backlog |

## Estado de la remediación (implementada — ver detalle en cada Fase)

**✅ Implementado y verificado** (build + 221/221 tests backend + `npm run build` + smoke test en
preview con datos reales de Postgres):
- F2-01 + F4-01 — round-trip de `categoria` (GET/PUT/import) en `AsignaturaResponse`,
  `UpdateAsignaturaRequest`, `ImportDtos.AsignaturaDto` + servicios. Confirmado en vivo: editar
  categoría → guardar → refetch completo → persiste.
- F3-02 (parcial) — `categoria` ahora viaja en el request de `generar` (ventana horaria sigue pendiente).
- F5-01 — `mensajeErrorHttp` ya lee el envelope `{error}` de `SesionesController`/`HorarioController`.
- F4-07 — `TipoAlternanciaConfigService` rechaza `PatronBase` desconocido (antes defaulteaba en silencio).
- F3-04 — `mapearSesiones` normaliza `docenteId: ""` a `undefined`.
- F4-04 — payload de `import/curriculum` pasado a camelCase.
- F4-05 — `espaciosActualizados` agregado a la interface FE `ImportExcelStatsDto`.
- F3-05 — comentario de paridad de `Semana` en `models.ts` corregido para coincidir con el backend.

**Pendiente — requiere decisión de producto** (no implementado, ver cada Fase): F2-02 (ventana
horaria editable en la UI), F2-06 (dónde vive la traducción de disponibilidad de grupo), F3-01
(¿virtualidad declarada por el usuario?), F1-03/F4-03 (disponibilidad docente estructurada).

**Backlog / fuera de alcance intencional:** F2-04 (POST /asignaturas muerto), F5-05/06/07
(dashboard agregador / histórico de horarios / auth), F2-03, F4-02/06 (baja prioridad).

**Hallazgo nuevo durante la implementación:** `PatronBaseAlternancia.cs` contradice a
`SemanaAcademica.cs` sobre si la semana A es par o impar — inconsistencia interna del backend
(no de contrato FE/BE), documentada en Fase 3 (F3-05) pero no corregida sin confirmar la
convención real del motor.

## Verificación ejecutada

`dotnet build SOEA.sln` (0 errores) + `dotnet test SOEA.sln` (221/221) + `npm run build` (limpio,
solo warnings de bundle-size preexistentes) + smoke test en preview: backend + frontend corriendo
contra Postgres real, edición de asignatura con categoría verificada end-to-end, sin errores de
consola.
