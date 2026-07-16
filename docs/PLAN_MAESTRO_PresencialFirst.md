# Plan Maestro — Migración al modelo Presencial-First

> **Fuente única de verdad del progreso** del cambio de rumbo descrito en
> `docs/CambioDeRumbo_AsignaturaGrupo_PresencialFirst.md`.
> Cada etapa actualiza este archivo al cerrar. Última actualización: **2026-07-11 (cierre Etapa 5 — auditoría matemática A/B/C)**.

El cambio es estructural y grande, así que se ejecuta **segmentado en etapas** para evitar romper el pipeline. Regla de oro: cada etapa debe dejar la solución compilando y con los tests verdes.

---

## Leyenda de estado
- ✅ hecho · 🔄 en curso · ⏳ pendiente · 🚫 bloqueado (dato de §8 del change spec)

## Matriz Etapas × Change Requests

| CR | Tema | E1 (datos) | E2 (docente/grafo) | E3+ (motores) | Bloqueos |
|---|---|:--:|:--:|:--:|---|
| CR-01 | `Grupo` de primera clase (aforo) | ✅ (ya existía) | — | — | 🚫 nº estudiantes (§8) |
| CR-02 | `Sesion.DocenteId` nullable; docente fuera del pipeline + asignación post-gen | ✅ | ✅ | ✅ E3+E4 | — |
| CR-03 | Dos flujos (`TipoFlujo`) | ✅ datos | — | ✅ E1 (3 tracks: teoría presencial/virtual + laboratorio, `MapearSesionesIniciales`) | — |
| CR-04 | Alternancia opcional por sesión (`PatronAlternanciaId`, `Bloqueada`) | ✅ datos | — | ✅ (`AplicarPrioridadPresencial` respeta `Bloqueada`; alternancia solo en el track de laboratorio) | — |
| CR-05 | Prioridad por `Categoria` | ✅ datos | — | ✅ SC-PRES en `EvaluadorFitness` (**informativa desde auditoría B2** — no afecta el ranking del GA, solo se reporta) | — |
| CR-06 | Aforo `Espacio.Capacidad >= Grupo.Estudiantes` | — | — | ✅ HC-CAP en CP-SAT + `AsignadorEspacios` + validador post-gen (auditoría A1) | — |
| CR-07 | Ventana horaria por asignatura | ✅ datos | — | ✅ HC-VH — decidida **hard**, en CP-SAT + dominio del GA + validador post-gen (auditoría A1/B3) | — |
| CR-08 | Quitar disponibilidad docente del núcleo; grafo por grupo | — | 🔄 HC-I02 degradada | ✅ E3 (eje de grupo, HC-C01) | — |

---

## Etapa 1 — Andamiaje de dominio ✅ (cerrada 2026-06-19)

**Objetivo:** establecer el vocabulario de datos (enums + campos) de forma 100% aditiva, sin tocar motores ni la nullability de `DocenteId`.

### Hecho
- Enums nuevos: `TipoFlujo { Laboratorio, AulaVirtual }`, `CategoriaAsignatura { Obligatoria, Optativa, Electiva }` (`src/SOEA.Domain/Enums/`).
- `Sesion`: `TipoFlujo` (default `Laboratorio`), `PatronAlternanciaId?` (FK → `TipoAlternanciaConfig`), `Bloqueada` (default false) + mutadores `EstablecerFlujo/EstablecerPatronAlternancia/Bloquear/Desbloquear`. Params de ctor opcionales al final → no rompe call sites.
- `Asignatura`: `Categoria` (default `Obligatoria`), `HoraInicioMin?/HoraFinMax?` (ventana horaria, `TimeOnly?`) + validación `inicio < fin` + mutadores `EstablecerCategoria/EstablecerVentanaHoraria` + `ActualizarDatos` extendido.
- EF Core: `HasConversion<string>()` para los enums, FK opcional `SetNull` a `TiposAlternancia`, defaults en BD, índice `ix_sesion_patron_alternancia_id`.
- Migración `20260619181339_EtapaInicialPresencialFirst` creada y aplicada sobre `SOEAdb`.
- Tests: `AsignaturaTests` (nuevo) + casos añadidos a `SesionTests`. Suite completa **213/213 verde**; arquitectura (NetArchTest) intacta.
- Docs actualizados: este plan, `docs/domain.md`, `docs/algorithms.md`, `CLAUDE.md`, anotaciones en el change spec.

### Reutilización clave (no se crearon duplicados)
- Catálogo de patrón = entidad existente **`TipoAlternanciaConfig`** + su seed (no entidad `PatronAlternancia` nueva).
- Patrón EF `HasConversion<string>()`; tipo `TimeOnly?` (ya usado en `BloqueTiempo`); patrón de mutadores `Establecer*`; `EntidadBase`; `SOEABdContextFactory`.

---

## Etapa 2 — Docente fuera del núcleo ✅ (cerrada 2026-06-19)

**Objetivo:** sacar al docente del núcleo de generación, de forma defensiva, sin todavía introducir el grafo-por-grupo.

### Hecho
- `Sesion.DocenteId` → `Guid?` (nullable) + `Validar` relajado (docente opcional). Migración `20260619194707_Etapa2DocenteOpcional` (`docente_id` `DROP NOT NULL`) creada y aplicada sobre `SOEAdb`.
- Null-guards en todos los consumidores: `ConstructorGrafoConflictos` (arista `HasValue`), `MotorConstraintProgramming` (HC-I03 `HasValue`), `OperadoresGeneticos`/`MotorGenetico`/`EvaluadorFitness` (`HasValue`/`.Value`), `ValidadorRestriccionesDuras`, `GenerarHorarioService`, `CrearSesionManualService`, `ImportarCurriculumService`.
- **HC-I02 degradada** (primer paso de CR-08): la disponibilidad docente deja de ser hard constraint de generación en Fase 2 (CP-SAT: se quita el filtro de dominio por disponibilidad y los rechazos por "sin disponibilidad" / "bloques válidos") y en Fase 3 (GA: `_startsValidos` ya no filtra por disponibilidad). Se conserva el dato `Docente.Disponibilidad` y su uso **blando** en SC-06. HC-I01 (NoOverlap docente), HC-I03 (máx. horas contrato) y HC-S01 (espacio) intactos.
- Tests: CP-SAT de disponibilidad invertidos a factible (`Docente_SinDisponibilidad_SeAgendaIgual`); `Docente_MaxHorasExcedidas` sigue infactible (HC-I03 preservado); `Mutar_RespetaCabeEnDia` reescrito; `Constructor_SinDocente_CreaSesion` nuevo. Suite **213/213 verde**; arquitectura intacta.
- **Precondición §9.3 verificada:** `CromosomaHorario.StartB` existe (Issue 2 cerrado) antes de tocar HC-I02.

### Lo que NO entró (defensivo, no semántico)
- Se conserva el round-robin de `MapearSesionesIniciales`: el camino de generación aún no produce `DocenteId = null`. El campo nullable es andamiaje hasta CR-08 (grupo como eje).

---

## Etapa 3 — Grupo como eje ✅ (cerrada 2026-06-19)

**Objetivo:** cerrar CR-08 — el grupo/cohorte reemplaza al docente como eje de conflicto y de optimización en las tres fases. Docente fuera del pipeline (se asigna después de generar).

### Hecho
- **Cohorte implícita:** un run = una sola cohorte. `GenerarHorarioService` asigna un `GrupoId` sintético único por run a todas las sesiones (iniciales y fijas); `DocenteId = null`; round-robin de docente eliminado.
- **Fase 1** (`ConstructorGrafoConflictos`): arista por `GrupoId` (cohorte) en vez de docente ⇒ con cohorte única, grafo completo (todas las sesiones se serializan).
- **Fase 2** (`MotorConstraintProgramming`): **HC-C01** = NoOverlap por `(grupo, semana)` reemplaza HC-I01 (docente); HC-I03 (máx. horas docente) eliminado. HC-S01/S03/S04 (espacio) intactos.
- **Fase 3** (`EvaluadorFitness`, `OperadoresGeneticos`, `MotorGenetico`): ergonomía y reparación de solapes re-clavadas a `GrupoId`. SC-06 balancea sobre los días operativos de la grilla (el grupo no declara disponibilidad). SC-BAL y guarda de aulas intactas.
- **Validador** (`ValidadorRestriccionesDuras`): HC-C01 (cohorte) reemplaza HC-I01; `ValidarCargaSemanal` (HC-I03) eliminado.
- Tests re-clavados a cohorte (grafo, validador, CP-SAT, genéticos) + casos de **serialización de cohorte** (`Cohorte_NoCabeEnLaGrilla`, `Cohorte_MasSesionesQueBloques`, `DosSesionesMismaCohorte_NoSolapan`). Suite **213/213 verde**; arquitectura intacta.
- **Sin migración:** `grupo_id` ya existía (nullable, sin FK); solo se puebla.

### Correctness ganada
- HC-C01 (cohorte) **no existía** en CP-SAT/validador antes (solo docente+espacio). Ahora un grupo no puede estar en dos sesiones a la vez (presencial o virtual). HC-C01 ⊇ el viejo HC-I01, así que no se debilita ninguna garantía.

### Lo que NO entró (etapa posterior)
- HU-04 `PATCH /api/sesiones/{id}` + validación condicional de docente en edición; asignación de docente post-generación.
- Multi-cohorte real (varios grupos por run), `Semestre`/`GrupoId` explícitos en el DTO.

---

## Etapa 4 — Asignación de docente post-generación ✅ (cerrada 2026-06-20)

**Objetivo:** cerrar el 2º rol diferido de CR-02 — permitir asignar (y desasignar) un docente a una sesión ya generada por el pipeline, con la validación correcta para el modelo presencial-first.

### Hecho
- **Mutador `Sesion.AsignarDocente(Guid?)`** (Domain): permite asignar o desasignar el docente post-generación. Rechaza `Guid.Empty`; acepta null para desasignar.
- **`AsignarDocenteSesionService`** (Application): valida **solape duro** (HC-I01 en edición — rechaza si el docente ya tiene otra sesión que se solapa en día/semana, usando span real de duración) y calcula **advertencias blandas** (HC-I02: bloque fuera de disponibilidad declarada; HC-I03: carga supera máximo). Persiste vía `IRepositorio<Sesion>.UpdateAsync`.
- **`PATCH /api/sesiones/{id}/docente`** (API — nuevo `SesionesController`): 200 OK (con advertencias), 409 Conflict (solape), 404, 400.
- **Sin migración:** `docente_id` ya es nullable (Etapa 2); solo un `UPDATE` SQL.
- **Tests:** 11 tests nuevos; suite **224/224 verde**. Arquitectura intacta (NetArchTest).

### Política de validación en edición (§11 #3 resuelta)
HC-I01 (solape físico) → **duro** (409). HC-I02 (disponibilidad) y HC-I03 (carga máx.) → **blandos** (advertencias). Coherente con la degradación HC-I02 de Etapa 2.

---

## Etapa 5 — Auditoría matemática del pipeline ✅ (cerrada 2026-07-11)

**Objetivo:** cerrar CR-05/CR-06/CR-07 (que este plan traía como "Etapa 5+ pendiente" pero ya estaban implementados sin actualizar aquí) y corregir vacíos de corrección/optimización encontrados en una auditoría end-to-end del pipeline de 3 fases. Fases A (corrección) → B (optimización) → C (deuda), ~255 tests verdes al cierre.

### Fase A — Corrección
- **A1:** `SOEA.Domain.Services.CalculadorDominioSesion` — fuente única de HC-G01 (franja de grupo) + HC-VH (ventana), consumida por Fase 1, Fase 2 y el GA (antes cada motor la calculaba distinto y el GA/validador ni la conocían). `ValidadorRestriccionesDuras` pasó de validar 2 reglas (HC-C01, HC-S01) a 7 (+ HC-VH, HC-G01, HC-CAP, HC-S03, HC-S05, HC-BASE) sobre la salida final del GA. `AsignadorEspacios` (Fase 3) ahora respeta HC-S05 (espacio fijo) y HC-CAP (aforo), que antes ignoraba.
- **A2:** `MapearSesionesFijas` ya no descarta en silencio una sesión del horario base cuyo día/hora no calza con la grilla — reporta `[WARN]` en los logs y un conteo en la respuesta.
- **A3:** HC-SU01 ("8+8" como hard constraint) confirmada **obsoleta** — ver `docs/domain.md`.

### Fase B — Optimización
- **B1:** el GA pasó de steady-state (1 hijo/generación, ≤200 evaluaciones) a (μ+λ) con elitismo (`TamañoPoblación` hijos/generación, ~10.000 evaluaciones con la config por defecto), con mejor fitness monótono no-creciente garantizado.
- **B2:** SC-PRES dejó de sumar al fitness (era una constante para el conjunto de sesiones del run — no dirigía ninguna optimización); se reporta aparte como `PenalizacionPresencial`.
- **B3:** Fase 1 (`AgendadorColoracionGrafo`) usa `CalculadorDominioSesion` (antes ignoraba HC-G01/HC-VH) y recorre candidatos en round-robin por día (antes amontonaba todo el lunes por la mañana, dado que con cohorte única el grafo de conflictos es completo).
- **B4:** `MapearConfiguracion` ya no descarta `PesoBalanceSemanas`, `PesoPresencialFirst` ni `Semilla` del DTO — la ejecución es reproducible si el frontend fija una semilla.

### Fase C — Deuda
- **C1:** grilla horaria extraída a `SOEA.Domain.Services.GrillaInstitucional` (fuente única); el rango real (06:00–22:00 L-V / 06:00–13:00 Sáb) sigue siendo un **dato bloqueante sin confirmar por Rosa** — no se inventó un valor nuevo. HC-T02/HC-T04 quedan de-scope explícito hasta esa confirmación.
- **C2:** `PesoAlmuerzo` → `PesoMaxHorasSeguidas` (no pondera almuerzo, pondera SC-09: rachas de >N horas); el umbral de horas (antes hardcodeado en 6) es ahora `UmbralHorasSeguidas` configurable.
- **C3:** este archivo, `docs/domain.md` y `docs/algorithms.md` actualizados al estado real.

### Decisiones que quedaron cerradas (antes en §11 del change spec)
1. Ratificación de la relajación de Tipo A — **resuelto:** TipoA/TipoB son tipos de alternancia semanal, no un patrón de 8+8. HC-SU01 obsoleta.
2. Ventana horaria (CR-07) — **resuelto: hard** (HC-VH).

### Decisiones que siguen abiertas — NO asumir
1. Fuente del nº de estudiantes por grupo.
2. `Categoria` (obligatoria/optativa/electiva) ¿reemplaza o coexiste con niveles 1/2/3 de Rosa?
3. Rango horario institucional real (dato bloqueante, ver C1 arriba).
4. HC-T02 (labs no después de 19:30) y HC-T04 (receso 12:00–13:00): ¿aplican? Sin confirmación, no implementadas.
