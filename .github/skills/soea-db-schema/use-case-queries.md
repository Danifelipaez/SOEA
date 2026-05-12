---
name: soea-use-case-queries
description: "Índice de consultas por caso de uso SOEA. Úsalo para: navegar a la query correcta por caso, entender qué ejecutar en cada etapa de trabajo."
---

# Guía: Queries por Caso de Uso

> **Referencia completa de queries:** [soea-queries-repository.md](soea-queries-repository.md)  
> **Flujos completos:** [soea-use-cases.md](soea-use-cases.md)

---

## 🚀 Caso 1: Preparar Datos

| # | Actividad | Query |
|---|-----------|-------|
| 1.1 | Obtener asignaturas de programa | [query_get_asignaturas_programa](soea-queries-repository.md#query_get_asignaturas_programa) |
| 1.2 | Obtener docentes con disponibilidad | [query_get_docentes_disponibles](soea-queries-repository.md#query_get_docentes_disponibles) |
| 1.3 | Calcular espacios requeridos | [query_calcular_espacios_requeridos](soea-queries-repository.md#query_calcular_espacios_requeridos) |
| 1.4 | Identificar asignaturas de laboratorio | [query_get_asignaturas_laboratorio](soea-queries-repository.md#query_get_asignaturas_laboratorio) |

**Flujo:** [soea-use-cases.md#caso-1-preparar-datos-para-generar-horario](soea-use-cases.md#caso-1-preparar-datos-para-generar-horario)

---

## ✅ Caso 2: Validar Horario (Restricciones Duras)

| # | Restricción | Query |
|---|-------------|-------|
| 2.1 | HC-S01: Conflictos de espacio | [query_HC_S01_detectar_conflictos_espacio](soea-queries-repository.md#query_HC_S01_detectar_conflictos_espacio) |
| 2.2 | HC-S02: Exceso de capacidad | [query_HC_S02_exceso_capacidad](soea-queries-repository.md#query_HC_S02_exceso_capacidad) |
| 2.3 | HC-S03: Tipo de espacio requerido | [query_HC_S03_tipo_espacio_requerido](soea-queries-repository.md#query_HC_S03_tipo_espacio_requerido) |
| 2.4 | HC-S04: Sesiones virtuales sin espacio | [query_HC_S04_sesiones_virtuales_sin_espacio](soea-queries-repository.md#query_HC_S04_sesiones_virtuales_sin_espacio) |
| 2.5 | HC-I01: Docente multitarea | [query_HC_I01_docente_multitarea](soea-queries-repository.md#query_HC_I01_docente_multitarea) |
| 2.6 | HC-I02: Disponibilidad docente | [query_HC_I02_disponibilidad_docente](soea-queries-repository.md#query_HC_I02_disponibilidad_docente) |
| 2.7 | HC-I03: Horas máximas | [query_HC_I03_horas_maximas_docente](soea-queries-repository.md#query_HC_I03_horas_maximas_docente) |
| 2.8 | HC-T01: Horario operación | [query_HC_T01_fuera_horario_operacion](soea-queries-repository.md#query_HC_T01_fuera_horario_operacion) |
| 2.9 | HC-T02: Labs a tiempo | [query_HC_T02_laboratorio_tiempo_minimo](soea-queries-repository.md#query_HC_T02_laboratorio_tiempo_minimo) |
| 2.10 | HC-C01: Cohorte sin solapamiento | [query_HC_C01_cohorte_sin_solapamiento](soea-queries-repository.md#query_HC_C01_cohorte_sin_solapamiento) |
| 2.11 | HC-C02: Horas totales asignatura | [query_HC_C02_horas_totales_cohorte](soea-queries-repository.md#query_HC_C02_horas_totales_cohorte) |

**Flujo completo:** [soea-use-cases.md#caso-2-validar-horario-generado](soea-use-cases.md#caso-2-validar-horario-generado)  
**Documentación:** [validation-rules.md](validation-rules.md)  
**Especificación:** [hard-constraints.md](../../docs/business-rules/hard-constraints.md)

---

## 📊 Caso 3: Generar Reportes

| # | Reporte | Query |
|---|---------|-------|
| 3.1 | Carga de docentes | [query_carga_docente_por_semana](soea-queries-repository.md#query_carga_docente_por_semana) |
| 3.2 | Ocupación de espacios | [query_ocupacion_espacios](soea-queries-repository.md#query_ocupacion_espacios) |
| 3.3 | Distribución horaria | [query_distribucion_horaria_por_dia](soea-queries-repository.md#query_distribucion_horaria_por_dia) |

**Flujo:** [soea-use-cases.md#caso-3-generar-reportes-de-carga](soea-use-cases.md#caso-3-generar-reportes-de-carga)

---

## 🔄 Caso 4: Análisis de Alternancia

| # | Análisis | Query |
|---|----------|-------|
| 4.1 | Virtual vs Presencial | [query_sesiones_virtuales_vs_presenciales](soea-queries-repository.md#query_sesiones_virtuales_vs_presenciales) |
| 4.2 | Distribución de alternancia | [query_distribucion_alternancia](soea-queries-repository.md#query_distribucion_alternancia) |

**Flujo:** [soea-use-cases.md#caso-4-análisis-de-alternancia](soea-use-cases.md#caso-4-análisis-de-alternancia)

---

## 📈 Caso 5: Dashboard Ejecutivo

| # | Resumen | Query |
|---|---------|-------|
| 5.1 | Resumen completo del horario | [query_dashboard_resumen_horario](soea-queries-repository.md#query_dashboard_resumen_horario) |

**Flujo:** [soea-use-cases.md#caso-5-dashboard-ejecutivo](soea-use-cases.md#caso-5-dashboard-ejecutivo)

---

## 🎯 Búsqueda Rápida por Necesidad

**Necesito...** → **Ejecutar caso:**

| Necesito | Caso | Queries |
|----------|------|---------|
| Preparar generación | 1 | 1.1, 1.2, 1.3 |
| Validar rápido | 2 | 2.1, 2.2, 2.5, 2.7 |
| Validar completo | 2 | 2.1 a 2.11 |
| Ver carga de docentes | 3 | 3.1 |
| Ver ocupación espacios | 3 | 3.2 |
| Ver distribución temporal | 3 | 3.3 |
| Analizar alternancia | 4 | 4.1, 4.2 |
| Resumen ejecutivo | 5 | 5.1 |
| Todo completo | 1+2+3+4+5 | Todas (18 queries) |

---

## Navegación Rápida

- **¿Cuál es la query más importante?** → [query_HC_S01_detectar_conflictos_espacio](soea-queries-repository.md#query_HC_S01_detectar_conflictos_espacio) (detecta 80% de problemas)
- **¿Por dónde empiezo?** → [Caso 1: Preparar Datos](soea-use-cases.md#caso-1-preparar-datos-para-generar-horario)
- **¿Cómo valido?** → [Caso 2: Validación](soea-use-cases.md#caso-2-validar-horario-generado) (seguir orden 2.1 → 2.11)
- **¿Qué reportes hay?** → [Caso 3: Reportes](soea-use-cases.md#caso-3-generar-reportes-de-carga)
- **¿Está listo para publicar?** → [Caso 5: Dashboard](soea-use-cases.md#caso-5-dashboard-ejecutivo)

---

## Fuentes de Verdad

| Elemento | Ubicación |
|----------|-----------|
| Queries SQL | [soea-queries-repository.md](soea-queries-repository.md) |
| Casos de uso | [soea-use-cases.md](soea-use-cases.md) |
| Validación HC | [validation-rules.md](validation-rules.md) |
| Especificación HC | [hard-constraints.md](../../docs/business-rules/hard-constraints.md) |
| Especificación SC | [soft-constraints.md](../../docs/business-rules/soft-constraints.md) |
| Implementación validación | `SOEA.Application.Services.ConstraintValidator` |
