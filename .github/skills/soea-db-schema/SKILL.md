---
name: soea-db-schema
user-invocable: true
description: "Base de conocimiento del esquema PostgreSQL SOEAdb. Úsalo cuando necesites: estructura de tablas, queries SQL, validación de restricciones duras (HC), casos de uso, reportes. Integración PostgreSQL automática en chat."
---

# Skill: Base de Conocimiento PostgreSQL SOEAdb

> **Arquitectura:** Alineada con Clean Architecture. No duplica especificación de negocio (en `docs/business-rules/`).

## 📚 Índice de Documentación

### Punto de Entrada Recomendado
- **¿Qué necesitas hacer?** → [use-case-queries.md](use-case-queries.md) (búsqueda rápida por necesidad)
- **¿Cuál es la estructura de la BD?** → [schema-quick-reference.md](schema-quick-reference.md) (tablas y campos)

### Documentación Detallada

#### 1. 🔍 Queries SQL (Fuente Única)
**Archivo:** [soea-queries-repository.md](soea-queries-repository.md)

Todas las queries SQL están aquí UNA VEZ. Contiene:
- 4 queries de preparación (Caso 1)
- 11 queries de validación HC (Caso 2)
- 4 queries de reportes (Caso 3)
- 2 queries de análisis de alternancia (Caso 4)
- 1 query de dashboard (Caso 5)

**Total:** 22 queries listas para usar

#### 2. 🎯 Casos de Uso (Flujos)
**Archivo:** [soea-use-cases.md](soea-use-cases.md)

Cómo usar las queries en situaciones reales:
- **Caso 1:** Preparar datos para generar horario
- **Caso 2:** Validar horario generado (11 restricciones HC)
- **Caso 3:** Generar reportes de carga
- **Caso 4:** Analizar alternancia
- **Caso 5:** Dashboard ejecutivo

#### 3. ✅ Validación de Restricciones
**Archivo:** [validation-rules.md](validation-rules.md)

Guía práctica para verificar cada restricción dura (HC):
- Explicación de cada HC
- Query SQL correspondiente
- Cómo interpretar resultados
- Checklist de validación completa

#### 4. 📍 Guía por Necesidad
**Archivo:** [use-case-queries.md](use-case-queries.md)

Tabla rápida: "Necesito...  → Ejecutar estos queries"

#### 5. 🔗 Integración PostgreSQL
**Archivo:** [postgres-integration.instructions.md](postgres-integration.instructions.md)

Cómo el agente ejecuta queries automáticamente desde chat:
- Detección de contexto (validación, reporte, análisis)
- Inyección de parámetros
- Manejo de resultados
- Troubleshooting

---

## 🏗️ Estructura del Esquema (Resumen)

## Información General

**Servidor:** localhost  
**Base de Datos:** SOEAdb  
**Versión PostgreSQL:** 18.3  
**Propietario:** postgres  

## Tablas del Sistema

### 1. **Programas**
*Almacena los programas académicos (carreras, planes de estudio)*

| Campo | Tipo | Restricciones | Descripción |
|-------|------|---------------|-------------|
| **Id** | uuid | PK, NOT NULL | Identificador único |
| **Nombre** | varchar(255) | NOT NULL | Nombre del programa (ej: Ingeniería Informática) |
| **Codigo** | varchar(50) | NOT NULL, UNIQUE | Código único del programa |

**Relaciones:** 1:N con Asignaturas

---

### 2. **Asignaturas**
*Define las asignaturas/cursos que se imparten en un programa*

| Campo | Tipo | Restricciones | Descripción |
|-------|------|---------------|-------------|
| **Id** | uuid | PK, NOT NULL | Identificador único |
| **Nombre** | varchar(255) | NOT NULL | Nombre de la asignatura |
| **Codigo** | varchar(20) | NOT NULL, UNIQUE | Código único de la asignatura |
| **BloqueSemanales** | integer | NOT NULL | Número de bloques semanales (horas) |
| **RequiereLab** | boolean | NOT NULL, default=false | Indica si requiere laboratorio |
| **Alternancia** | varchar(50) | NOT NULL | Tipo de alternancia (TypeA, TypeB, NonAlternating) |
| **ProgramaId** | uuid | FK → Programas, NOT NULL | Referencia al programa |

**Relaciones:** N:1 con Programas, 1:N con Sesiones

**Índices:**
- Asignaturas_pkey (Id)
- Codigo_Asignaturas (Codigo - UNIQUE)

---

### 3. **Docentes**
*Información de los docentes/profesores del sistema*

| Campo | Tipo | Restricciones | Descripción |
|-------|------|---------------|-------------|
| **Id** | uuid | PK, NOT NULL | Identificador único |
| **Nombre** | varchar(100) | NOT NULL | Nombre del docente |
| **Apellido** | varchar(100) | NOT NULL | Apellido del docente |
| **Correo** | varchar(200) | NOT NULL, UNIQUE | Email único del docente |
| **Maximo_hrs_semanales** | integer | NOT NULL | Máximo de horas semanales permitidas |

**Relaciones:** 
- 1:N con Sesiones
- M:N con FranjasHorarias (a través de DisponibilidadDocente)

**Índices:**
- Docentes_pkey (Id)
- Correo_Docentes (Correo - UNIQUE)

---

### 4. **Espacios**
*Aulas, laboratorios y espacios físicos disponibles*

| Campo | Tipo | Restricciones | Descripción |
|-------|------|---------------|-------------|
| **Id** | uuid | PK, NOT NULL | Identificador único |
| **Nombre** | varchar(255) | NOT NULL | Nombre del espacio (ej: Aula 101, Lab Física) |
| **Tipo** | varchar(20) | NOT NULL | Tipo de espacio (Aula, Laboratorio, etc.) |
| **Capacidad** | integer | NOT NULL | Capacidad máxima de personas |
| **Ubicacion** | varchar(100) | NOT NULL | Ubicación física (edificio, sector) |
| **piso** | integer | NOT NULL | Número de piso |

**Relaciones:** 1:N con Sesiones (nullable - sesiones virtuales sin espacio)

**Índices:**
- Espacios_pkey (Id)

---

### 5. **FranjasHorarias**
*Define los bloques de tiempo disponibles en el horario*

| Campo | Tipo | Restricciones | Descripción |
|-------|------|---------------|-------------|
| **Id** | uuid | PK, NOT NULL | Identificador único |
| **Dia_semana** | varchar(10) | NOT NULL | Día de la semana (Lunes, Martes, ..., Domingo) |
| **hora_inicio** | time | NOT NULL | Hora de inicio (formato HH:MM:SS) |
| **Hora_fin** | time | NOT NULL | Hora de fin (formato HH:MM:SS) |

**Relaciones:**
- 1:N con Sesiones
- M:N con Docentes (a través de DisponibilidadDocente)

**Índices:**
- FranjasHorarias_pkey (Id)

---

### 6. **DisponibilidadDocente**
*Tabla de asociación M:N que indica disponibilidad de docentes en franjas horarias*

| Campo | Tipo | Restricciones | Descripción |
|-------|------|---------------|-------------|
| **Docente_id** | uuid | FK → Docentes, PK, NOT NULL | Referencia al docente |
| **Franja_id** | uuid | FK → FranjasHorarias, PK, NOT NULL | Referencia a la franja horaria |

**Clave Primaria:** (Docente_id, Franja_id)

**Relaciones:** 
- M:1 con Docentes
- M:1 con FranjasHorarias

---

### 7. **Horarios**
*Versiones de horarios generados (borrador, aprobado, activo)*

| Campo | Tipo | Restricciones | Descripción |
|-------|------|---------------|-------------|
| **Id** | uuid | PK, NOT NULL | Identificador único del horario |
| **Semestre** | varchar(20) | NOT NULL | Identificación del semestre (ej: 2024-1, 2024-2) |
| **Estado** | varchar(20) | NOT NULL, default='borrador' | Estado: borrador, validado, aprobado, activo |
| **Generado_en** | timestamp | NOT NULL, default=now() | Fecha y hora de generación |
| **Hard_constraint_violations** | integer | NOT NULL, default=0 | Cantidad de restricciones duras violadas |
| **Soft_constraint_fitness_score** | numeric(8,2) | NOT NULL, default=0 | Puntuación de calidad (0-100) |

**Relaciones:** 1:N con Sesiones

**Índices:**
- Horarios_pkey (Id)

---

### 8. **Sesiones**
*Asignaciones de aula, docente, franja horaria y grupo (lo más granular del sistema)*

| Campo | Tipo | Restricciones | Descripción |
|-------|------|---------------|-------------|
| **Id** | uuid | PK, NOT NULL | Identificador único |
| **Horario_id** | uuid | FK → Horarios, NOT NULL | Referencia al horario contenedor |
| **Asignatura_id** | uuid | FK → Asignaturas, NOT NULL | Referencia a la asignatura |
| **Espacio_id** | uuid | FK → Espacios, nullable | Ref. al espacio (null = sesión virtual) |
| **Docente_id** | uuid | FK → Docentes, NOT NULL | Docente asignado |
| **Franja_id** | uuid | FK → FranjasHorarias, NOT NULL | Franja horaria asignada |
| **Tipo_alternancia** | varchar(20) | NOT NULL | Tipo: TypeA, TypeB, NonAlternating |
| **Modalidad** | varchar(20) | NOT NULL | Modalidad: Presencial, Virtual, Híbrida |
| **Estado** | varchar(20) | NOT NULL | Estado de la sesión |
| **Duracion_horas** | integer | NOT NULL | Duración en horas |

**Relaciones:**
- N:1 con Horarios
- N:1 con Asignaturas
- N:1 con Docentes
- N:1 con Espacios (nullable)
- N:1 con FranjasHorarias

**Índices:**
- Sesiones_pkey (Id)

**Notas importantes:**
- `Espacio_id` es nullable para sesiones virtuales
- Las sesiones virtuales se identifican por `Espacio_id = null`
- `Tipo_alternancia` puede tomar valores: TypeA, TypeB, NonAlternating
- `Modalidad` diferencia entre Presencial, Virtual, Híbrida

---

## Diagrama de Relaciones

```
Programas (1) ──→ (N) Asignaturas
                            ↓
                        Sesiones ←─── Horarios
                        ↙  ↓  ↘
                    Docentes  Espacios  FranjasHorarias
                        ↑
                DisponibilidadDocente
                        ↓
                  FranjasHorarias
```

---

## Valores Enumerados

### AlternanciaType
- `TypeA` - Alternancia tipo A
- `TypeB` - Alternancia tipo B
- `NonAlternating` - Sin alternancia

### EstadoHorario
- `borrador` - En desarrollo
- `validado` - Pasó validaciones
- `aprobado` - Aprobado por autoridad
- `activo` - En uso actual

### Modalidad
- `Presencial` - Clases en aula física
- `Virtual` - En línea
- `Híbrida` - Combinación

---

## Consultas Comunes

### Obtener todas las asignaturas de un programa
```sql
SELECT a.* FROM "Asignaturas" a
WHERE a."ProgramaId" = '{programa_uuid}'
ORDER BY a."Codigo";
```

### Obtener horario con todas sus sesiones
```sql
SELECT h.*, s.* FROM "Horarios" h
LEFT JOIN "Sesiones" s ON h."Id" = s."Horario_id"
WHERE h."Id" = '{horario_uuid}';
```

### Verificar disponibilidad de docente
```sql
SELECT d.*, f.* FROM "Docentes" d
INNER JOIN "DisponibilidadDocente" dd ON d."Id" = dd."Docente_id"
INNER JOIN "FranjasHorarias" f ON dd."Franja_id" = f."Id"
WHERE d."Id" = '{docente_uuid}';
```

### Sesiones virtuales (sin espacio)
```sql
SELECT * FROM "Sesiones"
WHERE "Espacio_id" IS NULL;
```

---

## Cómo Usar Esta Skill

1. **Pregunta sobre una entidad:** "¿Cuáles son los campos de Sesiones?"
2. **Pregunta sobre relaciones:** "¿Cómo se relacionan Docentes y FranjasHorarias?"
3. **Consulta estructuras:** "¿Qué es Alternancia?"
4. **Solicita consultas:** "Dame una query para obtener todas las sesiones de un horario"

Esta skill se invoca automáticamente cuando detecta palabras clave relacionadas con la estructura de datos.
