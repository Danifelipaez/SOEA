# Guía del agente SOEA

Usa esta guía del workspace para cualquier tarea de programación en SOEA. Mantén los cambios pequeños, sigue la arquitectura documentada y enlaza la documentación del proyecto en lugar de repetirla.

## Empezar aquí

- Usa [README.md](README.md) para ver la descripción general del proyecto y la estructura del repositorio.
- Usa [docs/architecture/module-map.md](docs/architecture/module-map.md) para decidir qué proyecto es el dueño de un cambio.
- Usa [docs/architecture/architecture-overview.md](docs/architecture/architecture-overview.md) para entender la estructura del sistema.
- Usa [docs/requirements/glossary.md](docs/requirements/glossary.md) para mantener consistentes los términos del dominio.

## Reglas de trabajo

- Mantén la lógica de dominio en `SOEA.Domain`; la orquestación en `SOEA.Application`; las integraciones en el proyecto de infraestructura o motor correspondiente.
- No cruces límites de capa solo para hacer un cambio más fácil. Si una dependencia parece incorrecta, mueve la lógica al proyecto dueño.
- Prefiere las reglas de negocio documentadas frente a suposiciones, especialmente para alternancia, asignación de espacios y restricciones de programación.
- No persistas sesiones virtuales como filas de espacio físico. Las sesiones virtuales se modelan con valores nulos en el espacio.
- Trata `AlternanciaType` como el conjunto canónico del dominio: `TypeA`, `TypeB` y `NonAlternating`.

## Antes de editar código

- Lee primero el documento más relevante:
  - [docs/business-rules/hard-constraints.md](docs/business-rules/hard-constraints.md)
  - [docs/business-rules/soft-constraints.md](docs/business-rules/soft-constraints.md)
  - [docs/business-rules/alternancia.md](docs/business-rules/alternancia.md)
  - [docs/algorithm/problem-definition-uctp.md](docs/algorithm/problem-definition-uctp.md)
  - [docs/data/data-dictionary.md](docs/data/data-dictionary.md)
  - [docs/data/json-output-spec.md](docs/data/json-output-spec.md)

## Validación

- Usa `dotnet build` para comprobar la solución.
- Usa `dotnet test` para el proyecto de pruebas xUnit en `test/SOEA.Tests`.
- Para trabajo de API, ejecuta `dotnet run --project src/SOEA.API/SOEA.API.csproj` cuando sea útil una verificación manual.

## Frontend

- El trabajo de frontend vive en [frontend/soea-angular/README.md](frontend/soea-angular/README.md).
- Mantén los cambios de frontend dentro del workspace de Angular y no los mezcles con proyectos de backend.

## Enfoque de pruebas

- Prioriza pruebas que correspondan a la capa afectada: invariantes del dominio, orquestación de la aplicación, comportamiento del motor o integración de API.
- Revisa [docs/testing/test-plan.md](docs/testing/test-plan.md) y [docs/testing/acceptance-criteria.md](docs/testing/acceptance-criteria.md) cuando un cambio afecte el comportamiento, la validación o una salida visible para el usuario.