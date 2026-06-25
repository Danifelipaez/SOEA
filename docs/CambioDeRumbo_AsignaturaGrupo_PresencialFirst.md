# SOEA — Especificación de Cambio de Rumbo
## Modelo centrado en Asignatura/Grupo y lógica Presencial-First

> **Tipo de documento:** Especificación de cambio (change spec) para consumo de un agente de IA de programación.
> **Audiencia:** Agente de IA de codificación + Daniel (líder de desarrollo).
> **Idioma de trabajo:** Español. Terminología en inglés solo donde es estándar (hard constraint, fitness function, etc.).
> **Estado:** Propuesta de cambio aprobada por el líder de desarrollo. Algunos puntos requieren ratificación (ver §11) y dependen de datos bloqueados (ver §8).
> **Progreso de implementación (segmentado):** ver `docs/PLAN_MAESTRO_PresencialFirst.md` (fuente única de verdad). **Etapa 1 cerrada (2026-06-19):** andamiaje de datos de **CR-03** (`Sesion.TipoFlujo`), **CR-04** (`Sesion.PatronAlternanciaId?`/`Bloqueada`), **CR-05** (`Asignatura.Categoria`) y **CR-07** (`Asignatura.HoraInicioMin?`/`HoraFinMax?`) implementado vía migración `EtapaInicialPresencialFirst`. La **lógica de motor** de esos CR y los cambios rompedores (**CR-02** `DocenteId` nullable, **CR-06** HC-CAP, **CR-08** grafo por grupo) quedan **pendientes** para etapas posteriores.
> **Supersede parcialmente:** `SOEA_Arquitectura.md` (§9 hard constraints, §9 Fase 1, §13.2) y `BasedeConocimientov6.md` (§4, §5, Fase 1, §11 roadmap).

---

## 0. Cómo usar este documento

Cada cambio está identificado como **CR-NN** (Change Request) y trae:
- **Qué cambia** — descripción precisa.
- **Motivación** — por qué.
- **Entidades / campos afectados** — superficie concreta de código.
- **Reglas** — comportamiento esperado.
- **Guardrails** — qué NO hacer.
- **Código actual** — ruta exacta del archivo y delta respecto al estado presente en el repositorio.

El agente NO debe inventar datos marcados como bloqueados (§8). El agente NO debe reintroducir la disponibilidad docente como eje. El agente respeta los invariantes de §2 y los guardrails globales de §9.

---

## 0.5 Mapa de archivos clave (estado actual del repositorio)

| Artefacto | Ruta en el repositorio |
|---|---|
| Entidad `Grupo` | `src/SOEA.Domain/Entities/Grupo.cs` |
| Entidad `Docente` | `src/SOEA.Domain/Entities/Docente.cs` |
| Entidad `Sesion` | `src/SOEA.Domain/Entities/sesion.cs` |
| Entidad `Asignatura` | `src/SOEA.Domain/Entities/Asignatura.cs` |
| Entidad `Espacio` | `src/SOEA.Domain/Entities/Espacio.cs` |
| Entidad `AsignacionSemanal` | `src/SOEA.Domain/Entities/AsignacionSemanal.cs` |
| Entidad `BloqueTiempo` | `src/SOEA.Domain/Entities/BloqueTiempo.cs` |
| Entidad `Horario` (agregado) | `src/SOEA.Domain/Entities/Horario.cs` |
| Catálogo `TipoAlternanciaConfig` | `src/SOEA.Domain/Entities/TipoAlternanciaConfig.cs` |
| Enum `TipoAlternancia` | `src/SOEA.Domain/Enums/TipoAlternancia.cs` |
| Enum `TipoEspacio` | `src/SOEA.Domain/Enums/TipoEspacio.cs` |
| Enum `Modalidad` | `src/SOEA.Domain/Enums/Modalidad.cs` |
| Enum `SemanaAcademica` | `src/SOEA.Domain/Enums/SemanaAcademica.cs` |
| Enum `PatronBaseAlternancia` | `src/SOEA.Domain/Enums/PatronBaseAlternancia.cs` |
| `IAsignaturaRepositorio` | `src/SOEA.Domain/Interfaces/IAsignaturaRepositorio.cs` |
| `IGrupoRepositorio` | `src/SOEA.Domain/Interfaces/IGrupoRepositorio.cs` |
| `IDocenteRepositorio` | `src/SOEA.Domain/Interfaces/IDocenteRepositorio.cs` |
| `ISesionRepositorio` | `src/SOEA.Domain/Interfaces/ISesionRepositorio.cs` |
| `ITipoAlternanciaConfigRepositorio` | `src/SOEA.Domain/Interfaces/ITipoAlternanciaConfigRepositorio.cs` |
| `IMotorColoracionGrafo` | `src/SOEA.Domain/Interfaces/IMotorColoracionGrafo.cs` |
| `IMotorConstraintProgramming` | `src/SOEA.Domain/Interfaces/IMotorConstraintProgramming.cs` |
| `IMotorGenetico` | `src/SOEA.Domain/Interfaces/IMotorGenetico.cs` |
| `ConstructorGrafoConflictos` (Fase 1) | `src/SOEA.Engine.GraphColoring/ConstructorGrafoConflictos.cs` |
| `MotorConstraintProgramming` (Fase 2) | `src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs` |
| `CromosomaHorario` | `src/SOEA.Engine.Genetic/CromosomaHorario.cs` |
| `EvaluadorFitness` | `src/SOEA.Engine.Genetic/EvaluadorFitness.cs` |
| `MotorGenetico` (Fase 3) | `src/SOEA.Engine.Genetic/MotorGenetico.cs` |
| `GenerarHorarioService` | `src/SOEA.Application/Features/Horario/GenerarHorarioService.cs` |
| `HorarioController` | `src/SOEA.API/Controllers/HorarioController.cs` |
| `AsignaturasController` | `src/SOEA.API/Controllers/AsignaturaController.cs` |
| `DocentesController` | `src/SOEA.API/Controllers/DocentesController.cs` |
| `EspaciosController` | `src/SOEA.API/Controllers/EspaciosController.cs` |
| `ImportController` | `src/SOEA.API/Controllers/ImportController.cs` |
| `ModalidadSemanal` (domain service) | `src/SOEA.Domain/Services/ModalidadSemanal.cs` |
| `BloquesPlanner` (domain service) | `src/SOEA.Domain/Services/BloquesPlanner.cs` |

---

## 1. Resumen del cambio de paradigma

El eje del modelo deja de ser **la disponibilidad docente** y pasa a ser **asignatura + grupo de estudiantes + espacio físico**, bajo una lógica de asignación **presencial-first**.

**Razón raíz:** al momento de hacer la programación académica del siguiente semestre **no existen aún los listados de docentes asignados**. Por tanto, la disponibilidad docente no puede ser el input que dirige la generación del horario. El docente pasa a ser un atributo asignable (y editable) de la sesión, no la restricción que ordena el problema.

| Dimensión | Antes (KB actual) | Ahora (este cambio) |
|---|---|---|
| Eje de generación | Disponibilidad docente | Asignatura + Grupo + Espacio |
| Ventana horaria | Definida por docente | Definida por la **Secretaría Académica** por asignatura |
| Docente | Hard constraint, input obligatorio | Atributo opcional, asignable y editable; hasta 2 roles por asignatura |
| Aforo | Implícito | **Número de estudiantes del Grupo** (explícito) |
| Conflicto en Fase 1 | Docente compartido | **Grupo/cohorte** (programa-semestre); docente solo como arista condicional |
| Distribución presencial/virtual | Tipo A fijo 8+8, Tipo B 12+4 | **Presencial-first**; alternancia configurable y opcional por sesión |
| Prioridad de presencialidad | Niveles 1/2/3 (Rosa) | **Por tipo de asignatura:** obligatoria > optativa > electiva |
| Actor configurador principal | Admin de Espacios | **Secretaría Académica** (por facultad) |

---

## 2. Invariantes (principios rectores)

Estos principios son obligatorios. Los nuevos (N) y los conservados del KB (C):

1. **(N) Presencial-first.** El algoritmo intenta asignar TODAS las sesiones como presenciales primero. La alternancia (virtualización parcial) solo se introduce cuando el aforo físico se satura.
2. **(N) Alternancia como válvula de presión.** Al llenarse el aforo del horario, se empiezan a aplicar los distintos tipos de alternancia para liberar espacio físico.
3. **(N) Prioridad de presencialidad por tipo de asignatura.** Las obligatorias conservan presencialidad primero; optativas y electivas son las primeras candidatas a recibir alternancia.
4. **(N) El docente no es el eje.** No es input de generación ni hard constraint de orden. Es atributo asignable/editable.
5. **(N) Grupo y número de estudiantes son la base del aforo.**
6. **(C) Las sesiones virtuales siempre se registran** a la misma hora que su contraparte presencial, etiquetadas como virtuales, para contabilizar carga (KB §13.4). Implementado en `src/SOEA.Domain/Services/ModalidadSemanal.cs` y materializado en `AsignacionSemanal.Modalidad == Modalidad.Virtual` con `EspacioId = null`.
7. **(C) Preservación de bloques de 3 horas** salvo imposibilidad matemática (KB §5). Controlado en `src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs` — la duración de sesión es dato de entrada fijo (CLAUDE.md regla 6).
8. **(C) El espacio liberado nunca queda ocioso**; se reasigna (KB §13.3).
9. **(C) Generación desde cero.** El "viejo Eureka" es solo fuente de datos, no base de iteración (KB §13.6). `GenerarHorarioService` (`src/SOEA.Application/Features/Horario/GenerarHorarioService.cs`) siempre inicializa desde cero. *Nota: corrige la afirmación contradictoria de `BasedeConocimientov6.md` §6 "Flujo de Entrada de Datos", que debe alinearse a desde-cero.*
10. **(C) Límites operativos:** fin máx. 21:30; sábados hasta 13:00–14:00; inicio en horas en punto (KB §5). Modelados como rango en `BloqueTiempo` (06:00–22:00, sábados 06:00–14:00) en `src/SOEA.Domain/Entities/BloqueTiempo.cs`.
11. **(C) Clean Architecture:** las dependencias apuntan al dominio; `SOEA.Domain` sin referencias salientes. Verificado por `test/SOEA.Tests/Architecture/ArchitectureTests.cs` (NetArchTest).

---

## 3. Cambios al modelo de dominio

### CR-01 — Entidad `Grupo` (estudiantes) de primera clase
- **Qué cambia:** `Grupo` deja de ser un placeholder y se implementa como un grupo de estudiantes con conteo real.
- **Campos afectados:**
  - `Grupo.Id`
  - `Grupo.numeroEstudiantes` (int) — fuente de aforo proyectado.
  - `Grupo.programa`, `Grupo.cohorte` / `semestre` — para conflictos de Fase 1.
- **Reglas:** el aforo de la sesión se deriva de `Grupo.numeroEstudiantes` (ver CR-06).
- **Guardrails:** `numeroEstudiantes` es **dato bloqueado** (§8). No hardcodear cifras; modelar el campo y la validación, dejar el valor como entrada.

> **Código actual — `src/SOEA.Domain/Entities/Grupo.cs`**
>
> La entidad ya existe con los campos esenciales bajo nombres ligeramente distintos:
> - `EstudiantesInscritos` (int, validado > 0) ↔ `numeroEstudiantes` del CR.
> - `ProgramaId` (Guid) ↔ `Grupo.programa` del CR.
> - `Semestre` (int, rango 1–10) ↔ `Grupo.cohorte/semestre` del CR.
> - `Nombre` y `Alternancia` (TipoAlternancia) también presentes.
>
> **Delta requerido:** ningún campo nuevo en la entidad. Verificar que `EstudiantesInscritos` sea el campo usado como fuente de aforo en CR-06 y en `ConstructorGrafoConflictos` para las aristas por grupo.
>
> **IGrupoRepositorio** en `src/SOEA.Domain/Interfaces/IGrupoRepositorio.cs` — implementa `IRepositorio<Grupo>` con `GetByNombreYProgramaAsync`. Agregar consulta por programa+semestre para Fase 1.

---

### CR-02 — Entidad `Docente`: degradada y con dos roles
- **Qué cambia:** el docente se vuelve **opcional** y soporta **dos roles por asignatura/grupo**:
  - Docente de **aula/virtual** (componente teórico / salón / sincrónico).
  - Docente de **laboratorio** (componente práctico).
  - Pueden ser la misma persona o personas distintas (caso "modular").
- **Campos afectados:**
  - `Sesion.docenteId` ahora **nullable**.
  - Modelar el rol: o bien `Sesion.tipoFlujo` (ver CR-03) determina qué docente aplica, o bien la asignatura porta `docenteAulaVirtualId?` y `docenteLaboratorioId?`.
- **Reglas:** la asignación de docente puede ocurrir **después** de la generación del horario (vía edición, ver CR-07 / §7).
- **Guardrails:** **eliminar la disponibilidad docente como input de generación y como hard constraint de orden** (ver CR-08). No bloquear la generación por ausencia de docente.

> **Código actual — `src/SOEA.Domain/Entities/sesion.cs`**
>
> `Sesion.DocenteId` es actualmente `Guid` (no nullable). Deben hacerse dos cambios:
> 1. Cambiar `Guid DocenteId` → `Guid? DocenteId` en la entidad y ajustar la configuración EF en `src/SOEA.Infrastructure.Data/Configurations/SesionConfiguration.cs`.
> 2. Añadir migración: `dotnet ef migrations add SesionDocenteNullable --startup-project ../SOEA.API` (desde `src/SOEA.Infrastructure.Data`).
>
> **Código actual — `src/SOEA.Domain/Entities/Docente.cs`**
>
> `Docente` tiene `Disponibilidad` (List\<FranjaHoraria\>) y `BloquesDisponibles` (List\<BloqueTiempo\>). CR-08 los degrada; no eliminar todavía — ver CR-08.
>
> **Código actual — `src/SOEA.Domain/Entities/Asignatura.cs`**
>
> `Asignatura` tiene un solo `DocenteId?` (Guid nullable). Para el esquema modular (dos roles), añadir `DocenteAulaVirtualId?` y `DocenteLaboratorioId?` como alternativa a derivarlo de `Sesion.tipoFlujo`. Decisión de diseño pendiente: si el rol queda en `Asignatura` o en `Sesion` vía `tipoFlujo` (CR-03).
>
> **IDocenteRepositorio** en `src/SOEA.Domain/Interfaces/IDocenteRepositorio.cs` — no requiere cambios para este CR.

---

### CR-03 — Asignatura con **dos flujos de sesión independientes**
- **Qué cambia:** una asignatura-grupo se descompone en dos flujos schedulables por separado:
  - **Flujo Laboratorio** (presencial en laboratorio).
  - **Flujo Aula/Virtual** (salón presencial o virtual sincrónico).
  - Cada flujo tiene **su propio horario, su propio docente y su propio número de sesiones**.
- **Campos afectados:**
  - `Sesion.tipoFlujo` ∈ { `Laboratorio`, `AulaVirtual` }.
  - `AsignacionSemanal` debe poder representar ambos flujos en paralelo por asignatura-grupo.
  - `CromosomaHorario` (Issue 2): el modelo bi-semanal `semanaA`/`semanaB` debe contemplar que un flujo puede ser presencial en semana A y virtual en semana B, **independientemente** del otro flujo.
- **Reglas:** los horarios de Laboratorio y Aula/Virtual de la misma asignatura **pueden diferir** (HU-03).
- **Guardrails:** no forzar que ambos flujos compartan franja. No fusionar flujos en una sola sesión.

> **Código actual — `src/SOEA.Domain/Entities/sesion.cs`**
>
> `Sesion` **no tiene** campo `tipoFlujo`. El tipo de espacio (`TipoEspacio.Laboratorio` vs `TipoEspacio.Salon`) en `Espacio` (`src/SOEA.Domain/Entities/Espacio.cs`) es la única distinción implícita actual.
>
> **Delta requerido:**
> 1. Crear enum `TipoFlujo { Laboratorio, AulaVirtual }` en `src/SOEA.Domain/Enums/TipoFlujo.cs`.
> 2. Añadir `Sesion.TipoFlujo` (TipoFlujo, non-nullable) y migración correspondiente.
>
> **Código actual — `src/SOEA.Domain/Entities/AsignacionSemanal.cs`**
>
> Tiene `SesionId`, `Semana` (SemanaAcademica), `BloqueTiempoId`, `EspacioId?`, `Modalidad`. Con `TipoFlujo` en `Sesion`, `AsignacionSemanal` hereda la distinción por referencia — no requiere campo adicional propio.
>
> **Código actual — `src/SOEA.Engine.Genetic/CromosomaHorario.cs`**
>
> Estructura bi-semanal ya implementada: `Start[]` (Semana A) y `StartB[]` (Semana B), indexados por `SesionIds[]`. La independencia de flujos implica que dos sesiones de la misma asignatura-grupo (una `Laboratorio`, otra `AulaVirtual`) son genes independientes en el cromosoma — esto ya es compatible con la estructura actual.

---

### CR-04 — Alternancia configurable y **opcional por sesión**
- **Qué cambia:** cada sesión puede tener **uno, otro, o ningún** patrón de alternancia. "Ninguno" = presencial puro.
  - Ejemplos de patrón (catálogo, no exhaustivo): `2h_presencial_1h_virtual`, `intercalado_semanal_presencial_virtual`, otros que defina la matriz de Rosa+directores.
- **Campos afectados:**
  - `Sesion.patronAlternanciaId?` (nullable; null = presencial puro).
  - `PatronAlternancia` (catálogo): describe la mezcla presencial/virtual (horas presenciales, horas virtuales, regla semanal).
  - `TipoAlternancia`: tratar como **enum abierto / catálogo**, NO cableado rígidamente a "A/B".
  - `Sesion.bloqueada` (bool): si true, el optimizador no puede cambiar su alternancia (p. ej. casos fijados como Química Orgánica).
- **Reglas:** el optimizador asigna/ajusta `patronAlternanciaId` solo en sesiones no bloqueadas, y solo cuando la presión de aforo lo exige (presencial-first, §5).
- **Guardrails:**
  - No hardcodear el catálogo de patrones (es **dato bloqueado**, la "matriz de alternancias", §8).
  - **No reintroducir el "Tipo A = 8+8 inmodificable" como regla global.** La rigidez ahora se expresa por sesión con `bloqueada=true`, no por un tipo global. (Esta relajación de KB §13.2 requiere ratificación — §11.)

> **Código actual — `src/SOEA.Domain/Enums/TipoAlternancia.cs`**
>
> ```csharp
> public enum TipoAlternancia { TipoA, TipoB, SinAlternancia }
> ```
> Enum fijo de 3 valores. El CR lo convierte en catálogo abierto representado por `PatronAlternancia`.
>
> **Código actual — `src/SOEA.Domain/Entities/TipoAlternanciaConfig.cs`**
>
> Ya existe una entidad de catálogo editable con campos `Nombre`, `PatronBase` (PatronBaseAlternancia), `SemanasPresenciales`, `Color`, `EsSistema`, `Activo`. Esta entidad es el análogo más cercano al `PatronAlternancia` del CR. Reusar y extender en lugar de crear desde cero.
>
> **Código actual — `src/SOEA.Domain/Interfaces/ITipoAlternanciaConfigRepositorio.cs`**
>
> Interfaz de repositorio para el catálogo ya existe en Domain.
>
> **Delta requerido en `src/SOEA.Domain/Entities/sesion.cs`:**
> 1. Añadir `Sesion.PatronAlternanciaId?` (Guid nullable) referenciando `TipoAlternanciaConfig`.
> 2. Añadir `Sesion.Bloqueada` (bool, default false).
> 3. `Sesion.Alternancia` (TipoAlternancia) puede derivarse del `PatronAlternanciaId` o mantenerse como campo calculado para compatibilidad transitoria.
>
> **En `src/SOEA.Engine.Genetic/EvaluadorFitness.cs`:** el optimizador solo modifica `PatronAlternanciaId` en sesiones donde `Bloqueada == false`.

---

### CR-05 — Prioridad de presencialidad por **tipo de asignatura**
- **Qué cambia:** el orden de preservación de presencialidad se rige por la categoría curricular:
  1. **Obligatorias** — preservan presencialidad primero.
  2. **Optativas**
  3. **Electivas** — primeras candidatas a recibir alternancia / virtualización.
- **Campos afectados:**
  - `Asignatura.categoria` ∈ { `Obligatoria`, `Optativa`, `Electiva` }.
- **Reglas:** la `EvaluadorFitness` y la lógica de asignación de alternancia (§5) usan esta categoría como criterio de orden.
- **Guardrails:** confirmar relación con los niveles 1/2/3 de Rosa (§11): coexisten o el de tipo los reemplaza. No asumir que se eliminan los niveles 1/2/3 sin confirmación.

> **Código actual — `src/SOEA.Domain/Entities/Asignatura.cs`**
>
> `Asignatura` **no tiene** campo `Categoria`. Los campos actuales son: `Nombre`, `Codigo`, `HorasPorSesion`, `SesionesPorSemana`, `SesionesLaboratorioSemestre`, `Alternancia`, `ProgramaId`, `DocenteId?`, `EspacioFijoId?`.
>
> **Delta requerido:**
> 1. Crear enum `CategoriaAsignatura { Obligatoria, Optativa, Electiva }` en `src/SOEA.Domain/Enums/CategoriaAsignatura.cs`.
> 2. Añadir `Asignatura.Categoria` (CategoriaAsignatura) y migración.
>
> **Código actual — `src/SOEA.Engine.Genetic/EvaluadorFitness.cs`**
>
> Soft constraints actuales: SC-01 (huecos ociosos, peso 3), SC-06 (balance entre días, peso 2), SC-09 (>6h seguidas, peso 3), SC-BAL (desbalance semanas A/B, peso 2), guarda de capacidad de aulas (peso 1000).
>
> **Delta requerido en `EvaluadorFitness`:** añadir penalización SC-PRES que incrementa el costo cuando una sesión `Obligatoria` tiene alternancia y una `Electiva` es presencial pura (inverso del criterio de presencial-first). El `EvaluadorFitness` necesita acceso a `Asignatura.Categoria` vía el contexto de evaluación.

---

### CR-06 — Aforo derivado del Grupo
- **Qué cambia:** la hard constraint de capacidad se evalúa como `Espacio.capacidad >= Grupo.numeroEstudiantes`.
- **Campos afectados:** `Espacio.capacidad`, `Grupo.numeroEstudiantes`.
- **Guardrails:** ambos lados son **datos bloqueados** (§8). Modelar la restricción, no los valores.

> **Código actual — `src/SOEA.Domain/Entities/Espacio.cs`**
>
> `Espacio.Capacidad` (int, validado > 0) — campo ya existe.
>
> **Código actual — `src/SOEA.Domain/Entities/Grupo.cs`**
>
> `Grupo.EstudiantesInscritos` (int, validado > 0) — campo ya existe bajo este nombre (`numeroEstudiantes` en la terminología del CR).
>
> **Código actual — `src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs`**
>
> La constraint `Espacio.Capacidad >= Grupo.EstudiantesInscritos` **no está implementada** como hard constraint en CP-SAT. Actualmente HC-S03 solo verifica que sesiones de laboratorio usen espacios de tipo `Laboratorio`; no verifica aforo.
>
> **Delta requerido en `MotorConstraintProgramming`:** añadir HC-CAP: filtrar espacios candidatos a los que cumplan `Espacio.Capacidad >= sesion.Grupo.EstudiantesInscritos` antes de crear las variables de asignación de espacio. Requiere que `GenerarHorarioService` pase `Grupo.EstudiantesInscritos` en el request.

---

### CR-07 — Ventana horaria definida por la Secretaría Académica (reemplaza disponibilidad docente)
- **Qué cambia:** el acotamiento temporal de una asignatura ya NO viene del docente, sino de un **rango horario definido por la Secretaría Académica** (p. ej. "esta materia entre 6:00 y 12:00").
- **Campos afectados:**
  - `Asignatura.ventanaHoraria` (o por flujo: `Sesion.ventanaHoraria`) — { horaInicioMin, horaFinMax }.
  - Posible nuevo actor/rol `SecretariaAcademica` (por facultad) en el modelo de roles.
- **Reglas:** la generación respeta esta ventana como acotamiento (candidata a hard constraint). Los límites operativos globales (§2.10) siguen aplicando por encima.
- **Guardrails:** no derivar ventanas de listados docentes inexistentes.

> **Código actual — `src/SOEA.Domain/Entities/Asignatura.cs`**
>
> `Asignatura` **no tiene** campo de ventana horaria. Los límites operativos globales están modelados como rango de `BloqueTiempo` (06:00–22:00) en `src/SOEA.Domain/Entities/BloqueTiempo.cs`.
>
> **Delta requerido:**
> 1. Añadir `Asignatura.HoraInicioMin` (TimeOnly o int en minutos) y `Asignatura.HoraFinMax` nullable; o crear un value object `VentanaHoraria { HoraInicioMin, HoraFinMax }` en `src/SOEA.Domain/ValueObjects/`.
> 2. En `src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs`: añadir constraint HC-VH que filtra bloques candidatos a los que caigan dentro de `[HoraInicioMin, HoraFinMax]`.
> 3. Nuevo endpoint de API (§7): `PUT /api/asignaturas/{id}/ventana-horaria` en `src/SOEA.API/Controllers/AsignaturaController.cs`.
> 4. **No** añadir endpoint que requiera autenticación JWT hasta que esté implementado el rol `SecretariaAcademica` (pendiente en CLAUDE.md §3 — autenticación JWT es ítem sin checkmark).

---

### CR-08 — Eliminar disponibilidad docente del núcleo
- **Qué cambia:** se retira `Docente.disponibilidad` como input de generación y como hard constraint del pipeline.
- **Reglas:** si en el futuro hay docente asignado, la verificación de no-solapamiento docente se hace como **validación en edición** (§7), no como restricción de generación.
- **Guardrails:** no eliminar el concepto de no-solapamiento docente del todo; **degradarlo** a validación condicional, activa solo cuando hay docente asignado.

> **Código actual — `src/SOEA.Domain/Entities/Docente.cs`**
>
> `Docente.Disponibilidad` (List\<FranjaHoraria\>) y `Docente.BloquesDisponibles` (List\<BloqueTiempo\>) — existen y son input actual de generación.
>
> **Código actual — `src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs`**
>
> HC-I02: "Respeto a disponibilidad docente" — hard constraint activa en Fase 2. **Este es el constraint a degradar.** Debe desactivarse de la generación y reutilizarse como validación condicional en edición.
>
> **Código actual — `src/SOEA.Engine.GraphColoring/ConstructorGrafoConflictos.cs`**
>
> `TienenConflicto()` evalúa (1) mismo `DocenteId`, (2) mismo `EspacioId` con excepción TipoA/TipoB. La arista por docente es HOY la arista primaria de Fase 1.
>
> **Delta requerido (en orden de impacto):**
> 1. **`ConstructorGrafoConflictos`:** añadir arista por `Grupo.ProgramaId + Grupo.Semestre` como criterio primario. El criterio por `DocenteId` pasa a ser condicional: solo crear arista si `s1.DocenteId.HasValue && s1.DocenteId == s2.DocenteId`.
> 2. **`MotorConstraintProgramming`:** desactivar HC-I02 (disponibilidad docente) como hard constraint de generación. Mover la lógica de verificación a un servicio de validación de edición (Application layer).
> 3. **`GenerarHorarioService`:** ya no es necesario cargar ni pasar `Docente.Disponibilidad` como input del pipeline.
> 4. **`Docente`:** conservar los campos `Disponibilidad` y `BloquesDisponibles` en la entidad (para futura validación en edición), pero marcarlos como no usados en generación con un comentario de dominio.
>
> **Nota sobre Issue HC-I02 (guardrail §9.3):** al remover HC-I02 de Fase 2, el bug "Fase 3 viola disponibilidad docente" queda parcialmente obsoleto. Confirmar con Daniel antes de planear su fix.

---

## 4. Reclasificación de restricciones

### Hard constraints — removidas o degradadas
- **Disponibilidad docente:** removida del núcleo de generación; degradada a validación condicional en edición (CR-08).
  - Código afectado: HC-I02 en `src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs`.

### Hard constraints — conservadas (con fuente actualizada)
- **Capacidad:** `Espacio.capacidad >= Grupo.numeroEstudiantes` (CR-06). Nueva HC-CAP a implementar en `MotorConstraintProgramming`.
- **Dependencia de equipamiento / fijación de salón** (KB §5). Implementada como HC-S03 y `Asignatura.EspacioFijoId?` en `src/SOEA.Domain/Entities/Asignatura.cs`.
- **Registro de sesiones virtuales** a la misma hora que su presencial (KB §5, invariante 2.6). Implementado en `src/SOEA.Domain/Services/ModalidadSemanal.cs`; `AsignacionSemanal.EspacioId = null` cuando `Modalidad == Virtual`.
- **Preservación de bloques de 3h** (invariante 2.7). `Asignatura.HorasPorSesion` es inmutable en generación (CLAUDE.md regla 6).
- **Separación de sesiones múltiples:** ≥ 1 día de intervalo, no consecutivas (KB §5). Implementar en CP-SAT Fase 2.
- **Límites operativos** (invariante 2.10). Rango de `BloqueTiempo` en `src/SOEA.Domain/Entities/BloqueTiempo.cs`.
- **Ventana horaria por asignatura** definida por Secretaría (CR-07) — candidata a hard. Nueva HC-VH a implementar.

### Fase 1 (coloración de grafos) — cambio de fundamento
- **Antes:** aristas por docente compartido. Implementación actual en `src/SOEA.Engine.GraphColoring/ConstructorGrafoConflictos.cs`, método `TienenConflicto()`.
- **Ahora:** aristas por **grupo/cohorte** (un grupo no puede estar en dos sesiones simultáneas) y por **programa + semestre** (no solapar bloques del mismo semestre). El docente genera arista **solo si está asignado** (condicional).
- *Nota: `BasedeConocimientov6.md` ya contemplaba el conflicto de cohorte; ahora ese eje se vuelve primario y el docente secundario.*
- **Delta concreto:** modificar `ConstructorGrafoConflictos.TienenConflicto()` para añadir criterio `s1.GrupoId.HasValue && s1.GrupoId == s2.GrupoId` como arista primaria, antes del criterio de docente.

### Soft constraints — cambios y conservadas
- **Prioridad de presencialidad por tipo de asignatura** (CR-05) — nuevo criterio SC-PRES a añadir en `src/SOEA.Engine.Genetic/EvaluadorFitness.cs`.
- Conservadas en `EvaluadorFitness`: SC-01 huecos ociosos (peso 3), SC-06 balance días (peso 2), SC-09 >6h seguidas (peso 3), SC-BAL desbalance semanas A/B (peso 2), guarda capacidad aulas (peso 1000). Ergonomía docente y fatiga docente son condicionales a docente asignado — actualmente ligadas a `DocenteId` no-nullable; revisar al hacer `DocenteId` nullable (CR-02).

---

## 5. Lógica de asignación: Presencial-First + alternancia progresiva

### Objetivo
Maximizar presencialidad, y solo virtualizar (vía alternancia) lo necesario para que todo quepa en el aforo físico, sacrificando presencialidad primero en electivas/optativas y al final en obligatorias.

### Pseudocódigo de referencia (heurística constructiva)
```
ENTRADA:
  sesiones            // todas las sesiones (ambos flujos), con grupo, ventana horaria, categoría, duración
  espacios            // laboratorios con capacidad
  catalogoAlternancia // patrones disponibles (BLOQUEADO §8)

PASO 1 — Intento full presencial:
  ordenar franjas cronológicamente (primer horario → último)
  para cada sesión: intentar asignarla presencial respetando
      capacidad, ventana horaria, equipamiento, separación ≥1 día, límites operativos
  registrar conflictos de saturación (sesiones que no caben)

PASO 2 — Liberación por alternancia (solo si hay saturación):
  mientras exista saturación de aforo:
     seleccionar candidata a virtualizar según prioridad inversa:
        Electiva  → primero
        Optativa  → después
        Obligatoria → último recurso
     y entre iguales, preferir la sesión que libere más espacio físico
     aplicar a esa sesión un patronAlternancia del catálogo (si no está bloqueada)
        → mueve N semanas/horas de laboratorio a virtual (registrando la virtual, invariante 2.6)
     reasignar el espacio liberado a sesiones pendientes (no dejar ocioso, invariante 2.8)

PASO 3 — Preservación:
  garantizar que las sesiones de 3h se mantienen salvo imposibilidad matemática
  garantizar que sesiones bloqueadas no fueron alteradas

SALIDA: horario presencial-first con alternancia mínima necesaria
```

### Reconciliación con el pipeline de 3 fases
- **Fase 1 (grafo):** `src/SOEA.Engine.GraphColoring/ConstructorGrafoConflictos.cs` — conflictos por grupo/cohorte (post CR-08). `AgendadorColoracionGrafo` ejecuta Welsh-Powell.
- **Fase 2 (CP / OR-Tools):** `src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs` — factibilidad física: capacidad (HC-CAP, nuevo), equipamiento (HC-S03), ventana horaria (HC-VH, nuevo), separación, límites. Timeout: 120 s (`CpSat:TimeoutSegundos` en `appsettings.json`). Genera el espacio de soluciones presencial-first factibles.
- **Fase 3 (genético):** `src/SOEA.Engine.Genetic/MotorGenetico.cs` — la heurística presencial-first de arriba se usa para **sembrar la población inicial** y/o como sesgo del `HardConstraintRepairOperator`; el GA optimiza las soft constraints (incl. prioridad por tipo, CR-05 vía SC-PRES en `EvaluadorFitness`). El criterio "primero presencial, luego cortar por horario y no por tipo" se modela como orden de reparación, no como recableado del cromosoma. Parámetros actuales: 200 generaciones, población 50, convergencia 30 generaciones estancadas.
- **Issue 2 (cromosoma bi-semanal):** `src/SOEA.Engine.Genetic/CromosomaHorario.cs` ya implementa `Start[]` (Semana A) y `StartB[]` (Semana B). Este cambio se integra DENTRO del restructure de `CromosomaHorario`. No abrir un frente paralelo.

---

## 6. Historias de usuario (formalizadas)

> Actor principal nuevo: **Secretaría Académica** (por facultad).

**HU-01 — Asignar materia en rango horario**
Como Secretaría Académica de la facultad X, quiero asignar la materia Y dentro de un rango de horas (p. ej. 6:00–12:00), para que el algoritmo programe sin depender de listados de docentes.
- *Criterios:* la ventana se respeta en la generación; si la materia no cabe en la ventana, se reporta cuello de botella (no se viola la ventana silenciosamente).
- *Endpoint candidato:* `PUT /api/asignaturas/{id}/ventana-horaria` en `src/SOEA.API/Controllers/AsignaturaController.cs`.

**HU-02 — Dos docentes por asignatura (modular)**
Como Secretaría Académica, quiero poner un docente para las clases de aula/virtuales y otro para laboratorio, para soportar el esquema modular.
- *Criterios:* `docenteAulaVirtual?` y `docenteLaboratorio?` independientes y nullable; pueden coincidir.
- *Campos candidatos en `Asignatura`:* `DocenteAulaVirtualId?` y `DocenteLaboratorioId?` (actualmente solo existe `DocenteId?`).

**HU-03 — Horarios distintos por flujo**
Como Secretaría Académica, quiero tener horarios diferentes entre las clases de laboratorio y las de salón/virtuales, para reflejar la realidad operativa.
- *Criterios:* flujo Laboratorio y flujo Aula/Virtual se programan por separado (CR-03). Requiere `Sesion.TipoFlujo` (nuevo enum).

**HU-04 — Editar una sesión**
Como Secretaría Académica, quiero modificar la hora, el número de sesiones, el docente y el grupo de una sesión, para ajustar ante excepciones.
- *Criterios:* edición validada contra hard constraints vigentes; ver endpoint §7. Cambiar docente dispara validación condicional de no-solapamiento (CR-08).
- *Endpoint candidato:* `PATCH /api/sesiones/{id}` — no existe actualmente; crear en `src/SOEA.API/Controllers/` con un nuevo `SesionController` o bajo `HorarioController`.

**HU-05 — Grupo de estudiantes con conteo**
Como sistema, necesito representar el Grupo como grupo de estudiantes con número de estudiantes, para derivar el aforo (CR-01, CR-06).
- *Estado:* `Grupo.EstudiantesInscritos` ya existe en `src/SOEA.Domain/Entities/Grupo.cs`. Vincular con la constraint HC-CAP en Fase 2.

**HU-06 — Reconfigurar alternancia para rellenar aforo**
Como Secretaría Académica, quiero seleccionar una asignatura con cupo libre y cambiar su tipo de alternancia, para que el algoritmo rellene el espacio restante hasta saturar productivamente (invariante 2.8).
- *Criterios:* el cambio re-dispara la asignación sobre el espacio restante sin alterar lo ya fijado/bloqueado. Requiere `Sesion.Bloqueada` (CR-04).
- *Endpoint candidato:* `PATCH /api/sesiones/{id}/alternancia`.

---

## 7. Cambios a la API

- **Edición de sesión** (HU-04): endpoint para modificar `hora`, `numeroSesiones`, `docenteId`, `grupoId` de una `Sesion`. Respuesta valida contra hard constraints vigentes y devuelve conflictos estructurados. Candidato: `PATCH /api/sesiones/{id}`.
- **Validación de intercambio**: `POST /api/horario/validar-intercambio` (dependencia ya identificada en `src/SOEA.API/Controllers/HorarioController.cs`) — soporta el flujo drag-and-drop del frontend.
- **Clasificación presencial/virtual por sesión**: toda respuesta de horario debe incluir, por sesión, su clasificación presencial vs. virtual y su `patronAlternancia` aplicado. El DTO `SesionGeneradaDto` (`src/SOEA.Application/Features/Horario/Responses/GenerarHorarioResponse.cs`) ya incluye `Virtual` y `Alternancia`; añadir `PatronAlternanciaId` y `TipoFlujo`.
- **Asignación de ventana horaria por asignatura** (HU-01): `PUT /api/asignaturas/{id}/ventana-horaria` en `AsignaturaController`.
- **Guardrails de API:** las operaciones que envían/publican/persisten cambios institucionales requieren confirmación explícita del usuario antes de ejecutarse; no auto-publicar.

---

## 8. Datos bloqueantes (actualizado)

| Dato | Fuente | Estado | Notas |
|---|---|---|---|
| **Número de estudiantes por grupo** | Secretaría Académica / matrícula | ⏳ Bloqueado (NUEVO) | Base del aforo (CR-06). Campo `Grupo.EstudiantesInscritos` ya modelado; falta el valor real. |
| Catálogo de patrones de alternancia (la "matriz de alternancias maleables") | Rosa + Directores de Química | ⏳ Bloqueado (NUEVO, alta palanca) | Desbloquea `TipoAlternanciaConfig` / `PatronAlternanciaId` y CR-04. La entidad catálogo `TipoAlternanciaConfig` en `src/SOEA.Domain/Entities/TipoAlternanciaConfig.cs` ya existe; falta poblarla con datos reales. |
| Capacidad (aforo) de cada laboratorio | Rosa | ⏳ Bloqueado | `Espacio.Capacidad` modelado en `src/SOEA.Domain/Entities/Espacio.cs`; falta valor real. |
| Clasificación obligatoria/optativa/electiva | Malla curricular | 🟡 Probablemente derivable | Confirmar disponibilidad; si lo está, CR-05 queda desbloqueado. Campo `Asignatura.Categoria` aún no existe — crear cuando se confirme. |
| Prioridad niveles 1/2/3 | Rosa | ⏳ Bloqueado / en revisión | ¿Coexiste con CR-05 o se reemplaza? (§11) |
| Duración fija por asignatura (2h/3h) | Rosa / mallas | ⏳ Bloqueado | `Asignatura.HorasPorSesion` modelado; falta la lista completa. Solo confirmada: Química Orgánica 3h. |
| Formato de exportación institucional | Camila/Roberto; posible vía ing. César | ⏳ Bloqueado | César construyó el sistema de programación actual. |

**Regla:** ningún motor se implementa con valores inventados para estos ítems. Modelar entidades y restricciones; dejar los valores como entrada.

---

## 9. Guardrails globales para el agente de IA

1. **No reintroducir la disponibilidad docente como eje** ni como hard constraint de generación. El campo `Docente.Disponibilidad` existe en `src/SOEA.Domain/Entities/Docente.cs` pero no debe usarse en el pipeline de generación post-CR-08.
2. **No inventar** aforos, número de estudiantes, patrones de alternancia, prioridades ni duraciones bloqueadas (§8).
3. **Respetar la secuencia de issues:** primero cerrar **Issue 2** (restructure bi-semanal de `CromosomaHorario` en `src/SOEA.Engine.Genetic/CromosomaHorario.cs`). **No tocar Issue 1 (HC-I02)** hasta cerrar Issue 2. *Observación crítica:* al remover la disponibilidad docente como hard constraint, el bug HC-I02 (Fase 3 violando disponibilidad docente) podría quedar **parcialmente obviado o redefinido** — confirmar con Daniel antes de planear su fix.
4. **No abrir frentes paralelos:** integrar estos cambios dentro del restructure de Issue 2, no en una rama de modelo viejo.
5. **Preservar invariantes §2** (registro de virtuales via `ModalidadSemanal` en `src/SOEA.Domain/Services/ModalidadSemanal.cs`, bloques 3h, desde cero, no ocioso, límites operativos via rango `BloqueTiempo`, Clean Architecture verificada por `test/SOEA.Tests/Architecture/ArchitectureTests.cs`).
6. **Integridad de datos sobre fallos silenciosos:** ante llaves ambiguas (homogeneización de docentes, `FusionDocentesService` en `src/SOEA.API/Controllers/DocentesController.cs`), lanzar excepción de dominio, no propagar mismatch.
7. **Constraint placement por tipo:** concurrencias de grupo/cohorte en Fase 1 (`ConstructorGrafoConflictos`, aristas binarias); espacio y aforo en Fase 2 (`MotorConstraintProgramming`, OR-Tools).
8. **Cambios que tocan decisiones inamovibles del KB §13 requieren ratificación** de Profe Rafa (§11). El agente puede implementar, pero debe dejar el punto anotado como pendiente de ratificación.

---

## 10. Impacto en la documentación existente (qué actualizar)

| Documento / sección | Acción |
|---|---|
| `SOEA_Arquitectura.md` §9 (hard constraints) | Remover disponibilidad docente del núcleo; actualizar fuente de capacidad a Grupo. |
| `SOEA_Arquitectura.md` Fase 1 | Cambiar fundamento de aristas: docente → grupo/cohorte (refleja cambio en `ConstructorGrafoConflictos.TienenConflicto()`). |
| `SOEA_Arquitectura.md` §13.2 | Reescribir: alternancia configurable por sesión via `Sesion.PatronAlternanciaId?`; rigidez vía `Sesion.Bloqueada`. (Ratificación §11.) |
| `BasedeConocimientov6.md` §4, §5 | Redefinir Tipo A/B como catálogo de alternancias por sesión via `TipoAlternanciaConfig`. |
| `BasedeConocimientov6.md` §6 (Flujo de Entrada) | Corregir contradicción: el sistema genera **desde cero** (`GenerarHorarioService`), no itera el Excel. |
| `BasedeConocimientov6.md` §11 (roadmap) | Obsoleto: el horizonte real se ata al fin de prácticas (ago/sep), no al 22 de mayo. |
| `CLAUDE.md`, `docs/domain.md`, `docs/algorithms.md` | Reflejar nuevo modelo: `Grupo.EstudiantesInscritos` como fuente de aforo, dos flujos via `TipoFlujo`, `Asignatura.VentanaHoraria`, presencial-first. |

---

## 11. Decisiones que requieren confirmación (NO asumir)

1. **Fuente del número de estudiantes** por grupo (matrícula vs. Secretaría). El campo `Grupo.EstudiantesInscritos` ya existe pero sin datos reales.
2. **Prioridad:** ¿`obligatoria/optativa/electiva` (CR-05) reemplaza los niveles 1/2/3 de Rosa, o coexisten? Afecta si se añade `CategoriaAsignatura` o un campo numérico.
3. **Disponibilidad docente residual:** ¿se conserva `Docente.Disponibilidad` como validación blanda en edición cuando hay docente, o se elimina por completo? Determina si los campos permanecen en `src/SOEA.Domain/Entities/Docente.cs`.
4. **Ratificación de la relajación de Tipo A** (KB §13.2 → alternancia por sesión via `Sesion.Bloqueada`) por Profe Rafa, dado que §13 exige firma explícita.
5. **¿La ventana horaria por asignatura (CR-07) es hard o soft constraint?** Determina si va en `MotorConstraintProgramming` (HC-VH) o en `EvaluadorFitness` (SC-VH).
6. **Alcance del piloto:** ¿arrancar con Química General como caso único (como se planteó en reunión) o con el conjunto completo desde el inicio?

---

*Fin del documento. Para modificaciones, contactar al líder de desarrollo (Daniel).*
