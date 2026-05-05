# SOEA Agent Guide

Use this workspace guide for any coding task in SOEA. Keep changes small, follow the documented architecture, and link to the project docs instead of restating them.

## Start Here

- Use [README.md](README.md) for the project overview and repository layout.
- Use [docs/architecture/module-map.md](docs/architecture/module-map.md) to decide which project owns a change.
- Use [docs/architecture/architecture-overview.md](docs/architecture/architecture-overview.md) for system structure.
- Use [docs/requirements/glossary.md](docs/requirements/glossary.md) to keep domain terms consistent.

## Working Rules

- Keep domain logic in `SOEA.Domain`; orchestration in `SOEA.Application`; integrations in the matching infrastructure or engine project.
- Do not cross layer boundaries just to make a change easier. If a dependency looks wrong, move the logic to the owning project instead.
- Prefer the documented business rules over assumptions, especially for alternancia, space assignment, and scheduling constraints.
- Do not persist virtual sessions as physical space rows. Virtual sessions are modeled with null space values.
- Treat `AlternanciaType` as the canonical set from the domain: `TypeA`, `TypeB`, and `NonAlternating`.

## Before Editing Code

- Read the most relevant doc first:
  - [docs/business-rules/hard-constraints.md](docs/business-rules/hard-constraints.md)
  - [docs/business-rules/soft-constraints.md](docs/business-rules/soft-constraints.md)
  - [docs/business-rules/alternancia.md](docs/business-rules/alternancia.md)
  - [docs/algorithm/problem-definition-uctp.md](docs/algorithm/problem-definition-uctp.md)
  - [docs/data/data-dictionary.md](docs/data/data-dictionary.md)
  - [docs/data/json-output-spec.md](docs/data/json-output-spec.md)

## Validation

- Use `dotnet build` to check the solution.
- Use `dotnet test` for the xUnit test project under `test/SOEA.Tests`.
- For API work, run `dotnet run --project src/SOEA.API/SOEA.API.csproj` when a manual check is useful.

## Frontend

- Frontend work lives under [frontend/soea-angular/README.md](frontend/soea-angular/README.md).
- Keep frontend changes inside the Angular workspace and do not mix them into backend projects.

## Testing Focus

- Prefer tests that match the touched layer: domain invariants, application orchestration, engine behavior, or API integration.
- Review [docs/testing/test-plan.md](docs/testing/test-plan.md) and [docs/testing/acceptance-criteria.md](docs/testing/acceptance-criteria.md) when a change affects behavior, validation, or user-visible output.