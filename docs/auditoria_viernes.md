# auditoria viernes - resumen para AI

Fecha: 2026-05-29

Fuentes principales
- [audit/AUDITORIA.md](audit/AUDITORIA.md)
- [audit/REMEDIACION.md](audit/REMEDIACION.md)
- [CLAUDE.md](CLAUDE.md)
- [Status_Task.md](Status_Task.md)

Objetivo
- Darle a una AI una lista priorizada para resolver hallazgos, sin rehacer el diagnostico.
- Respetar Clean Architecture y las reglas no negociables del proyecto.

Resumen ejecutivo (top riesgos)
- P0: credenciales en repo, API publica sin auth, cp_model_debug.txt se escribe siempre, GA puede entregar horarios invalidos, falta validador post-fase-3.
- P0/P1: ImportController mezcla capa API con DbContext y tiene N+1 + SaveChanges multiples.
- P1: UX y accesibilidad frontend casi nula, errores no manejados, URLs hardcodeadas.

## docs/architecture.md

P0
- Sacar credenciales del repo y mover a user-secrets/env vars.
- Agregar auth minima (API key o JWT) y proteger endpoints.
- Apagar escritura de cp_model_debug.txt en produccion.

P1
- Extraer ImportCurriculumService a Application, eliminar acceso directo a DbContext desde controller.
- Agregar middleware global de excepciones con ProblemDetails.
- Definir environment.ts para apiBaseUrl y usarlo en servicios del frontend.

P2
- Alinear versiones de paquetes (Logging.Abstractions) en todos los proyectos.
- Refactor de parseos/mapas duplicados compartidos.

## docs/domain.md

P0
- Validar restricciones duras despues de Fase 3 antes de publicar un horario.
- Evitar publicar horarios con violaciones duras y con sesiones sin bloque/espacio valido.

P1
- Clarificar reglas en codigo para HC-* y SC-* (docstrings o comentarios de negocio).
- Definir semantica explicita cuando un docente es null o sin disponibilidad.

P2
- Revisar setters publicos de Sesion si se necesitan restricciones adicionales fuera del motor.

## docs/algorithms.md

P0
- Reparador GA: no aceptar cromosoma invalido si no se puede reparar.
- Validar HC-I01/HC-S01 despues del GA.

P1
- HC-S03 debe basarse en requiere-lab, no en EspacioId preasignado.
- HC-S01 O(N^2): considerar AddNoOverlap por espacio.
- Random seed inyectable y parametros del GA configurables.

P2
- Parametrizar CP-SAT timeout y pesos de fitness via config.

## Frontend (sin doc dedicada en docs/)

P1
- Accesibilidad basica (aria-label, mat-label, mat-error).
- Manejo uniforme de errores (HttpInterceptor).
- Estados de carga y estados vacios.

P2
- OnPush + trackBy en HorarioComponent.
- Eliminar tipos any, tipar modelos.
- Cache de catalogos en StateService.

## Testing y CI (sin doc dedicada en docs/)

P0
- Tests e2e del pipeline de generacion (WebApplicationFactory o Testcontainers).

P1
- Tests de controllers y LectorExcel con fixtures.
- Configurar tests headless en frontend y agregar CI.

## Comandos de verificacion
- dotnet build SOEA.sln
- dotnet test SOEA.sln
- dotnet run --project src/SOEA.API/SOEA.API.csproj
- cd frontend/soea-angular
- npm ci
- npm test -- --watch=false --browsers=ChromeHeadless
- npm run build

Notas
- Mantener reglas de CLAUDE.md: dependencias API->Application->Domain y motores stateless.
- No asumir datos bloqueantes (capacidad labs, prioridad, lista Tipo A/B, duracion fija).

## Resolución — 2026-05-29 (sin auth, por indicación: login será vía cuenta Microsoft de la uni)

Resuelto en esta pasada (build verde, 159/159 tests):
- **P0.1 Credenciales fuera del repo:** `appsettings.json` con `DefaultConnection` vacío; password movido a `appsettings.Development.json` (gitignored + `git rm --cached`); `SOEABdContextFactory` resuelve la cadena vía env `SOEA_DESIGN_TIME_DB` o el appsettings del API. *Pendiente del usuario:* rotar la contraseña real (quedó en el historial git).
- **P0.2 cp_model_debug.txt:** ahora detrás de `CpSat:ExportarModelo` (default false) vía `CpSatOptions`. Ya no se escribe en cada solve.
- **P0.3 Validación post-generación:** nuevo `ValidadorRestriccionesDuras` (HC-I01 docente / HC-S01 espacio, consciente de duración y semana). `GenerarHorarioService` ya no hardcodea `violaciones=0`; si detecta solapes devuelve `EsFactible=false`. + 4 tests.
- **P1.3 / HC-I02:** `MapearDocentes` solo aplica fallback "todos los bloques" cuando NO hay info de disponibilidad; el motor rechaza (infactible) un docente sin bloques que calcen con la grilla en vez de tratarlo como "sin restricción". (Esto puso en verde el test pre-existente en rojo.)
- **P1.4 Middleware global de excepciones:** `AddProblemDetails()` + `UseExceptionHandler()` + `UseStatusCodePages()`. Más endpoint `GET /api/health`.
- **P1.5 environment.ts:** `environment.ts`/`environment.prod.ts` con `apiBaseUrl`; `fileReplacements` en `angular.json`; servicios usan `environment.apiBaseUrl` (sin URLs hardcodeadas).
- **P1.9 CI:** `.github/workflows/ci.yml` (build+test backend, build frontend).
- **P2.1 (parcial):** `cargarFacultades/cargarProgramas` tipados a `Facultad[]`/`Programa[]`.
- **P2.3 (parcial):** timeout CP-SAT configurable vía `CpSat:TimeoutSegundos`.
- **P2.20 (parcial):** endpoint `/api/health`.

Omitido / pendiente (motivo):
- **P0.5 / 3.2 Auth:** omitido a propósito — la uni usará login con cuenta Microsoft (último paso).
- **P0.4 Reparador GA:** ~~la Fase 3 (Genetic) está OMITIDA en Incremento 1~~ — **Corrección 2026-06-16:** la Fase 3 SÍ está activa en `GenerarHorarioService` (confirmado leyendo `MotorGenetico.cs`); el reparador (`OperadoresGeneticos.Reparar`) sí corre en el pipeline. Lo pendiente real es el Incremento 2: cromosoma con `StartB` independiente para `SinAlternancia` + SC-BAL (ver `docs/algorithms.md`).
- **P1.1 Extraer ImportCurriculumService / N+1 / SaveChanges:** refactor grande (L) sin tests de cobertura; requiere su propia tarea.
- **P1.2 HC-S03 RequiereLaboratorio:** depende de datos bloqueantes (clasificación lab de Rosa) + migración EF.
- **P1.7 Accesibilidad / P1.10-11 Tests front/controllers / P0.6 e2e:** alcance grande, no abordado aquí.
