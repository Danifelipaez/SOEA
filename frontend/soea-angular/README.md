# soea-angular

## Purpose
Angular frontend for SOEA — the role-based scheduling web application.

## Overview
This is the Angular workspace for the SOEA frontend. It provides a single-page application (SPA)
with distinct views and permissions for each role:

| Role | Capabilities |
|---|---|
| Admin | Upload Excel data, configure parameters, trigger optimization, manage users |
| Coordinator | Review generated schedules, flag issues, approve or request re-optimization |
| Instructor | View personal teaching timetable |
| Student | View cohort timetable |

## Planned Structure (to be scaffolded)

```text
soea-angular/
├── src/
│   ├── app/
│   │   ├── core/                  ← auth, guards, interceptors, models
│   │   ├── shared/                ← reusable components (schedule grid, loading spinner)
│   │   ├── features/
│   │   │   ├── admin/             ← Excel upload, optimization trigger, user management
│   │   │   ├── coordinator/       ← schedule review, approval workflow
│   │   │   ├── instructor/        ← personal timetable view
│   │   │   └── student/           ← cohort timetable view
│   │   ├── app-routing.module.ts
│   │   └── app.module.ts
│   ├── assets/
│   └── environments/
│       ├── environment.ts         ← development API URL
│       └── environment.prod.ts    ← production API URL
├── angular.json
├── package.json
└── tsconfig.json
```

## Getting Started

> This workspace has not been scaffolded yet. To create it, run:

```bash
ng new soea-angular --routing --style=scss
```

Then install any required dependencies (e.g., Angular Material, ngx-charts for schedule visualization).

## API Integration

The frontend communicates with the SOEA backend via the REST API defined in `docs/data/json-output-spec.md`.
The base API URL is configured in `src/environments/environment.ts`.

## Related Docs

- Roles and permissions: `docs/requirements/stakeholders.md`
- Schedule JSON format: `docs/data/json-output-spec.md`
- Domain terminology: `docs/requirements/glossary.md`
