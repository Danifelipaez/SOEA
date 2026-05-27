# Auditoría de calidad — SOEA

**Fecha:** 2026-05-21
**Alcance:** backend (`src/`), frontend (`frontend/soea-angular/`), tests (`test/`).
**Producto:** este documento + [REMEDIACION.md](REMEDIACION.md) (plan priorizado).
**Estado del baseline:**
- `dotnet build SOEA.sln` → OK con 5 warnings CS8600 (nullable).
- `dotnet test SOEA.sln` → **135/135 PASS**, 2.0 s.
- `npm test` (frontend) → falla por `--run` no soportado por `ng test`, pero el wrapper existe; los specs reales son solo `app.spec.ts`.

Severidades: **Crítica** (vulnerabilidad explotable, bug de correctitud, o bloqueante de producción) · **Alta** (deuda operacional con impacto cercano) · **Media** (mantenibilidad) · **Baja** (cosmético/nice-to-have).

---

## 1. Calidad de código, arquitectura y lógica de negocio

### 1.1 Clean Architecture y separación de capas

- **[Alta] `ImportController` inyecta `SOEABdContext` directamente** — [src/SOEA.API/Controllers/ImportController.cs:13-15](src/SOEA.API/Controllers/ImportController.cs#L13-L15). El controller habla EF Core directamente, salta la capa Application y los repositorios. Viola la dirección de dependencia declarada en `CLAUDE.md` (`API → Application → Domain ← Infrastructure`). Recomendación: extraer un `ImportCurriculumService` en `SOEA.Application/Features/Import/`.
- **[Media] DTOs definidos dentro del controller** — [src/SOEA.API/Controllers/ImportController.cs:316-395](src/SOEA.API/Controllers/ImportController.cs#L316-L395) y [src/SOEA.API/Controllers/DocentesController.cs:11-18](src/SOEA.API/Controllers/DocentesController.cs#L11-L18). Mezcla niveles. Mejor en `SOEA.Application/.../Requests` y `.../Responses` (como ya se hace para Asignaturas y Horario).
- **[Baja] Naming inconsistente repositorios**: `AsignaturaRepository` vs `HorarioRepositorio`/`DocenteRepositorio` (mix EN/ES). Ver [src/SOEA.Infrastructure.Data/Repositories/](src/SOEA.Infrastructure.Data/Repositories/).

### 1.2 Métodos largos / God methods

- **[Crítica] `ImportController.ImportCurriculum` — 270 líneas** — [src/SOEA.API/Controllers/ImportController.cs:35-306](src/SOEA.API/Controllers/ImportController.cs#L35-L306). 7 bucles secuenciales con `SaveChangesAsync` entre cada uno, mapeo DTO→entidad, transacción manual, validación de existencia con consultas case-insensitive client-side. Imposible de testear unitariamente, difícil de mantener. Necesita ser un servicio de Application con etapas separadas.
- **[Alta] `LectorExcel`** total 578 líneas con métodos individuales >100 líneas — [src/SOEA.Infrastructure.Excel/LectorExcel.cs](src/SOEA.Infrastructure.Excel/LectorExcel.cs). Tres métodos públicos que mezclan parseo + construcción de entidades + deduplicación.
- **[Alta] `GenerarHorarioService.MapearDocentes` — 91 líneas** — [src/SOEA.Application/Features/Horario/GenerarHorarioService.cs:138-229](src/SOEA.Application/Features/Horario/GenerarHorarioService.cs#L138-L229). Mezcla: parseo de disponibilidad, parsing de franja horaria por string (`StartsWith("Matutino")`), filtrado de bloques, fallback "permitir todos" cuando hay 0 bloques.
- **[Media] `GenerarHorarioService.MapearSesionesIniciales` — 84 líneas** — [src/SOEA.Application/Features/Horario/GenerarHorarioService.cs:244-327](src/SOEA.Application/Features/Horario/GenerarHorarioService.cs#L244-L327). Round-robin de docentes, parseo de alternancia, derivación de duración por sesión. Múltiples responsabilidades.

### 1.3 Manejo de errores

- **[Alta] No hay middleware global de excepciones** — [src/SOEA.API/Program.cs](src/SOEA.API/Program.cs). `HorarioController` y `ImportController` tienen try/catch propios; `AsignaturasController`, `DocentesController`, `EspaciosController` solo capturan `ArgumentException`. Otras excepciones (DB, null refs) burbujean como 500 sin formato consistente. Agregar `app.UseExceptionHandler()` o un `ProblemDetails` middleware.
- **[Media] `catch` silencioso de JSON malformado** — [src/SOEA.API/Controllers/DocentesController.cs:91-92](src/SOEA.API/Controllers/DocentesController.cs#L91-L92). `catch { /* ignore malformed stored JSON */ }` sin logging. Si la BD tiene JSON corrupto, se devuelve `disp = null` y el frontend muestra docentes sin disponibilidad sin saber por qué.
- **[Media] HC-I03 lanza excepciones a través de `return ResultadoFactibilidad`** — [src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs:147-176](src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs#L147-L176). Mezcla pre-validación con resolución; la API no diferencia "infactible matemáticamente" de "infactible por validación previa".

### 1.4 `async`/`await` mal aplicado

- **[Media] `Task.FromResult` en 3 motores** — ([src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs:35](src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs#L35), [src/SOEA.Engine.Genetic/MotorGenetico.cs:44](src/SOEA.Engine.Genetic/MotorGenetico.cs#L44), [src/SOEA.Engine.GraphColoring/AgendadorColoracionGrafo.cs](src/SOEA.Engine.GraphColoring/AgendadorColoracionGrafo.cs)). Los motores son CPU-bound síncronos envueltos como `Task`. Bajo carga, un request bloquea el thread pool durante minutos (CP-SAT puede ir hasta 120 s). Usar `Task.Run` o exponer endpoint con cola asíncrona / background job.

### 1.5 Correctitud algorítmica de los 3 motores

#### Fase 1 — Graph Coloring (Welsh-Powell)
- **[Alta] Sesiones sin bloque asignado pasan a Fase 2 con `BloqueTiempoId = Guid.Empty`** — [src/SOEA.Engine.GraphColoring/AgendadorColoracionGrafo.cs](src/SOEA.Engine.GraphColoring/AgendadorColoracionGrafo.cs). Cuando Welsh-Powell no encuentra color para una sesión la deja sin bloque; el código asume que CP-SAT lo resolverá, pero en `MotorConstraintProgramming` la sesión recibe variable `IntVar(0, bloques.Count-1)` sin warm-start hint (línea 86-101 no agrega hint cuando `BloqueTiempoId == Guid.Empty`). El comportamiento es correcto pero no está documentado y el lector puede pensar que es un bug.

#### Fase 2 — CP-SAT
- **[Crítica] Escritura a disco en cada solve: `cp_model_debug.txt`** — [src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs:240](src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs#L240). `File.WriteAllText("cp_model_debug.txt", model.Model.ToString())` se ejecuta en cada request. Llena el disco de la API en producción y expone el modelo (que contiene IDs de docentes/sesiones). Mover detrás de `IHostEnvironment.IsDevelopment()` o `ILogger.LogTrace`.
- **[Alta] HC-S01 (no room overlap) es O(N²)** — [src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs:181-207](src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs#L181-L207). Crea `sameTime`/`sameSpace` BoolVars para cada par de sesiones. Para 200 sesiones son ~40 000 pares × 2 BoolVars × 2 reified constraints. CP-SAT lo maneja, pero el modelo crece innecesariamente. Solución estándar: `AddNoOverlap` por espacio o variables intervalo por sesión.
- **[Alta] HC-S03 solo se aplica si la sesión ya viene con `EspacioId` apuntando a un Laboratorio** — [src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs:213-235](src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs#L213-L235). Si una sesión REQUIERE laboratorio pero no viene preasignada (es lo normal), la restricción NO se aplica. Falta una marca en la sesión/asignatura como `RequiereLaboratorio` y la condición debería usar esa marca, no `EspacioId`.
- **[Alta] HC-I02 falla silenciosamente** — [src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs:122-145](src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs#L122-L145). Si `docenteDict.TryGetValue` falla o `BloquesDisponibles` está vacío, loguea warning y NO aplica la restricción. La sesión queda libre de asignarse a cualquier bloque. Combinado con [GenerarHorarioService.cs:213-214](src/SOEA.Application/Features/Horario/GenerarHorarioService.cs#L213-L214) que hace fallback a "todos los bloques disponibles" cuando el docente no tiene disponibilidad configurada, el sistema PUEDE generar horarios para docentes que el usuario explícitamente marcó "no disponible" en todos los días. Bug funcional silencioso.

#### Fase 3 — Genetic
- **[Crítica] El reparador puede dejar conflictos sin resolver** — [src/SOEA.Engine.Genetic/OperadoresGeneticos.cs:99-164](src/SOEA.Engine.Genetic/OperadoresGeneticos.cs#L99-L164). El loop de reparación intenta 10 veces buscar bloque/espacio libre; si todas fallan, hace `TryAdd` con el valor inválido (líneas 121, 156). El cromosoma reparado puede VIOLAR HC-I01 o HC-S01. Como el `Horario` final se publica con `violacionesRestriccionesDuras: 0` codificado a mano en [GenerarHorarioService.cs:105](src/SOEA.Application/Features/Horario/GenerarHorarioService.cs#L105), no hay verificación posterior. **El usuario puede recibir un horario que reporta "factible" con conflictos reales.**
- **[Alta] No se re-validan restricciones duras tras Fase 3** — [src/SOEA.Application/Features/Horario/GenerarHorarioService.cs:101-108](src/SOEA.Application/Features/Horario/GenerarHorarioService.cs#L101-L108). El `Horario` se construye con `violacionesRestriccionesDuras: 0` literal. Si la mutación o el reparador rompieron una restricción, no hay forma de saberlo. Debería re-evaluarse con el `EvaluadorFitness` o un validator dedicado.
- **[Media] `Random(42)` semilla fija** — [src/SOEA.Engine.Genetic/MotorGenetico.cs:62](src/SOEA.Engine.Genetic/MotorGenetico.cs#L62). Hace la búsqueda reproducible (bueno para tests) pero PRODUCE el mismo horario subóptimo siempre para la misma entrada. Debe ser inyectable: `Random` real en producción, seed fija solo en tests.
- **[Media] Hiperparámetros hardcodeados como `const int`** — [src/SOEA.Engine.Genetic/MotorGenetico.cs:21-26](src/SOEA.Engine.Genetic/MotorGenetico.cs#L21-L26) (Población 50, MaxGen 200, etc.) y [src/SOEA.Engine.Genetic/EvaluadorFitness.cs:20-22](src/SOEA.Engine.Genetic/EvaluadorFitness.cs#L20-L22) (pesos SC-01=3, SC-06=2, SC-09=1). El `dashboard-developer` del frontend tiene un formulario para ajustar estos valores pero NO viaja al backend (no hay endpoint para configurarlos).
- **[Baja] `poblacion.OrderBy(p => p.fitness).First()`** — [src/SOEA.Engine.Genetic/MotorGenetico.cs:155](src/SOEA.Engine.Genetic/MotorGenetico.cs#L155). O(N log N) cuando `MinBy(p => p.fitness)` es O(N).

### 1.6 Invariantes de dominio

- **[Alta] `Horario.MarcarComoPublicado` solo valida `ViolacionesRestriccionesDuras > 0`** — [src/SOEA.Domain/Entities/Horario.cs:41-47](src/SOEA.Domain/Entities/Horario.cs#L41-L47). No valida que las sesiones tengan `BloqueTiempoId != Guid.Empty` ni `EspacioId` no null para sesiones presenciales. Combinado con 1.5/Fase 3, un horario "publicado" puede ser inválido.
- **[Media] `Sesion.AsignarBloqueTiempo` y `AsignarEspacio` son setters públicos** — entidades del dominio sin validación de coherencia. Los motores los usan repetidamente sin chequear si ya están asignados. Aceptable en el contexto de optimización pero no debería ser la única API pública.
- **[Media] `Asignatura.AsignarDocente(Guid?)`** permite docente null sin validación — el motor lo trata como "elegir cualquiera" pero el dominio no documenta esa semántica.

### 1.7 Duplicación

- **[Media] `MapToDto` repetido en cada controller CRUD** — [DocentesController.cs:86-102](src/SOEA.API/Controllers/DocentesController.cs#L86-L102), `EspaciosController` (análogo), `AsignaturasController` (análogo). Extraer a un `IMapper` o métodos estáticos compartidos.
- **[Media] `ParseTipoEspacio` definido en 3 sitios** — [GenerarHorarioService.cs:388-393](src/SOEA.Application/Features/Horario/GenerarHorarioService.cs#L388-L393), `EspaciosController`, `ImportController`. Diferentes patrones (`switch` vs `=> match`).
- **[Media] `ParseDiaSemana` y conversión Dia ↔ string** repetido en `GenerarHorarioService`, `LectorExcel`, frontend. Centralizar.

### 1.8 Comentarios

- **[Baja] Comentarios "qué hace" en `LectorExcel` y `ImportController`** — `// Facultades`, `// Programas`, etc. son etiquetas evidentes que pueden borrarse o convertirse en `#region`/método extraído.
- **[Media — ausentes]** Las restricciones HC-* y SC-* (HC-I01, HC-S03, SC-09) están solo en CLAUDE.md. Los archivos del motor las mencionan por código sin explicar la regla de negocio. Un docstring al inicio de cada bloque que cite la regla mejora la trazabilidad. Ejemplo positivo: [src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs:105-115](src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs#L105-L115) ya lo hace bien para HC-I01.

---

## 2. Contratos API y alineación frontend ↔ backend

### 2.1 Mapa endpoint ↔ consumidor

| Endpoint | Backend | Frontend | Notas |
|---|---|---|---|
| `POST /api/horario/generar` | [HorarioController.cs:29](src/SOEA.API/Controllers/HorarioController.cs#L29) | [horario-api.service.ts:124](frontend/soea-angular/src/app/core/horario-api.service.ts#L124) | OK |
| `GET /api/asignaturas` | AsignaturasController | [persistencia.service.ts:86](frontend/soea-angular/src/app/core/persistencia.service.ts#L86) | OK |
| `GET /api/asignaturas/{id}` | AsignaturasController | — | **Huérfano** |
| `POST /api/asignaturas` | AsignaturasController | — | **Huérfano**: el frontend solo crea asignaturas via import masivo |
| `DELETE /api/asignaturas/{id}` | AsignaturasController | [persistencia.service.ts:89](frontend/soea-angular/src/app/core/persistencia.service.ts#L89) | OK |
| `GET/POST/PUT/DELETE /api/docentes[/{id}]` | [DocentesController.cs](src/SOEA.API/Controllers/DocentesController.cs) | [persistencia.service.ts:45-60](frontend/soea-angular/src/app/core/persistencia.service.ts#L45-L60) | OK |
| `GET/POST/PUT/DELETE /api/espacios[/{id}]` | EspaciosController | [persistencia.service.ts:65-80](frontend/soea-angular/src/app/core/persistencia.service.ts#L65-L80) | OK |
| `GET /api/facultades`, `GET /api/programas` | [ImportController.cs:20-32](src/SOEA.API/Controllers/ImportController.cs#L20-L32) | [persistencia.service.ts:35-40](frontend/soea-angular/src/app/core/persistencia.service.ts#L35-L40) | OK |
| `POST /api/import/curriculum` | [ImportController.cs:34](src/SOEA.API/Controllers/ImportController.cs#L34) | [persistencia.service.ts:93](frontend/soea-angular/src/app/core/persistencia.service.ts#L93) | OK |
| — | — | — | **Falta `PUT /api/asignaturas/{id}`**: el frontend no puede editar una asignatura sin borrarla y re-crearla |

### 2.2 Alineación de DTOs y casing JSON

- **[INFO — no es un hallazgo] El casing JSON ES coherente: camelCase en ambos lados.** ASP.NET Core 6+ aplica `JsonNamingPolicy.CamelCase` por defecto en `AddControllers().AddJsonOptions(...)` aunque no se configure explícitamente. Verificado en [Program.cs:51-54](src/SOEA.API/Program.cs#L51-L54). Los DTOs C# (PascalCase: `Semestre`, `Asignaturas`, ver [GenerarHorarioRequest.cs](src/SOEA.Application/Features/Horario/Requests/GenerarHorarioRequest.cs)) se serializan automáticamente a camelCase, que es lo que el frontend espera. Reportes anteriores que sugerían "mismatch PascalCase/camelCase" eran falsos positivos.
- **[Media] `mensajeError` del response NUNCA se setea en éxito** — [GenerarHorarioResponse](src/SOEA.Application/Features/Horario/Responses/GenerarHorarioResponse.cs) lo declara opcional; cuando es factible no lo llena. OK funcionalmente pero el frontend en [horario.component.ts](frontend/soea-angular/src/app/features/horario/horario.component.ts) puede leerlo como `undefined`.
- **[Media] `Docente.disponibilidad: any` en frontend** — [models.ts:26](frontend/soea-angular/src/app/core/models.ts#L26). El backend define exactamente `Dictionary<string, DisponibilidadDiaDto>` en [GenerarHorarioRequest.cs:46](src/SOEA.Application/Features/Horario/Requests/GenerarHorarioRequest.cs#L46), pero el frontend renuncia al tipado. Riesgo: cualquier rename en backend pasa silencioso. Tipar con `Record<string, DisponibilidadDia>` reutilizando la interfaz ya existente en `horario-api.service.ts:37`.
- **[Media] `cargarFacultades(): Observable<any[]>` y `cargarProgramas(): Observable<any[]>`** — [persistencia.service.ts:35-40](frontend/soea-angular/src/app/core/persistencia.service.ts#L35-L40). Existen `Facultad` y `Programa` en `models.ts:1-10`. Usarlos.
- **[Media] `importarCurriculum(payload: any)`** — [persistencia.service.ts:93](frontend/soea-angular/src/app/core/persistencia.service.ts#L93). Tipar el payload acorde a `CurriculumExcelDto`.

### 2.3 Status codes y manejo de errores en frontend

- **[Alta] 500 no se muestra al usuario en CRUD de docentes/espacios** — [persistencia.service.ts](frontend/soea-angular/src/app/core/persistencia.service.ts) no tiene `catchError` en sus métodos. Una excepción de servidor llega al componente como `Observable.error` no manejado y se ve como nada (botón "guardar" no hace nada). Centralizar con un `HttpInterceptor`.
- **[Alta] 422 (infactibilidad) se trata como genérico** — [horario-api.service.ts:153-156](frontend/soea-angular/src/app/core/horario-api.service.ts#L153-L156). El backend devuelve `GenerarHorarioResponse` con `EsFactible=false` y `MensajeError`. El frontend lo devuelve como `err.error` y depende del componente formatear bien al usuario; no hay un parser uniforme. Hace que un fallo de motor parezca un error genérico.
- **[Media] 404 no se diferencia en `PUT /api/docentes/{id}`** — el frontend solo recibe error; no hay flujo para "el docente fue borrado por otro usuario". Aceptable para piloto.

### 2.4 Features asimétricas

- **[Media] `POST /api/asignaturas` huérfano** — ver tabla 2.1. Decisión: o exponer creación individual en UI, o borrar el endpoint.
- **[Media] No existe `PUT /api/asignaturas/{id}`** — el frontend no permite editar una asignatura una vez creada (solo borrar y crear de nuevo). Si la UI necesita edición, agregarlo.
- **[Media] No existe `GET /api/horarios/{id}` ni `GET /api/horarios`** — el `Horario` se persiste tras generar pero no se puede listar ni recuperar. El frontend siempre regenera. Si el dashboard-admin necesita historial, falta este endpoint.
- **[Alta] `dashboard-developer` permite ajustar hiperparámetros del GA pero el backend no acepta override** — ver hallazgo 1.5. El formulario es decorativo.
- **[Alta] `dashboard-admin` calcula KPIs con `13 * 6 slots` hardcoded** — el cálculo de ocupación asume grilla fija que no concuerda con [GenerarBloquesTiempo](src/SOEA.Application/Features/Horario/GenerarHorarioService.cs#L333-L358) (16 horas L-V + 7 sábado = 87 slots). Reemplazar por respuesta del backend o derivar de las sesiones.

### 2.5 UX y usabilidad del frontend

- **[Crítica] Cero accesibilidad: 0 ocurrencias de `aria-label`, `aria-*` o `role=` en todo `frontend/soea-angular/src`.** Confirmado por grep. Botones de icono (eliminar, editar) sin texto alternativo: invisibles para lectores de pantalla. Mat-form-fields sin `<mat-label>` consistente.
- **[Alta] Formularios sin `<mat-error>` visible** — los `FormControl` tienen validators (`Validators.required`, etc.) pero el template no expone los errores con `mat-error`. El usuario ve "no pasa nada" cuando el campo es inválido.
- **[Alta] Estados de carga ausentes** — la generación de horario puede durar 120 s+ (timeout CP-SAT). El componente tiene un `ProgressDialogComponent` pero no se observa un indicador para los CRUD (`guardarDocente`, `actualizarEspacio`).
- **[Media] Sin estado vacío** — cuando no hay datos cargados, los componentes muestran tablas vacías sin mensaje "No hay docentes registrados todavía".
- **[Media] URLs duplicadas hardcoded** — `http://localhost:5066/api` en [horario-api.service.ts:80](frontend/soea-angular/src/app/core/horario-api.service.ts#L80) y [persistencia.service.ts:31](frontend/soea-angular/src/app/core/persistencia.service.ts#L31). Sin `environment.ts`/`environment.prod.ts`. Mover a `environments/environment.ts` con `apiBaseUrl`.
- **[Media] 15 `subscribe(` sin verificación de cleanup** — riesgo de memory leaks si los componentes se destruyen mientras la request está pendiente. Usar `takeUntilDestroyed(this.destroyRef)` (Angular 16+) o el patrón `async` pipe.
- **[Media] 18 tipos `: any`** en 6 archivos del frontend. Listado completo arriba (2.2). Cada uno es un punto donde TypeScript no protege contra cambios de schema.
- **[Baja] Mezcla idiomas en UI** — propiedades en español, pero algunos labels en inglés en el código de los componentes.

---

## 3. Seguridad, configuración y performance

### 3.1 Credenciales/secrets en repo

- **[Crítica] Password de PostgreSQL en texto plano en 2 lugares**:
  - [src/SOEA.API/appsettings.json:3](src/SOEA.API/appsettings.json#L3) — `"Password=2356"` en config commiteada.
  - [src/SOEA.Infrastructure.Data/Context/SOEABdContextFactory.cs:11](src/SOEA.Infrastructure.Data/Context/SOEABdContextFactory.cs#L11) — connection string completa hardcodeada para migraciones design-time.
  - Mover a User Secrets (`dotnet user-secrets`) para desarrollo y variables de entorno / Azure Key Vault / similar para producción. El factory de design-time debe leer de `appsettings.Development.json` (gitignored) o env vars, no del código.
- **[Media] Generación de emails sintéticos sin nombre real** — [DocentesController.cs:47](src/SOEA.API/Controllers/DocentesController.cs#L47) `$"{id}@soea.local"` y [GenerarHorarioService.cs:220](src/SOEA.Application/Features/Horario/GenerarHorarioService.cs#L220) `$"docente-{id}@soea.edu"`. Estos correos se persisten en BD pero nunca representan al docente real (la UI no recoge el correo). Si en el futuro se envían notificaciones, irán a buzones inexistentes.

### 3.2 Autenticación y autorización

- **[Crítica] La API está completamente pública.** No hay `[Authorize]` en ningún controller, no hay `AddAuthentication()` ni `UseAuthentication()` en [Program.cs](src/SOEA.API/Program.cs). La línea `app.UseAuthorization()` (línea 67) **NO hace nada útil sin autenticación previa** — es un placeholder. Cualquier cliente puede crear/editar/borrar docentes, espacios, asignaturas y disparar generaciones de horario (que consumen 120 s de CPU). Aceptable solo si el sistema corre en intranet aislada. Para producción: agregar JWT/OAuth2 con roles (Admin/Docente/etc.).

### 3.3 CORS

- **[Media] CORS solo permite `http://localhost:4200`** — [Program.cs:18](src/SOEA.API/Program.cs#L18). Adecuado para desarrollo, pero no hay configuración por entorno. Para staging/prod necesita policy distinta.

### 3.4 Validación de input

- **[Alta] `ImportController.ImportCurriculum` no valida tamaño ni estructura del payload** — [src/SOEA.API/Controllers/ImportController.cs:35](src/SOEA.API/Controllers/ImportController.cs#L35). Un cliente podría enviar millones de Facultades/Programas y agotar memoria. Agregar `[RequestSizeLimit]` y validación con FluentValidation o DataAnnotations.
- **[Alta] `LectorExcel` no valida estructura del Excel** — [src/SOEA.Infrastructure.Excel/LectorExcel.cs](src/SOEA.Infrastructure.Excel/LectorExcel.cs) asume columnas en posiciones fijas (A–J). Un archivo con columnas en otro orden produce datos corruptos en silencio. Verificar headers antes de parsear.
- **[Alta] Excel: no se valida tamaño ni tipo MIME del archivo subido.** EPPlus puede consumir RAM proporcional al archivo. Atacante puede subir XLSX bomba.
- **[Media] DTOs sin DataAnnotations** — `[Required]`, `[Range]`, `[MaxLength]` ausentes en todos los DTOs de Application/API. `[ApiController]` valida ModelState automáticamente, pero como no hay anotaciones, no se valida nada salvo nullability básica.

### 3.5 HTTPS y headers de seguridad

- **[Media] `UseHttpsRedirection()` presente pero sin HSTS** — [Program.cs:66](src/SOEA.API/Program.cs#L66). Para producción agregar `app.UseHsts()` y considerar `X-Content-Type-Options`, `X-Frame-Options`, CSP via middleware o `NWebSec`.

### 3.6 EPPlus / Excel

- **[Alta] `LectorExcel` no fija `ExcelPackage.LicenseContext`.** EPPlus 5+ requiere declarar contexto de licencia (NonCommercial o Commercial). Si EPPlus 8 mantiene este requisito y no está declarado, en algún build futuro fallará. Verificar y declararlo explícitamente. [Versión instalada: EPPlus 8.0.1](src/SOEA.Infrastructure.Excel/SOEA.Infrastructure.Excel.csproj#L8).

### 3.7 Performance backend

- **[Alta] `cp_model_debug.txt` escrito en disco cada solve** — duplicado de 1.5. Riesgo I/O + disco lleno.
- **[Alta] `ImportController.ImportCurriculum` con 7 `SaveChangesAsync` separados** — [src/SOEA.API/Controllers/ImportController.cs](src/SOEA.API/Controllers/ImportController.cs) líneas 76, 117, 147, 165, 217, 261, 278. Cada uno es un round-trip a Postgres. Mejor: 1 sola transacción con un único `SaveChangesAsync` al final (todo está dentro de `BeginTransactionAsync`). Para 10k filas la diferencia es 10× a 100×.
- **[Alta] N+1 en `ImportController.ImportCurriculum`**: `_context.Facultades.FirstOrDefault(x => x.Nombre.ToLower() == f.Nombre.ToLower())` (línea 57 y similares 98, 123, 153, 196, 236) hace 1 query por elemento. Para 1000 asignaturas son 1000 queries. Cargar a memoria una vez, comparar contra `Dictionary`.
- **[Media] `LectorExcel` deduplica en memoria pero no valida tamaño** — para Excels grandes puede consumir mucha RAM antes de fallar.
- **[Media] CP-SAT timeout y GA hiperparámetros no configurables** — duplicado de 1.5. Imposible ajustar sin recompilar.

### 3.8 Performance frontend

- **[Media] `HorarioComponent` (495 líneas, no 900+) sin `OnPush` change detection.** Una matriz drag-drop redibuja todo en cada `setSesiones`. Para 87 slots × 200 sesiones, latencia perceptible.
- **[Media] Llamadas a catálogos (facultades, programas) sin cache** — cada vez que se navega a `/ingesta` se vuelve a pedir. Cachear en `StateService` con invalidación manual.
- **[Media] 15 `.subscribe(` sin `takeUntilDestroyed`** — duplicado de 2.5. Memory leak potencial.

### 3.9 Logging y observabilidad

- **[Media] Solo `ILogger<T>` básico, sin Serilog ni structured sinks.** Logs van a stdout (`appsettings.json` solo configura LogLevel). Para producción agregar Serilog + un sink (Seq/ELK/Datadog) y correlation IDs por request.
- **[Baja] `cp_model_debug.txt` mezcla logging con artefacto físico.**

### 3.10 Dependencias

- EPPlus 8.0.1 — verificar licencia (NonCommercial vs Commercial).
- Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1, EF Core 10.0.8 — actuales.
- Google.OrTools 9.15.6755 — actuales (versión Q4 2024).
- Microsoft.AspNetCore.OpenApi 10.0.7 — actual.
- `Microsoft.Extensions.Logging.Abstractions 10.0.0-preview.4` (en Engine.ConstraintProg y Engine.Genetic) **vs `10.0.8`** en otros proyectos. **[Baja]** Mezclar preview y release. Alinear todos a `10.0.8`.

---

## 4. Testing y cobertura

### 4.1 Inventario backend

`test/SOEA.Tests/` reporta **135 tests pasando**. Distribución verificada:

| Archivo | Cobertura |
|---|---|
| Entities (BloqueTiempo, Docente, Espacio, Grupo, Horario, Sesion) | Invariantes de dominio (~1100 líneas combinadas) |
| ValueObjects (IntervaloTiempo) | OK |
| Engine: MotorConstraintProgrammingTests, OperadoresGeneticosTests, ConstructorGrafoConflictosTests | OK, **pero no cubren los bugs de Fase 3** (1.5) |
| Architecture (NetArchTest) | 5 reglas — capas Clean Architecture |

### 4.2 Gaps backend

- **[Crítica] `GenerarHorarioService.EjecutarAsync` sin tests de integración.** El orquestador de las 3 fases es el código más crítico del sistema y no tiene un solo test end-to-end con datos sintéticos.
- **[Crítica] Ningún test detecta los bugs de 1.5/Fase 3** — un test que tome una salida del GA y verifique HC-I01/HC-S01 detectaría el escape de violaciones.
- **[Alta] Ningún controller tiene tests de integración** (`WebApplicationFactory`). `HorarioController`, `ImportController`, CRUDs.
- **[Alta] `LectorExcel` sin tests con fixtures.** El parseo de Excel es muy propenso a regresión.
- **[Alta] `ImportController.ImportCurriculum` (270 líneas) sin tests** — implícito por punto anterior.
- **[Media] Repositorios sin tests** (no hay InMemory provider ni Testcontainers).
- **[Media] Tests de arquitectura podrían ampliarse**: prohibir que controllers usen `DbContext` directamente (capturaría el bug 1.1 / `ImportController`).

### 4.3 Frontend

- **[Crítica] Solo existe `app.spec.ts`.** 0 specs para componentes complejos (`HorarioComponent` 495 líneas, todos los tabs de Ingesta) ni para servicios (`HorarioApiService`, `PersistenciaService`, `StateService`).
- **[Alta] `package.json` declara test pero `ng test` no acepta `--run`** — la ejecución headless no está configurada. Sin CI no se corren tests en cada commit.
- **[Media] No hay vitest.config ni coverage configurado.**

### 4.4 CI/CD

- **[Alta] No hay `.github/workflows/` propios** (solo en `node_modules`). Sin pipeline que corra `dotnet test` o `ng test` en PRs. Agregar GitHub Actions con jobs para build, test backend, test frontend, build frontend.

---

## 5. Resumen ejecutivo

| Severidad | Calidad/Lógica | Contratos/UX | Seguridad/Perf | Testing | **Total** |
|---|---:|---:|---:|---:|---:|
| Crítica | 3 | 1 | 2 | 3 | **9** |
| Alta | 5 | 5 | 7 | 4 | **21** |
| Media | 13 | 9 | 9 | 3 | **34** |
| Baja | 2 | 1 | 2 | 0 | **5** |
| **Total** | **23** | **16** | **20** | **10** | **69** |

### Hallazgos Críticos (top 9)

1. **`ImportCurriculum` es un God method de 270 líneas con `DbContext` inyectado en controller, 7 SaveChanges y N+1 queries.**
2. **`cp_model_debug.txt` se escribe al disco en cada generación** — leak operacional + posible llenado de disco.
3. **El reparador del GA puede dejar conflictos HC-I01/HC-S01 sin resolver** y el horario se publica con `violacionesRestriccionesDuras: 0` hardcoded — puede emitir horarios inválidos sin alertar.
4. **Password de Postgres commiteada en 2 archivos** del repo.
5. **API completamente pública** — sin autenticación ni autorización, cualquier cliente puede mutar datos y disparar generaciones de 120 s.
6. **Cero atributos de accesibilidad en frontend** — incumple WCAG.
7. **Pipeline end-to-end (`GenerarHorarioService`) sin tests de integración.**
8. **Ningún test detecta los bugs algorítmicos de Fase 3.**
9. **Frontend sin tests** salvo `app.spec.ts`.

### Hallazgos NO confirmados (descartados por verificación empírica)

- "Mismatch PascalCase/camelCase entre backend y frontend" — **falso positivo**. ASP.NET Core MVC aplica camelCase por defecto y el frontend lo recibe correctamente.
