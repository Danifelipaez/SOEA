# Plan maestro — SOEA: cerrar las 14 peticiones de `docs/pendiente.txt`

> **Raíz del repo:** `D:\__PracticasUniversitarias\Visual studio code\SOEA`
> (el cwd de la sesión, `D:\__PracticasUniversitarias\SOEA`, es otra carpeta con sólo diagramas).
> Todas las rutas de este documento son relativas a la raíz del repo.

---

## Context

`docs/pendiente.txt` lista 14 peticiones más un bloque `VERIFICA` sobre alternancia. Leídas una por
una parecen 15 tareas independientes; leídas contra el código son **6 causas raíz**. Parchear
síntoma por síntoma significaría tocar los mismos 4 sitios espejo (CP-SAT, GA, validador, UI) una
vez por petición.

El diagnóstico concreto:

| Causa raíz | Evidencia en código | Peticiones que explica |
|---|---|---|
| **R1 — El grupo no es el eje del pipeline.** Las sesiones se expanden desde asignaturas y reciben un `GrupoId` sintético común. | `GenerarHorarioService.cs:63-65` (`?? Guid.NewGuid()`), `:85` (`MapearSesionesIniciales(request.Asignaturas, grupoIdRun)`). Efecto colateral: el grafo de conflictos queda completo siempre (`ConstructorGrafoConflictos.cs:49`) y HC-C01 serializa el run entero (`MotorConstraintProgramming.cs:286-295`). | 3, 5, 9, 10, 14 |
| **R2 — No existe modelo de requisitos de espacio por grupo.** Sólo hay un uuid por asignatura, y sólo para laboratorios. | `Asignatura.EspacioFijoId` (`Asignatura.cs:57`); `Grupo` no tiene ninguna propiedad de espacio (`Grupo.cs`). | 3, 4, 7, 14 |
| **R3 — La disponibilidad del grupo nunca llega al motor.** Tres fallos independientes en cadena. | (a) `horario.component.ts:441` llama `generarHorario(...)` con 6 argumentos — el 7.º, `grupos`, nunca se pasa. (b) `GruposController.cs:86` y `:119` sólo guardan `DisponibilidadUiJson`; `Grupo.Disponibilidad` queda `[]` para siempre y el DTO ni siquiera tiene el campo. (c) el modelo son 2 franjas sobre el bloque de inicio (`CalculadorDominioSesion.cs:22-23`) — la dimensión "día" se descarta. | 5, 9, 10, 14 |
| **R4 — Faltan restricciones duras.** | Cero implementación de separación de días (grep `separacion\|consecutiv` no devuelve nada en `src/`). `HC-S03` sólo protege laboratorios (`ValidadorRestriccionesDuras.cs:123-125`): una teoría presencial puede caer en un laboratorio. Alternancia decidida por zigzag ciego `ab++ % 2` (`GenerarHorarioService.cs:810`), sin pareja ni espacio. | 7, 11, `VERIFICA` |
| **R5 — La persistencia va por lotes y destruye datos.** | `asignaturas-tab.component.ts:203-218` colapsa los 3 tracks en uno y manda todo a `POST /import/curriculum`, que usa el constructor legacy (`ImportController.cs:259-260` → `Asignatura.cs:161-162`) que pone virtual y laboratorio en 0. `POST /api/asignaturas`, que sí respeta los 3 tracks, nunca se llama. | 6, 8 |
| **R6 — UI fragmentada.** Grupos y asignaturas en pestañas distintas; editar en el horario no recalcula nada; el color de celda sólo codifica alternancia. | `ingesta.component.ts:34-36`; `horario.component.ts:638-649` (`commitLocal` es sólo memoria); `horario.component.ts:262` (`altColor`). | 1, 2, 12, 13 |

**Resultado buscado:** las 14 peticiones cerradas apoyándose en un núcleo compartido pequeño, sin
duplicar reglas entre CP-SAT, el algoritmo genético, el validador y la UI.

### Decisiones ya tomadas (confirmadas con el usuario)

1. **Multi-grupo real.** Las sesiones se expanden desde grupos y llevan su `GrupoId` real.
2. **`Asignatura.EspacioFijoId` se elimina.** Los requisitos de espacio viven sólo en `Grupo`.
3. **Paleta derivada del id** de asignatura. Sin columna nueva, sin selector, sin migración.
4. **Orden: núcleo → motor → UI.**

---

## La espina: abstracciones compartidas

Estas piezas se construyen **una vez** en P1/P4 y las consumen todas las fases. Cada una sustituye
código hoy duplicado — no son capas nuevas sobre lo existente.

### Backend

| # | Pieza | Qué sustituye | Dónde |
|---|---|---|---|
| **A1** | `DisponibilidadSemanal` (value object) — parsea el JSON por día que ya produce la UI y responde `PermiteInicio(BloqueTiempo, int duracion)`. | El parser por día **ya existe** para docentes en `GenerarHorarioService.MapearDocentes` (`:426-521`). Se **levanta** a Domain y pasa a servir también a grupos. No se reescribe. Mata `franjasDeGrupo` (`horario-api.service.ts:222-246`) y el parseo de franjas de `MapearGrupos` (`:668-678`). | `src/SOEA.Domain/ValueObjects/DisponibilidadSemanal.cs` |
| **A2** | `CalculadorEspaciosSesion.Candidatos(sesion, grupo, espacios)` — fuente única de espacios candidatos (HC-S03 + HC-S05 + HC-CAP). | Hoy la misma lógica vive **4 veces**: `MotorConstraintProgramming.cs:304-357`, `AsignadorEspacios.cs:87-115`, `ValidadorRestriccionesDuras.cs:118-143`, `horario.component.ts:798-804`. | `src/SOEA.Domain/Services/CalculadorEspaciosSesion.cs` (espejo exacto de `CalculadorDominioSesion`) |
| **A3** | `TipoSesion.De(sesion)` → `TeoriaPresencial \| TeoriaVirtual \| Laboratorio`. **Función pura** derivada del par `(TipoFlujo, Modalidad)` que ya existe. | Nada persistido nuevo. Es el espejo backend de `TipoSesionUi` (`models.ts:137`). | `src/SOEA.Domain/Services/TipoSesion.cs` |
| **A4** | `Sesion.PatronAlternanciaId` como **clave de pareja** de alternancia. | **Ya existe** (`sesion.cs:40`), hoy sólo sirve de traza. Se le da su significado real. | — |
| **A5** | `ReglasSesion.SeparacionDiasOk(diaA, diaB)` — `|a − b| ≥ 2`. | Regla nueva, 3 líneas, consumida por CP-SAT, el validador y `CrearSesionManualService`. | `src/SOEA.Domain/Services/ReglasSesion.cs` |

`CalculadorDominioSesion` (`Domain/Services/`) **no se sustituye: se extiende**. Ya es el punto único
de dominio de inicios y su docstring lo dice explícitamente. `BloquesPermitidos` cambia su parámetro
`IReadOnlyCollection<FranjaHoraria>` por `DisponibilidadSemanal`; los 4 consumidores (Fase 1, CP-SAT,
GA, validador) heredan la granularidad por día gratis.

### Frontend

| # | Pieza | Qué sustituye | Dónde |
|---|---|---|---|
| **B1** | `CatalogoService.guardar(tipo, entidad)` / `.eliminar(tipo, id)` — un único camino de persistencia. | Los **4 `guardarEnBD()` casi idénticos** (`asignaturas-tab:163`, `docentes-tab:156`, `espacios-tab:148`, `grupo-tab:145`) y los 8 botones Guardar/Cargar. | `src/app/core/catalogo.service.ts` |
| **B2** | `<app-disponibilidad-editor>` con `ControlValueAccessor`. | Markup + lógica `getDisp/setDisp/tipoLabel` **copiados literalmente** en `docentes-tab.component.ts:219-277` y `grupo-tab.component.ts:196-263`. | `src/app/shared/disponibilidad-editor/` |
| **B3** | `<app-requisitos-espacio>` — filas `{tipo de sesión → espacio concreto o tipo}`. | Nuevo, pero reutiliza el patrón `.track` de steppers (`asignaturas-tab:269-301`) y `<app-searchable-select>`. | `src/app/shared/requisitos-espacio/` |
| **B4** | `StateService.gruposByAsignatura` (computed) + `colorDeAsignatura(id)`. | Los `.filter(g => g.asignaturaId === …)` sueltos en `horario:770`, `alternancia-tab:219`, `docentes-tab:105`. | `src/app/core/state.service.ts` |

Se reutiliza tal cual, sin tocar: `<app-searchable-select>`, `<app-confirm-delete-dialog>`, y las
clases del design system de `src/styles.css` (`.pophd/.popbd/.popfoot`, `.table`, `.seg`, `.track`,
`.tag`). **No se introduce ninguna librería de UI nueva** — Material sigue siendo sólo plomería.

---

## Planes hijos

### P1 — Núcleo de datos: el grupo como eje
> Cierra la mitad de datos de 3, 4, 5, 7, 14. Habilita todo lo demás.

1. **`DisponibilidadSemanal` (A1).** Extraer el parser por día de `MapearDocentes`
   (`GenerarHorarioService.cs:426-521`) a `src/SOEA.Domain/ValueObjects/`. `Docente` y `Grupo` lo
   exponen derivándolo de su `DisponibilidadUiJson`, que **ya está persistido**.
   - `Grupo.Disponibilidad: List<FranjaHoraria>` (`Grupo.cs:45`) **se borra**: en la BD siempre vale
     `[]` porque el controller nunca la escribe. Migración: `DROP COLUMN disponibilidad`. Es
     eliminación, no sustitución.
   - Corregir el acento en el parser: `'especific'` nunca casa con `"Franja específica"`.
2. **`RequisitoEspacio` en `Grupo` (A2, datos).** Lista `{ TipoSesion, EspacioId?, TipoEspacio, Sesiones }`
   como **columna JSON** en `Grupo`, siguiendo el convertidor que ya usa `GrupoConfiguration.cs:57-62`.
   Sin tabla nueva, sin repositorio nuevo.
3. **Migración de datos + borrado de `Asignatura.EspacioFijoId`.** Una migración que primero copia
   `EspacioFijoId` como requisito de laboratorio a cada grupo de esa asignatura y después borra la
   columna. Limpiar `UpdateAsignaturaRequest.cs:25`, `AsignaturaResponse.cs:20`,
   `AsignaturaService.cs:64`, `GenerarHorarioRequest.cs:100`.
4. **`GruposController`**: añadir `Disponibilidad`/`RequisitosEspacio` al DTO y persistirlos en
   `Create` (`:86`) y `Update` (`:119`) — hoy se pierden silenciosamente.
5. **`MapearSesionesIniciales` pasa a ser grupo-driven** (`GenerarHorarioService.cs:536-596`): itera
   grupos, y por cada grupo expande los 3 tracks de **su** asignatura con el `GrupoId` real. Se
   elimina `grupoIdRun` (`:63-65`).
6. **`horario.component.ts:441`**: pasar el 7.º argumento `grupos`.

**Verificación:** `dotnet test SOEA.sln`. Test nuevo: dos grupos con disponibilidad distinta sobre la
misma asignatura producen dominios de inicio distintos. Un test de regresión que falle si
`GruposController.Create` deja de persistir la disponibilidad.

---

### P2 — Motor: las restricciones que faltan
> Cierra 5, 7, 8, 9, 10, 11, 14 y el bloque `VERIFICA`.

1. **HC-G01 por día.** `CalculadorDominioSesion.BloquesPermitidos` acepta `DisponibilidadSemanal`.
   Fase 1, CP-SAT (`MotorConstraintProgramming.cs:228-242`), GA (`OperadoresGeneticos.cs:88-90`) y
   validador (`ValidadorRestriccionesDuras.cs:108-116`) lo heredan sin cambios propios. **Esto es
   la petición 5, y por construcción también 9 y 10**: con el `GrupoId` real de P1, HC-C01
   (`MotorConstraintProgramming.cs:286-295`) ya separa las sesiones de un mismo grupo, y la
   disponibilidad se cruza por grupo, no por run.
2. **HC-S03 tipado por `TipoSesion` (A3).** Extraer `CalculadorEspaciosSesion` (A2) y sustituir los
   4 sitios espejo. `TeoriaPresencial` exige un espacio del tipo declarado por el grupo (por
   defecto, no-laboratorio); `TeoriaVirtual` no consume espacio; `Laboratorio` exige laboratorio.
   **Esto es la petición 7**, y con P1 la 14: el candidato sale del requisito del grupo.
3. **HC-SEP (petición 11).** En CP-SAT, canalizar el día con `AddElement(start, diaPorIdx, diaVar)`
   y exigir `|dia_i − dia_j| ≥ 2` entre sesiones del mismo `(grupo, asignatura, track)` cuando hay
   ≥2 por semana. Misma regla (`ReglasSesion`, A5) en `ValidadorRestriccionesDuras` y en
   `CrearSesionManualService` para que la creación manual no la pueda violar.
4. **Alternancia por parejas y atómica (`VERIFICA`).** `AplicarPrioridadPresencial`
   (`GenerarHorarioService.cs:799-815`) deja de asignar `tiposAB[ab++ % 2]` a sesiones sueltas y
   pasa a **emparejar**: dos sesiones elegibles, de duración igual y con requisito de espacio
   compatible, reciben `TipoA`/`TipoB` y **un mismo `PatronAlternanciaId`** (A4). Un candidato sin
   pareja no alterna. En CP-SAT, para cada pareja: mismo `start` y mismo `spaceVar` → alternancia
   atómica por espacio. Nuevo `HC-ALT` en el validador: toda sesión alternada tiene pareja con el
   patrón opuesto, mismo bloque y mismo espacio. Los tres puntos de `VERIFICA` quedan cubiertos por
   esta única pieza.
5. **Petición 8 (backend).** Las sesiones teóricas **sí** se generan hoy (`:588-591`); lo que las
   hace invisibles es que una teoría virtual no tiene `EspacioIdHogar` (`:314-317` sólo lo llena
   desde asignaciones presenciales). Rellenar el hogar desde el requisito de espacio del grupo. La
   parte de UI va en P4.

**Verificación:** tests por restricción, siguiendo el patrón de
`test/SOEA.Tests/Engine/ConstraintProg/MotorConstraintProgrammingTests.cs`. Como mínimo: HC-G01 por
día llega a CP-SAT (hoy **no hay ni un test** que le pase un grupo con disponibilidad); dos sesiones
semanales caen con ≥1 día de separación; una teoría presencial nunca cae en un laboratorio; una
pareja de alternancia comparte espacio y bloque.

---

### P3 — Persistencia inmediata
> Cierra 6 y la mitad de datos de 8.

1. **`POST /api/asignaturas`** ya existe y respeta los 3 tracks (`AsignaturaService.cs:16-27`).
   Ampliar `CreateAsignaturaRequest` con `Categoria` y `Alternancia`, y **llamarlo desde el
   frontend**. Con esto muere la ruta que colapsaba los tracks (R5) — es la causa real de la
   petición 8.
2. **`FacultadesController` y `ProgramasController`** (GET + POST). Hoy los GET viven sueltos en
   `ImportController.cs:98,104` y no hay ningún camino de escritura. Heredan de `BaseRepository`,
   que ya hace `SaveChangesAsync` en cada escritura (`BaseRepository.cs:30,44,53`).
3. **`CatalogoService.guardar()` (B1)** y cableado en los 4 `openDialog().afterClosed()`
   (`asignaturas-tab:128`, `grupo-tab:115`, `espacios-tab:104`, `docentes-tab:112`), que hoy sólo
   tocan memoria. El patrón de referencia ya está en el repo: `alternancia-tab.component.ts:175-189`
   (escritura optimista + estado por fila + snack en fallo).
4. **Borrar:** los 8 botones Guardar/Cargar, los 4 `guardarEnBD()`, y
   `GuardadoResultadoDialogComponent`, que queda huérfano. `POST /import/excel` **se conserva** — es
   el único camino de ingesta desde Excel.

**No se construye** un endpoint combinado asignatura+grupo: el cliente encadena
`POST /api/asignaturas` y `POST /api/grupos`. Añadirlo sólo si hace falta atomicidad real entre
ambos (p. ej. si un fallo del segundo obliga a deshacer el primero).

**Verificación:** `npm test`. Crear una asignatura con teoría presencial + virtual + laboratorio,
recargar la página, y comprobar que los 3 tracks sobreviven — hoy no sobreviven.

---

### P4 — UI unificada
> Cierra 1, 2, 4, 12 y la mitad visual de 8.

1. **Petición 1.** Eliminar `<mat-tab label="Grupos">` (`ingesta.component.ts:34-36`) y convertir cada
   fila de asignatura en desplegable con sus grupos, usando `gruposByAsignatura` (B4). Expansión con
   `@if` + un `Set<string>` de signals — el patrón que ya usan `alternancia-tab:131` e
   `import-resultado-dialog:55`. Sin librería nueva. `GrupoDialogComponent` se reutiliza tal cual:
   ya es standalone y ya recibe `MAT_DIALOG_DATA: Grupo | undefined`.
   Columnas de asignatura: nombre · código · sesiones/semana · programa · **nº de grupos** · acciones.
   Columnas de grupo: nombre · espacio · día · hora · docente · nº estudiantes · disponibilidad · acciones.
2. **Petición 2.** Cápsula `nuevo grupo +` bajo el formulario de `AsignaturaDialogComponent`. El
   diálogo pasa a cerrar `{ asignatura, grupos }`; el padre encadena los dos POST de P3.
3. **Petición 4.** `<app-requisitos-espacio>` (B3) dentro del diálogo de grupo. Se quita el selector
   `espacioFijoId` de la asignatura (`asignaturas-tab:307-310`), que además hoy sólo aparece si hay
   laboratorios.
4. **Petición 12.** `colorDeAsignatura(id)` (B4): hash determinístico del id sobre una rampa fija
   coherente con los tokens de `styles.css:10-56`. El fondo de `.gcell` indica asignatura; el borde
   izquierdo sigue indicando alternancia (`horario.component.ts:262` se conserva). Leyenda en la
   barra superior.
5. **Petición 8 (UI).** Un chip extra `Virtual (sin espacio)` en el selector de espacios
   (`horario.component.ts:104-108`) para que las teorías virtuales dejen de ser invisibles en una
   grilla orientada a espacios.
6. **B2** (`<app-disponibilidad-editor>`) se extrae aquí y lo consumen los diálogos de docente y de
   grupo.

**Verificación:** `npm start`, crear una asignatura con dos grupos en un solo flujo, desplegar la
fila, generar el horario y comprobar colores, chip virtual y que los grupos aparecen bajo su
asignatura.

---

### P5 — Recálculo mínimo al editar
> Cierra 13.

La maquinaria ya existe y no hay que escribir motor nuevo: `sesionesFijas` se traduce a
**igualdades duras** en CP-SAT (`MotorConstraintProgramming.cs:200-205`), congela genes en el GA
(`OperadoresGeneticos.cs:72-76`) y la valida `HC-BASE`.

1. **Arreglar la identidad.** `MapearSesionesFijas` (`GenerarHorarioService.cs:394`) genera GUIDs
   nuevos, así que una sesión fija no se puede corresponder con la que el usuario editó. Conservar
   el `Sesion.Id` de entrada.
2. **`POST /api/horario/reacomodar`**: recibe el horario actual + la sesión editada con su nuevo
   `(día, hora, espacio)`. El servidor fija la editada en su nuevo slot, detecta con
   `ValidadorRestriccionesDuras.DetectarSolapes` (`:147-160`) **sólo** las sesiones que ahora chocan
   con ella, las libera, fija todas las demás y ejecuta el pipeline existente. Con >90 % de las
   variables fijadas por igualdad, CP-SAT resuelve en milisegundos y el resto del horario no se
   mueve.
3. **Frontend:** `EditarSesionDialogComponent.commitLocal()` (`horario.component.ts:638-649`) pasa de
   mutar memoria a llamar al endpoint y refrescar desde la respuesta.

**Verificación:** test de integración — mover una sesión y comprobar que ninguna sesión no
conflictiva cambia de bloque ni de espacio.

---

## Verificación de extremo a extremo

```bash
dotnet build SOEA.sln && dotnet test SOEA.sln
```

```bash
npm --prefix frontend/soea-angular test
```

Prueba manual completa, con backend en `http://localhost:5066` y frontend en `http://localhost:4200`:

1. Crear facultad, programa y 2 espacios (1 laboratorio, 1 salón) — cada "Aceptar" debe persistir sin
   ningún botón de guardar.
2. Crear una asignatura con teoría presencial (2 ses/sem), teoría virtual (1) y laboratorio (1), y en
   el mismo flujo dos grupos con disponibilidades y requisitos de espacio distintos.
3. Recargar el navegador: los 3 tracks, los 2 grupos, sus disponibilidades y sus requisitos siguen ahí.
4. Generar el horario y comprobar: ninguna sesión fuera de la disponibilidad declarada; las 2 sesiones
   semanales con ≥1 día de separación; la teoría presencial en el salón, nunca en el laboratorio; la
   teoría virtual visible en el chip `Virtual`; cada asignatura con su color; las parejas de
   alternancia compartiendo espacio y bloque.
5. Editar una sesión en la vista de horario: sólo esa sesión y sus conflictos se mueven.

---

## Riesgos y límites conocidos

- **Escala de CP-SAT.** Multi-grupo multiplica variables y intervalos. `CpSat:TimeoutSegundos`
  (`appsettings.json`) ya es configurable; medir con datos reales antes de dar P2 por cerrado.
- **La migración de P1 borra `asignatura.espacio_fijo_id`.** El copiado a los grupos debe ir en la
  misma migración y **antes** del `DROP`.
- **`MotorConstraintProgramming.cs:460-463` devuelve `MotivoInfactibilidad.Espacio` siempre**, aunque
  la causa real sea otra, lo que hace que el bucle de cesión de la Etapa 2 se dispare sin motivo.
  Conviene corregirlo dentro de P2; no lo pide ninguna petición, pero enturbia el diagnóstico de
  todas.
- **`GrillaInstitucional`** genera L–V 06:00–22:00 y sábado 06:00–13:00, y su propio comentario
  (`:14-18`) marca el rango real como dato **no confirmado** por la coordinación académica. Sigue
  siendo un dato bloqueante (CLAUDE.md §4) y no se inventa aquí.
- **La disponibilidad del docente sigue fuera del pipeline** (degradada a aviso en la asignación
  posterior). Ninguna de las 14 peticiones la reabre; se deja como está.

## Fuera de alcance deliberado

| Se omite | Cuándo añadirlo |
|---|---|
| Endpoint combinado asignatura+grupo | Cuando haga falta atomicidad real entre ambas escrituras |
| Columna `color` en `Asignatura` | Cuando el usuario quiera elegir el color a mano |
| Solver incremental propio | Cuando fijar por igualdad deje de ser suficientemente rápido |
| Tabla y repositorio para requisitos de espacio | Cuando haya que consultarlos por espacio, no por grupo |
| Calendario de disponibilidad por espacio | Cuando exista el requisito (hoy no está pedido) |
