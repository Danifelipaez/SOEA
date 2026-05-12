---
name: postgres-integration
applyTo: ["**/*soea*query*.md", "**/validation*.md", "**/use-case*.md"]
description: "Instrucciones para integrar queries PostgreSQL en chat. Automatizado: cuando se pregunte por datos/validación/reportes de SOEA, buscar en soea-queries-repository.md, inyectar parámetros, ejecutar contra SOEAdb."
---

# Integración: PostgreSQL + soea-db-schema Skill

**Propósito:** Orquestar la ejecución de queries SQL desde chat de manera consistente y eficiente.

---

## Referencias Centrales

| Elemento | Ubicación |
|----------|-----------|
| **Todas las queries SQL** | [soea-queries-repository.md](soea-queries-repository.md) |
| **Casos de uso** | [soea-use-cases.md](soea-use-cases.md) |
| **Validación de restricciones** | [validation-rules.md](validation-rules.md) |
| **Especificación de restricciones** | [docs/business-rules/](../../docs/business-rules/) |

---

## Flujo de Ejecución

```
Usuario pregunta en chat
    ↓
Agente detecta contexto (validación/reporte/análisis)
    ↓
Busca en [soea-restrictions-reference.md] o [soea-use-cases.md]
    ↓
Extrae Query ID (ej: query_HC_S01_detectar_conflictos_espacio)
    ↓
Localiza en [soea-queries-repository.md]
    ↓
Inyecta parámetros (ej: {horario_id} = '2024-1-sem1')
    ↓
Verifica conexión PostgreSQL activa: mcp://4A5A3BA3.../SOEAdb
    ↓
Ejecuta query contra BD
    ↓
Formatea resultados
    ↓
Retorna respuesta en chat
```

---

## Detección Automática de Contexto

### Contexto: VALIDACIÓN

**Palabras clave que activan:**
- "validar", "válido", "inválido"
- "conflicto", "error", "viola"
- "restricción", "HC-", "constraint"
- "horario " + "cumple", "pasa", "aprueba"

**Acción:** Buscar en [validation-rules.md](validation-rules.md)
→ Extraer query correspondiente de [soea-queries-repository.md](soea-queries-repository.md)

**Ejemplo:**
> Usuario: "¿Tiene el horario 2024-1 conflictos de espacio?"

```
1. Detectar: palabra clave "conflicto"
2. Buscar en validation-rules.md → HC-S01
3. Obtener [query_HC_S01_detectar_conflictos_espacio](soea-queries-repository.md#query_HC_S01_detectar_conflictos_espacio)
4. Inyectar: horario_id = '2024-1'
5. Ejecutar contra SOEAdb
6. Si resultado vacío → "✅ Sin conflictos"
7. Si hay resultados → "❌ {n} conflictos: {detalles}"
```

---

### Contexto: REPORTE

**Palabras clave que activan:**
- "carga", "utilización", "ocupación"
- "docente", "espacio", "horario"
- "reporte", "analizar", "distribución"
- "cuánto", "cuántos", "qué"

**Acción:** Buscar en [soea-use-cases.md#caso-3](soea-use-cases.md#caso-3-generar-reportes-de-carga)
→ Extraer query de [soea-queries-repository.md](soea-queries-repository.md)

**Ejemplo:**
> Usuario: "¿Cuántas horas tiene López asignadas?"

```
1. Detectar: palabra clave "cuántas horas" + "López" (docente)
2. Buscar en soea-use-cases.md#Caso-3 → Reporte carga docente
3. Obtener [query_carga_docente_por_semana](soea-queries-repository.md#query_carga_docente_por_semana)
4. Inyectar: horario_id = horario_activo()
5. Ejecutar contra SOEAdb
6. Filtrar resultados por nombre "López"
7. Mostrar: "López: 20 horas / 20 máximo = 100% utilización"
```

---

### Contexto: PREPARACIÓN

**Palabras clave que activan:**
- "preparar", "listo", "antes de"
- "datos", "asignaturas", "docentes", "espacios"
- "generar", "comenzar", "empezar"

**Acción:** Buscar en [soea-use-cases.md#caso-1](soea-use-cases.md#caso-1-preparar-datos-para-generar-horario)
→ Extraer queries de [soea-queries-repository.md](soea-queries-repository.md)

**Ejemplo:**
> Usuario: "¿Tenemos todo para generar el horario?"

```
1. Detectar: palabra clave "preparar" / "generar"
2. Buscar en soea-use-cases.md#Caso-1
3. Ejecutar 3 queries:
   - [query_get_asignaturas_programa](soea-queries-repository.md#query_get_asignaturas_programa)
   - [query_get_docentes_disponibles](soea-queries-repository.md#query_get_docentes_disponibles)
   - [query_calcular_espacios_requeridos](soea-queries-repository.md#query_calcular_espacios_requeridos)
4. Validar resultados:
   - ✅ Asignaturas: 8 con alternancia definida
   - ✅ Docentes: 12 con disponibilidad
   - ✅ Espacios: 15 aulas + 3 labs
5. Retornar: "✅ Todos los datos OK, listo para generar"
```

---

## Inyección de Parámetros

### Parámetro: `{horario_id}`

**Obtención:**
1. De contexto de chat: Si usuario mencionó ID (ej: "horario 2024-1")
2. De búsqueda en BD:
   ```sql
   SELECT "Id", "Semestre", "Estado" FROM "Horarios"
   WHERE "Estado" = 'activo'
   ORDER BY "Generado_en" DESC LIMIT 1;
   ```
3. Preguntar al usuario: "¿Cuál es el ID del horario?"

**Validación:** Debe ser UUID válido (36 caracteres)

---

### Parámetro: `{programa_id}`

**Obtención:**
1. De contexto: Si usuario mencionó programa (ej: "Ingeniería Informática")
2. De búsqueda:
   ```sql
   SELECT "Id", "Nombre", "Codigo" FROM "Programas"
   WHERE "Nombre" ILIKE '%{búsqueda}%' LIMIT 5;
   ```

---

### Parámetro: `{docente_id}`

**Obtención:**
1. De contexto: Si usuario mencionó nombre (ej: "López")
2. De búsqueda:
   ```sql
   SELECT "Id", "Nombre", "Apellido" FROM "Docentes"
   WHERE "Nombre" ILIKE '%López%' LIMIT 1;
   ```

---

## Manejo de Resultados

### Si Query Devuelve Vacío (0 filas)

**Interpretación según tipo de query:**

- **HC-query vacío** → ✅ Restricción cumplida (bueno)
- **Reporte vacío** → ⚠️ Datos no encontrados (revisar parámetros)

**Acción:**
```
Si HC-query:
  Mostrar: "✅ Validación {HC}: CUMPLIDA"

Si Reporte:
  Mostrar: "Hmm, no encontré datos. ¿Verificamos los parámetros?"
  Sugerir: "¿El horario_id es correcto? Ejecutar query_dashboard_resumen_horario"
```

---

### Si Query Devuelve Resultados

**Interpretación según tipo de query:**

- **HC-query con resultados** → ❌ Restricción violada (malo)
- **Reporte con resultados** → ✅ Datos disponibles (analizar)

**Acción:**

HC-violation:
```
❌ Validación {HC}: VIOLADA

Conflictos encontrados:
{mostrar tabla de resultados}

Recomendación: {acción correctiva}
```

Reporte:
```
Resultados:
{mostrar tabla de resultados}

Interpretación:
{análisis del valor}
```

---

## Ejemplos de Ejecución Completa

### Ejemplo 1: Validación Rápida

```
Usuario: "¿Está válido el horario 2024-1-sem1?"

Agente:
1. Horario ID: 2024-1-sem1
2. Ejecutar query_HC_S01_detectar_conflictos_espacio → Vacío ✅
3. Ejecutar query_HC_I01_docente_multitarea → Vacío ✅
4. Ejecutar query_HC_I03_horas_maximas_docente → Vacío ✅
5. Mostrar: "✅ Validación rápida PASADA (3 restricciones críticas OK)"
```

---

### Ejemplo 2: Validación Completa

```
Usuario: "Validar horario completo"

Agente:
1. Obtener horario_id = '2024-1-sem1' (activo)
2. Ejecutar 11 queries HC (en orden)
3. Contar resultados vacíos vs con resultados
4. Mostrar tabla:
   ✅ HC-S01: OK
   ✅ HC-S02: OK
   ❌ HC-I03: 2 docentes excedidos
   ✅ HC-T01: OK
   ...
5. Conclusión: "2 violaciones encontradas. Recomendación: reasignar sesiones."
```

---

### Ejemplo 3: Reporte de Docentes

```
Usuario: "¿Cómo está la carga de docentes?"

Agente:
1. Obtener horario_id = '2024-1-sem1'
2. Ejecutar query_carga_docente_por_semana
3. Mostrar tabla ordenada por utilización DESC
4. Destacar: "López: 100% (20/20h) - A máxima capacidad"
5. Sugerir: "¿Podemos distribuir alguna sesión a García (60%)?
```

---

## Seguridad y Restricciones

### Queries PERMITIDAS
✅ SELECT (reportes, análisis)
✅ WITH / CTE (cálculos)
✅ JOIN / UNION (combinaciones)
✅ GROUP BY / aggregates (resúmenes)

### Queries NO PERMITIDAS
❌ INSERT, UPDATE, DELETE
❌ DROP, ALTER, TRUNCATE
❌ Tablas de configuración del sistema

**Validación:** Antes de ejecutar, verificar que query comienza con SELECT.

---

## Troubleshooting

| Problema | Solución |
|----------|----------|
| "Connection not found" | Verificar conexión a SOEAdb: `mcp://4A5A3BA3-D129-45AD-B2ED-40811458BBA1/SOEAdb` |
| "Placeholder {horario_id} not resolved" | Buscar en contexto o pedir al usuario ID explícitamente |
| "No results / 0 filas" | Normal para HC-queries (significa OK). Para reportes, revisar parámetros. |
| "Query muy lenta (timeout)" | Agregar LIMIT 1000 o buscar índices faltantes |
| "Error de sintaxis SQL" | Verificar escapado de comillas en inyección de parámetros |

---

## Checklist: Integración Correcta

- [ ] Conexión a SOEAdb activa antes de ejecutar
- [ ] Parámetros inyectados correctamente (sin SQL injection)
- [ ] Query comienza con SELECT
- [ ] Resultado interpretado según tipo de query (HC vs Reporte)
- [ ] Mensaje al usuario es claro y accionable
- [ ] Si hay error, sugerir alternativa o query relacionada

---

## Referencias

- **Queries:** [soea-queries-repository.md](soea-queries-repository.md)
- **Casos:** [soea-use-cases.md](soea-use-cases.md)
- **Validación:** [validation-rules.md](validation-rules.md)
- **DB Schema:** [schema-quick-reference.md](schema-quick-reference.md)
- **Restricciones:** [hard-constraints.md](../../docs/business-rules/hard-constraints.md)
