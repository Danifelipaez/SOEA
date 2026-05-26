---
name: run-migration
description: Add and apply an EF Core migration for SOEA with the correct startup-project flags. Invoke when the user says "add migration", "crear migración", "apply migration", "nueva migración", or provides a migration name to run.
disable-model-invocation: true
---

Ejecuta los siguientes comandos desde la raíz del repositorio en orden:

**1. Crear la migración:**
```
dotnet ef migrations add {{MIGRATION_NAME}} --project src/SOEA.Infrastructure.Data --startup-project src/SOEA.API
```

**2. Aplicar la migración a la base de datos:**
```
dotnet ef database update --project src/SOEA.Infrastructure.Data --startup-project src/SOEA.API
```

Reporta la salida completa de ambos comandos. Si el primer comando falla, no ejecutes el segundo.
