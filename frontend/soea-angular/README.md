# soea-angular

## Propósito
Frontend en Angular para SOEA: la aplicación web de programación basada en roles.

## Resumen
Este es el workspace de Angular para el frontend de SOEA. Proporciona una aplicación de una sola página (SPA)
con vistas y permisos diferenciados para cada rol:

| Rol | Capacidades |
|---|---|
| Administrador | Cargar datos de Excel, configurar parámetros, lanzar la optimización, administrar usuarios |
| Coordinador | Revisar horarios generados, marcar incidencias, aprobar o solicitar una nueva optimización |
| Docente | Ver su horario personal de clases |
| Estudiante | Ver el horario de su cohorte |

## Estructura prevista (pendiente de generar)

```text
soea-angular/
├── src/
│   ├── app/
│   │   ├── core/                  ← autenticación, guards, interceptores, modelos
│   │   ├── shared/                ← componentes reutilizables (rejilla de horarios, spinner de carga)
│   │   ├── features/
│   │   │   ├── admin/             ← carga de Excel, disparo de optimización, gestión de usuarios
│   │   │   ├── coordinator/       ← revisión de horarios, flujo de aprobación
│   │   │   ├── instructor/        ← vista de horario personal
│   │   │   └── student/           ← vista de horario de cohorte
│   │   ├── app-routing.module.ts
│   │   └── app.module.ts
│   ├── assets/
│   └── environments/
│       ├── environment.ts         ← URL de la API en desarrollo
│       └── environment.prod.ts    ← URL de la API en producción
├── angular.json
├── package.json
└── tsconfig.json
```

## Primeros pasos

> Este workspace todavía no ha sido generado. Desde la raíz del repositorio, créalo dentro de `frontend/` para que la app quede en la carpeta correcta:

```bash
cd frontend && ng new soea-angular --routing --style=scss
```

Después instala las dependencias necesarias (por ejemplo, Angular Material, ngx-charts para la visualización de horarios).

## Integración con la API

El frontend se comunica con el backend de SOEA a través de la API REST definida en `docs/data/json-output-spec.md`.
La URL base de la API se configura en `src/environments/environment.ts`.

## Documentos relacionados

- Roles y permisos: `docs/requirements/stakeholders.md`
- Formato JSON de horarios: `docs/data/json-output-spec.md`
- Terminología del dominio: `docs/requirements/glossary.md`
