# Plan Maestro — Migración al modelo Presencial-First

> **Fuente única de verdad del progreso** del cambio de rumbo descrito en
> `docs/CambioDeRumbo_AsignaturaGrupo_PresencialFirst.md`.
> Cada etapa actualiza este archivo al cerrar. Última actualización: **2026-06-20 (cierre Etapa 4)**.

El cambio es estructural y grande, así que se ejecuta **segmentado en etapas** para evitar romper el pipeline. Regla de oro: cada etapa debe dejar la solución compilando y con los tests verdes.

---

## Leyenda de estado
- ✅ hecho · 🔄 en curso · ⏳ pendiente · 🚫 bloqueado (dato de §8 del change spec)

## Matriz Etapas × Change Requests

| CR | Tema | E1 (datos) | E2 (docente/grafo) | E3+ (motores) | Bloqueos |
|---|---|:--:|:--:|:--:|---|
| CR-01 | `Grupo` de primera clase (aforo) | ✅ (ya existía) | — | — | 🚫 nº estudiantes (§8) |
| CR-02 | `Sesion.DocenteId` nullable; docente fuera del pipeline + asignación post-gen | ✅ | ✅ | ✅ E3+E4 | — |
| CR-03 | Dos flujos (`TipoFlujo`) | ✅ datos | — | ⏳ lógica de programación por flujo | — |
| CR-04 | Alternancia opcional por sesión (`PatronAlternanciaId`, `Bloqueada`) | ✅ datos | — | ⏳ uso en optimizador | 🚫 matriz de patrones (§8) |
| CR-05 | Prioridad por `Categoria` | ✅ datos | — | ⏳ SC-PRES en `EvaluadorFitness` | 🟡 clasificación (malla) |
| CR-06 | Aforo `Espacio.Capacidad >= Grupo.Estudiantes` | — | — | ⏳ HC-CAP en CP-SAT | 🚫 aforos (§8) |
| CR-07 | Ventana horaria por asignatura | ✅ datos | — | ⏳ HC-VH/SC-VH (decidir hard/soft, §11) | — |
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

## Pendiente (resumen por etapa siguiente)

### Etapa 5+ — Lógica de motores (CR-03, CR-04, CR-05, CR-06, CR-07)
- HC-CAP (aforo) y HC-VH (ventana horaria) en `MotorConstraintProgramming`.
- SC-PRES (prioridad por categoría) en `EvaluadorFitness`.
- Programación independiente por `TipoFlujo`; uso de `PatronAlternanciaId`/`Bloqueada` en presencial-first.
- Ventana horaria en el DTO/API (HU-01), exponer `TipoFlujo`/`PatronAlternanciaId` en respuestas.

### Decisiones abiertas (§11 del change spec) — NO asumir
1. Fuente del nº de estudiantes por grupo.
2. `Categoria` (obligatoria/optativa/electiva) ¿reemplaza o coexiste con niveles 1/2/3 de Rosa?
3. Ratificación de la relajación de Tipo A (Profe Rafa).
4. Ventana horaria (CR-07): ¿hard (HC-VH) o soft (SC-VH)?
5. ~~Alcance del piloto: ¿Química General único o conjunto completo?~~ — **Resuelto:** conjunto completo de programas de la Universidad del Magdalena.
