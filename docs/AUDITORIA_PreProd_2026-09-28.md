# Auditoría pre-producción SOEA — 2026-09-28

**Objetivo.** Decidir si el código actual puede ir a producción, y catalogar redundancia, lógica mala/incoherente y malas prácticas.
**Objetivo de despliegue.** `origin/main` = `c480015`. **Producción hoy** = `b3d67d9` (ver §2).
**Método.** Worktree limpio en `c480015`; API levantada contra `SOEAdb_preprod` (copia de `SOEAdb`, ya eliminada); frontend en el navegador integrado; producción solo con lecturas (§8).
**Etiquetas.** **[vivo]** = reproducido contra un API/BD corriendo · **[código]** = por lectura. Esfuerzo: XS < 1 h · S ≤ ½ día · M ≈ 1 día · L ≥ 2 días.
**No se editó código ni se hizo commit/push/deploy.** Único archivo escrito en el repo: este informe.

---

## 1. Veredicto

### A — Delta `b3d67d9 → c480015` (lo que se desplegaría): **GO**

Un solo commit funcional (`c480015`, pre-chequeo de capacidad de aulas de CP-SAT, +128/−39 y un test). No añade migraciones.

1. **Alcance mínimo y aislado.** Solo cambia el pre-chequeo previo al solve en `MotorConstraintProgramming.cs:139-241`; el contrato con `GenerarHorarioService` (enum `MotivoInfactibilidad`) no cambia.
2. **Compatibilidad de esquema.** El delta no toca datos. Prod ejecuta `b3d67d9`: su DLL contiene `AttachUnchanged` (de `2bd7c6a`) y **no** contiene `NombreClaseEspacio` (de `c480015`) **[vivo]**; y responde 200 en `/api/grupos` y `/api/CriteriosCesionAlternancia`, que ya necesitan el esquema de M14. **No pude leer `__EFMigrationsHistory`** (el clasificador de permisos denegó la conexión a la BD de prod, §8): la versión de esquema queda *inferida*, no medida. Como el delta no tiene migraciones, no bloquea.
3. **Gates verdes.** Build Release 0 errores · 469/469 tests con los 5 `[PostgresFact]` ejecutados · 5/5 de arquitectura · frontend 140/140 (§3).
4. **Sin regresión de factibilidad.** El nuevo criterio (tipo explícito del requisito > default por `TipoSesion`) coincide con `CalculadorEspaciosSesion.CumpleTipo` (`CalculadorEspaciosSesion.cs:52-62`). No encontré caso que el código anterior aceptara y el nuevo rechace.
5. **Riesgo heredado, no introducido.** El pre-chequeo sigue ignorando el `EspacioId` fijo del requisito (**NEW-5**, High): rechaza instancias factibles. Existía antes del delta y el propio commit lo declara "fuera de alcance".

Notas para el despliegue (no son condiciones bloqueantes): publicar desde un worktree limpio con `-r linux-x64`; el deploy es manual (`.azure/deployment-plan.md`); tras desplegar, comprobar por Kudu que la DLL contiene `NombreClaseEspacio`. Aceptar el riesgo de NEW-5 le corresponde a quien despliega (Daniel Paez).

### B — Sistema completo: **NO-GO** (2 bloqueantes abiertos)

1. **SEC-2 (bloqueante).** API pública, anónima, con `DELETE`/`POST` sobre todo el catálogo y el horario, sin límite de tasa; `POST /generar` sin tope superior de carga (CPU de un B1ms). Además HTTP plano (SEC-3). Hoy prod está casi vacío (0 asignaturas, 0 grupos, sin horario), por eso el daño actual es bajo; deja de serlo con el primer uso real.
2. **NEW-2 (bloqueante).** Borrar un espacio usado por el horario responde 204 y deja filas de `AsignacionesSemanales` apuntando a un aula inexistente; el horario vigente sigue mostrándola **[vivo]**. Corrompe el producto principal en silencio.
3. **NEW-3 (High).** El índice único aula/bloque/semana no distingue semestres: generar otro semestre da un 409 genérico **[vivo]**, y el front tiene `"2026-1"` fijo. "Semestre" es hoy solo una etiqueta.
4. **Integridad y validación que mienten (High).** Faltan FKs (DB-6); `/reacomodar` no valida choque de docente (NEW-4, reproducido: mismo docente en dos clases a la vez); el aviso de disponibilidad docente nunca sale (L-2); el editor de grupos borra la disponibilidad importada (L-3).
5. **Errores que el usuario no entiende (High).** El front muestra "Error desconocido" para todo 409/404/500 (NEW-7); la generación concurrente devuelve un 409 genérico (L-9); dependencias con vulnerabilidad alta (SEC-1).

**Para pasar a GO WITH CONDITIONS** (quién acepta: el patrocinador del proyecto, no un técnico): (a) cerrar NEW-2 — FK de `AsignacionesSemanales.espacio_id` y purga por asignación (M); (b) mitigar SEC-2 — `httpsOnly=true`, restricción de acceso (lista de IP o Easy Auth), `SemaphoreSlim(1)` y tope superior en la configuración del GA (S–M); (c) aceptar por escrito NEW-3 ("un solo horario vigente a la vez") o corregirlo; (d) correr el SQL de solo lectura del anexo A.3 en prod. NEW-4, L-2, L-3 y NEW-7 deberían corregirse antes del primer uso real, aunque por regla no sean bloqueantes.

---

## 2. Estado de producción

| Ítem | Resultado | Confianza / evidencia |
|---|---|---|
| Commit desplegado | **`b3d67d9`** | Alta. DLL con `AttachUnchanged` (2bd7c6a) ✔ y sin `NombreClaseEspacio` (c480015) ✔ **[vivo]**. Kudu: último OneDeploy 2026-09-15 17:59 UTC; mtimes de DLL 12:02–12:03: interpretados como hora local (-05:00), coinciden con el commit `b3d67d9` de las 12:02; es una interpretación, lo concluyente es la prueba de símbolos. Kudu no registra SHA. |
| Historial de deploys | 10 registros; los últimos: 09-15 17:59, 09-15 07:58, 09-15 05:52, 09-08, 08-15… | `GET /api/deployments`. |
| Modificación posterior | `az webapp show` → `lastModifiedTimeUtc 2026-09-23T03:35Z` sin deploy asociado en Kudu | Probable cambio de configuración; `az monitor activity-log list` (filtrado a eventos correctos, sin lecturas) no devolvió nada, no atribuible. |
| Migraciones aplicadas | **No medido.** Inferido 23 (hasta `M14_ClavesAjenasSesionesYGrupos`) | `Migrate()` corre al arrancar (`Program.cs:148`) y la app está sana. `.azure/deployment-plan.md` dice 20 (2026-09-08): **desfasado**. |
| Huérfanos (ítem 0.3) | **No medido** (BD de prod inaccesible para mí) | Ver A.3. Indicio: `GET /api/grupos` → `[]`. |
| Catálogos semilla | `CriteriosCesionAlternancia`: **4 filas** ✔ (GET). `TiposAlternancia`: no expuesto por el API, no medido | **[vivo]**. |
| Datos en prod | 0 asignaturas · 0 grupos · 24 docentes · 2 espacios · 9 facultades · 22 programas · `horario/actual?semestre=2026-1` → 404 | Solo conteos, GET. Prod está casi vacío: riesgo de migración de datos bajo. |
| App Service | `httpsOnly=false` · `alwaysOn=false` · FTPS solo · TLS mín. 1.2 · HTTP/2 off · `DOTNETCORE|10.0` · Basic sin slots · sin `healthCheckPath` | `az webapp show/config show`. |
| Ajustes (solo nombres; valores solo los no secretos) | `ConnectionStrings__DefaultConnection`, `ASPNETCORE_ENVIRONMENT=Production`, `AllowedOrigins__0/1` (2 orígenes del Static Web App), `CpSat__SweepGrupos=true`, `CpSat__TimeoutSegundos=120`, `CpSat__ExportarModelo=false`, `WEBSITE_HTTPLOGGING_RETENTION_DAYS` | `az webapp config appsettings list`. |
| PostgreSQL | Flexible B1ms v16 · respaldo 7 días · acceso público habilitado · auth por contraseña **y** AAD | `az postgres flexible-server show`. La lista de reglas de firewall falló por un argumento; no se reintentó. |
| GET en vivo | `/api/health` 200 (0,86 s) · `/api/CriteriosCesionAlternancia` 200 · SWA raíz 200 · Swagger/OpenAPI **404** (no expuestos ✔) · `http://…/api/health` **200 sin redirección** (SEC-3) · CORS: origen ajeno sin `Access-Control-Allow-Origin` ✔, origen del SWA permitido ✔ | **[vivo]**. |

---

## 3. Gates

| Comando (en el worktree `c480015`) | Resultado |
|---|---|
| `dotnet build SOEA.sln -c Release` | 0 errores · 42 advertencias: 12 × CS8629 y NU1903 en 2 paquetes (`Microsoft.OpenApi 2.0.0`; `System.Security.Cryptography.Xml 9.0.3`) |
| `dotnet test SOEA.sln` (con `SOEA_TEST_DB`) | **469 superados, 0 omitidos** (corridas A y B). `BloqueantesPostgresTests` 5/5 ejecutados. **Ojo:** 2 de las 4 corridas completas omitieron esos 5 en silencio (NEW-9) |
| `dotnet test --filter "FullyQualifiedName~Architecture"` | 5/5 |
| `dotnet list package --vulnerable --include-transitive` | Alta: `Microsoft.OpenApi 2.0.0` (API, Tests; GHSA-v5pm-xwqc-g5wc). Alta: `System.Security.Cryptography.Xml 9.0.3` (Excel, Tests; 8 avisos, entre ellos GHSA-37gx-xxp4-5rgx, GHSA-23rf-6693-g89p, GHSA-6588-8gv4-xfgh, GHSA-8q5v-6pqq-x66h, GHSA-cvvh-rhrc-wg4q, GHSA-g8r8-53c2-pm3f, GHSA-mmjf-rqrv-855v, GHSA-w3x6-4m5h-cxqf) |
| `npm ci` | 581 paquetes; 23 vulnerabilidades (13 moderadas, 10 altas) contando dev |
| `npm run build` | OK. Aviso: bundle inicial **506,87 kB** > presupuesto 500 kB; estilos de `horario.component` sobre presupuesto. Sin NG8113 |
| `npm test` (vitest) | 23 archivos · **140/140** |
| `npm audit --omit=dev` | 7 (4 moderadas, 3 altas): `@angular/compiler`, `core`, `common`, `animations`, `router`… (XSS por i18n y bypass de sanitización, `GHSA-jj27-h5hq-8x99`, `GHSA-hh8m-fm6v-7cvg`); arreglo disponible |
| Agente `architecture-guard` | 28 hallazgos (ver §4, filas `ARQ-*`); Domain limpio, dependencias en la dirección correcta, motores sin estado |

---

## 4. Hallazgos

Ordenados por severidad. Cada fila trae evidencia, cómo reproducir y una propuesta de una línea. Nada se corrigió.

### Bloqueantes

| ID | Sev | Categoría | Evidencia | Repro | Fix propuesto | Esf. |
|---|---|---|---|---|---|---|
| **SEC-2** (NEW) | **Bloq.** | Seguridad | **[vivo]** GET anónimos 200 en prod (`/api/grupos`, `/api/docentes`…). **[código]** `Program.cs:158-159` `UseHttpsRedirection` + `UseAuthorization` sin autenticación; ningún `[Authorize]`; sin rate limiting; controllers exponen `DELETE`/`POST`. **[vivo local]** `POST /generar` con `maxGeneraciones=2 000 000 000` y `umbralConvergencia=2 000 000 000`: sin respuesta en 30 s, CPU pegada hasta que el cliente abortó (`MotorGenetico.cs:81-82` solo pone cotas inferiores) | `curl https://soea-api.azurewebsites.net/api/docentes`; en local, script con esa configuración | Restringir acceso (lista de IP en App Service o Easy Auth/clave por header) + tope superior de `TamañoPoblacion`/`MaxGeneraciones`/`UmbralConvergencia` + `SemaphoreSlim(1)` en `/generar` | S–M |
| **NEW-2** | **Bloq.** | Lógica / integridad | **[vivo]** Espacio nuevo, `POST /generar` (28 sesiones lo usan), `DELETE /espacios/{id}` → **204**; en BD 28 filas de `AsignacionesSemanales` con `espacio_id` inexistente y `GET /horario/actual` sigue devolviendo ese `espacioId`. Causa: `SesionCascadeService.cs:27` busca por `Sesion.espacio_id` (el aula *fija*), no por asignaciones; `AsignacionesSemanales.espacio_id` no tiene FK (DB-6). Con un espacio exigido por grupos sí devuelve 409 claro | Crear espacio, generar, borrarlo, consultar `select count(*) from "AsignacionesSemanales" a where not exists(select 1 from "Espacios" e where e.id=a.espacio_id)` | FK `AsignacionesSemanales.espacio_id → Espacios` (RESTRICT) con saneamiento previo, y que `EspacioService.DeleteAsync` bloquee/purgue por asignaciones | M |

### Altas

| ID | Sev | Categoría | Evidencia | Repro | Fix propuesto | Esf. |
|---|---|---|---|---|---|---|
| **NEW-3** | Alta | Lógica / diseño | **[vivo]** `POST /generar` con `semestre:"2026-2"` estando vigente `2026-1` → 409 "Conflicto de datos". Log: `23505 ux_asignacion_semanal_espacio_conflicto`. El índice `(espacio_id, semana, bloque_tiempo_id)` (`AsignacionSemanalConfiguration.cs:67-69`) no incluye horario/semestre y solo se limpia el mismo semestre (`GenerarHorarioService.cs:477-488`). El front fija `'2026-1'` (L-8) | Generar `2026-1`, luego generar `2026-2` | Decidir "un solo horario vigente" (documentarlo y ocultar el semestre) o acotar el índice por `horario_id`; mapear 23505 a mensaje claro | M |
| **NEW-4** | Alta | Lógica (falta validación) | **[vivo]** Mismo docente asignado a dos sesiones (PATCH rechaza el solape con 409 ✔); luego `POST /reacomodar` de una al hueco de la otra, aula y grupo libres → **200, `esFactible:true`, sin advertencia**; ambas quedan el sábado 10:00. `ReacomodarHorarioService.cs:25,36,46` inyecta `IDocenteRepositorio` sin usarlo | Ver arriba | Reacomodar debe pasar por `ValidadorRestriccionesDuras` con HC-I01, como la sesión manual | S |
| **L-2** | Alta | Lógica | **[vivo]** `PATCH /sesiones/{id}/docente` con un docente no disponible ese día → 200 `advertencias: []`. `BaseRepository.GetByIdAsync` usa `FindAsync` (`BaseRepository.cs:34-35`) y `DocenteRepositorio` solo hace `Include` en `GetAll` (`:19`); hay 165 filas de disponibilidad en BD; `AsignarDocenteSesionService.cs:206` salta el chequeo si `BloquesDisponibles` está vacío | PATCH a una sesión del miércoles con un docente de lun/mar | Sobrescribir `GetByIdAsync` con `Include(BloquesDisponibles)` | XS |
| **L-3** | Alta | Incoherencia / pérdida de datos | **[vivo]** el import guarda `"Martes"`, `"Miercoles"` (BD/API). **[código]** el front lee minúsculas: `disponibilidad-editor.component.ts:87,96` (`src[dia]`), `asignaturas-tab.component.ts:212-216`. Con `[defaultNoDisponible]="true"` (`grupo-tab:65`, `asignaturas-tab:546`) y `writeValue` que llama `onChange` (`:105`), abrir el editor deja **todos los días cerrados** y Guardar los persiste. Síntoma **[vivo]**: cada grupo importado sale como "datos incompletos" (filas rojas) | Importar un Excel, abrir un grupo y Guardar | Normalizar claves (minúscula, sin tilde) al importar y al leer | XS |
| **NEW-5** | Alta | Lógica (pre-existente; en el delta) | **[vivo]** Sesión de laboratorio + requisito con aula fija de tipo Salón y 0 laboratorios → **422 "No caben las laboratorios… 0h"**; añadiendo un laboratorio al catálogo el mismo caso da **200 y la sesión queda en el Salón fijo**. `MotorConstraintProgramming.cs:179-181` ignora `EspacioId` para sesiones de laboratorio; `CumpleTipo` (`:54`) acepta el aula fija sin mirar su tipo. El importador escribe justo ese patrón (requisito Laboratorio con `espacioId` de un Salón; visto en los grupos importados y en la BD local). Errata de UI: "las laboratorios" | Ver script D1/D2 en anexo A.4 | Con `EspacioId`, contar la demanda contra el tipo real de esa aula (o excluirla del pre-chequeo); ignorar también `Sesion.EspacioId` de sesiones fijas | S |
| **NEW-6** | Alta | Lógica / pérdida silenciosa | **[vivo]** Excel de 4 filas donde una no trae docente → `gruposCreados:3`, advertencia "sin docente asignado" y `gruposSinDocente:0`. `LectorExcel.cs:246` crea el grupo solo dentro de `if (!string.IsNullOrWhiteSpace(txtDocente))`. Contradice CR-02 (docente opcional) | Importar una fila sin docente | Crear el grupo aunque no haya docente | S |
| **NEW-7** | Alta | Mala práctica (front) | **[código]** `horario-api.service.ts:301-303` relanza `err.error` (objeto) para todo estado ≠ 0/400; `horario.component.ts:902` lee `err.mensajeError \|\| err.message \|\| err.error` → **"Error desconocido"** ante 409/404/500. El cuerpo 409 real es `{title,status}` **[vivo]** | Provocar un 409 (generación concurrente) desde la UI | `throw new Error(mensajeErrorHttp(err))` salvo 422 | XS |
| **NEW-8** | Alta | Incoherencia (front) | **[código]** Cambiar solo la alternancia en el diálogo Editar → `guardar()` → `commitLocal()` (`horario.component.ts:1136,1181-1190`): sin llamada HTTP; se pierde al recargar. Por ALT-05 la alternancia viene de los datos persistidos | Editar una sesión de laboratorio, cambiar "Semana A/B", recargar | Quitar el selector o persistirlo por API | S |
| **L-9** | Alta | Lógica | **[vivo]** 2 `POST /generar` simultáneos, mismo semestre: 3/3 rondas → una 200 y la otra **409 "Conflicto de datos"** | Ver anexo A.4 | `SemaphoreSlim(1)` o `pg_advisory_xact_lock` y mensaje claro | S |
| **L-10** | Alta | Mala práctica | **[código]** relajación + hasta 20 solves de barrido, cada uno con `TimeoutSegundos` (120 s) completo (`MotorConstraintProgramming.cs:658,730-745,786-820`); el presupuesto de 5 min del bucle de cesión solo se revisa entre iteraciones (`GenerarHorarioService.cs:230-234`); todo en un HTTP síncrono (Azure corta a 230 s). Peor caso teórico ≫ 2 000 s. **No reproducido en vivo**: una instancia tipo "casillero" (22 grupos, 21 aulas) se resolvió en 0,2 s | — | Un deadline único (`CancellationTokenSource` con plazo) compartido por todo el pipeline | S |
| **DB-6** | Alta | Integridad | **[vivo]** Faltan FKs: `Asignaturas.programa_id`, `Programas.facultad_id`, `AsignacionesSemanales.{sesion_id,bloque_tiempo_id,espacio_id}`. `POST /asignaturas` con `programaId` inexistente → **201**; `DELETE /facultades/{id}` con programas → **204** y el programa persiste | Ver anexo A.4 | Migración con FKs + saneamiento previo | M |
| **SEC-1** | Alta | Dependencias | **[vivo]** §3: 2 paquetes NuGet y 7 vulnerabilidades npm de prod. `Cryptography.Xml` llega por EPPlus (subida anónima de `.xlsx`) | `dotnet list package --vulnerable` | Actualizar EPPlus/`Cryptography.Xml`, `Microsoft.OpenApi` y Angular ≥ 21.2.20 | S |
| **SEC-3** | Alta | Seguridad | **[vivo]** `httpsOnly=false`; `http://soea-api.azurewebsites.net/api/health` → 200 sin redirección. `UseHttpsRedirection` no actúa en App Service | `curl -i http://soea-api.azurewebsites.net/api/health` | `az webapp update --https-only true` | XS |
| **L-1** | Alta | Mala práctica | **[código]** `POST /generar` usa el catálogo que envía el navegador (`GenerarHorarioRequest.cs:14-22`); IDs inválidos → `Guid.NewGuid()` (`GenerarHorarioService.cs:612`) y, sin FK, la asignación con un aula fantasma se persiste. **[vivo]** un espacio del request que no existe en BD → 409 genérico al persistir | Enviar un espacio con id nuevo | El request lleva `{semestre, horarioBaseId?}` y el servicio lee de repositorios | L |

### Medias

| ID | Sev | Categoría | Evidencia | Fix propuesto | Esf. |
|---|---|---|---|---|---|
| **OPS-1** | Media | Ops | **[código]** `Migrate()` incondicional al arrancar (`Program.cs:148`) sin slots: una migración fallida = crash-loop. `/api/health` es estático (`:163`), no toca la BD. Sin `healthCheckPath`. M14 ya sanea, por eso baja de Alta | Migrar como paso de despliegue (bundle) y health con `CanConnect` | M |
| **SEC-5** (NEW) | Media | Legal | **[código]** `Program.cs:22` `ExcelPackage.License.SetNonCommercialPersonal("SOEA")`: la licencia "Personal" es de uso individual; una universidad debería usar la de organización | Validar con la universidad y usar `SetNonCommercialOrganization(...)` | XS |
| **NEW-9** | Media | Pruebas | **[vivo]** 2 de 4 corridas completas omitieron en silencio los 5 tests de Postgres (mismo entorno, mismo `SOEA_TEST_DB`). Causa probable, no probada: `PostgresPruebas.Resolver` conecta con `Timeout=3` y un `catch` devuelve `null` (`PostgresPruebas.cs:46-57`), así que un arranque lento se convierte en "omitido". **[código]** `ci.yml` no tiene servicio Postgres ni `SOEA_TEST_DB`: en CI esos tests nunca corren. No hay tests de integración de reacomodar ni de asignar docente | Fallar (no omitir) cuando `CI=true`; service container `postgres`; subir el timeout | S |
| **NEW-10** | Media | Lógica | **[vivo]** Una generación infactible devuelve un `horarioId` aleatorio distinto cada vez (`GenerarHorarioService.cs:305,428`); D1–D3 dieron 3 ids | Devolver `null` | XS |
| **L-11** | Media | Incoherencia | **[vivo]** Formatos de error: JSON `{title,status,detail}` sin `type` (GET asignatura 404); `text/plain` (DELETE docente 404, `/generar` sin asignaturas, POST grupos 400); `problem+json` sin `detail` (GET grupo 404); validación de modelo **en inglés** ("The semestre field is required", enum inválido con detalle técnico); subida de 35 MB → mensaje en inglés con "30000000 bytes" | `Problem()` en todos los controllers + `InvalidModelStateResponseFactory` en español | M |
| **NEW-11** | Media | Lógica | **[vivo]** `DELETE /grupos/{id}` de un grupo con sesiones en el horario vigente → 204 y borra en cascada sus sesiones, sin aviso a nivel API (decisión del 2026-09-15; el front sí confirma) | Devolver el número de sesiones purgadas o exigir `?confirmar=true` | S |
| **DUP-1** | Media | Redundancia | **[código]** HC-* reimplementadas: CP-SAT, `ValidadorRestriccionesDuras`, GA, `AsignarDocenteSesionService` (TimeOnly), `ReacomodarHorarioService.Solapa` (`:116`), front (`seSolapanHorarios`). NEW-4 y L-2 son el resultado | Que las operaciones puntuales armen el conjunto y llamen al validador | L |
| **DB-10** | Media | Incoherencia | **[vivo]** 79/79 grupos con 30 estudiantes, 2/2 espacios con capacidad 30, docentes con 40 h y franja Matutino. **[código]** `LectorExcel.cs:239,254-256,280`, `GenerarHorarioService.cs:613`, `ImportController.cs:291`. Viola CLAUDE.md §4 | `NULL` = "sin dato" y reportarlo por nombre | M |
| **L-6** | Media | Incoherencia | **[vivo]** `/revisar`: ocupación 43 % = 167 h / (4 aulas × 96 h); el aula real tiene 88 h/semana (`dashboard-admin.component.ts:169-170`, 16×6). "Presencial/Virtual 78 / 0" con 37 parejas que alternan (deduplica por id). Cuenta 2 aulas sin uso | Exponer la grilla desde el API | S |
| **L-14 / PERF-3** | Media | Mala práctica (front) | **[vivo]** NG0955 en `/catalogo` (`alternancia-tab.component.ts:42` `track grupo.programa`; hay dos "INGENIERIA PESQUERA"). 7 GET en cada entrada a `/horario`; `GET /horario/actual` da 404 cuando no hay horario (en prod hoy) | `track` por id; cargar una vez y refrescar por mutación; responder 204 | S |
| **DB-1/2/3/5/7** | Media | 3FN | **[vivo]** esquema: `Horarios.sesion_ids` es `text` JSON sin FK (DB-2); `Grupos.facultad_id` junto a `programa_id`/`asignatura_id` (DB-3); `Docentes.disponibilidad='[0]'` y `disponibilidad_ui_json` nulo en 24/24, `Grupos` con JSON en `text` (DB-5); sin CHECKs ni únicos por nombre: dos facultades "ZZ-AUDIT"/"zz-audit" → 201 y 201 (DB-7). DB-1 (`Sesiones` vs `AsignacionesSemanales` duplican datos) **[código]** | Plan de migraciones de la auditoría 09-14 | L |
| **ARQ-1…6** | Media | Mala práctica | `architecture-guard` (verificado en parte): lógica de negocio en controllers — `ImportController.cs:97-341` (≈250 líneas, defaults de 30 estudiantes, franjas y `TipoFlujo`) y `:52-56` (lee repos), `GruposController.cs:80-149` (valida y construye entidades; `GrupoService` solo borra), `FacultadesController`/`ProgramasController`/`EspaciosController` (CRUD directo contra repos, id elegido por el cliente); `BaseRepository.cs:27-55` con `SaveChanges` por operación | Crear `FacultadService`, `ProgramaService`, `GrupoService.Crear/Actualizar`, `ImportarCurriculumService.EjecutarDesdeExcelAsync`; servidor genera el `Id` | L |
| **VAL-1** | Media | Lógica | **[código]** El validador ignora en silencio las asignaciones que no resuelve (`ValidadorRestriccionesDuras.cs:126-127`); `Reacomodar` exime por `Bloqueada`, que nunca se activa (`ReacomodarHorarioService.cs:224`; 0 de 78 sesiones **[vivo]**). Las filas de `Horarios` nunca se borran (11 para `2026-1` tras ~10 corridas **[vivo]**) | Registrar/abortar en el validador; eliminar `Bloqueada` o hacerla efectiva | S |

### Bajas

| ID | Categoría | Evidencia / fix corto |
|---|---|---|
| **DB-4 / DB-9 / DB-8** | Incoherencia | **[vivo]** columnas muertas: 0/78 `bloqueada`, `motivo_conflicto` vacío, `Horarios.estado='Borrador'` siempre y `hard_constraint_violations=0`, `Grupos.alternancia='SinAlternancia'` 79/79; 24/24 docentes con correo `@soea.local`. Eliminar en la próxima tanda de migraciones |
| **L-8** | Incoherencia | **[código]** `"2026-1"` en `catalogo.service.ts:212`, `horario-api.service.ts:160,250`, `horario.component.ts:882`; "Ing. Sistemas" fijo en `journey-bar.component.ts:45`. Leer del estado |
| **L-12 / L-13** | Incoherencia | **[vivo]** `POST /espacios` con `"Salon"` → 400 (exige `"Salón"`, mensaje claro); **[código]** `/reacomodar` sigue diciendo "sin cruzar la medianoche…" (`ReacomodarHorarioService.cs:96`). Aceptar ambas grafías |
| **L-7** | Incoherencia | **[código]** el front exige docentes para generar (`horario.component.ts:873`) aunque el backend los sacó del pipeline |
| **UX-1** | UX | **[vivo]** todas las filas del catálogo en rojo (consecuencia de L-3). **[código]** los ✓ del recorrido son `i < activeIdx` (`journey-bar.component.ts:88-90`): marcan posición, no avance real. `window.confirm/prompt` ×3 |
| **DUP-2…8** | Redundancia | **[código]** DUP-2: 5 parsers de día (`DocenteService.DiaKey`, `CrearSesionManualService.cs:176`, `GenerarHorarioService.cs:911`, `DisponibilidadSemanal.cs:167`, `LectorExcel.cs:511`). DUP-4: `Math.Ceiling(DuracionHoras)` en **13** sitios de 10 archivos. DUP-6: `GrupoDto`, `EspacioDto`, `AsignaturaDto`, `RequisitoEspacioDto` duplicados en `API.Controllers` y `Application.Requests`. DUP-3, 5, 7, 8: **no re-contados** en esta pasada (siguen el snapshot) |
| **SOB-1…8** | Código muerto / sobre-ingeniería | **[código]** SOB-1: `POST /import/curriculum` (`ImportController.cs:97-342`) y `importarCurriculum` (`persistencia.service.ts:247`) sin llamadores. SOB-2: `LectorExcel.ParsearBloqueDisponibilidad` muerto (1 referencia); `ComoFranjasCoarse`, `EstablecerFlujo`, `EstablecerPatronAlternancia`, `MarcarComoPublicado`, `ActualizarFitnessScore`, `ActualizarMaximoHoras` y `ExisteAsync` solo los toca la definición o los tests; **`VirtualizarSesion` y `FranjaHoraria` sí se usan → sacarlos de la lista**. `src/SOEA.ConsoleRunner` ya no está en el repo (solo residuo local). SOB-3: `provideCharts`/`chart.js` en `app.config.ts` sin ningún gráfico. SOB-4: dependencias opcionales `= null` en `AsignarDocenteSesionService.cs:35-37`. SOB-5: 5 `SaveChangesAsync` en repos. SOB-7: 159 comentarios "auditoría" en `src`; `GenerarHorarioService` 1191 líneas (`EjecutarAsync` 438) |
| **ARQ-7…** | Arquitectura | `Application.csproj` referencia los 3 proyectos Engine sin usarlos (verificado: 0 `using SOEA.Engine`); `TipoAlternanciaConfig` sin repositorio; los NetArchTest no cubren 10 de 11 repos (`HaveNameEndingWith("Repository")`), ni motores, ni controllers; `MotorGenetico.cs:205-240` muta las `Sesion` de entrada; el motor CP-SAT escribe a disco (`:647`) |
| **Higiene** | Repo | **[código]** versionados: `cp_model_debug.txt` ×2 (≈4,4 MB, con ids de sesión/docente, a pesar de `.gitignore:16-17`), `.claude/settings.json.bak`, `.claude/settings.local.json`, `.gemini/**`; `SOEA.sln` y `SOEA.slnx` duplicados; nombre de clase `AsignaturasController` en archivo `AsignaturaController.cs`; bases locales sobrantes `SOEAdb_audit`, `SOEAdb_m14` |
| **Rendimiento** | Front | **[código]** sin `OnPush` (0 componentes); bundle inicial 506,87 kB > 500 kB; métodos en plantillas; horarios base en `localStorage` (2 usos) |

---

## 5. Ítems de la auditoría 2026-09-14

| Ítem | Estado | Evidencia |
|---|---|---|
| P0-1 M14 sanea antes de las FK | **FIXED (por test)** | Test `P0_1` verde contra Postgres real. La BD local ya tiene M14 aplicada y 0 huérfanos en 13 chequeos; no repetí la migración sobre datos sucios |
| P0-2 Import con columna Espacio | **FIXED** | **[vivo]** import con "Martes"/"Miércoles" y Espacio → 200; 0 sesiones fantasma; test `P0_2` |
| P0-3 Generar con horario base | **FIXED (por test)** | `P0_3` verde. No repetí el caso manualmente |
| P0-4 Doble reserva al crear sesión manual | **FIXED** | **[vivo]** solape parcial de 1 h → 409 con nombres de sesión y aula. El mensaje repite la regla por cada par (verboso) |
| P0-5 Sesiones fantasma | **FIXED** | Test `P0_5` verde; 0 sesiones sin asignación tras el import |
| DB-1, 2, 3, 4, 5, 7, 8, 9 | **OPEN** | §4 |
| DB-6 | **OPEN** | Reproducido; además provoca NEW-2 |
| DB-10 | **OPEN** | §4 |
| L-1 | **OPEN** | §4 |
| L-2 | **OPEN** | §4 |
| L-3 | **OPEN, peor** | El editor destruye la disponibilidad |
| L-4 | **FIXED** | `persistencia.service.ts:270` ya usa `gruposSinDocente` (pero ver NEW-6: el valor nunca es > 0 por Excel) |
| L-5 | **FIXED (queda código muerto)** | `umbralConvergencia: 30` fijo en `horario-api.service.ts:201`; `pesoAlm` en `models.ts:99` |
| L-6 | **PARTIAL** | Grilla del editor corregida; KPI de `/revisar` sigue con 96 h/aula |
| L-7, L-8 | **OPEN** | §4 |
| L-9, L-10 | **OPEN** | §4 |
| L-11 | **PARTIAL** | Existe `GlobalExceptionHandler`; siguen ≥ 4 formatos |
| L-12, L-13 | **OPEN** | §4 |
| L-14 | **OPEN** | NG0955 reproducido |
| DUP-1 | **PARTIAL** | La sesión manual usa el validador; reacomodar y asignar docente no |
| DUP-2, 4, 6 | **OPEN** | Recontados; DUP-3, 5, 7, 8 = snapshot sin re-contar |
| SOB-1, 2, 3, 4, 5, 7 | **OPEN** | §4 (SOB-2 corregida: `VirtualizarSesion`/`FranjaHoraria` se usan) |
| SOB-6, SOB-8 | **PARTIAL / no evaluado** | SOB-8: panel del GA retirado, `localStorage` persiste; SOB-6 (utilidad de la Fase 1) no se midió |
| PERF-1 (memoria) | **No reevaluado** | Sin cambios de cliente que lo invaliden |
| PERF-2 | **OPEN** | Lecturas de tabla completa siguen en `AsignarDocenteSesionService` y otros |
| PERF-3, PERF-4, PERF-5 | **OPEN / PARTIAL** | PERF-3 reproducido (7 GET); PERF-4 sin OnPush; PERF-5: presupuesto sigue excedido, NG8113 resuelto |
| SEC-1 | **OPEN** | §3 |
| OPS-1 | **OPEN** | §4 |
| DOC-1 | **OPEN** | §7 |
| UX-1 | **OPEN** | §4 |

---

## 6. Lista "Listo para entregar cuando…"

| Criterio | Estado |
|---|---|
| Huérfanos = 0 en local y en prod | **Local ✅** (13 chequeos en la copia). **Prod ⏳ no medido** — SQL en A.3 |
| Tests de integración contra Postgres: importar Excel con espacios · generar con y sin base · sesión manual con solape parcial · reacomodar · asignar docente fuera de disponibilidad con aviso | **Parcial.** Existen 5 (`P0_1…P0_5`). Faltan reacomodar y asignar docente (y el aviso hoy no sale: L-2). Además se omiten en silencio (NEW-9) |
| `dotnet build` sin NU1903 · `ng build` en presupuesto y sin NG8113 · consola sin errores ni NG0955 | **❌** NU1903 ×2 · presupuesto excedido (NG8113 ✅) · NG0955 presente |
| Todos los errores como ProblemDetails en español | **❌** L-11 |
| CLAUDE.md refleja el estado real | **❌** §7 |

---

## 7. Desfase de documentación

**`CLAUDE.md` (raíz).**
- "19 migraciones aplicadas" → hay **23** (última `M14_ClavesAjenasSesionesYGrupos`).
- "9 controllers" y luego lista 10 → hay **10**.
- "~257 métodos de prueba en 30 archivos" → **436** atributos en **63** archivos (469 casos).
- "xUnit · NSubstitute · datos en `TestData/`" → no existen; el patrón real son fakes a mano (`test/SOEA.Tests/Fakes/RepositorioFakes.cs`) y EF InMemory / Postgres real.
- "`ILectorExcel` expone tres métodos" → expone **uno** (`LeerCurriculumAsync`).
- La lista de comandos no incluye `SOEA_TEST_DB` (sin ella, 5 tests se omiten).
- El estado "Autenticación JWT … pendiente" contradice "un solo operador sin login": elegir una y añadir la decisión de exposición (SEC-2).

**`.azure/deployment-plan.md`.** Dice "20 migraciones en prod (2026-09-08)": añadir el deploy del 2026-09-15 (`b3d67d9`, 23 migraciones inferidas); documentar que Kudu no guarda el SHA y que la DLL sirve para identificarlo (símbolo `NombreClaseEspacio` = `c480015`); registrar que `lastModified 2026-09-23` no tiene deploy asociado; y añadir la comprobación de `httpsOnly` como pendiente.

**`docs/`.** `algorithms.md` y `domain.md` son la fuente de los IDs HC-*/SC-* del código; `docs/business-rules/*` y `docs/data/*` los usan con otro significado (SC-01, SC-06, SC-09). `docs/PLAN_ENTREGA_Auditoria.md` debe marcar P0-1…P0-5 como cerrados y registrar la consulta 0.3 como pendiente.

**Herramientas.** Los skills `angular-forms`, `angular-signals`, `azure-deploy`, `dotnet-backend-patterns` y `playwright-cli` apuntan a `.claude/skills/…`, que no existe.

---

## 8. Anexo

### A.1 Comandos ejecutados contra producción (todos de solo lectura)

Todos desde `az` (sesión de `dfpaez@unimagdalena.edu.co`, suscripción Azure for Students) y `curl`. Ninguno mutó nada.

| Herramienta | Comando |
|---|---|
| az | `az account show` · `az webapp show -g rg-soea -n soea-api` · `az webapp config show …` · `az webapp config appsettings list …` (solo se imprimieron valores de `CpSat__*`, `AllowedOrigins__*`, `ASPNETCORE_ENVIRONMENT`) · `az postgres flexible-server show -g rg-soea -n soea-pg-srv` · `az monitor activity-log list -g rg-soea` |
| az (token) | `az account get-access-token --resource https://management.azure.com` (para Kudu; no se imprimió) |
| Kudu | `GET /api/deployments` · `HEAD /api/vfs/site/wwwroot/{SOEA.API,SOEA.Application,SOEA.Engine.ConstraintProg,SOEA.Infrastructure.Data}.dll` · `GET` de dos DLL canalizado a `grep -c` **sin guardarlas en disco** (`SOEA.Engine.ConstraintProg.dll`, `SOEA.Infrastructure.Data.dll`) |
| API | `GET` a `/api/health` (https y http; con `Origin` ajeno y del SWA) · `/api/CriteriosCesionAlternancia` · `/api/grupos` · `/api/asignaturas` · `/api/docentes` · `/api/espacios` · `/api/facultades` · `/api/programas` · `/api/horario/actual` (con y sin `semestre`) · `/swagger/index.html` · `/openapi/v1.json` · raíz del SWA |
| **Denegado** | El intento de leer la BD de prod (`az account get-access-token --resource-type oss-rdbms` + `psql` con `default_transaction_read_only=on`) fue **bloqueado por el clasificador de permisos**. No se ejecutó ni se rodeó. Queda pendiente: migraciones aplicadas, consulta de huérfanos y `TiposAlternancia`. |

### A.2 Entorno de pruebas y limpieza

- Worktree `origin/main`@`c480015` (eliminado). API Release en `:5066` con `ConnectionStrings__DefaultConnection` → `SOEAdb_preprod`, `CpSat__SweepGrupos=true`. Frontend `ng serve` :4200 (checkout principal; el frontend es idéntico entre `3a405a7` y `c480015`).
- `SOEAdb_preprod` eliminada. `SOEAdb` intacta (78 sesiones · 79 grupos · 31 asignaturas · 2 espacios · 5 horarios · 23 migraciones, igual que al inicio). API y servidor del preview detenidos.
- No toqué las bases locales `SOEAdb_audit` y `SOEAdb_m14` (previas a esta auditoría).

### A.3 SQL de solo lectura para correr en prod (pendiente de tu decisión)

Conectar con AAD, `sslmode=require`, y forzar `PGOPTIONS="-c default_transaction_read_only=on"`.

```sql
select count(*), max("MigrationId") from "__EFMigrationsHistory";        -- esperado: 23 · 20260912153501_M14_ClavesAjenasSesionesYGrupos
select count(*) from "TiposAlternancia";                                   -- esperado: 3
select count(*) from "CriteriosCesionAlternancia";                         -- esperado: 4 (ya visto por GET)
-- huérfanos: la consulta del anexo de docs/PLAN_ENTREGA_Auditoria.md, más:
select 'asig.sesion_id', count(*) from "AsignacionesSemanales" a where not exists(select 1 from "Sesiones" s where s.id=a.sesion_id)
union all select 'asig.espacio_id', count(*) from "AsignacionesSemanales" a where a.espacio_id is not null and not exists(select 1 from "Espacios" e where e.id=a.espacio_id)
union all select 'asig.bloque_id', count(*) from "AsignacionesSemanales" a where not exists(select 1 from "BloqueTiempos" b where b.id=a.bloque_tiempo_id);
```

### A.4 Resultados E2E (SOEAdb_preprod, API `c480015`)

| # | Escenario | Resultado |
|---|---|---|
| 1 | Import Excel con Espacio y "Martes"/"Miércoles"; abrir grupo y Guardar | **Import ✅ 200** (P0-2). El editor **❌** cierra todos los días (L-3, por código; síntoma en UI vivo: todos los grupos "incompletos"). Además NEW-6 |
| 2 | Generar sin y con horario base | Sin base ✅ 200 (79 sesiones, 0,4–2,2 s). Con base: cubierto solo por `P0_3` (verde), no repetido a mano |
| 3 | Instancia que necesita emparejar | ✅ Con un solo aula: 78 sesiones, 37 parejas, 78 filas `AsignacionesSemanales` (una por sesión, ALT-05), 152 filas en el DTO (74 contrapartes derivadas) |
| 4 | Sesión manual solapada 1 h | ✅ 409 con mensaje en español y nombres (verboso) |
| 5 | Reacomodar al hueco del docente | ❌ 200 sin validar (NEW-4) |
| 6 | Docente fuera de disponibilidad | ❌ 200, `advertencias: []` (L-2) |
| 7 | Solo cambiar alternancia y recargar | ❌ por código (NEW-8) |
| 8 | Borrar espacio con asignaciones | ❌ 204 + 28 huérfanos (NEW-2); con requisitos de grupo ✅ 409 claro |
| 9 | Dos `/generar` simultáneos | ❌ 200 + 409 genérico ×3 (L-9) |
| 10 | Infactible grande con SweepGrupos | No reproducido; 22 grupos/21 aulas → 422 en 0,2 s. Riesgo por código (L-10) |
| 11 | GA con parámetros enormes | ❌ sin respuesta en 30 s; se cancela al abortar el cliente (SEC-2) |
| 12 | Muestreo de errores | ❌ ≥ 4 formatos, validación en inglés (L-11); UI: "Error desconocido" (NEW-7, por código) |
| 13 | `.xls` falso y 35 MB | `.xls` → 400 genérico ✅ (pero el endpoint promete `.xls`, que EPPlus no lee); 35 MB → 400 con texto técnico en inglés; sin `RequestSizeLimit` propio (rige el de Kestrel, 30 MB) |
| 14 | DELETE facultad con programas · POST asignatura con programa inexistente | ❌ 204 y 201 (DB-6); duplicados por mayúsculas aceptados (DB-7) |
| 15 | Recorrer las rutas con la consola abierta | `/catalogo`: **NG0955** ×≥15. `/horario`, `/revisar`, `/publicar`: sin errores. 7 GET al entrar a `/horario` (PERF-3) |
| Delta | D1/D2/D3 (laboratorio + aula fija Salón, 0 laboratorios) | D1 y D2 → 422 (falso rechazo, NEW-5); D3 → 422 correcto. Con un laboratorio en el catálogo, D2 → 200 y la sesión queda en el Salón fijo |

Efectos en la copia (descartables): grupo `c.g[0]` borrado, asignaciones de docente de prueba, 2 espacios y 3 grupos importados "ZZ", varias regeneraciones.

### A.5 Quién hizo qué / lo que no verifiqué

- El reporte de `architecture-guard` se trató como dato. Verifiqué: referencias de `Application.csproj` a los motores, archivos versionados (`cp_model_debug.txt`), `BaseRepository` con `SaveChanges` por operación, controllers de Facultad/Programa/Espacio sin guardas (reproducido en vivo) y la lógica de `ImportController`/`GruposController` (leídos al citarlos). No verifiqué lo demás: `TipoAlternanciaConfig` sin repositorio, huecos de NetArchTest, mutación de `Sesion` en `MotorGenetico`, escritura a disco del motor CP-SAT, `HorarioRepositorio` y el resto de sus filas.
- No re-conté DUP-3, 5, 7 y 8; no evalué SOB-6 ni PERF-1.
- No probé la UI para S7 ni la edición de un grupo importado en el navegador: NEW-8 y la parte destructiva de L-3 son **[código]** (más el síntoma visible en la UI).

---

## 9. Registro de correcciones

El resto del documento es el estado **al momento de la auditoría** (`c480015`); aquí solo se anota lo corregido después. Sin commitear.

### NEW-2 — corregido en código (2026-09-28)

- **Causa:** `SesionCascadeService.EliminarPorEspacioAsync` buscaba solo por `Sesion.espacio_id` (que solo llevan las sesiones fijas/manuales) y `AsignacionesSemanales.espacio_id` no tenía FK.
- **Cambio:** `SesionRepositorio.GetIdsByEspacioIdAsync` incluye las sesiones cuya asignación usa el aula; FK `AsignacionesSemanales.espacio_id → Espacios` (Restrict) en `AsignacionSemanalConfiguration`; migración `M15_FkAsignacionEspacio`.
- **M15 sanea antes de la FK:** borra las sesiones que tengan una asignación con aula inexistente (con todas sus asignaciones) y las asignaciones sin sesión. Corre sola al arrancar (`Migrate()`). En prod no se pudo medir cuántas filas tocaría (lectura de la BD denegada); con prod sin horario debería ser un no-op.
- **Tests (fallan sin el fix, pasan con él):** `NEW2_BorrarEspacioUsadoSoloPorAsignaciones_…` y `NEW2_M15_SaneaAsignacionesConAulaInexistente_…` en `BloqueantesPostgresTests`.
- **Efecto colateral [código]:** con la FK, una generación cuyo catálogo del navegador traiga un aula inexistente falla al guardar con un 409 en vez de dejar huérfanas en silencio (L-1 sigue abierto).
- **No cubre:** el resto de DB-6 (`sesion_id`, `bloque_tiempo_id`, `programa_id`, `facultad_id`).

### SEC-2 — parcialmente mitigado en código; falta la parte de infraestructura

Hecho:
- **Topes del GA** en `GenerarHorarioService.MapearConfiguracion` (población 10–200, generaciones 1–1000, convergencia 1–1000, probabilidades 0–1, pesos 0–100); se valida al entrar a `EjecutarAsync`, antes de CP-SAT. Fuera de rango → 400 con mensaje en español.
- **Una generación a la vez:** limitador de concurrencia global (`Program.cs`, `[EnableRateLimiting("generar")]` en `POST /horario/generar`), sin cola; la segunda responde **429** con "Ya hay un horario generándose…". Cierra también L-9.
- **Frontend:** `manejarError` solo reenvía el cuerpo crudo en un 422; un ProblemDetails (429/409/500) llega como `Error` con su `detail` (cierra la causa de NEW-7 en `horario-api.service.ts`).
- **Tests:** `MapearConfiguracionTests` (9 casos fuera de rango + límite), `SEC2_ConfiguracionDelGaSinTope_…`, `SEC2_SegundaGeneracionSimultanea_…` (deterministas: retienen un candado sobre `Horarios`), spec de 429 en `horario-api.service.spec.ts`.
- **Suite tras los cambios:** backend 482/482 con 0 omitidos; frontend 141/141.

Pendiente (decisión y cambio en prod, no hecho):
- `httpsOnly=true` en la app de App Service (SEC-3).
- Restricción de acceso a la API (lista de IP de la universidad, o Easy Auth). Sin esto la API sigue siendo pública y anónima: los topes limitan el daño de `/generar`, pero `DELETE`/`POST` sobre el catálogo siguen abiertos.
- `/reacomodar` también llama a CP-SAT y no comparte el límite (una sola solución, con timeout).

### NEW-3 — corregido en código (2026-09-28)

- **Decisión:** el generador ya asume varios semestres a la vez (limpia solo las corridas del mismo semestre), así que se corrigió el índice en vez de documentar "un solo horario vigente".
- **Cambio:** `AsignacionesSemanales.horario_id` (nullable, FK Restrict a `Horarios`); el índice único `ux_asignacion_semanal_espacio_conflicto` pasa a `(horario_id, espacio_id, semana, bloque_tiempo_id)`. Migración `M16_AsignacionPorHorario`: rellena `horario_id` desde `Horarios.sesion_ids` (mismo cruce que M14) y crea un índice propio de `espacio_id` para la FK de M15. Escriben el campo `GenerarHorarioService`, `CrearSesionManualService` y `ReacomodarHorarioService` (`AsignacionSemanal.AsignarHorario`).
- **Tests:** generar un segundo semestre con las mismas aulas → 200 y cada semestre conserva su horario; toda asignación persistida (generada y manual) lleva el horario de su corrida; M16 rellena filas existentes y el índice rechaza el duplicado solo dentro del mismo horario.
- **Riesgo conocido:** `horario_id` es nullable; un escritor nuevo que no lo fije dejaría esa fila fuera del índice (un NULL no choca con otro). El test de escritores lo cubre para los tres actuales.
- **No cubre:** el `"2026-1"` fijo del frontend (L-8) — el backend ya admite varios semestres, la UI no ofrece elegirlo.

### NEW-4 — corregido (2026-09-28)

- `ValidadorRestriccionesDuras.ValidarDocentes` (HC-I01, independiente de la semana; fuera de `Validar` a propósito: el docente no es eje de generación, CR-08). `ReacomodarHorarioService` lo aplica sobre el resultado del movimiento y rechaza (`EsFactible:false`, sin persistir) los solapes que el movimiento introduce; uno previo no bloquea mover otra sesión.
- Test: mismo docente en dos sesiones de grupos y aulas distintos, mover una al hueco de la otra → rechazado (falla sin el fix). Más dos unitarios del validador.
- **Nota:** el informe decía "como la sesión manual", pero esa hace su propio chequeo en línea (`CrearSesionManualService`); la duplicación (DUP-1) sigue: ahora son tres sitios (asignar docente, sesión manual, reacomodar). `IDocenteRepositorio` sigue inyectado sin uso en `ReacomodarHorarioService`.

### L-2 — corregido (2026-09-28)

- `DocenteRepositorio.GetByIdAsync` sobrescrito con `Include(BloquesDisponibles)` (con tracking, como `FindAsync`; `BaseRepository.GetByIdAsync` pasa a `virtual`). Tests contra Postgres: docente sin disponibilidad en la franja → advertencia; con ella → sin advertencia (el primero falla sin el fix).
- **Ojo semántico, sin resolver:** `Docente.BloquesDisponibles` se rellena desde el Excel de horario existente y el propio doc de la entidad lo describe como "bloques en los que el docente ya tiene clases". Con la corrección, asignar un docente importado a una sesión fuera de esos bloques avisa "fuera de la disponibilidad declarada"; puede salir seguido. Además el aviso ignora `DisponibilidadUiJson` (lo que la coordinadora edita en la UI). Conviene decidir cuál es la fuente de verdad de la disponibilidad docente antes de mostrar estos avisos como alarma.

### L-3 — corregido (2026-09-28)

- **Backend:** `LectorExcel` guarda las claves de día normalizadas (`lunes … miercoles … sabado`, con `NormalizadorTexto.Normalizar`) en vez del nombre del enum (`Martes`, `Sábado`). Test contra Postgres con filas de miércoles y sábado (falla sin el fix).
- **Frontend:** `catalogo.service.ts` normaliza las claves al mapear docentes y grupos que llegan de la API (`normalizarClavesDia` / `normalizarClavesDiaJson`), así los grupos ya importados en BDs existentes también se leen bien sin migración de datos. Specs añadidos.
- No verificado en el navegador (el mapeo sí, por spec y por el test de import).

### Estado tras este bloque

Backend 491/491 con 0 omitidos; frontend 144/144; `ng build` sin errores (mismas advertencias de presupuesto que en la auditoría). Sin commitear.

### NEW-6 — corregido (2026-09-28)

- `LectorExcel`: el docente es opcional (CR-02). Una fila sin docente ya no se descarta: se crea el grupo (y la sesión predefinida) con `DocenteId` nulo y queda una advertencia que dice que se importó igual. Antes toda la creación estaba dentro del `if` del docente. `ImportarCurriculumService` ya toleraba grupos sin docente, así que `gruposSinDocente` por fin cuenta (L-4).
- Test contra Postgres: Excel con una fila con docente y otra sin → `gruposCreados:2`, `gruposSinDocente:1`, grupo con `DocenteId` nulo (falla sin el fix: 1 grupo).
- **Comportamiento a tener presente:** varias filas sin docente y sin número de grupo, de la misma asignatura y programa, caen en un solo grupo (la clave del grupo usa el docente cuando no hay número de grupo).

### NEW-8 — corregido (2026-09-28)

- **Decisión:** se quita el selector, no se persiste por API. Por ALT-05 la alternancia la fija el generador al emparejar sesiones, y un cambio a mano no podría dejar pareja y aula coherentes (HC-ALT); un endpoint para eso contradiría "emparejar es el único mecanismo".
- `EditarSesionDialogComponent` (`horario.component.ts`): la alternancia de un laboratorio se muestra como texto de solo lectura ("Semanas en que se dicta: Semana A… Se define al generar el horario; no se cambia desde aquí"). Se eliminaron el selector, el parche local en `reacomodar()` y la alternancia/semana de `commitLocal()`; `hayCambios` ya solo mira docente y franja.
- Specs (`editar-sesion-alternancia.spec.ts`): texto sin `.seg-opt`, nada que guardar sin cambios, y guardar solo el docente conserva la alternancia sin llamar a `/reacomodar`.
- **Sin cambio:** el diálogo "Agregar clase" (`CrearSesionDialogComponent`) sigue ofreciendo alternancia y esa sí viaja al servidor (`CrearSesionManualService`); una sesión manual TipoA/TipoB sin pareja es un caso que conviene revisar con ALT-05.
- No verificado en el navegador; los specs renderizan el componente real.

### Estado tras este bloque

Backend 492/492 con 0 omitidos; frontend 147/147; `ng build` sin errores. Sin commitear.

### SEC-1 — corregido (2026-09-28)

**NuGet (2 paquetes, ambos transitivos):**
- `Microsoft.OpenApi` 2.0.0 → **2.7.5** (GHSA-v5pm-xwqc-g5wc): referencia directa en `SOEA.API.csproj`, en la línea 2.x que usa `Microsoft.AspNetCore.OpenApi` 10.0.
- `System.Security.Cryptography.Xml` 9.0.3 → **10.0.12** (8 avisos altos; para la línea 10 el último se corrige en 10.0.10): referencia directa en `SOEA.Infrastructure.Excel.csproj` (lo trae EPPlus 8.0.1, y ese proyecto lee Excel subido por cualquiera). No se tocó EPPlus.
- `dotnet list package --vulnerable --include-transitive`: 0 proyectos con vulnerabilidades; el build ya no emite NU1903.
- Comprobado en vivo: la API en Desarrollo sigue generando `/openapi/v1.json` con la nueva `Microsoft.OpenApi` (200, OpenAPI 3.1.1, 26 rutas, 39 esquemas). Las pruebas de Excel (import con EPPlus) pasan con la nueva `Cryptography.Xml`.

**npm (7 de producción, más 11 de herramientas de desarrollo):**
- Los diez paquetes `@angular/*` de runtime y toolchain pasan a **21.2.24** (`^21.2.24` en `package.json`); hay que subirlos juntos porque `compiler-cli` y los `@angular/*` se fijan entre sí a una versión exacta. Luego `npm audit fix` (22 paquetes de herramientas —vitest, postcss, browserslist, tar, undici…—, todos subidas menores/parche dentro de sus rangos).
- `npm audit` (completo): **0 vulnerabilidades**. `npm ci` desde el lockfile funciona.
- El bundle inicial pasa de 506,87 kB a 510,01 kB (+3 kB por Angular 21.2.24); el presupuesto de 500 kB ya estaba superado desde la auditoría.
- Nota: `npm install --legacy-peer-deps` dejó el lockfile sin `@emnapi/wasi-threads` y `npm ci` fallaba; una `npm install` normal posterior lo corrigió.

**Pendiente:** prod sigue con las versiones anteriores hasta el próximo despliegue; después de desplegar conviene confirmar por Kudu que las DLL traen `System.Security.Cryptography.Xml` 10.0.12 y `Microsoft.OpenApi` 2.7.5.

### Estado tras este bloque

Backend 492/492 con 0 omitidos; frontend 147/147; `ng build` sin errores. Sin commitear.

### DB-6 — corregido en código (2026-09-29)

- **FK que faltaban** (migración `M17_ClavesAjenasCatalogoYAsignaciones`; `espacio_id` y `horario_id` ya las tenían por M15/M16):
  - `Programas.facultad_id → Facultades` (Restrict).
  - `Asignaturas.programa_id → Programas` (Restrict).
  - `AsignacionesSemanales.sesion_id → Sesiones` (**Cascade**: la asignación es la materialización de la sesión y no tiene sentido sin ella).
  - `AsignacionesSemanales.bloque_tiempo_id → BloqueTiempos` (Restrict) más su índice.
- **Saneamiento previo (solo datos derivados de una corrida):** se borran las sesiones con una asignación en un bloque inexistente (mismo criterio que M15) y las asignaciones sin sesión o sin bloque. Programas sin facultad y asignaturas sin programa son catálogo y NOT NULL: no se borran ni se inventa un padre; la FK falla nombrando la tabla y la restricción (como M14).
- **Comprobado antes de escribirla:** prod (por GET a `/api/facultades` y `/api/programas`): 9 facultades, 22 programas, 0 programas con facultad inexistente, y 0 asignaturas (auditoría), así que M17 no debería fallar allí; `SOEAdb` local (SELECT, en M14): 0 huérfanos de los cuatro tipos. Lo que M17 tocaría en las asignaciones de prod sigue sin medirse (lectura de la BD denegada).
- **Mensajes claros en vez de 204/201/409 genérico:** `FacultadService.DeleteAsync` (409 "tiene N programa(s)…") y `ProgramaService.DeleteAsync` (409 "tiene N asignatura(s) y M grupo(s)…") reemplazan a los controllers que borraban sin mirar; `AsignaturaService` responde 400 "El programa indicado no existe…" al crear/editar con un programa inexistente (dependencia opcional, como en `AsignarDocenteSesionService`).
- **Tests contra Postgres (los tres de API fallan sin el fix):** borrar facultad con programas → 409 y sin programas → 204; borrar programa con asignaturas → 409; crear asignatura con programa inexistente → 400; M17 sanea huérfanos, las cuatro FK rechazan referencias inexistentes y borrar una sesión arrastra sus asignaciones. Las migraciones de P0_1, M15 y M16 ya atraviesan también M17.
- **No cubre:** DB-1, 2, 3, 5, 7 (esquema/3FN: `Horarios.sesion_ids` en JSON sin FK, `Grupos.facultad_id` redundante, JSON en `text`, únicos por nombre) ni DB-10 (valores inventados por defecto).

### Estado tras este bloque

Última corrida completa de la suite (antes de añadir los 4 tests de DB-6, con M17 y los servicios ya aplicados): backend 492/492 con 0 omitidos. Los 4 tests nuevos pasan por separado (4/4). **La corrida completa final quedó sin ejecutar** (denegada por el clasificador de permisos); se espera 496/496. Frontend sin cambios en este bloque (147/147). Sin commitear.

### L-10 — corregido en código (2026-09-29)

- **Causa:** relajación + hasta 20 solves de barrido + el bucle de cesión, cada solve con su `CpSat.TimeoutSegundos` (120 s) completo; el presupuesto de 5 min del bucle solo se miraba entre iteraciones (un solve en curso lo sobrepasaba); todo dentro de un HTTP síncrono que Azure corta a los 230 s.
- **Cambio (`GenerarHorarioService`):** `EjecutarAsync` crea un `CancellationTokenSource` enlazado al del cliente con `CancelAfter(plazo)` y pasa ese token a las tres fases. El motor CP-SAT ya cancelaba su solver con `StopSearch()` y el GA ya comprobaba el token por generación, así que el plazo interrumpe el solve en marcha. **Plazo por defecto: 200 s** (`PlazoTotalPorDefecto`, deja ~30 s para persistir y responder); parámetro opcional del constructor para tests. Se retiró el presupuesto propio de 5 min del bucle de cesión, que el plazo deja sin efecto.
- **Qué recibe el usuario al vencer el plazo:**
  - Antes de tener solución de Fase 2 (Fase 1/2, relajación, barrido, cesión): respuesta 422 con `motivoInfactibilidad:"Timeout"` y mensaje "La generación superó el tiempo máximo (200 s)… reduzca el número de asignaturas o grupos"; no se persiste nada. El frontend ya traduce `Timeout`.
  - Durante la Fase 3 (optimización): no se pierde el trabajo; se publica la solución de Fase 2 sin optimizar, con un aviso en el log (mismo camino que el fallback por violaciones; se restaura el estado de alternancia previo a la Fase 3).
  - Si cancela el cliente, sigue siendo una cancelación (499), no un timeout.
- **Tests (sin BD):** `GenerarHorarioPlazoTotalTests` — plazo agotado en Fase 2 → Timeout claro y nada persistido; plazo agotado en Fase 3 → 200 con la solución de Fase 2; cancelación del cliente no se disfraza de timeout. Los dos primeros fallan sin `CancelAfter` (comprobado, con guarda de 30 s).
- **Límites conocidos:**
  - El plazo no es configurable por `appsettings` (constante + parámetro de constructor); si hiciera falta afinarlo por instalación, exponerlo como `CpSat`/`Generacion` en `Program.cs`.
  - Si el plazo vence durante la relajación o el barrido diagnóstico de una infactibilidad ya probada, se reporta `Timeout` en vez del diagnóstico (se pierde la explicación, no el resultado).
  - Se probó con dobles que no terminan, no con un solver real saturado (una instancia pequeña no reproduce el problema).
  - `/reacomodar` sigue con un solo solve acotado por `CpSat.TimeoutSegundos`, sin plazo propio.

### Estado tras este bloque

Sin BD: 479/479 (incluye los 3 tests nuevos). Con Postgres: los 4 tests de DB-6 y, antes de DB-6/L-10, la suite completa (492/492); **no se ha vuelto a correr la suite con Postgres después de los cambios de L-10** (la corrida completa con SOEA_TEST_DB fue denegada por el clasificador). Esperado en total: 499. Frontend sin cambios (147/147). Sin commitear.

### Lote de pendientes (2026-09-29)

**Nota sobre la rama.** `main-local` **no contiene** `c480015` (el delta auditado está solo en `origin/main`; el clasificador de permisos denegó aplicarlo con `git cherry-pick -n`, así que no se hizo). Por eso el pre-chequeo de esta rama es el anterior a ese commit y los arreglos de abajo se escribieron sobre él. **Al fusionar `origin/main` habrá un conflicto en `MotorConstraintProgramming.cs` y su test**: conviene quedarse con la versión de esta rama (cubre también lo que arreglaba `c480015`) y conservar sus tests `M1bis`.

**Corregidos en código:**
- **NEW-5:** el pre-chequeo de capacidad mandaba cada sesión a la bolsa "laboratorios" o "salones" solo por su `TipoSesion`. Ahora usa los espacios que la sesión realmente puede usar (aula fija o `TipoEspacio` explícito del requisito de grupo, con la misma prioridad que el modelo); una sesión de laboratorio con aula fija de tipo Salón ya no se rechaza en falso con "0h". Se mantienen las dos bolsas y sus mensajes (un primer intento de agrupar por conjunto exacto de candidatos rechazaba antes de llegar al barrido diagnóstico que nombra a los grupos responsables; se descartó). Tests `M1bis_…` y `NEW5_…` (fallan con el pre-chequeo anterior).
- **NEW-10:** `GenerarHorarioResponse.HorarioId` es `Guid?`; una generación fallida (infactible, plazo agotado) devuelve `null` en vez de un Guid aleatorio.
- **L-12 / L-13:** `POST /espacios` acepta `Salon`/`Salón` en cualquier capitalización; los dos mensajes de "cruzar la medianoche" dicen ahora que la sesión terminaría después del cierre del día.
- **NEW-9:** si `SOEA_TEST_DB` está definida y el servidor no responde, las pruebas de Postgres **fallan con un mensaje claro** (5 intentos de 10 s) en vez de omitirse; sin variable siguen omitiéndose. `ci.yml` levanta un contenedor `postgres:16` y define `SOEA_TEST_DB`. (No verificable hasta que corra CI en GitHub.)
- **SOB-1:** eliminado `POST /import/curriculum` y sus ≈250 líneas de lógica en el controller (+ DTOs y helpers, y `importarCurriculum` del front). Sin llamadores.
- **L-11:** los errores usan un solo formato (`problem+json` con `detail`): los `BadRequest("…")`/`NotFound()` de los controllers son ahora excepciones que traduce `GlobalExceptionHandler`; la validación de modelo responde en español (`ValidacionModelo`) y un cuerpo por encima de 30 MB responde 413 "Archivo demasiado grande" en vez del texto en inglés con bytes. `GET /horario/actual` sigue devolviendo 404 vacío cuando no hay horario (el front lo trata como estado normal).
- **VAL-1 (parcial):** el validador reporta `DATOS: …` cuando una asignación apunta a una sesión o bloque que no puede resolver (antes las saltaba y contaba "0 violaciones"). Con las FK de M15–M17 no deberían existir en BD. El doble de Fase 2 de un test usaba un bloque inexistente y se corrigió. Sigue abierto: `Bloqueada` (efecto real no comprobado) y que las filas de `Horarios` nunca se borren.
- **DB-10 (parcial):** el importador avisa por nombre (máx. 5 y "y N más") de lo que asume porque el Excel no lo trae: capacidad 30 de los espacios, 30 estudiantes por grupo, 40 h y franja matutina de los docentes. No cambia el esquema ni pregunta el dato real (CLAUDE.md §4: lo da Rosa).
- **L-7:** el front ya no exige docentes cargados para generar. **L-8:** el semestre sale de `StateService.semestre` (constante `SEMESTRE_POR_DEFECTO` en `core/semestre.ts`); la barra del recorrido muestra "Semestre …" en vez del "2026-1 · Ing. Sistemas" fijo. La UI **aún no permite elegir** semestre. **L-14:** `track` por id de programa en la pestaña de alternancia (NG0955). **L-6:** el KPI de ocupación usa 88 h por aula y semana (`core/jornada.ts`), no 96.
- **Bundle:** se quitaron `chart.js` y `ng2-charts` (registrados en `app.config.ts` y sin ningún uso): el bundle inicial baja de 510 kB a **390 kB**, ya bajo el presupuesto de 500 kB. `npm audit`: 0.
- **Documentación (§7):** corregidos en `CLAUDE.md` los datos falsos (26 migraciones, 10 controllers, cantidad de pruebas, sin NSubstitute ni `TestData/`, `ILectorExcel` con un solo método, cómo correr los tests de Postgres, API sin autenticación) y añadido el estado de despliegue y de migraciones a `.azure/deployment-plan.md`.

**No se tocó (necesita decisión tuya, cambio en prod o es demasiado grande para hacerlo sin verificar contra la BD):**
- SEC-2 (restricción de acceso a la API) y SEC-3 (`httpsOnly`): cambios en producción, y falta el rango de IP o el método.
- L-2: qué es la disponibilidad docente (ver arriba).
- SEC-5: declarar la licencia de EPPlus como organización es una decisión legal de la universidad.
- OPS-1: `Migrate()` como paso de despliegue y un chequeo de salud que toque la BD (sin poder probarlo aquí).
- L-1 (el generador lee el catálogo del navegador), DUP-1 (una sola implementación de las reglas duras), DUP-2…8, ARQ-1…7, DB-1/2/3/5/7 (esquema/3FN), DB-4/8/9 (columnas muertas), NEW-11, PERF-3, OnPush, UX-1 y el resto de SOB: refactors grandes o decisiones de producto; varios necesitan migraciones que no puedo verificar sin la BD (ver estado de pruebas).
- Higiene del repo (`cp_model_debug.txt` versionados, `SOEA.sln`/`.slnx`, nombre de clase/archivo de `AsignaturasController`, bases locales sobrantes): son cambios de archivos versionados o de bases del usuario.

### Estado tras este lote

Sin BD: 495/495; frontend 150/150; `ng build` sin errores (solo queda el aviso de presupuesto del CSS de `horario.component`, ya existente). **Las 20 pruebas con Postgres no se han vuelto a correr** desde DB-6 (denegado por el clasificador); este lote toca flujos que ejercitan (pre-chequeo de CP-SAT, `HorarioId`, mensajes de error de controllers, import). Sin commitear.

### Verificación completa con PostgreSQL (2026-09-29)

Con `SOEA_TEST_DB` apuntando al servidor local y autorización expresa del usuario: **515/515 pruebas, 0 omitidas** (las 20 de `BloqueantesPostgresTests` incluidas: P0-1…P0-5, NEW-2, NEW-3, NEW-6, DB-6, L-2, L-3 y SEC-2 sobre el API completo con migraciones M1–M17). Las bases `soea_it_*` que crean las pruebas se borran al terminar; solo quedan `SOEAdb`, `SOEAdb_audit` y `SOEAdb_m14`, que ya existían y no se tocaron. Esto sustituye las salvedades de "no se ha vuelto a correr la suite con Postgres" de los apartados anteriores.
