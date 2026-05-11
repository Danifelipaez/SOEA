---
name: soea-queries-repository
description: "Repositorio centralizado de queries SQL para SOEA. Cada query SQL existe UNA sola vez aquí. Otros archivos enlazan por ID. Úsalo para: ejecutar validaciones, generar reportes, explorar datos, verificar restricciones HC/SC."
---

# Repositorio Centralizado: Queries SQL SOEA

**Principio:** Cada query SQL existe UNA ÚNICA VEZ aquí. Otros archivos (.md y código) enlazan por `query_ID`.

> **Integración con Clean Architecture:** Estas queries son para *exploración* en chat. La validación real está en `SOEA.Application.Services.ConstraintValidator`.

---

## Queries de Preparación: Obtener Datos Base

### query_get_asignaturas_programa
**Caso de uso:** Listar asignaturas de un programa antes de generar horario
**Restricción verificada:** N/A (exploración)
**Parámetros:** `{programa_id}`

```sql
SELECT a."Id", a."Codigo", a."Nombre", a."BloqueSemanales", 
       a."RequiereLab", a."Alternancia", a."ProgramaId"
FROM "Asignaturas" a
WHERE a."ProgramaId" = '{programa_id}'
ORDER BY a."Codigo";
```

**Resultado esperado:**
```
Id (uuid) | Codigo | Nombre | BloqueSemanales | RequiereLab | Alternancia
──────────┼────────┼────────┼─────────────────┼─────────────┼────────────
...
```

---

### query_get_docentes_disponibles
**Caso de uso:** Obtener docentes con disponibilidad asignada
**Restricción verificada:** HC-I02 (disponibilidad)
**Parámetros:** ninguno

```sql
SELECT DISTINCT d."Id", d."Nombre", d."Apellido", d."Correo", 
       d."Maximo_hrs_semanales",
       COUNT(dd."Franja_id") as franjas_disponibles
FROM "Docentes" d
LEFT JOIN "DisponibilidadDocente" dd ON d."Id" = dd."Docente_id"
GROUP BY d."Id", d."Nombre", d."Apellido", d."Correo", d."Maximo_hrs_semanales"
HAVING COUNT(dd."Franja_id") > 0
ORDER BY d."Nombre";
```

**Resultado esperado:**
```
Id | Nombre | Apellido | Correo | Maximo_hrs_semanales | franjas_disponibles
──┼────────┼──────────┼────────┼──────────────────────┼────────────────────
```

---

### query_calcular_espacios_requeridos
**Caso de uso:** Verificar capacidad de infraestructura
**Restricción verificada:** N/A (planeamiento)
**Parámetros:** ninguno

```sql
SELECT e."Tipo", COUNT(*) as cantidad, 
       SUM(CASE WHEN e."Capacidad" >= 40 THEN 1 ELSE 0 END) as grandes,
       SUM(CASE WHEN e."Capacidad" < 40 THEN 1 ELSE 0 END) as pequeños,
       ROUND(AVG(e."Capacidad")::NUMERIC, 0) as capacidad_promedio
FROM "Espacios" e
GROUP BY e."Tipo"
ORDER BY e."Tipo";
```

**Resultado esperado:**
```
Tipo | cantidad | grandes | pequeños | capacidad_promedio
─────┼──────────┼─────────┼──────────┼───────────────────
```

---

### query_get_asignaturas_laboratorio
**Caso de uso:** Identificar asignaturas que requieren laboratorio
**Restricción verificada:** HC-S03 (tipo de espacio)
**Parámetros:** `{programa_id}` (opcional, si omitir = todas)

```sql
SELECT a."Codigo", a."Nombre", a."BloqueSemanales",
       (SELECT COUNT(*) FROM "Espacios" WHERE "Tipo" = 'Laboratorio') as labs_disponibles
FROM "Asignaturas" a
WHERE a."RequiereLab" = true
ORDER BY a."Codigo";
```

**Resultado esperado:**
```
Codigo | Nombre | BloqueSemanales | labs_disponibles
───────┼────────┼─────────────────┼──────────────────
```

---

## Queries de Validación: Restricciones Duras (HC)

### query_HC_S01_detectar_conflictos_espacio
**Caso de uso:** Detectar conflictos de ocupación de espacio por alternancia
**Restricción verificada:** HC-S01 - Un espacio NO puede alojar dos sesiones con mismo tipo de alternancia
**Documento:** [hard-constraints.md#HC-S01](../../docs/business-rules/hard-constraints.md#HC-S01)
**Parámetros:** `{horario_id}`

```sql
SELECT s1."Id" as sesion1, s1."Tipo_alternancia",
       s2."Id" as sesion2, s2."Tipo_alternancia",
       e."Nombre" as espacio, f."Dia_semana", f."hora_inicio",
       a1."Codigo" as asignatura1, a2."Codigo" as asignatura2
FROM "Sesiones" s1
JOIN "Sesiones" s2 ON 
    s1."Espacio_id" = s2."Espacio_id" AND
    s1."Franja_id" = s2."Franja_id" AND
    s1."Horario_id" = s2."Horario_id" AND
    s1."Id" < s2."Id"
JOIN "Espacios" e ON s1."Espacio_id" = e."Id"
JOIN "FranjasHorarias" f ON s1."Franja_id" = f."Id"
JOIN "Asignaturas" a1 ON s1."Asignatura_id" = a1."Id"
JOIN "Asignaturas" a2 ON s2."Asignatura_id" = a2."Id"
WHERE s1."Espacio_id" IS NOT NULL
  AND (s1."Tipo_alternancia" = s2."Tipo_alternancia" OR
       s1."Tipo_alternancia" = 'NonAlternating' OR
       s2."Tipo_alternancia" = 'NonAlternating')
ORDER BY e."Nombre", f."Dia_semana", f."hora_inicio";
```

**Resultado esperado:** **Vacío** (0 filas = sin conflictos ✅)

**Si hay resultados:** ❌ Horario inválido

---

### query_HC_S02_exceso_capacidad
**Caso de uso:** Verificar exceso de capacidad de espacio
**Restricción verificada:** HC-S02 - Estudiantes en mismo espacio/tiempo no exceden capacidad
**Documento:** [hard-constraints.md#HC-S02](../../docs/business-rules/hard-constraints.md#HC-S02)
**Parámetros:** `{horario_id}`
**Nota:** Requiere tabla `Cohortes` con cantidad de estudiantes

```sql
SELECT 
    e."Nombre" as espacio,
    e."Capacidad",
    f."Dia_semana",
    f."hora_inicio",
    COUNT(DISTINCT s."Id") as sesiones_asignadas,
    COALESCE(SUM(c."cantidad_estudiantes"), 0) as estudiantes_totales,
    CASE 
        WHEN COALESCE(SUM(c."cantidad_estudiantes"), 0) > e."Capacidad" THEN '❌ SOBRE CAPACIDAD'
        ELSE '✅ DENTRO CAPACIDAD'
    END as estado
FROM "Sesiones" s
JOIN "Espacios" e ON s."Espacio_id" = e."Id"
JOIN "FranjasHorarias" f ON s."Franja_id" = f."Id"
LEFT JOIN "Cohortes" c ON s."Asignatura_id" = c."asignatura_id"
WHERE s."Horario_id" = '{horario_id}' AND s."Modalidad" = 'Presencial'
GROUP BY e."Id", e."Nombre", e."Capacidad", f."Dia_semana", f."hora_inicio"
HAVING COALESCE(SUM(c."cantidad_estudiantes"), 0) > e."Capacidad";
```

**Resultado esperado:** **Vacío** (0 filas = capacidades OK ✅)

---

### query_HC_S03_tipo_espacio_requerido
**Caso de uso:** Verificar que laboratorios estén asignados a espacios de tipo "Laboratorio"
**Restricción verificada:** HC-S03 - Asignaturas con RequiereLab=true en espacios tipo Laboratorio
**Documento:** [hard-constraints.md#HC-S03](../../docs/business-rules/hard-constraints.md#HC-S03)
**Parámetros:** `{horario_id}`

```sql
SELECT s."Id", a."Codigo", a."Nombre", a."RequiereLab", 
       e."Nombre" as espacio, e."Tipo"
FROM "Sesiones" s
JOIN "Asignaturas" a ON s."Asignatura_id" = a."Id"
JOIN "Espacios" e ON s."Espacio_id" = e."Id"
WHERE s."Horario_id" = '{horario_id}'
  AND a."RequiereLab" = true 
  AND e."Tipo" != 'Laboratorio';
```

**Resultado esperado:** **Vacío** (todas las asignaturas de lab en laboratorios ✅)

---

### query_HC_S04_sesiones_virtuales_sin_espacio
**Caso de uso:** Verificar que sesiones virtuales no tengan espacio asignado
**Restricción verificada:** HC-S04 - Sesiones virtuales con Espacio_id = NULL
**Documento:** [hard-constraints.md#HC-S04](../../docs/business-rules/hard-constraints.md#HC-S04)
**Parámetros:** `{horario_id}`

```sql
SELECT s."Id", a."Codigo", s."Modalidad", s."Espacio_id"
FROM "Sesiones" s
JOIN "Asignaturas" a ON s."Asignatura_id" = a."Id"
WHERE s."Horario_id" = '{horario_id}'
  AND s."Modalidad" = 'Virtual' 
  AND s."Espacio_id" IS NOT NULL;
```

**Resultado esperado:** **Vacío** (sesiones virtuales sin espacio ✅)

---

### query_HC_I01_docente_multitarea
**Caso de uso:** Detectar docentes con 2+ sesiones en mismo espacio de tiempo
**Restricción verificada:** HC-I01 - Un docente NO puede tener dos sesiones simultáneamente
**Documento:** [hard-constraints.md#HC-I01](../../docs/business-rules/hard-constraints.md#HC-I01)
**Parámetros:** `{horario_id}`

```sql
SELECT 
    d."Nombre" || ' ' || d."Apellido" as docente,
    f."Dia_semana",
    f."hora_inicio",
    f."Hora_fin",
    COUNT(*) as sesiones_simultáneas,
    STRING_AGG(a."Codigo", ', ') as asignaturas
FROM "Sesiones" s
JOIN "Docentes" d ON s."Docente_id" = d."Id"
JOIN "FranjasHorarias" f ON s."Franja_id" = f."Id"
JOIN "Asignaturas" a ON s."Asignatura_id" = a."Id"
WHERE s."Horario_id" = '{horario_id}'
GROUP BY d."Id", d."Nombre", d."Apellido", f."Dia_semana", f."hora_inicio", f."Hora_fin"
HAVING COUNT(*) > 1
ORDER BY d."Nombre";
```

**Resultado esperado:** **Vacío** (sin docentes bidocentes ✅)

---

### query_HC_I02_disponibilidad_docente
**Caso de uso:** Detectar sesiones con docentes fuera de disponibilidad
**Restricción verificada:** HC-I02 - Docente solo puede asignarse a franjas en DisponibilidadDocente
**Documento:** [hard-constraints.md#HC-I02](../../docs/business-rules/hard-constraints.md#HC-I02)
**Parámetros:** `{horario_id}`

```sql
SELECT s."Id", s."Docente_id", s."Franja_id",
       d."Nombre" || ' ' || d."Apellido" as docente,
       f."Dia_semana", f."hora_inicio"
FROM "Sesiones" s
JOIN "Docentes" d ON s."Docente_id" = d."Id"
JOIN "FranjasHorarias" f ON s."Franja_id" = f."Id"
WHERE s."Horario_id" = '{horario_id}'
  AND NOT EXISTS (
    SELECT 1 FROM "DisponibilidadDocente" dd
    WHERE dd."Docente_id" = s."Docente_id" 
    AND dd."Franja_id" = s."Franja_id"
  );
```

**Resultado esperado:** **Vacío** (todos los docentes disponibles en sus franjas ✅)

---

### query_HC_I03_horas_maximas_docente
**Caso de uso:** Detectar docentes con asignación mayor a su máximo contratado
**Restricción verificada:** HC-I03 - Docente NO puede exceder Maximo_hrs_semanales
**Documento:** [hard-constraints.md#HC-I03](../../docs/business-rules/hard-constraints.md#HC-I03)
**Parámetros:** `{horario_id}`

```sql
SELECT 
    d."Id",
    d."Nombre" || ' ' || d."Apellido" as docente,
    d."Maximo_hrs_semanales",
    SUM(s."Duracion_horas") as horas_asignadas,
    (SUM(s."Duracion_horas") - d."Maximo_hrs_semanales") as exceso,
    CASE 
        WHEN SUM(s."Duracion_horas") > d."Maximo_hrs_semanales" THEN '❌ EXCEDIDO'
        ELSE '✅ OK'
    END as estado
FROM "Sesiones" s
JOIN "Docentes" d ON s."Docente_id" = d."Id"
WHERE s."Horario_id" = '{horario_id}'
GROUP BY d."Id", d."Nombre", d."Apellido", d."Maximo_hrs_semanales"
HAVING SUM(s."Duracion_horas") > d."Maximo_hrs_semanales"
ORDER BY exceso DESC;
```

**Resultado esperado:** **Vacío** (todos docentes dentro de límite ✅)

---

### query_HC_T01_fuera_horario_operacion
**Caso de uso:** Detectar sesiones fuera de horario de operación (07:00–21:30)
**Restricción verificada:** HC-T01 - Sesiones entre 07:00 y 21:30
**Documento:** [hard-constraints.md#HC-T01](../../docs/business-rules/hard-constraints.md#HC-T01)
**Parámetros:** `{horario_id}`

```sql
SELECT s."Id", a."Codigo", f."Dia_semana", f."hora_inicio", f."Hora_fin"
FROM "Sesiones" s
JOIN "Asignaturas" a ON s."Asignatura_id" = a."Id"
JOIN "FranjasHorarias" f ON s."Franja_id" = f."Id"
WHERE s."Horario_id" = '{horario_id}'
  AND (f."hora_inicio" < '07:00:00' OR f."Hora_fin" > '21:30:00');
```

**Resultado esperado:** **Vacío** (todas las sesiones dentro de horario ✅)

---

### query_HC_T02_laboratorio_tiempo_minimo
**Caso de uso:** Detectar laboratorios que empiezan después de 19:30
**Restricción verificada:** HC-T02 - Laboratorios deben terminar a las 21:30 como máximo
**Documento:** [hard-constraints.md#HC-T02](../../docs/business-rules/hard-constraints.md#HC-T02)
**Parámetros:** `{horario_id}`

```sql
SELECT s."Id", a."Codigo", a."Nombre", f."hora_inicio", f."Hora_fin"
FROM "Sesiones" s
JOIN "Asignaturas" a ON s."Asignatura_id" = a."Id"
JOIN "FranjasHorarias" f ON s."Franja_id" = f."Id"
WHERE s."Horario_id" = '{horario_id}'
  AND a."RequiereLab" = true 
  AND f."hora_inicio" > '19:30:00';
```

**Resultado esperado:** **Vacío** (laboratorios con tiempo suficiente ✅)

---

### query_HC_C01_cohorte_sin_solapamiento
**Caso de uso:** Detectar cohortes con dos sesiones en el mismo espacio de tiempo
**Restricción verificada:** HC-C01 - Una cohorte NO puede tener dos sesiones simultáneas
**Documento:** [hard-constraints.md#HC-C01](../../docs/business-rules/hard-constraints.md#HC-C01)
**Parámetros:** `{horario_id}`
**Nota:** Requiere tabla Cohortes o lógica que agrupe por asignatura

```sql
SELECT s1."Id" as sesion1, s2."Id" as sesion2,
       a1."Codigo", a2."Codigo",
       f."Dia_semana", f."hora_inicio"
FROM "Sesiones" s1
JOIN "Sesiones" s2 ON 
    s1."Franja_id" = s2."Franja_id" AND
    s1."Horario_id" = s2."Horario_id" AND
    s1."Id" < s2."Id"
JOIN "FranjasHorarias" f ON s1."Franja_id" = f."Id"
JOIN "Asignaturas" a1 ON s1."Asignatura_id" = a1."Id"
JOIN "Asignaturas" a2 ON s2."Asignatura_id" = a2."Id"
WHERE s1."Horario_id" = '{horario_id}';
```

**Resultado esperado:** **Vacío** (sin solapamientos de cohorte ✅)

---

### query_HC_C02_horas_totales_cohorte
**Caso de uso:** Verificar que horas programadas de asignatura coincidan con BloqueSemanales
**Restricción verificada:** HC-C02 - Horas programadas = Asignatura.BloqueSemanales
**Documento:** [hard-constraints.md#HC-C02](../../docs/business-rules/hard-constraints.md#HC-C02)
**Parámetros:** `{horario_id}`

```sql
SELECT a."Codigo", a."Nombre", a."BloqueSemanales",
       COALESCE(SUM(s."Duracion_horas"), 0) as horas_programadas,
       CASE 
           WHEN COALESCE(SUM(s."Duracion_horas"), 0) = a."BloqueSemanales" THEN '✅ OK'
           ELSE '❌ DIFERENCIA'
       END as estado
FROM "Asignaturas" a
LEFT JOIN "Sesiones" s ON a."Id" = s."Asignatura_id" AND s."Horario_id" = '{horario_id}'
GROUP BY a."Id", a."Codigo", a."Nombre", a."BloqueSemanales"
HAVING COALESCE(SUM(s."Duracion_horas"), 0) != a."BloqueSemanales"
ORDER BY a."Codigo";
```

**Resultado esperado:** **Vacío** (todas asignaturas con horas correctas ✅)

---

## Queries de Reportes: Carga y Utilización

### query_carga_docente_por_semana
**Caso de uso:** Reporte de utilización de docentes
**Parámetros:** `{horario_id}`

```sql
SELECT 
    d."Id",
    d."Nombre" || ' ' || d."Apellido" as docente,
    d."Correo",
    d."Maximo_hrs_semanales",
    ROUND(SUM(s."Duracion_horas")::NUMERIC, 2) as horas_asignadas,
    ROUND((SUM(s."Duracion_horas")::NUMERIC / d."Maximo_hrs_semanales") * 100, 1) as porcentaje_utilizacion,
    COUNT(DISTINCT s."Franja_id") as franjas_asignadas
FROM "Sesiones" s
JOIN "Docentes" d ON s."Docente_id" = d."Id"
WHERE s."Horario_id" = '{horario_id}'
GROUP BY d."Id", d."Nombre", d."Apellido", d."Correo", d."Maximo_hrs_semanales"
ORDER BY porcentaje_utilizacion DESC;
```

---

### query_ocupacion_espacios
**Caso de uso:** Reporte de ocupación de espacios
**Parámetros:** `{horario_id}`

```sql
SELECT 
    e."Id",
    e."Nombre" as espacio,
    e."Tipo",
    e."Capacidad",
    e."Ubicacion",
    COUNT(DISTINCT s."Id") as sesiones_asignadas,
    COUNT(DISTINCT s."Franja_id") as franjas_ocupadas,
    STRING_AGG(DISTINCT f."Dia_semana", ', ' ORDER BY f."Dia_semana") as dias_usados
FROM "Sesiones" s
JOIN "Espacios" e ON s."Espacio_id" = e."Id"
JOIN "FranjasHorarias" f ON s."Franja_id" = f."Id"
WHERE s."Horario_id" = '{horario_id}' AND s."Espacio_id" IS NOT NULL
GROUP BY e."Id", e."Nombre", e."Tipo", e."Capacidad", e."Ubicacion"
ORDER BY sesiones_asignadas DESC;
```

---

### query_distribucion_horaria_por_dia
**Caso de uso:** Reporte de distribución temporal
**Parámetros:** `{horario_id}`

```sql
SELECT 
    f."Dia_semana",
    f."hora_inicio",
    f."Hora_fin",
    COUNT(*) as sesiones,
    COUNT(DISTINCT s."Asignatura_id") as asignaturas_distintas,
    COUNT(DISTINCT s."Docente_id") as docentes_distintos,
    ROUND(AVG(s."Duracion_horas")::NUMERIC, 1) as duracion_promedio
FROM "Sesiones" s
JOIN "FranjasHorarias" f ON s."Franja_id" = f."Id"
WHERE s."Horario_id" = '{horario_id}'
GROUP BY f."Dia_semana", f."hora_inicio", f."Hora_fin"
ORDER BY 
    CASE f."Dia_semana"
        WHEN 'Lunes' THEN 1 WHEN 'Martes' THEN 2 WHEN 'Miércoles' THEN 3
        WHEN 'Jueves' THEN 4 WHEN 'Viernes' THEN 5 WHEN 'Sábado' THEN 6
        WHEN 'Domingo' THEN 7
    END,
    f."hora_inicio";
```

---

### query_sesiones_virtuales_vs_presenciales
**Caso de uso:** Análisis de modalidad
**Parámetros:** `{horario_id}`

```sql
SELECT 
    s."Modalidad",
    COUNT(*) as cantidad_sesiones,
    COUNT(DISTINCT s."Asignatura_id") as asignaturas,
    COUNT(DISTINCT s."Docente_id") as docentes,
    ROUND((COUNT(*)::NUMERIC / (SELECT COUNT(*) FROM "Sesiones" WHERE "Horario_id" = '{horario_id}')) * 100, 1) as porcentaje
FROM "Sesiones" s
WHERE s."Horario_id" = '{horario_id}'
GROUP BY s."Modalidad";
```

---

### query_distribucion_alternancia
**Caso de uso:** Análisis de tipo de alternancia
**Parámetros:** `{horario_id}`

```sql
SELECT 
    s."Tipo_alternancia",
    COUNT(*) as sesiones,
    COUNT(DISTINCT s."Asignatura_id") as asignaturas,
    COUNT(DISTINCT s."Docente_id") as docentes,
    ROUND((COUNT(*)::NUMERIC / (SELECT COUNT(*) FROM "Sesiones" WHERE "Horario_id" = '{horario_id}')) * 100, 1) as porcentaje
FROM "Sesiones" s
WHERE s."Horario_id" = '{horario_id}'
GROUP BY s."Tipo_alternancia"
ORDER BY porcentaje DESC;
```

---

### query_dashboard_resumen_horario
**Caso de uso:** Resumen ejecutivo completo de un horario
**Parámetros:** `{horario_id}`

```sql
SELECT 
    h."Id",
    h."Semestre",
    h."Estado",
    TO_CHAR(h."Generado_en", 'YYYY-MM-DD HH24:MI:SS') as generado_en,
    h."Hard_constraint_violations",
    h."Soft_constraint_fitness_score",
    COUNT(DISTINCT s."Id") as total_sesiones,
    COUNT(DISTINCT s."Asignatura_id") as asignaturas_unicas,
    COUNT(DISTINCT s."Docente_id") as docentes_asignados,
    COUNT(DISTINCT s."Espacio_id") FILTER (WHERE s."Espacio_id" IS NOT NULL) as espacios_usados,
    COUNT(CASE WHEN s."Modalidad" = 'Presencial' THEN 1 END) as sesiones_presenciales,
    COUNT(CASE WHEN s."Modalidad" = 'Virtual' THEN 1 END) as sesiones_virtuales,
    CASE 
        WHEN h."Hard_constraint_violations" = 0 THEN '✅ VÁLIDO'
        ELSE '❌ INVÁLIDO (' || h."Hard_constraint_violations" || ' HC violations)'
    END as estado_validacion
FROM "Horarios" h
LEFT JOIN "Sesiones" s ON h."Id" = s."Horario_id"
WHERE h."Id" = '{horario_id}'
GROUP BY h."Id", h."Semestre", h."Estado", h."Generado_en", 
         h."Hard_constraint_violations", h."Soft_constraint_fitness_score";
```

---

## Uso de Este Repositorio

**Convención:** Todos los archivos .md del skill usan la forma `[query_ID](soea-queries-repository.md#query_ID)` para enlazar.

**En código .NET:** La capa `SOEA.Infrastructure.Data` puede incluir estos queries como constantes o métodos.

**DRY:** Una query = un único lugar de edición.
