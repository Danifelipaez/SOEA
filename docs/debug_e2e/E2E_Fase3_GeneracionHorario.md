# Debug E2E — Fase 3: Generación de horario

> Auditoría de contrato Frontend ↔ Backend. Ver `docs/debug_e2e/README.md`. Solo diagnóstico.

## Alcance

| Endpoint | BE | FE call-site |
|---|---|---|
| `POST /api/horario/generar` | `HorarioController` → `GenerarHorarioService` ← `GenerarHorarioRequest` → `GenerarHorarioResponse` | `horario-api.service.ts` (builder + `mapearSesiones`) |
| `POST /api/horario/sesion-manual` | `CrearSesionManualService` ← `CrearSesionManualRequest` | `persistencia.service.ts:181-191` |

Archivos: `GenerarHorarioRequest.cs` (DTOs anidados), `GenerarHorarioResponse.cs` (`SesionGeneradaDto`),
`GenerarHorarioService.cs`, `CrearSesionManualRequest.cs`, `Sesion.cs`; FE `horario-api.service.ts`,
`features/horario/horario.component.ts`, `models.ts` (`Sesion` L105-124).

## Matriz de contrato — generar (request)

| Campo BE `AsignaturaDto` | ¿lo envía el FE `AsignaturaApiDto`? | ¿lo usa la generación? | Veredicto |
|---|---|---|---|
| Id/Nombre/DocenteId/HorasPorSesion/SesionesPorSemana/ProgramaId/Alternancia/EspacioFijoId | ✓ | ✓ | ✅ |
| Creditos / HorasSemanales | ✓ (ambos, derivados) | ✓ (prefiere HorasSemanales, `GenerarHorarioService.cs:484-485`) | ⚠ F3-03 |
| **EsVirtual** | ✗ (hardcoded `false`, `horario-api.service.ts:173`) | ✓ (`L470`) | ❌ F3-01 |
| **Categoria** | ✗ (no existe en el DTO FE) | ✓ (`ParseCategoria`, `L69`) | ❌ F3-02 |
| **HoraInicioMin / HoraFinMax** | ✗ | ✓ (`L72-76`) | ❌ F3-02 |

## Matriz de contrato — generar (response `SesionGeneradaDto` → `Sesion` FE)

| Campo | BE emite | FE `mapearSesiones` | Veredicto |
|---|---|---|---|
| Id/AsignaturaId/EspacioId/EspacioIdHogar/Dia/HoraInicio/HoraFin/Virtual | ✓ | ✓ | ✅ |
| DuracionHoras | ✓ (siempre) | fallback `diffHoras` si falta | ⚠ F3-04 (fallback muerto) |
| Alternancia / Semana | ✓ string (JsonStringEnumConverter no aplica; ya son string) | cast a unión | ✅ |
| DocenteId | `""` si no asignado (`L568`) | `docenteId?` opcional | ⚠ F3-04 (""≠undefined) |

## Hallazgos

### F3-01 — `esVirtual` hardcodeado a `false` · Media · Silent-drop de feature
- **Síntoma:** el builder del request fija `esVirtual: false` para toda asignatura
  (`horario-api.service.ts:173`). El BE consume `dto.EsVirtual` para forzar `Modalidad.Virtual`
  (`GenerarHorarioService.cs:470`), pero nunca lo recibe en `true`.
- **Impacto:** no se puede declarar una asignatura como nativamente virtual desde la UI. (La
  virtualización por saturación/categoría SC-PRES sigue ocurriendo en el motor, así que no es
  "no hay virtuales", sino "el usuario no puede declararlas".)
- **Fix propuesto:** exponer `esVirtual` en el model FE `Asignatura` + `mapAsignatura` + builder; o
  documentar que la virtualidad es 100% decisión del motor y eliminar el campo del DTO.
- **Test:** marcar una asignatura virtual → generar → sus sesiones salen `virtual:true` sin lab.

### F3-02 — `categoria`/`horaInicioMin`/`horaFinMax` no llegan al motor · **Parcialmente resuelto** · Silent-drop
- **Síntoma:** `AsignaturaApiDto` (`horario-api.service.ts:55-67`) no tiene esos campos, así que
  SC-PRES corre con **todo = Obligatoria** (`ParseCategoria` default, `GenerarHorarioService.cs:664`)
  y la ventana dura HC-VH nunca se aplica (`L72-76`).
- **Causa raíz:** encadenado con F2-01/F2-02 — los datos ni siquiera existen en el model FE para poder
  enviarse. El motor tiene las features; la UI no las alimenta.
- **Fix aplicado (solo categoría):** `AsignaturaApiDto.categoria?` agregado en `horario-api.service.ts`
  y el builder de `generarHorario()` ahora envía `categoria: a.categoria`, desbloqueado por F2-01
  (el GET ya devuelve la categoría real). SC-PRES recibe datos reales cuando el usuario la fija.
- **Pendiente:** `horaInicioMin`/`horaFinMax` (HC-VH) siguen sin DTO/UI en ningún lado (F2-02) —
  decisión de producto separada, no incluida en este fix.
- **Test:** asignatura marcada Electiva → con espacios saturados se virtualiza antes que una
  Obligatoria (pendiente de correr un caso real; la data ahora llega, el comportamiento del motor
  con `Categoria` ya estaba implementado desde antes).

### F3-03 — `creditos` y `horasSemanales` redundantes/derivados · Baja · Semantic
- **Síntoma:** el FE calcula ambos como `sesionesPorSemana * horasPorSesion`
  (`horario-api.service.ts:167-168`) y además envía `horasPorSesion`/`sesionesPorSemana`. El BE prefiere
  `HorasSemanales`, luego `Creditos`, luego deriva (`GenerarHorarioService.cs:484-485`).
- **Riesgo:** cuádruple fuente de la misma magnitud; si un día se editan por separado, divergen.
- **Fix propuesto:** enviar solo `horasPorSesion`/`sesionesPorSemana` y derivar en el BE; o documentar
  cuál es la fuente autoritativa.
- **Test:** enviar `horasSemanales` inconsistente con `horasPorSesion*sesiones` → verificar cuál gana.

### F3-04 — Defensas muertas y `DocenteId` vacío en el mapeo de sesiones · **Resuelto (síntoma B)** · Semantic
- **Síntoma A:** `SesionGeneradaDto.DuracionHoras` siempre viene del BE
  (`GenerarHorarioService.cs` mapea `s.DuracionHoras`), pero el FE tiene un fallback `diffHoras`
  (`horario-api.service.ts:199`) que nunca se ejecuta → código muerto defensivo (inofensivo).
- **Síntoma B:** el BE emite `DocenteId = ""` cuando la sesión no tiene docente (`L568`), mientras el
  FE tipa `Sesion.docenteId?` como opcional. Un `""` no es `undefined`: cualquier chequeo
  `if (s.docenteId)` lo trata como falsy (ok), pero `s.docenteId === undefined` sería `false`.
- **Fix aplicado (síntoma B):** `mapearSesiones` ahora mapea `s.docenteId || undefined`
  (`horario-api.service.ts`). Síntoma A (fallback muerto `diffHoras`) se deja — es defensivo e
  inofensivo, no vale la pena tocarlo sin necesidad (YAGNI).

### F3-05 — Semántica de `Semana` (A/B) documentada al revés · **Resuelto** · Semantic (doc)
- **Síntoma:** `SesionGeneradaDto.Semana` dice "A (impares) / B (pares)"
  (`GenerarHorarioResponse.cs:44-45`), pero el model FE `Sesion.semana` dice "A = pares / B = impares"
  (`models.ts:120-123`). Documentación contradictoria del mismo campo.
- **Riesgo:** si algo actúa sobre la paridad de semana (calendario, export), off-by-one-week.
- **Fix aplicado:** verificado contra `SemanaAcademica.cs` (fuente de verdad: "Semana A = semanas
  impares, Semana B = semanas pares") y `GenerarHorarioResponse.cs` (ya correcto). Se corrigió el
  comentario de `models.ts` para que coincida.
- **Hallazgo nuevo, fuera de alcance de esta auditoría FE/BE:** `PatronBaseAlternancia.cs` dice
  "PresencialEnSemanaA ⇒ presencial en semanas A (**pares**)", que contradice a `SemanaAcademica.cs`
  ("Semana A = **impares**"). Es una inconsistencia interna del backend (no de contrato con el FE);
  no se tocó sin confirmar cuál es la convención real usada por el motor. Requiere investigación
  aparte antes de decidir qué comentario/código corregir.

### F3-06 — `sesion-manual` exige docente aunque el modelo lo hace opcional · Baja · Semantic
- **Síntoma:** `CrearSesionManualRequest.DocenteId` es `Guid` no-nullable
  (`CrearSesionManualRequest.cs:9`); el FE lo envía requerido. Pero presencial-first hace el docente
  opcional (`Sesion.DocenteId` nullable, `PATCH /sesiones/{id}/docente`).
- **Fix propuesto:** hacer `DocenteId` nullable en el request manual para alinearlo con el resto del
  flujo, o documentar que la creación manual sí lo exige a propósito.
- **Test:** crear sesión manual sin docente → hoy 400; decidir si debe permitirse.

### F3-07 — `SesionFijaApiDto.docenteId` opcional en FE vs requerido en el popup · Baja · Type
- **Síntoma:** FE `SesionFijaApiDto.docenteId?` opcional (`horario-api.service.ts:23`); el BE
  `SesionFijaDto.DocenteId` es `string` (default vacío); REQUISITOS marca DocenteId* requerido en el
  popup "Agregar sesión fija".
- **Fix propuesto:** alinear requeridness (probablemente opcional, coherente con presencial-first) y
  actualizar REQUISITOS.

## Decisiones de producto pendientes
- ¿La virtualidad la declara el usuario (F3-01) o es 100% del motor?
- Convención real de paridad de `Semana` (F3-05) — requiere confirmación con el motor.
