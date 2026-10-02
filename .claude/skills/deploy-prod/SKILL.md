---
name: deploy-prod
description: Despliega SOEA a producción en Azure — backend soea-api (App Service), frontend soea-frontend (Static Web App) y migraciones/limpieza de la BD PostgreSQL soea-pg-srv. Úsala siempre que el usuario pida desplegar, publicar, subir, "pasar a prod" o actualizar SOEA en Azure, migrar o limpiar la BD de producción, o saber qué versión corre en prod, aunque no diga "Azure" ni "deploy". También para un ensayo ("ensayo", "dry run", "simula el deploy", "prepara el deploy sin subir") que lo deja todo listo sin tocar Azure.
---

# Desplegar SOEA a producción

El deploy es 100 % manual (el CI solo compila y prueba) y tiene tres trampas que ya causaron incidentes. Esta skill existe para no repetirlas:

1. **Las fechas de Azure mienten.** Hubo prod con código de semanas atrás pese a un deploy "de hoy". La única prueba de qué corre es el SHA incrustado en la DLL (Fase 1).
2. **Desplegar una rama a la que le faltan commits de prod revierte arreglos en silencio.** Compara siempre en las dos direcciones.
3. **La subida desde esta máquina es lentísima** (~10-150 KB/s): un zip de ~31 MB tarda ~1 h. El CLI puede colgarse o decir "failed" *después* de un deploy exitoso. Nunca reintentes ni mates una subida sin mirar Kudu antes.

| Recurso | Valor |
|---|---|
| Grupo | `rg-soea` (suscripción "Azure for Students") |
| API | `soea-api` · https://soea-api.azurewebsites.net · Kudu `https://soea-api.scm.azurewebsites.net` (solo token AAD, `scm.allow=false`) · Linux .NET 10, Basic, **sin slots** |
| Frontend | `soea-frontend` (Static Web App) · https://thankful-ground-0812d1f0f.7.azurestaticapps.net |
| BD | `soea-pg-srv.postgres.database.azure.com` / `SOEAdb` · login AAD · regla de firewall `allow-daniel-local` |
| psql | `/c/Program Files/PostgreSQL/18/bin/psql.exe` |

Detalle histórico y workarounds: `.azure/deployment-plan.md`. Usa el scratchpad de la sesión (`$S` abajo) para el worktree, el publish y los zips. Cada llamada a Bash abre una shell nueva: redefine `S`, `TOKEN` y `PGPASSWORD` en cada una, o encadena los pasos que los comparten.

Las lecturas de la BD de prod pueden requerir la aprobación del usuario o ser denegadas por el control de permisos. Si se deniegan, deja ese dato sin medir y dilo; no busques otra vía.

## Consulta de versión

Si solo preguntan qué corre en prod o cuánto le falta, no compiles ni preguntes. Haz esto y responde:
- SHA de prod (receta de la Fase 1) y el último registro de `…scm…/api/deployments` como contexto (fecha, `status`, `active`). Manda el SHA, no la fecha.
- `git fetch`, luego `git rev-list --count` y `git log --oneline` en las dos direcciones contra `origin/main`.
- Migraciones pendientes: última fila de `__EFMigrationsHistory` contra la lista del repo.
- Frontend: el `main-*.js` que referencia el `index.html` servido contra el que quedó anotado en `.azure/deployment-plan.md` §5 en el último deploy.

Formato: SHA de prod · commits que le faltan (lista) · commits que main no tiene · migraciones pendientes · lo que no se pudo medir.

## Modo ensayo

Si el pedido dice "ensayo", "dry run", "simula" o "sin subir", haz las Fases 1 a 3 completas y detente antes de la Fase 4. En ensayo no hay commits ni push, ni escrituras en Azure (deploy, app settings, firewall) ni en la BD. Si leer la BD exige tocar el firewall, pregunta o deja ese punto sin medir. Cierra con el informe final, en condicional ("se desplegaría…", "M19 borraría N filas…").

## Fase 1 — Explorar (solo lectura)

**Qué corre en prod.** El SDK incrusta el commit en `AssemblyInformationalVersion`:

```bash
TOKEN=$(az account get-access-token --resource https://management.azure.com --query accessToken -o tsv)
curl -s -H "Authorization: Bearer $TOKEN" -o "$S/prod.dll" https://soea-api.scm.azurewebsites.net/api/vfs/site/wwwroot/SOEA.API.dll
PROD_SHA=$(grep -aoE '\+[0-9a-f]{40}' "$S/prod.dll" | head -1 | tr -d +)
```

Como contexto, mira también el último registro de `…scm…/api/deployments` con el mismo Bearer. Su fecha es la real; `lastModifiedTimeUtc` de la app no lo es.

El frontend no lleva SHA, pero su build es determinista: el `main-*.js` que referencia el `index.html` servido es el que produce compilar ese commit. Compáralo con el anotado en `.azure/deployment-plan.md` §5. Si no está anotado, compila el front de `$PROD_SHA` para reproducirlo (~1 min).

**Qué se desplegaría.** `git fetch`, luego el SHA candidato (por defecto `origin/main`):
- `git log --oneline $CANDIDATO..$PROD_SHA` tiene que salir **vacío**. Si no, prod tiene commits que el candidato no trae y hay que fusionarlos antes.
- `git log --oneline $PROD_SHA..$CANDIDATO` es lo que entra. Si sale vacío, no hay nada que desplegar.
- `git status --short -- src frontend test` muestra el trabajo sin commitear que afectaría al artefacto (los cambios en docs no cuentan). Anótalo para la Fase 2: solo se despliegan commits, para que el SHA incrustado diga la verdad.
- `git worktree list`: si quedan worktrees de deploys anteriores, menciónalos. No los borres sin preguntar.

**BD.** Compara tu IP (`curl -s https://api.ipify.org`) con la regla `allow-daniel-local` (`az postgres flexible-server firewall-rule list -g rg-soea -s soea-pg-srv`). Si difieren, actualizar la regla es una escritura: fuera de ensayo, hazla (`firewall-rule update -g rg-soea -s soea-pg-srv -n allow-daniel-local --start-ip-address $IP --end-ip-address $IP`) y dilo en el informe. Lee en modo solo lectura:

```bash
export PGPASSWORD=$(az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv)
export PGOPTIONS="-c default_transaction_read_only=on"
PSQL=("/c/Program Files/PostgreSQL/18/bin/psql.exe" "host=soea-pg-srv.postgres.database.azure.com dbname=SOEAdb user=$(az account show --query user.name -o tsv) sslmode=require")
"${PSQL[@]}" -c 'select "MigrationId" from "__EFMigrationsHistory" order by 1 desc limit 3;'
```

Conteos en una sola llamada, para que el usuario apruebe una vez. Los catálogos semilla deben dar `BloqueTiempos` = 88, `TiposAlternancia` = 3 y `CriteriosCesionAlternancia` = 4:

```bash
Q=""; for t in AsignacionesSemanales Sesiones Horarios Grupos DisponibilidadDocente Docentes Asignaturas Espacios Programas Facultades BloqueTiempos TiposAlternancia CriteriosCesionAlternancia; do Q="$Q${Q:+ union all }select '$t', count(*) from \"$t\""; done
"${PSQL[@]}" -c "$Q"
```

**Migraciones pendientes.** Son las de `src/SOEA.Infrastructure.Data/Migrations/` (sin `.Designer`) posteriores a la última aplicada. Si la última aplicada ya es la última del candidato, dilo y salta este análisis. Busca en ellas `DELETE`, `DROP`, `DropColumn`, `DropTable` y `nullable: false`. Por cada `DELETE` de saneamiento, conviértelo en un `SELECT count(*)` con el mismo `WHERE` y ejecútalo: así sabes cuántas filas borraría. Si una migración elimina columnas, el código viejo fallará contra el esquema nuevo, y eso decide el orden de la Fase 4.

**Configuración.** Si el diff trae claves de configuración nuevas (`GetSection`, `configuration[...]`, `IOptions`), verifica que existan como App Setting en prod (`az webapp config appsettings list -g rg-soea -n soea-api --query "[].name"`). No quites el `--query`: sin él, el comando imprime los valores, incluida la cadena de conexión. Las que deben seguir: `ConnectionStrings__DefaultConnection`, `AllowedOrigins__0/1`, `CpSat__SweepGrupos=true`, `CpSat__TimeoutSegundos`, `CpSat__ExportarModelo=false`. No cambies la configuración por tu cuenta: reporta lo que falte.

## Fase 2 — Preguntar (siempre, en una sola AskUserQuestion)

Presenta primero lo hallado (prod = X, entra = N commits, migraciones pendientes y lo que borran, datos en prod). Después pregunta:

1. **Qué código publicar.** El SHA candidato. Si hay trabajo sin commitear, pregunta si hay que commitearlo y si hacer push; recomienda push a `origin/main` para que main coincida con prod. Si prod tiene commits que el candidato no trae, pregunta si fusionarlos.
2. **Qué hacer con la BD de prod:**
   - *Solo migrar* (por defecto): las migraciones corren solas al arrancar el API nuevo (`Database.Migrate()` en `Program.cs`), con los conteos de lo que borrarían.
   - *Vaciar datos operativos*: `AsignacionesSemanales`, `Sesiones`, `Horarios`, `Grupos`, `DisponibilidadDocente`, `Docentes`, `Asignaturas`, `Espacios`. Conserva `Facultades`, `Programas` y los catálogos semilla.
   - *Vaciar también facultades y programas.*

   Nunca ofrezcas vaciar `BloqueTiempos`, `TiposAlternancia` ni `CriteriosCesionAlternancia`: `ef database update` no los repone y la alternancia falla con un 409 que no apunta a la causa.

En modo ensayo, haz las mismas preguntas como "¿qué harías?" solo si cambian lo que se prepara. Si no, asume los valores por defecto y dilo.

## Fase 3 — Preparar (local, nada sale de la máquina)

```bash
git -c core.longpaths=true worktree add --detach "$S/wt" $SHA   # solo archivos versionados: appsettings.Development.json no puede colarse
cd "$S/wt"
export SOEA_TEST_DB=$(python -c "import json;print(json.load(open(r'<checkout-principal>/src/SOEA.API/appsettings.Development.json'))['ConnectionStrings']['DefaultConnection'])")
dotnet build SOEA.sln -c Release && dotnet test SOEA.sln -c Release --no-build   # exige 0 fallos y "Omitido: 0"; puede ir en segundo plano mientras corre el front
cd frontend/soea-angular && npm ci && npm test -- --watch=false && npm run build && cp -r dist/soea-angular/browser "$S/frontend-dist" && cd ../..
dotnet publish src/SOEA.API/SOEA.API.csproj -c Release -r linux-x64 --self-contained false -o "$S/publish"
python -c "import shutil; shutil.make_archive(r'$S/api', 'zip', r'$S/publish')"
```

Por qué así:
- Rutas de Windows: git necesita `core.longpaths` (pásalo con `-c`, sin tocar la configuración del repo) y `npm ci` falla si la ruta de `…/node_modules/@esbuild/win32-x64/esbuild.exe` (la del worktree más ~66 caracteres) supera 260. Si `$S/wt` pasa de ~180 caracteres, usa una ruta más corta.
- El `dist` se copia a `$S/frontend-dist` porque la limpieza borra el worktree.
- `SOEA_TEST_DB` hace falta porque en el worktree no existe `appsettings.Development.json`. Sin ella, las ~23 pruebas contra PostgreSQL se omiten en silencio. No imprimas su valor.
- Sin `-r linux-x64`, OR-Tools arrastra binarios de cinco plataformas: el zip pasa de ~31 MB a ~340 MB, y con esta red eso es inviable.
- `shutil.make_archive` escribe rutas con `/`. `Compress-Archive` de PowerShell 5 usa `\` y rompe el despliegue en Linux.

Comprueba antes de seguir: `publish` pesa ~85 MB y no contiene `appsettings.Development.json`, `.env` ni `cp_model_debug.txt`, y `grep -aoE '\+[0-9a-f]{40}' "$S/publish/SOEA.API.dll"` da `$SHA`. En ensayo, aquí terminas: limpia (manteniendo `api.zip` y `frontend-dist`) y salta al informe.

## Fase 4 — Ejecutar

El orden importa porque no hay slots: durante la subida (~1 h) sigue corriendo el código viejo.

1. **Limpieza de BD**, si se eligió. Hazla justo antes de subir, en una transacción, sin `PGOPTIONS` de solo lectura: `BEGIN; TRUNCATE <tablas elegidas> CASCADE;`. Revisa que los catálogos semilla conserven sus conteos y luego `COMMIT`. El código viejo tolera una BD vacía.
2. **Migraciones: deja que las aplique el API nuevo al arrancar.** Si las aplicaras antes, el código viejo pasaría toda la subida contra un esquema que quizá ya perdió columnas. Solo cuando todas sean aditivas y quieras detectar fallos pronto, aplícalas antes con `dotnet ef database update` (receta en `.azure/deployment-plan.md` §5).
3. **Backend**, en segundo plano (`run_in_background`), sin matarlo por parecer colgado:
   ```bash
   az webapp deploy -g rg-soea -n soea-api --src-path "$S/api.zip" --type zip --clean true --restart true --async true
   ```
   Al terminar, o si pasan más de 90 min, mira Kudu antes de concluir nada (Fase 5). Si Kudu no recibió nada y no hay avance, pasa al workaround de blob + AzCopy de `.azure/deployment-plan.md` §9 (puede pedir MFA interactivo; entonces solo el usuario puede seguir).
4. **Frontend**, cuando el backend nuevo ya responda, para que la UI nueva no llame a endpoints que aún no existen:
   ```bash
   SWA_CLI_DEPLOYMENT_TOKEN=$(az staticwebapp secrets list -n soea-frontend -g rg-soea --query properties.apiKey -o tsv) \
     swa deploy "$S/frontend-dist" --env production
   ```

## Fase 5 — Verificar

- Kudu: `GET https://soea-api.scm.azurewebsites.net/api/deployments` con el Bearer de la Fase 1. El último debe tener `status: 4`, `complete: true`, `active: true` y ningún error.
- El SHA de la DLL remota (receta de la Fase 1) es igual a `$SHA`. Esta es la prueba que cuenta.
- `__EFMigrationsHistory` termina en la última migración del candidato y los catálogos semilla siguen en 88/3/4.
- `GET /api/asignaturas` y `GET /api/grupos` devuelven 200. El `index.html` del frontend referencia el mismo `main-*.js` que tu `dist`.
- Si el API da 503 o no arranca, casi siempre es una migración que falló al arrancar. Lee `…scm…/api/logs/docker` antes de tocar nada.

Limpieza: `git -c core.longpaths=true worktree remove "$S/wt"` y `git worktree prune`; borra `publish`, los zips, `frontend-dist` y `prod.dll`. Actualiza en `.azure/deployment-plan.md` §5 la fecha, el SHA desplegado, las migraciones aplicadas y el `main-*.js` del frontend, que es lo único que permite saber después qué versión de la UI está publicada.

**Si hay que volver atrás:** el código se revierte desplegando el SHA anterior por este mismo flujo, pero las migraciones ya aplicadas se quedan. La BD se recupera con point-in-time restore del Flexible Server (`az postgres flexible-server restore`), que crea un servidor nuevo al que habría que apuntar la cadena de conexión. Es decisión del usuario, nunca automática.

## Informe final

Responde en español y en este orden:
1. **Resultado** en una línea: desplegado / ensayo listo / detenido en la fase X y por qué.
2. **Prod antes → después**: SHAs y número de commits.
3. **BD**: migraciones aplicadas, filas borradas (por las migraciones o por la limpieza) y estado de los catálogos semilla.
4. **Verificaciones**: cada comprobación de la Fase 5 con ✅ o ❌. En ensayo, en su lugar: las comprobaciones de la Fase 3 (pruebas, "Omitido: 0", tamaño, archivos prohibidos, SHA en la DLL), el tamaño del zip y la duración estimada de la subida (tamaño ÷ 10–150 KB/s).
5. **Pendientes o cambios hechos en Azure**: regla de firewall actualizada, app settings que faltan, `httpsOnly`, etc.
