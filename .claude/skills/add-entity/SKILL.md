---
name: add-entity
description: Scaffold a new SOEA domain entity following the mandatory 5-step Clean Architecture sequence from CLAUDE.md rule 3: (1) Domain entity, (2) IXRepositorio interface, (3) Infrastructure repository, (4) EF Core configuration, (5) DI registration. Invoke when the user says "add entity", "create entity", "nueva entidad", or names a new domain class to create.
---

El usuario quiere agregar una nueva entidad de dominio llamada {{ENTITY_NAME}}.

Sigue EXACTAMENTE la secuencia de 5 pasos de la regla 3 del CLAUDE.md. NO omitas ningún paso.

## Paso 1 — Entidad en Domain
Crea `src/SOEA.Domain/Entities/{{ENTITY_NAME}}.cs`.
- Sin dependencias externas (ni EF Core, ni ASP.NET, ni nada fuera de System).
- Propiedades con `init` o `private set`.
- Constructor que valide invariantes.

## Paso 2 — Interfaz de repositorio en Domain
Crea `src/SOEA.Domain/Interfaces/I{{ENTITY_NAME}}Repositorio.cs`.
- Hereda de `IRepositorio<{{ENTITY_NAME}}>`.
- Agrega solo los métodos de consulta específicos que la entidad necesite.

## Paso 3 — Repositorio en Infrastructure.Data
Crea `src/SOEA.Infrastructure.Data/Repositories/{{ENTITY_NAME}}Repository.cs`.
- Hereda de `RepositorioBase<{{ENTITY_NAME}}>` e implementa `I{{ENTITY_NAME}}Repositorio`.
- Usa `SOEABdContext` inyectado por constructor.
- Sin lógica de negocio — solo acceso a datos.

## Paso 4 — Configuración EF Core
Crea `src/SOEA.Infrastructure.Data/Configurations/{{ENTITY_NAME}}Configuration.cs`.
- Implementa `IEntityTypeConfiguration<{{ENTITY_NAME}}>`.
- Define tabla, PK, columnas, índices y relaciones.
- Registra la configuración en `SOEABdContext.OnModelCreating`.

## Paso 5 — Registro en DI
En `src/SOEA.Infrastructure.Data/DependencyInjection.cs`, agrega:
```csharp
services.AddScoped<I{{ENTITY_NAME}}Repositorio, {{ENTITY_NAME}}Repository>();
```

## Verificación
Después de crear todos los archivos, ejecuta:
```
dotnet build SOEA.sln
```
Si compila sin errores, recuerda al usuario crear la migración:
```
dotnet ef migrations add Add{{ENTITY_NAME}} --project src/SOEA.Infrastructure.Data --startup-project src/SOEA.API
dotnet ef database update --project src/SOEA.Infrastructure.Data --startup-project src/SOEA.API
```
