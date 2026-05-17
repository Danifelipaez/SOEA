# PR: Actualización de documentación `docs/`

## Resumen
Cambios conservadores y de mantenimiento aplicados a la carpeta `docs/`:
- Añadida la línea "Última actualización: 2026-05-16" bajo el encabezado principal en 24 archivos. (ver `docs/CHANGES.md`)
- Normalización básica de encabezados: comprobado que cada archivo tiene H1 como título principal.
- Verificación del repo: compilación (`dotnet build`) y tests (`dotnet test`) ejecutados correctamente.

## Archivos modificados
Consulta `docs/CHANGES.md` para el listado completo.

## Checklist antes de crear el PR
- [ ] Revisar los cambios en `docs/CHANGES.md`.
- [ ] Revisar manualmente archivos críticos: `docs/requirements/SRS.md`, `docs/business-rules/hard-constraints.md`, `docs/data/json-output-spec.md`.
- [ ] Ejecutar un verificador de enlaces Markdown (opcional): `markdown-link-check "docs/**/*.md"`.
- [ ] Confirmar si se desea aplicar actualizaciones de contenido más profundas (p. ej. actualización de ejemplos, diagramas).

## Comandos recomendados localmente
```bash
# Compilar solución
dotnet build SOEA.sln

# Ejecutar tests
dotnet test test/SOEA.Tests/SOEA.Tests.csproj

# Verificar enlaces Markdown (instalar markdown-link-check si se desea)
# npm install -g markdown-link-check
markdown-link-check "docs/**/*.md"
```

## Descripción del PR
- Rama sugerida: `docs/update-metadata-2026-05-16`
- Título sugerido: "docs: añadir metadatos de última actualización y normalizar encabezados"
- Descripción: breve motivo del cambio, pasos de verificación ejecutados, lista de archivos importantes.

---

Si confirmas, creo una rama y preparo los commits para el PR (o genero los parches que prefieras).