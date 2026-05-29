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
