---
name: soea-use-cases
description: "Flujos de trabajo SOEA con referencias a queries SQL. Úsalo para: entender casos de uso, ver qué queries ejecutar en cada etapa, seguir el proceso de preparación, validación, reportes y análisis."
---

# Casos de Uso: Workflow SOEA

> **Referencia:** Todas las queries están en [soea-queries-repository.md](soea-queries-repository.md)

---

## Caso 1: Preparar Datos para Generar Horario

**Objetivo:** Verificar que los datos base sean completos antes de ejecutar el motor de optimización.

### 1.1 Obtener Asignaturas de un Programa
**Query:** [query_get_asignaturas_programa](soea-queries-repository.md#query_get_asignaturas_programa)

**Flujo:**
1. Usuario selecciona programa
2. Ejecutar query con `{programa_id}`
3. Validar que:
   - [ ] Todas tienen `Alternancia` definida (TypeA, TypeB, NonAlternating)
   - [ ] `BloqueSemanales > 0`
   - [ ] Se devuelven al menos 4-6 asignaturas

**Acción:** Si hay errores → Revisar datos maestros en UI antes de continuar

---

### 1.2 Obtener Docentes con Disponibilidad
**Query:** [query_get_docentes_disponibles](soea-queries-repository.md#query_get_docentes_disponibles)

**Flujo:**
1. Ejecutar query sin parámetros
2. Validar que:
   - [ ] Hay al menos 5-10 docentes
   - [ ] Cada docente tiene al menos 1-2 franjas disponibles
   - [ ] `Maximo_hrs_semanales` es coherente (16-20 típico)

**Acción:** Si docente sin disponibilidad → Asignarle franjas en UI

---

### 1.3 Calcular Capacidad de Infraestructura
**Query:** [query_calcular_espacios_requeridos](soea-queries-repository.md#query_calcular_espacios_requeridos)

**Flujo:**
1. Ejecutar query sin parámetros
2. Validar que:
   - [ ] Hay espacios de tipo "Aula" (mayoría)
   - [ ] Hay espacios de tipo "Laboratorio" (si asignaturas requieren)
   - [ ] Capacidades son realistas (20-50 pax típico)

**Acción:** Si falta "Laboratorio" pero hay asignaturas que lo requieren → Problema ❌

---

### 1.4 Identificar Asignaturas de Laboratorio
**Query:** [query_get_asignaturas_laboratorio](soea-queries-repository.md#query_get_asignaturas_laboratorio)

**Flujo:**
1. Ejecutar query sin parámetros
2. Comparar:
   - Cantidad de asignaturas con `RequiereLab = true`
   - Vs. cantidad de laboratorios disponibles en query 1.3
3. Validar ratio: idealmente 1 lab por 2-3 asignaturas de lab

**Acción:** Si no hay laboratorios → No se pueden programar asignaturas de lab ❌

---

### ✅ Checklist Pre-Generación (Caso 1)
- [ ] Query 1.1: Al menos 4 asignaturas con alternancia definida
- [ ] Query 1.2: Al menos 5 docentes con disponibilidad
- [ ] Query 1.3: Espacios disponibles (aulas + labs)
- [ ] Query 1.4: Ratio laboratorios coherente
- [ ] Ir a **Caso 2** si todo OK

---

## Caso 2: Validar Horario Generado

**Objetivo:** Verificar que el horario cumple todas las restricciones duras (HC-S, HC-I, HC-T, HC-C).

> **Documento referencia:** [hard-constraints.md](../../docs/business-rules/hard-constraints.md)

### 2.1 Detectar Conflictos de Espacio (HC-S01)
**Query:** [query_HC_S01_detectar_conflictos_espacio](soea-queries-repository.md#query_HC_S01_detectar_conflictos_espacio)

**Flujo:**
1. Ejecutar query con `{horario_id}`
2. **Resultado esperado:** Vacío (0 filas)
3. Si hay resultados:
   - Identificar sesiones1 y sesion2 que comparten espacio/tiempo
   - Revisar tipos de alternancia (deben ser TypeA/TypeB mix, no mismo tipo)
   - **Acción:** Horario inválido ❌

---

### 2.2 Verificar Capacidades (HC-S02)
**Query:** [query_HC_S02_exceso_capacidad](soea-queries-repository.md#query_HC_S02_exceso_capacidad)

**Flujo:**
1. Ejecutar query con `{horario_id}`
2. **Resultado esperado:** Vacío (0 filas)
3. Si hay resultados:
   - Mostrar espacios sobre capacidad
   - Calcular diferencia: `estudiantes_totales - capacidad`
   - **Acción:** Reasignar sesiones a espacios más grandes o dividir cohorte ❌

---

### 2.3 Verificar Tipo de Espacio (HC-S03)
**Query:** [query_HC_S03_tipo_espacio_requerido](soea-queries-repository.md#query_HC_S03_tipo_espacio_requerido)

**Flujo:**
1. Ejecutar query con `{horario_id}`
2. **Resultado esperado:** Vacío (0 filas)
3. Si hay resultados:
   - Mostrar asignaturas de laboratorio en espacios tipo "Aula"
   - **Acción:** Reasignar a laboratorio disponible ❌

---

### 2.4 Verificar Sesiones Virtuales (HC-S04)
**Query:** [query_HC_S04_sesiones_virtuales_sin_espacio](soea-queries-repository.md#query_HC_S04_sesiones_virtuales_sin_espacio)

**Flujo:**
1. Ejecutar query con `{horario_id}`
2. **Resultado esperado:** Vacío (0 filas)
3. Si hay resultados:
   - Mostrar sesiones virtuales con espacio asignado (contradicción)
   - **Acción:** Remover espacio o cambiar modalidad a Presencial ❌

---

### 2.5 Detectar Docentes Sobrecargados (HC-I01)
**Query:** [query_HC_I01_docente_multitarea](soea-queries-repository.md#query_HC_I01_docente_multitarea)

**Flujo:**
1. Ejecutar query con `{horario_id}`
2. **Resultado esperado:** Vacío (0 filas)
3. Si hay resultados:
   - Mostrar docente con N sesiones simultáneas
   - **Acción:** Reasignar alguna sesión a otro docente ❌

---

### 2.6 Verificar Disponibilidad Docente (HC-I02)
**Query:** [query_HC_I02_disponibilidad_docente](soea-queries-repository.md#query_HC_I02_disponibilidad_docente)

**Flujo:**
1. Ejecutar query con `{horario_id}`
2. **Resultado esperado:** Vacío (0 filas)
3. Si hay resultados:
   - Mostrar docente asignado a franja NO disponible
   - **Acción:** Reasignar a franja en disponibilidad docente ❌

---

### 2.7 Verificar Horas Máximas Docente (HC-I03)
**Query:** [query_HC_I03_horas_maximas_docente](soea-queries-repository.md#query_HC_I03_horas_maximas_docente)

**Flujo:**
1. Ejecutar query con `{horario_id}`
2. **Resultado esperado:** Vacío (0 filas)
3. Si hay resultados:
   - Mostrar docentes con exceso de horas
   - Calcular cuántas horas remover
   - **Acción:** Reasignar sesiones a otros docentes ❌

---

### 2.8 Verificar Horario Operación (HC-T01)
**Query:** [query_HC_T01_fuera_horario_operacion](soea-queries-repository.md#query_HC_T01_fuera_horario_operacion)

**Flujo:**
1. Ejecutar query con `{horario_id}`
2. **Resultado esperado:** Vacío (0 filas)
3. Si hay resultados:
   - Mostrar sesiones antes de 07:00 o después de 21:30
   - **Acción:** Reasignar a franja dentro de 07:00–21:30 ❌

---

### 2.9 Verificar Laboratorios No Tarde (HC-T02)
**Query:** [query_HC_T02_laboratorio_tiempo_minimo](soea-queries-repository.md#query_HC_T02_laboratorio_tiempo_minimo)

**Flujo:**
1. Ejecutar query con `{horario_id}`
2. **Resultado esperado:** Vacío (0 filas)
3. Si hay resultados:
   - Mostrar laboratorios que empiezan después de 19:30
   - **Acción:** Reasignar a franja más temprana ❌

---

### 2.10 Verificar Cohorte Sin Solapamiento (HC-C01)
**Query:** [query_HC_C01_cohorte_sin_solapamiento](soea-queries-repository.md#query_HC_C01_cohorte_sin_solapamiento)

**Flujo:**
1. Ejecutar query con `{horario_id}`
2. **Resultado esperado:** Vacío (0 filas)
3. Si hay resultados:
   - Mostrar dos sesiones de misma cohorte en mismo tiempo
   - **Acción:** Reasignar una a franja diferente ❌

---

### 2.11 Verificar Horas Totales Asignatura (HC-C02)
**Query:** [query_HC_C02_horas_totales_cohorte](soea-queries-repository.md#query_HC_C02_horas_totales_cohorte)

**Flujo:**
1. Ejecutar query con `{horario_id}`
2. **Resultado esperado:** Vacío (0 filas)
3. Si hay resultados:
   - Mostrar asignaturas con horas incorrectas
   - Ejemplo: `Programación I requiere 4 horas pero se programaron 3`
   - **Acción:** Agregar/remover sesión o ajustar duración ❌

---

### ✅ Checklist Validación Completa (Caso 2)

**Ejecutar en orden:**
- [ ] Query 2.1: HC-S01 - Sin conflictos de espacio
- [ ] Query 2.2: HC-S02 - Sin exceso de capacidad
- [ ] Query 2.3: HC-S03 - Laboratorios en tipo correcto
- [ ] Query 2.4: HC-S04 - Virtuales sin espacio
- [ ] Query 2.5: HC-I01 - Sin docentes bidocentes
- [ ] Query 2.6: HC-I02 - Docentes en disponibilidad
- [ ] Query 2.7: HC-I03 - Docentes sin exceso de horas
- [ ] Query 2.8: HC-T01 - En horario de operación
- [ ] Query 2.9: HC-T02 - Labs con tiempo suficiente
- [ ] Query 2.10: HC-C01 - Cohortes sin solapamiento
- [ ] Query 2.11: HC-C02 - Horas totales correctas

**Si todo OK:** Horario es válido ✅ → Ir a **Caso 3**

---

## Caso 3: Generar Reportes de Carga

**Objetivo:** Entender cómo está siendo utilizada la infraestructura y recursos humanos.

### 3.1 Reporte: Carga de Docente
**Query:** [query_carga_docente_por_semana](soea-queries-repository.md#query_carga_docente_por_semana)

**Flujo:**
1. Ejecutar query con `{horario_id}`
2. Revisar tabla ordenada por `porcentaje_utilizacion` DESC
3. Interpretación:
   - 100%+ → Docente al máximo (verificar que cumple HC-I03)
   - 50-80% → Utilización normal
   - <50% → Docente subutilizado (oportunidad de optimización)

**Decisión:** ¿Hay distribución equitativa de carga?

---

### 3.2 Reporte: Ocupación de Espacios
**Query:** [query_ocupacion_espacios](soea-queries-repository.md#query_ocupacion_espacios)

**Flujo:**
1. Ejecutar query con `{horario_id}`
2. Revisar tabla por espacios más ocupados
3. Interpretación:
   - Espacios con 5+ sesiones → Altamente demandados
   - Espacios con 1-2 sesiones → Subutilizados
   - Días de uso → Distribución temporal

**Decisión:** ¿Se están aprovechando bien los espacios o hay ineficiencias?

---

### 3.3 Reporte: Distribución Horaria
**Query:** [query_distribucion_horaria_por_dia](soea-queries-repository.md#query_distribucion_horaria_por_dia)

**Flujo:**
1. Ejecutar query con `{horario_id}`
2. Revisar por franjas horarias
3. Interpretación:
   - Picos de carga (ej: 10:00 con 12 sesiones simultáneas)
   - Huecos (ej: 12:00-13:00 vacío - receso del mediodía)
   - Carga equilibrada por día

**Decisión:** ¿Hay balance temporal o hay sobrecarga en horarios específicos?

---

## Caso 4: Análisis de Alternancia

**Objetivo:** Entender la composición de modalidades y tipos de alternancia en el horario.

### 4.1 Reporte: Virtual vs Presencial
**Query:** [query_sesiones_virtuales_vs_presenciales](soea-queries-repository.md#query_sesiones_virtuales_vs_presenciales)

**Flujo:**
1. Ejecutar query con `{horario_id}`
2. Revisar porcentaje de Presencial vs Virtual
3. Interpretación:
   - Típicamente 70% Presencial, 30% Virtual (depende alternancia)
   - Si hay desbalance → Revisar si es intencional o error

**Decisión:** ¿La distribución presencial/virtual es la esperada?

---

### 4.2 Reporte: Distribución por Tipo de Alternancia
**Query:** [query_distribucion_alternancia](soea-queries-repository.md#query_distribucion_alternancia)

**Flujo:**
1. Ejecutar query con `{horario_id}`
2. Revisar TypeA vs TypeB vs NonAlternating
3. Interpretación:
   - TypeA ≈ TypeB (esperado si hay balance)
   - NonAlternating < 20% (típicamente solo labs)
   - Desbalance → Revisar si es intencional

**Decisión:** ¿La alternancia está bien distribuida?

---

## Caso 5: Dashboard Ejecutivo

**Objetivo:** Obtener resumen completo del horario de un vistazo.

### 5.1 Reporte: Resumen Ejecutivo
**Query:** [query_dashboard_resumen_horario](soea-queries-repository.md#query_dashboard_resumen_horario)

**Flujo:**
1. Ejecutar query con `{horario_id}`
2. Revisar campos clave:

```
Id: UUID único
Semestre: Identificación (ej: 2024-1)
Estado: borrador | validado | aprobado | activo
Hard_constraint_violations: DEBE SER 0 para válido ✅
Soft_constraint_fitness_score: Score de calidad (0-100, menor = mejor)
Total_sesiones: Cantidad de sesiones programadas
Asignaturas_unicas: Número de asignaturas
Docentes_asignados: Número de docentes
Espacios_usados: Número de espacios distintos
Presencial/Virtual: Distribución modalidad
Estado_validacion: ✅ VÁLIDO o ❌ INVÁLIDO
```

**Decisión:** ¿Horario está listo para publicar?

---

## Flujo Completo: Resumen Visual

```
┌─────────────────────────────────────────────────────────────┐
│ Caso 1: Preparar Datos                                      │
│ ├─ 1.1 Asignaturas del programa?                           │
│ ├─ 1.2 Docentes con disponibilidad?                        │
│ ├─ 1.3 Espacios suficientes?                               │
│ └─ 1.4 Laboratorios para labs?                             │
└──────────────┬────────────────────────────────────────────┘
               │ ✅ TODO OK
               ▼
┌─────────────────────────────────────────────────────────────┐
│ Ejecutar Motor de Optimización (3 fases)                   │
│ → Fase 1: Coloreado de grafos (conflictos)                │
│ → Fase 2: Constraint Programming (HC)                      │
│ → Fase 3: Algoritmo genético (SC)                          │
└──────────────┬────────────────────────────────────────────┘
               │ Horario generado
               ▼
┌─────────────────────────────────────────────────────────────┐
│ Caso 2: Validar (11 queries HC)                            │
│ ├─ 2.1-2.4 Restricciones de espacio (S)                    │
│ ├─ 2.5-2.7 Restricciones de docente (I)                    │
│ ├─ 2.8-2.9 Restricciones de tiempo (T)                     │
│ └─ 2.10-2.11 Restricciones de cohorte (C)                  │
└──────────────┬────────────────────────────────────────────┘
               │ ✅ TODO VÁLIDO (0 HC violations)
               ▼
┌─────────────────────────────────────────────────────────────┐
│ Caso 3: Reportes (4 queries)                               │
│ ├─ 3.1 Carga de docentes                                   │
│ ├─ 3.2 Ocupación de espacios                               │
│ ├─ 3.3 Distribución horaria                                │
│ └─ Revisar utilización de recursos                         │
└──────────────┬────────────────────────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────────────────────────┐
│ Caso 4: Análisis de Alternancia (3 queries)                │
│ ├─ 4.1 Virtual vs Presencial                               │
│ ├─ 4.2 TypeA vs TypeB vs NonAlternating                     │
│ └─ Validar distribución de modalidades                      │
└──────────────┬────────────────────────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────────────────────────┐
│ Caso 5: Dashboard Ejecutivo (1 query)                       │
│ └─ Resumen completo: ✅ VÁLIDO y listo para publicar       │
└─────────────────────────────────────────────────────────────┘
```

---

## Combinación de Casos: Ejemplos

### Ejemplo 1: Usuario quiere "validar rápido"
→ Ejecutar **Caso 2** (2.1 + 2.2 + 2.5 + 2.7 = 4 queries críticas)

### Ejemplo 2: Usuario quiere "análisis profundo"
→ Ejecutar **Caso 2** (todas) + **Caso 3** (todas) + **Caso 4** (todas) = 18 queries

### Ejemplo 3: Usuario quiere "solo reportes"
→ Ejecutar **Caso 3** (carga, ocupación, distribución) = 3 queries

### Ejemplo 4: Usuario quiere "preparación"
→ Ejecutar **Caso 1** (1.1 + 1.2 + 1.3) = 3 queries
