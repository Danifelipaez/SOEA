# Modelo relacional

## Propósito
Definir las tablas físicas de la base de datos, columnas, claves primarias/foráneas e índices de SOEA.
Copilot usa esto al generar configuraciones de entidades EF Core y migraciones de base de datos.

## Alcance
Todas las tablas del esquema de base de datos de producción.

---

## Tablas

> Nota: Este es un borrador inicial basado en el modelo de dominio. Debe refinarse después de la primera migración de EF Core.

### `Usuarios`
| Column | Type | Constraints |
|---|---|---|
| `Id_usuario` | uniqueidentifier | PK |
| `Nombre` | nvarchar(200) | NOT NULL |
| `Email` | nvarchar(200) | NOT NULL, UNIQUE |

### `Administradores`
| Column | Type | Constraints |
|---|---|---|
| `Id_usuario` | uniqueidentifier | PK, FK → Usuarios.Id_usuario |
| `Tipo_admin` | nvarchar(30) | NOT NULL, CHECK IN ('Programa','Admisiones','RedEdu') |

### `Docentes`
| Column | Type | Constraints |
|---|---|---|
| `Id_usuario` | uniqueidentifier | PK, FK → Usuarios.Id_usuario |
| `Tipo_vinculacion` | nvarchar(50) | NOT NULL |

### `Disponibilidad_Docente`
| Column | Type | Constraints |
|---|---|---|
| `Id_disponibilidad` | uniqueidentifier | PK |
| `Id_usuario` | uniqueidentifier | FK → Docentes.Id_usuario |
| `Dia_semana` | nvarchar(10) | NOT NULL, CHECK IN ('Monday','Tuesday','Wednesday','Thursday','Friday') |
| `Hora_inicio` | time | NOT NULL |
| `Hora_fin` | time | NOT NULL, CHECK > Hora_inicio |

### `Asignaturas`
| Column | Type | Constraints |
|---|---|---|
| `Id_asignatura` | uniqueidentifier | PK |
| `Nombre` | nvarchar(200) | NOT NULL |
| `Prog_academico` | nvarchar(200) | NOT NULL |
| `Req_equipamiento` | nvarchar(200) | NULL |
| `Tipo_de_clase` | nvarchar(30) | NOT NULL |
| `Duracion` | decimal(4,2) | NOT NULL, CHECK > 0 |
| `Num_semanas_en_lab` | int | NULL, CHECK >= 0 |
| `Creado_por_id_admin` | uniqueidentifier | FK → Administradores.Id_usuario |

### `Grupos_de_estudiantes`
| Column | Type | Constraints |
|---|---|---|
| `Id_grupo` | uniqueidentifier | PK |
| `Prog_academico` | nvarchar(200) | NOT NULL |
| `Cohorte` | nvarchar(50) | NOT NULL |
| `Num_estudiantes` | int | NOT NULL, CHECK > 0 |
| `Asignatura` | uniqueidentifier | FK → Asignaturas.Id_asignatura |
| `Creado_por_id_admin` | uniqueidentifier | FK → Administradores.Id_usuario |

### `Espacios_Academicos`
| Column | Type | Constraints |
|---|---|---|
| `Id_espacio` | uniqueidentifier | PK |
| `Nombre` | nvarchar(100) | NOT NULL |
| `Bloque` | nvarchar(50) | NULL |
| `Tipo` | nvarchar(30) | NOT NULL |
| `Capacidad` | int | NOT NULL, CHECK > 0 |
| `Equipamiento` | nvarchar(200) | NULL |
| `Es_virtual` | bit | NOT NULL, DEFAULT 0 |
| `Reservado_por_id_admin` | uniqueidentifier | NULL, FK → Administradores.Id_usuario |

### `Asignatura_Espacio`
| Column | Type | Constraints |
|---|---|---|
| `Id_asignatura` | uniqueidentifier | FK → Asignaturas.Id_asignatura |
| `Id_espacio` | uniqueidentifier | FK → Espacios_Academicos.Id_espacio |
| PK | composite | (`Id_asignatura`, `Id_espacio`) |

### `Sesiones`
| Column | Type | Constraints |
|---|---|---|
| `Id_sesion` | uniqueidentifier | PK |
| `Id_grupo` | uniqueidentifier | FK → Grupos_de_estudiantes.Id_grupo |
| `Id_espacio` | uniqueidentifier | NULL, FK → Espacios_Academicos.Id_espacio |
| `Id_docente` | uniqueidentifier | FK → Docentes.Id_usuario |
| `Dia_semana` | nvarchar(10) | NOT NULL, CHECK IN ('Monday','Tuesday','Wednesday','Thursday','Friday') |
| `Hora_inicio` | time | NOT NULL |
| `Hora_fin` | time | NOT NULL, CHECK > Hora_inicio |
| `Modalidad` | nvarchar(20) | NOT NULL, CHECK IN ('Presencial','Virtual') |
| `Semana_num` | int | NOT NULL, CHECK > 0 |
| `Es_alternancia_virtual` | bit | NOT NULL |
| `Creado_por_id_admin` | uniqueidentifier | FK → Administradores.Id_usuario |

Las sesiones virtuales se representan con `Sesiones.Id_espacio = NULL` y `Modalidad = 'Virtual'`.

---

## Índices

- `Sesiones(Id_grupo, Dia_semana, Hora_inicio)` — detección de conflictos por grupo
- `Sesiones(Id_docente, Dia_semana, Hora_inicio)` — detección de conflictos por docente
- `Sesiones(Id_espacio, Dia_semana, Hora_inicio)` — detección de conflictos por espacio
- `Grupos_de_estudiantes(Asignatura)` — consultas por asignatura
- `Asignaturas(Prog_academico)` — consultas por programa

---

## Preguntas abiertas

- ¿Se requiere historizar cambios en `Sesiones` (versionado de horarios)?
- ¿`Asignatura_Espacio` representa requisitos de equipamiento o restricciones duras de asignación?
