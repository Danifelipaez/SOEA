# soea-angular

## Propósito

Frontend en Angular para SOEA: la aplicación web que la coordinadora académica usa para cargar el catálogo del semestre, generar el horario optimizado, ajustarlo a mano y revisar sus indicadores de calidad.

> Este documento describe el frontend **realmente construido**, verificado contra `src/app/`. Una versión anterior de este README describía un diseño con 4 roles y login que nunca se implementó — ver la nota de corrección al final.

## Resumen

Aplicación de una sola página (SPA), **de un solo operador, sin login ni roles**: la coordinadora académica es la única usuaria, trabaja de punta a punta desde un PC de oficina. `Docente` y `Estudiante` son datos que ella administra (filas de catálogo), no cuentas con las que alguien inicia sesión. La organización de las pantallas sigue el journey real de trabajo (5 pasos), no las entidades del dominio — decisión de diseño documentada en `docs/DESIGN_BRIEF.md` y `docs/MAPEO_FLUJOS_FRONTEND.md`.

## Rutas (journey de 4 pasos navegables + 1 bloqueado)

| Ruta | Paso del journey | Componente | Qué hace |
|---|---|---|---|
| `/catalogo` | 1. Preparar catálogo | `IngestaComponent` | Tabs: Asignaturas (con Grupos como fila expandible), Docentes, Espacios, Alternancia. Carga por Excel o alta manual, fusión de docentes duplicados. |
| `/horario` | 2–3. Generar y ajustar | `HorarioComponent` | El componente más grande de la app: grid semanal (semana A/B), parámetros avanzados del algoritmo genético (colapsables), diálogos de edición/creación de sesión, exportar/importar horario en JSON (menú "Avanzado"). |
| `/revisar` | 4. Revisar | `DashboardAdminComponent` | KPIs de solo lectura: % ocupación de espacios, % presencial/virtual, franjas ociosas, carga por docente (semáforo Normal/Alerta/Límite). Todo calculado en el cliente desde el estado ya cargado, sin endpoint propio. |
| `/publicar` | 5. Publicar | `PublicarComponent` | Placeholder bloqueado — el backend todavía no implementa el servicio de publicación (`PublicarHorarioService` pendiente). |

La ruta raíz (`''`) redirige a `/catalogo`. La navegación entre pasos la controla `JourneyBarComponent` (`shared/journey-bar/`), una barra de progreso de 4 pasos.

## Estructura real

```text
soea-angular/
├── src/
│   ├── app/
│   │   ├── core/
│   │   │   ├── state.service.ts          ← estado global reactivo (Angular signals)
│   │   │   ├── persistencia.service.ts    ← wrapper HTTP, un método por endpoint del backend
│   │   │   ├── catalogo.service.ts        ← hidrata StateService desde la BD al arrancar
│   │   │   ├── horario-api.service.ts     ← arma requests de generación/reacomodo
│   │   │   ├── http-error.util.ts         ← mensajeErrorHttp()
│   │   │   └── models.ts                  ← interfaces TypeScript del dominio
│   │   ├── shared/
│   │   │   ├── journey-bar/               ← barra de progreso de 4 pasos
│   │   │   ├── disponibilidad-editor/     ← selector de disponibilidad por día (Docente y Grupo)
│   │   │   ├── requisitos-espacio/
│   │   │   ├── searchable-select/
│   │   │   ├── confirm-delete-dialog/
│   │   │   └── import-resultado-dialog/
│   │   ├── features/
│   │   │   ├── ingesta/                   ← /catalogo (tabs Asignaturas/Docentes/Espacios/Alternancia)
│   │   │   ├── horario/                   ← /horario (grid semanal + diálogos)
│   │   │   ├── dashboard-admin/           ← /revisar (KPIs)
│   │   │   └── publicar/                  ← /publicar (placeholder bloqueado)
│   │   ├── app.routes.ts
│   │   └── app.config.ts
│   ├── assets/
│   └── environments/
│       ├── environment.ts         ← http://localhost:5066/api (desarrollo)
│       └── environment.prod.ts    ← https://soea-api.azurewebsites.net/api (producción, validado 2026-06-17)
├── angular.json
├── package.json
└── tsconfig.json
```

## Stack real (verificado en `package.json`)

Angular 21.2 + Angular Material/CDK 21.2, RxJS. `chart.js`/`ng2-charts` están instalados y registrados (`provideCharts` en `app.config.ts`) pero **no se usan en ningún componente** — los KPIs de `DashboardAdminComponent` son barras/pills en CSS puro, no gráficas de librería. No hay ninguna librería de autenticación (no hay login que implementar).

## Primeros pasos

```bash
cd frontend/soea-angular
npm install
npm start        # → http://localhost:4200
npm run build
npm test          # vitest — cobertura mínima, ver nota abajo
```

## Integración con la API

El frontend consume la API REST de `src/SOEA.API` (9 controllers: Asignatura, CriteriosCesionAlternancia, Docentes, Espacios, Facultades, Grupos, Horario, Import, Programas, Sesiones). La API **no tiene autenticación** — es completamente pública en el estado actual del proyecto. La URL base se configura en `src/environments/environment.ts` / `environment.prod.ts`.

## Cobertura de pruebas

Débil: solo existe la prueba por defecto `app.spec.ts`. El pipeline de integración continua (`.github/workflows/ci.yml`) únicamente ejecuta `npm run build` para este proyecto, nunca `npm test` — así que no hay pruebas de frontend exigidas para que un cambio pase CI. Es una limitación conocida y pendiente, no una omisión de este documento.

## Documentos relacionados

- Journey y decisiones de alcance (fuente de verdad de la organización de pantallas): `docs/MAPEO_FLUJOS_FRONTEND.md`
- Brief de diseño / lenguaje visual: `docs/DESIGN_BRIEF.md`
- Inventario de formularios, campos y contrato de API por pantalla: `docs/REQUISITOS_FRONTEND.md`
- Terminología del dominio: `docs/domain.md`

---

## Nota de corrección (2026-08-07)

La versión anterior de este README describía un frontend que **nunca se construyó**: una SPA con 4 roles (Administrador/Coordinador/Docente/Estudiante), login, carpetas separadas por rol (`admin/coordinator/instructor/student`), y decía textualmente "este workspace todavía no ha sido generado". Nada de eso refleja el sistema real. También enlazaba a documentos que no existen en el repositorio actual (`docs/requirements/stakeholders.md`, `docs/data/json-output-spec.md`, `docs/requirements/glossary.md` — esos vivían en una estructura de docs/ archivada, ver `docs/archive/old/`). Este documento se reescribió por completo contra el código real de `src/app/` para que sirva como fuente confiable.
