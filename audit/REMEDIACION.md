# Plan de remediación — SOEA

**Insumo:** [AUDITORIA.md](AUDITORIA.md).
**Convención:**
- **P0** — bloqueante de producción (críticas de seguridad o correctitud algorítmica que pueden emitir resultados inválidos).
- **P1** — antes de la siguiente iteración mayor (impacto operacional alto).
- **P2** — backlog técnico (mantenibilidad, deuda agrupada).

Esfuerzo: **S** ≤ 1 día · **M** 1–3 días · **L** 3–8 días · **XL** > 1 sprint.

Las tareas dentro de cada prioridad están ordenadas por dependencias.

---

## P0 — Bloqueantes (atender antes de cualquier despliegue a un entorno compartido)

### P0.1 — Sacar credenciales del repositorio · S
- Quitar password de `appsettings.json` (sustituir por `"DefaultConnection": ""`); commitear `appsettings.Development.json` al `.gitignore`.
- Sustituir `SOEABdContextFactory` para leer la cadena desde variable de entorno (`DOTNET_DESIGN_TIME_DB`) en lugar de literal.
- Configurar `dotnet user-secrets init` en `SOEA.API` y mover la cadena de desarrollo allí.
- **Archivos:** [src/SOEA.API/appsettings.json](src/SOEA.API/appsettings.json), [src/SOEA.Infrastructure.Data/Context/SOEABdContextFactory.cs](src/SOEA.Infrastructure.Data/Context/SOEABdContextFactory.cs), `.gitignore`.
- **Definición de listo:** `grep -ri "Password=2356" .` no devuelve nada; `dotnet ef migrations add ...` y `dotnet run` siguen funcionando con la nueva configuración.
- **Acción adicional:** rotar la contraseña real de Postgres tras el commit (el secret quedó en historial git — considerar `git filter-repo` solo si el repo no se ha compartido).

### P0.2 — Apagar la escritura a disco de `cp_model_debug.txt` en cada solve · S
- Envolver la línea en `if (_env.IsDevelopment())` o detrás de un flag de config `Debug:ExportCpModel`.
- Inyectar `IHostEnvironment` o `IOptions<CpSatOptions>` en `MotorConstraintProgramming`.
- **Archivos:** [src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs:240](src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs#L240).
- **Definición de listo:** levantar la API en Release, generar un horario, verificar que NO se crea `cp_model_debug.txt` en cwd.

### P0.3 — Validar restricciones duras DESPUÉS de la Fase 3 · M
- En `GenerarHorarioService`, antes de crear el `Horario`, recorrer las sesiones finales y contar violaciones reales de HC-I01 (mismo docente, mismo bloque) y HC-S01 (mismo espacio, mismo bloque presencial). Setear `violacionesRestriccionesDuras` con ese conteo (no con `0` literal).
- Si > 0, retornar `EsFactible=false` con `MensajeError` que liste los conflictos.
- Agregar al menos un test que: inyecte sesiones que el reparador no logra arreglar y verifique que el response sea `EsFactible=false`.
- **Archivos:** [src/SOEA.Application/Features/Horario/GenerarHorarioService.cs:101-108](src/SOEA.Application/Features/Horario/GenerarHorarioService.cs#L101-L108), nuevo `ValidadorRestriccionesDuras` (puede vivir en `SOEA.Domain` o `SOEA.Application`), nuevo test en `test/SOEA.Tests/Engine/`.
- **Definición de listo:** el test pasa; la suite total sigue verde.

### P0.4 — Reparador del GA: no `TryAdd` un valor inválido · M
- En `OperadoresGeneticos.Reparar`, si tras 10 intentos no encontró slot libre, retornar `false` o emitir un flag de "reparación incompleta". El motor genético debe descartar ese cromosoma (no añadirlo a la población) o reintentar con nuevo seed.
- Idealmente: aumentar intentos antes de rendirse y/o usar búsqueda exhaustiva en lugar de aleatoria como fallback.
- **Archivos:** [src/SOEA.Engine.Genetic/OperadoresGeneticos.cs:99-164](src/SOEA.Engine.Genetic/OperadoresGeneticos.cs#L99-L164), [src/SOEA.Engine.Genetic/MotorGenetico.cs](src/SOEA.Engine.Genetic/MotorGenetico.cs).
- **Definición de listo:** P0.3 ya no detecta violaciones residuales en casos de prueba típicos; nuevo test para `OperadoresGeneticos.Reparar` con escenario imposible verifica que no se acepta cromosoma inválido.

### P0.5 — Capa de autenticación mínima en la API · L
- Para piloto interno: API Key en header (`X-API-Key`) validada en un `AuthorizationHandler`. Está en `appsettings` y se rota manualmente.
- Para versión final: JWT + `[Authorize]` en todos los controllers, con roles Admin/Docente/Lector según operación.
- **Archivos:** [src/SOEA.API/Program.cs](src/SOEA.API/Program.cs), todos los controllers en [src/SOEA.API/Controllers/](src/SOEA.API/Controllers/), `appsettings.json` con sección `Auth`, nuevo middleware/handler.
- **Definición de listo:** request sin credenciales devuelve 401 en todos los endpoints excepto `/api/health` (si se agrega). Frontend manda el header/token en interceptor.

### P0.6 — Test de integración end-to-end del pipeline · M
- `WebApplicationFactory` con DB InMemory (o Testcontainers Postgres).
- 1 test "golden path": envía un payload de 3 asignaturas, 2 docentes, 2 espacios; verifica respuesta `EsFactible=true`, 0 violaciones (validador P0.3).
- 1 test "infactibilidad": payload con docente sin disponibilidad para sus sesiones; espera 422 con `MensajeError` informativo.
- 1 test "reparación límite": payload diseñado para forzar el branch de P0.4.
- **Archivos:** nuevo `test/SOEA.Tests/Integration/HorarioPipelineTests.cs`.
- **Definición de listo:** los 3 tests pasan; tiempo total < 30 s (CP-SAT timeout reducido a 5 s en tests vía override de config).

---

## P1 — Antes de la próxima iteración mayor

### P1.1 — Extraer `ImportCurriculumService` de `ImportController` · L
- Mover los 270 líneas de `ImportCurriculum` a `SOEA.Application/Features/Import/ImportCurriculumService.cs`.
- El controller queda como un thin wrapper que llama `await _service.ExecuteAsync(dto)`.
- Reemplazar `_context.X.FirstOrDefault(...)` por lookups con `Dictionary` cargado al inicio (elimina N+1).
- Reducir a 1 solo `SaveChangesAsync` al final.
- Mantener la transacción única.
- **Archivos:** [src/SOEA.API/Controllers/ImportController.cs](src/SOEA.API/Controllers/ImportController.cs), nuevo `src/SOEA.Application/Features/Import/`.
- **Depende de:** P0.5 (auth) — para que el endpoint quede protegido al exponerse.
- **Definición de listo:** los tests existentes siguen verdes; un test de integración para `ImportCurriculumService` carga 100 filas y verifica que se hace ≤ 5 round-trips a la BD (medir con interceptor EF).

### P1.2 — HC-S03: marcar asignaturas que requieren laboratorio · M
- Agregar `RequiereLaboratorio: bool` a `Asignatura` (o derivar de `Alternancia == TipoA/TipoB`).
- En `MotorConstraintProgramming`, aplicar HC-S03 cuando la **asignatura** lo requiera, no cuando la sesión venga con `EspacioId` apuntando a un lab.
- **Archivos:** [src/SOEA.Domain/Entities/Asignatura.cs](src/SOEA.Domain/Entities/Asignatura.cs), [src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs:213-235](src/SOEA.Engine.ConstraintProg/MotorConstraintProgramming.cs#L213-L235), migración EF Core.

### P1.3 — HC-I02: rechazar en `GenerarHorarioService` docentes con disponibilidad vacía · S
- Quitar el fallback "todos los bloques" en [GenerarHorarioService.cs:213-214](src/SOEA.Application/Features/Horario/GenerarHorarioService.cs#L213-L214). Si el docente envía `NoDisponible: true` en todos los días, el servicio debe devolver 400 con mensaje claro o un response factible=false.
- **Archivos:** mismo.
- **Definición de listo:** un test envía docente sin disponibilidad y espera 400/422; no recibe un horario que asigna sesiones a ese docente.

### P1.4 — Middleware global de excepciones + `ProblemDetails` · S
- `app.UseExceptionHandler()` con un handler que emite `application/problem+json` con `traceId`.
- Quitar try/catch redundantes de los controllers (los específicos para `ArgumentException` pueden quedar).
- **Archivos:** [src/SOEA.API/Program.cs](src/SOEA.API/Program.cs), controllers.
- **Definición de listo:** una excepción no manejada devuelve un `ProblemDetails` con status 500 y `traceId`; el frontend recibe el mismo schema sea cual sea el error.

### P1.5 — Variables de entorno + `environment.ts` en frontend · S
- Crear `frontend/soea-angular/src/environments/environment.ts` y `environment.prod.ts` con `apiBaseUrl`.
- Reemplazar `'http://localhost:5066/api'` en [horario-api.service.ts:80](frontend/soea-angular/src/app/core/horario-api.service.ts#L80) y [persistencia.service.ts:31](frontend/soea-angular/src/app/core/persistencia.service.ts#L31) por `environment.apiBaseUrl`.
- Configurar `fileReplacements` en `angular.json` si no está.
- **Definición de listo:** `ng build --configuration production` usa la URL de prod; dev sigue funcionando.

### P1.6 — Interceptor HTTP en frontend (errores + auth) · M
- Implementar `HttpInterceptor` que:
  - Mapea errores a un `ErrorService` que muestra snackbar al usuario.
  - Agrega el header de auth (P0.5) si hay token en `StateService`.
- Eliminar el `catchError` ad-hoc de `HorarioApiService.manejarError` (queda solo la lógica específica de 422 ahí).
- **Archivos:** nuevo `frontend/soea-angular/src/app/core/http-error.interceptor.ts`, [horario-api.service.ts](frontend/soea-angular/src/app/core/horario-api.service.ts), [persistencia.service.ts](frontend/soea-angular/src/app/core/persistencia.service.ts), `app.config.ts`.
- **Depende de:** P0.5 (auth) para que el interceptor pueda añadir el header real.

### P1.7 — Accesibilidad básica en frontend (WCAG AA mínimo) · M
- Auditar templates de cada feature y agregar:
  - `aria-label` a botones de icono (delete, edit, settings).
  - `<mat-label>` y `<mat-error>` a todos los `mat-form-field`.
  - `role="region"` y `aria-labelledby` a las secciones principales.
  - Foco visible (CSS) en custom interactive elements.
- **Archivos:** todos los `*.component.html`/templates inline en `frontend/soea-angular/src/app/features/`.
- **Definición de listo:** Lighthouse Accessibility ≥ 90 en `/ingesta`, `/horario`, `/dashboard-admin`.

### P1.8 — Re-validar restricciones HC-I03 (max horas semanales) en GA · M
- Hoy solo se valida pre-solve en CP-SAT. La fase 3 puede mover sesiones y violarla. Agregar verificación en el validador de P0.3.
- **Archivos:** mismo validador de P0.3, posiblemente extendido a HC-I02 y HC-I03.

### P1.9 — Pipeline de CI en GitHub Actions · S
- `.github/workflows/ci.yml` con jobs:
  - `backend`: `dotnet restore`, `dotnet build`, `dotnet test` con coverlet.
  - `frontend`: `npm ci`, `npm run lint`, `npm test` (configurar `ng test --browsers=ChromeHeadless --watch=false`), `npm run build`.
- Bloquear merge a `main` si falla.
- **Definición de listo:** un PR de prueba muestra los 2 checks corriendo.

### P1.10 — Tests para controllers, servicios y `LectorExcel` · L
- `WebApplicationFactory` + Testcontainers Postgres (compartir base entre los tests de integración).
- `LectorExcel`: fixtures `.xlsx` en `test/SOEA.Tests/Fixtures/`, un test por cada método público + casos de columnas faltantes / orden alterado / tipos inválidos.
- Cobertura objetivo: 60% en `SOEA.Application` y `SOEA.Infrastructure.Excel`.

### P1.11 — Specs frontend para servicios y `HorarioComponent` · L
- Configurar `ng test` headless (Karma → Chromium).
- Specs prioritarios:
  - `HorarioApiService`: build payload, manejo 400/422, mapeo de sesiones.
  - `PersistenciaService`: rutas exactas + headers (auth) si P0.5 se hizo primero.
  - `StateService`: CRUD + helpers (`getProgramasByFacultad`, …).
  - `HorarioComponent`: render, drag-drop, validación visual.
- **Definición de listo:** `npm test` corre headless en CI; cobertura ≥ 50% en `core/` y `features/horario/`.

---

## P2 — Backlog técnico

Agrupado por tema. Cada grupo es un ticket que puede tomarse cuando haya capacidad.

### P2.1 — DTOs y modelos compartidos · M
- Mover los DTOs internos de `DocentesController`, `EspaciosController`, `ImportController` a `SOEA.Application/Features/.../Requests` y `.../Responses`.
- Tipar `models.ts:26 disponibilidad: any` con `Record<string, DisponibilidadDia>`.
- Tipar `cargarFacultades(): Observable<Facultad[]>`, `cargarProgramas(): Observable<Programa[]>`, `importarCurriculum(payload: CurriculumImport): Observable<ImportResult>`.
- Considerar generar interfaces TS desde el OpenAPI schema (`nswag` u `openapi-typescript`).

### P2.2 — Endpoints faltantes / huérfanos · M
- Decisión: `POST /api/asignaturas` ¿se borra o se conecta a UI? Probablemente se borra (toda creación es vía import).
- Si se necesita edición de asignaturas en UI: implementar `PUT /api/asignaturas/{id}` + `AsignaturaUpdateService`.
- Implementar `GET /api/horarios` y `GET /api/horarios/{id}` para que el dashboard pueda mostrar historial sin regenerar.

### P2.3 — Configuración de motores via `appsettings` · S
- `CpSatOptions { TimeoutSeconds }` e `GeneticOptions { PopulationSize, MaxGenerations, ConvergenceThreshold, MutationRate, CrossoverRate, FitnessWeights }`.
- Inyectar `IOptions<...>` en los motores; quitar `const int`.
- Exponer un endpoint admin `POST /api/admin/config/genetic` (protegido por auth) para que el `dashboard-developer` realmente impacte el motor.

### P2.4 — Refactor de mapeos duplicados · S
- Extraer `ParseTipoEspacio`, `ParseDiaSemana`, `MapToDto` a clases compartidas (o usar AutoMapper si se acepta la dependencia).

### P2.5 — Random inyectable en `MotorGenetico` · S
- Reemplazar `new Random(42)` por `IRandomProvider` inyectado. Implementación de producción: `Random.Shared`. Implementación de tests: seed fija.

### P2.6 — `MinBy` / colecciones eficientes en motores · S
- `poblacion.OrderBy(...).First()` → `MinBy`.
- `poblacion.Min(p => p.fitness)` cachear en una variable mantenida al insertar.

### P2.7 — Performance HC-S01 en CP-SAT · M
- Refactorizar el O(N²) actual a un patrón de `Interval`+`AddNoOverlap` por espacio. Reduce drasticamente las variables booleanas para problemas grandes.

### P2.8 — `OnPush` change detection + `trackBy` en `HorarioComponent` · S
- Marcar el componente como `ChangeDetectionStrategy.OnPush`; añadir `trackBy` en los `*ngFor` de la matriz.

### P2.9 — `takeUntilDestroyed` en 15 `.subscribe(` · S
- Reemplazar suscripciones manuales por el operador `takeUntilDestroyed(this.destroyRef)` (Angular 16+) o usar el `async` pipe.

### P2.10 — Logging estructurado (Serilog) + correlation IDs · M
- Reemplazar `ILogger` default por Serilog con `RequestLoggingMiddleware` y sink configurable. Útil cuando la API esté autenticada y se necesite auditar quién dispara qué.

### P2.11 — Validación de input (DataAnnotations / FluentValidation) · M
- Anotar todos los DTOs (`[Required]`, `[Range]`, `[MaxLength]`).
- Para `ImportCurriculumDto`: límite de elementos por colección (e.g. 5000 asignaturas máx).
- `[RequestSizeLimit(50_000_000)]` en endpoints que aceptan archivos.

### P2.12 — Validación de Excel · M
- En `LectorExcel`, verificar headers (`Facultad`, `Programa`, …) antes de leer celdas por posición. Si no coinciden, devolver error explícito.
- Definir constante `EXCEL_MAX_SIZE_BYTES` y rechazar si supera.

### P2.13 — Limpiar warnings CS8600 · S
- 5 warnings en `LectorExcel.cs:168` y `ConsoleRunner/Program.cs:72,77,82,102`. Convertir a checks explícitos de null.

### P2.14 — Alineación de versiones de paquetes · S
- `Microsoft.Extensions.Logging.Abstractions` en `Engine.ConstraintProg` y `Engine.Genetic` usa `10.0.0-preview.4`; el resto usa `10.0.8`. Subir a `10.0.8` consistente.

### P2.15 — Tests de arquitectura adicionales · S
- Regla: ningún controller referencia `SOEABdContext` (capturaría P1.1).
- Regla: ningún archivo en `SOEA.Application` referencia `Microsoft.EntityFrameworkCore` directamente.
- Regla: todos los repositorios implementan una interfaz en `SOEA.Domain.Interfaces`.

### P2.16 — Cache de catálogos en frontend · S
- `StateService` mantiene en signal `facultades`/`programas`; sin invalidación automática. Aceptable, pero añadir un botón "refrescar" o TTL para evitar inconsistencia tras un import.

### P2.17 — Mensaje vacío y skeletons · S
- Componentes con tablas: mostrar `<p>No hay X registrados todavía. <a>Importar curriculum</a></p>` cuando lista está vacía.
- Mat-progress-bar/spinner durante peticiones de CRUD.

### P2.18 — Limpieza de comentarios "qué hace" · S
- Eliminar los `// Facultades`, `// Programas`, etc. en `ImportController` y `LectorExcel`. Sustituir por extraer-método si la separación es útil.

### P2.19 — Documentar reglas HC-* y SC-* en código · S
- Añadir XML doc a cada bloque de restricción referenciando la regla (`HC-I02: ...`). Reduce el coste de incorporación de un nuevo dev.

### P2.20 — Endpoint salud + OpenAPI versionado · S
- `GET /api/health` (Microsoft.Extensions.Diagnostics.HealthChecks).
- Versionar OpenAPI (`/openapi/v1.json`) para que el frontend pueda detectar drift.

---

## Verificación global tras remediación

Al cerrar P0 + P1, ejecutar como suite de regresión:

```powershell
dotnet build SOEA.sln
dotnet test SOEA.sln                     # debe seguir 100% verde
cd frontend/soea-angular
npm ci
npm test -- --watch=false --browsers=ChromeHeadless
npm run build
```

Smoke manual del end-to-end:
1. Levantar API y frontend, autenticarse (P0.5).
2. Importar un curriculum de prueba (P1.1).
3. Generar horario (P0.3 + P0.6 garantizan que un horario "factible" es realmente factible).
4. Verificar que `cp_model_debug.txt` NO existe en el cwd de la API (P0.2).
5. Confirmar con `grep` que no quedan credenciales hardcoded (P0.1).
