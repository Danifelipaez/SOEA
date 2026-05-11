# Referencia Rápida: Esquema SOEAdb

## Tablas Principales

| Tabla | Descripción | Relaciones Clave |
|-------|-------------|-----------------|
| **Programas** | Carreras académicas | 1:N → Asignaturas |
| **Asignaturas** | Cursos/Materias | N:1 ← Programas, 1:N → Sesiones |
| **Docentes** | Profesores | 1:N → Sesiones, M:N ↔ FranjasHorarias |
| **Espacios** | Aulas/Laboratorios | 1:N → Sesiones (nullable) |
| **FranjasHorarias** | Bloques de tiempo | 1:N → Sesiones, M:N ↔ Docentes |
| **DisponibilidadDocente** | Disponibilidad docente | M:N Bridge |
| **Horarios** | Versiones de horarios | 1:N → Sesiones |
| **Sesiones** | Asignaciones concretas | Hub central |

## Campos Críticos

### Sesiones (más importante)
- `Horario_id`: qué horario contiene
- `Asignatura_id`: qué se enseña
- `Docente_id`: quién enseña
- `Franja_id`: cuándo
- `Espacio_id`: dónde (NULL = virtual)
- `Tipo_alternancia`: TypeA, TypeB, NonAlternating
- `Modalidad`: Presencial, Virtual, Híbrida
- `Duracion_horas`: cuánto tiempo

### Asignaturas
- `BloqueSemanales`: horas por semana
- `RequiereLab`: necesita laboratorio
- `Alternancia`: tipo de alternancia
- `ProgramaId`: a qué carrera pertenece

### Horarios
- `Estado`: borrador, validado, aprobado, activo
- `Hard_constraint_violations`: restricciones duras rotas (debe ser 0)
- `Soft_constraint_fitness_score`: calidad 0-100

## Tipos de Datos Especiales

- **UUID**: Todos los Id son UUID (128 bits únicos)
- **Alternancia**: TypeA | TypeB | NonAlternating
- **Sesiones Virtuales**: `Espacio_id IS NULL`
- **Timestamp**: Con zona horaria

## Restricciones de Integridad

- Asignaturas.Codigo: UNIQUE
- Programas.Codigo: UNIQUE
- Docentes.Correo: UNIQUE
- ForeignKeys: Todas con validación NOT VALID (revisar)
- DisponibilidadDocente PK: (Docente_id, Franja_id)

## Acceso Rápido

**¿Todas las asignaturas de un programa?**  
→ `Asignaturas WHERE ProgramaId = X`

**¿Docentes disponibles en una franja?**  
→ `DisponibilidadDocente JOIN Docentes WHERE Franja_id = X`

**¿Sesiones virtuales?**  
→ `Sesiones WHERE Espacio_id IS NULL`

**¿Horario actual?**  
→ `Horarios WHERE Estado = 'activo' ORDER BY Generado_en DESC`
