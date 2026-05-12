---
name: soea-validation-rules
description: "Guía de validación de restricciones duras SOEA. Úsalo para: ejecutar queries de validación, interpretar resultados, verificar que horario cumple HC-S01–HC-C02, entender qué significa cada restricción."
---

# Validación de Restricciones: Guía Práctica

> **Fuentes de verdad:**
> - Especificación de restricciones → [docs/business-rules/hard-constraints.md](../../docs/business-rules/hard-constraints.md)
> - Implementación de validación → `SOEA.Application.Services.ConstraintValidator`
> - Queries SQL → [soea-queries-repository.md](soea-queries-repository.md)

---

## Restricciones de Espacio (HC-S)

### HC-S01: Conflicto de Ocupación de Espacio

**Especificación:** [hard-constraints.md#HC-S01](../../docs/business-rules/hard-constraints.md#HC-S01)

> Un espacio NO puede alojar dos sesiones en el mismo espacio de tiempo si ambas cohortes son Tipo A, ambas son Tipo B, o alguna es NonAlternating.

**Query:** [query_HC_S01_detectar_conflictos_espacio](soea-queries-repository.md#query_HC_S01_detectar_conflictos_espacio)

**Validación:**
- [ ] Ejecutar query con `{horario_id}`
- [ ] Resultado debe estar **vacío** (0 filas) ✅
- [ ] Si hay filas: Conflicto detectado ❌

**Interpretación:** 
- `sesion1` y `sesion2` comparten `espacio` en `dia_semana` + `hora_inicio`
- Si ambas son mismo `Tipo_alternancia` → VIOLACIÓN ❌

---

### HC-S02: Exceso de Capacidad de Espacio

**Especificación:** [hard-constraints.md#HC-S02](../../docs/business-rules/hard-constraints.md#HC-S02)

> El total de estudiantes en sesiones presenciales en el mismo espacio/tiempo no debe exceder la capacidad.

**Query:** [query_HC_S02_exceso_capacidad](soea-queries-repository.md#query_HC_S02_exceso_capacidad)

**Validación:**
- [ ] Ejecutar query con `{horario_id}`
- [ ] Resultado debe estar **vacío** (0 filas) ✅
- [ ] Si hay filas: Capacidad excedida ❌

**Interpretación:**
- `estudiantes_totales > capacidad` → VIOLACIÓN ❌
- Diferencia: `estudiantes_totales - capacidad`

---

### HC-S03: Tipo de Espacio Requerido

**Especificación:** [hard-constraints.md#HC-S03](../../docs/business-rules/hard-constraints.md#HC-S03)

> Asignaturas con `RequiereLab=true` deben asignarse a espacios tipo "Laboratorio".

**Query:** [query_HC_S03_tipo_espacio_requerido](soea-queries-repository.md#query_HC_S03_tipo_espacio_requerido)

**Validación:**
- [ ] Ejecutar query con `{horario_id}`
- [ ] Resultado debe estar **vacío** (0 filas) ✅
- [ ] Si hay filas: Tipo de espacio incorrecto ❌

**Interpretación:**
- Asignatura con `RequiereLab=true` en espacio tipo "Aula" → VIOLACIÓN ❌

---

### HC-S04: Sesiones Virtuales sin Espacio Físico

**Especificación:** [hard-constraints.md#HC-S04](../../docs/business-rules/hard-constraints.md#HC-S04)

> Sesiones virtuales (`Modalidad='Virtual'`) deben tener `Espacio_id=NULL`.

**Query:** [query_HC_S04_sesiones_virtuales_sin_espacio](soea-queries-repository.md#query_HC_S04_sesiones_virtuales_sin_espacio)

**Validación:**
- [ ] Ejecutar query con `{horario_id}`
- [ ] Resultado debe estar **vacío** (0 filas) ✅
- [ ] Si hay filas: Sesión virtual con espacio asignado → VIOLACIÓN ❌

---

## Restricciones de Docente (HC-I)

### HC-I01: Docente No Puede Ser Bidocente

**Especificación:** [hard-constraints.md#HC-I01](../../docs/business-rules/hard-constraints.md#HC-I01)

> Un docente NO puede tener dos sesiones en el mismo espacio de tiempo.

**Query:** [query_HC_I01_docente_multitarea](soea-queries-repository.md#query_HC_I01_docente_multitarea)

**Validación:**
- [ ] Ejecutar query con `{horario_id}`
- [ ] Resultado debe estar **vacío** (0 filas) ✅
- [ ] Si hay filas: Docente sobrecargado ❌

**Interpretación:**
- Docente con `sesiones_simultáneas > 1` → VIOLACIÓN ❌
- Ejemplo: Docente López en lunes 09:00 con Programación I y II

---

### HC-I02: Disponibilidad de Docente

**Especificación:** [hard-constraints.md#HC-I02](../../docs/business-rules/hard-constraints.md#HC-I02)

> Docente solo puede asignarse a franjas en su tabla `DisponibilidadDocente`.

**Query:** [query_HC_I02_disponibilidad_docente](soea-queries-repository.md#query_HC_I02_disponibilidad_docente)

**Validación:**
- [ ] Ejecutar query con `{horario_id}`
- [ ] Resultado debe estar **vacío** (0 filas) ✅
- [ ] Si hay filas: Docente fuera de disponibilidad ❌

**Interpretación:**
- Docente López asignado a franja que NO está en su disponibilidad → VIOLACIÓN ❌

---

### HC-I03: Máximo de Horas Semanales

**Especificación:** [hard-constraints.md#HC-I03](../../docs/business-rules/hard-constraints.md#HC-I03)

> Docente NO puede exceder `Docentes.Maximo_hrs_semanales`.

**Query:** [query_HC_I03_horas_maximas_docente](soea-queries-repository.md#query_HC_I03_horas_maximas_docente)

**Validación:**
- [ ] Ejecutar query con `{horario_id}`
- [ ] Resultado debe estar **vacío** (0 filas) ✅
- [ ] Si hay filas: Docente con exceso de horas ❌

**Interpretación:**
- Docente López contratado 20h pero asignado 22h → VIOLACIÓN de 2h ❌

---

## Restricciones de Tiempo (HC-T)

### HC-T01: Horario de Operación

**Especificación:** [hard-constraints.md#HC-T01](../../docs/business-rules/hard-constraints.md#HC-T01)

> Sesiones deben estar entre 07:00 y 21:30.

**Query:** [query_HC_T01_fuera_horario_operacion](soea-queries-repository.md#query_HC_T01_fuera_horario_operacion)

**Validación:**
- [ ] Ejecutar query con `{horario_id}`
- [ ] Resultado debe estar **vacío** (0 filas) ✅
- [ ] Si hay filas: Sesión fuera de horario ❌

**Interpretación:**
- Sesión que empieza 06:30 o termina 22:00 → VIOLACIÓN ❌

---

### HC-T02: Laboratorios Deben Terminar a Tiempo

**Especificación:** [hard-constraints.md#HC-T02](../../docs/business-rules/hard-constraints.md#HC-T02)

> Laboratorios deben empezar a las 19:30 o antes (para terminar a las 21:30).

**Query:** [query_HC_T02_laboratorio_tiempo_minimo](soea-queries-repository.md#query_HC_T02_laboratorio_tiempo_minimo)

**Validación:**
- [ ] Ejecutar query con `{horario_id}`
- [ ] Resultado debe estar **vacío** (0 filas) ✅
- [ ] Si hay filas: Laboratorio demasiado tarde ❌

**Interpretación:**
- Lab que empieza 20:00 y duraria 3h → terminaría 23:00, fuera de horario ❌

---

## Restricciones de Cohorte (HC-C)

### HC-C01: Cohorte sin Solapamiento

**Especificación:** [hard-constraints.md#HC-C01](../../docs/business-rules/hard-constraints.md#HC-C01)

> Una cohorte NO puede tener dos sesiones en el mismo espacio de tiempo.

**Query:** [query_HC_C01_cohorte_sin_solapamiento](soea-queries-repository.md#query_HC_C01_cohorte_sin_solapamiento)

**Validación:**
- [ ] Ejecutar query con `{horario_id}`
- [ ] Resultado debe estar **vacío** (0 filas) ✅
- [ ] Si hay filas: Cohorte con solapamiento ❌

**Interpretación:**
- Cohorte A-3 con Programación I (09:00) y Matemática I (09:00) el mismo día → VIOLACIÓN ❌

---

### HC-C02: Horas Totales de Asignatura

**Especificación:** [hard-constraints.md#HC-C02](../../docs/business-rules/hard-constraints.md#HC-C02)

> Horas programadas de una cohorte deben coincidir con `Asignatura.BloqueSemanales`.

**Query:** [query_HC_C02_horas_totales_cohorte](soea-queries-repository.md#query_HC_C02_horas_totales_cohorte)

**Validación:**
- [ ] Ejecutar query con `{horario_id}`
- [ ] Resultado debe estar **vacío** (0 filas) ✅
- [ ] Si hay filas: Horas incorrectas ❌

**Interpretación:**
- Asignatura Programación I requiere 4h pero se programaron 3h → VIOLACIÓN ❌

---

## Checklist de Validación Completa

Ejecutar en este orden para un horario completo:

### Fase 1: Restricciones de Espacio (HC-S)
- [ ] [query_HC_S01_detectar_conflictos_espacio](soea-queries-repository.md#query_HC_S01_detectar_conflictos_espacio) → Vacío ✅
- [ ] [query_HC_S02_exceso_capacidad](soea-queries-repository.md#query_HC_S02_exceso_capacidad) → Vacío ✅
- [ ] [query_HC_S03_tipo_espacio_requerido](soea-queries-repository.md#query_HC_S03_tipo_espacio_requerido) → Vacío ✅
- [ ] [query_HC_S04_sesiones_virtuales_sin_espacio](soea-queries-repository.md#query_HC_S04_sesiones_virtuales_sin_espacio) → Vacío ✅

### Fase 2: Restricciones de Docente (HC-I)
- [ ] [query_HC_I01_docente_multitarea](soea-queries-repository.md#query_HC_I01_docente_multitarea) → Vacío ✅
- [ ] [query_HC_I02_disponibilidad_docente](soea-queries-repository.md#query_HC_I02_disponibilidad_docente) → Vacío ✅
- [ ] [query_HC_I03_horas_maximas_docente](soea-queries-repository.md#query_HC_I03_horas_maximas_docente) → Vacío ✅

### Fase 3: Restricciones de Tiempo (HC-T)
- [ ] [query_HC_T01_fuera_horario_operacion](soea-queries-repository.md#query_HC_T01_fuera_horario_operacion) → Vacío ✅
- [ ] [query_HC_T02_laboratorio_tiempo_minimo](soea-queries-repository.md#query_HC_T02_laboratorio_tiempo_minimo) → Vacío ✅

### Fase 4: Restricciones de Cohorte (HC-C)
- [ ] [query_HC_C01_cohorte_sin_solapamiento](soea-queries-repository.md#query_HC_C01_cohorte_sin_solapamiento) → Vacío ✅
- [ ] [query_HC_C02_horas_totales_cohorte](soea-queries-repository.md#query_HC_C02_horas_totales_cohorte) → Vacío ✅

### Resultado Final
Si **todos** resultan vacíos (0 filas cada uno):
```
Hard_constraint_violations = 0 ✅ HORARIO VÁLIDO
```

Si **alguno** tiene filas:
```
Hard_constraint_violations > 0 ❌ HORARIO INVÁLIDO
```

---

## Integración en Chat

**Patrón de uso en Copilot:**

> "¿Tiene el horario 2024-1 conflictos de espacio?"

**El agente:**
1. Busca [HC-S01](../../docs/business-rules/hard-constraints.md#HC-S01)
2. Obtiene [query_HC_S01_detectar_conflictos_espacio](soea-queries-repository.md#query_HC_S01_detectar_conflictos_espacio)
3. Ejecuta contra SOEAdb con horario_id='2024-1'
4. Si resultado vacío → "✅ Sin conflictos"
5. Si hay resultados → "❌ {n} conflictos: {detalles}"

---

## Restricciones Blandas (SC) - Reportes

> **Nota:** Las restricciones blandas (SC-01 a SC-09) no se validan aquí. Se optimizan en la Fase 3 (Algoritmo Genético).

> Para reportes de calidad, ver: [Caso 3 en soea-use-cases.md](soea-use-cases.md#caso-3-generar-reportes-de-carga)

---

## Preguntas Frecuentes

**P: ¿Qué significa que resultado esté "vacío"?**  
R: 0 filas devueltas = Restricción cumplida ✅

**P: ¿Puedo ignorar un conflicto?**  
R: No. Si HC violation > 0, horario es inválido.

**P: ¿Dónde está la lógica real de validación?**  
R: En `SOEA.Application.Services.ConstraintValidator`. La skill solo muestra queries para exploración.

**P: ¿Qué hago si hay conflicto?**  
R: Ver detalle en resultado de query → reasignar sesión → ejecutar query de nuevo.
