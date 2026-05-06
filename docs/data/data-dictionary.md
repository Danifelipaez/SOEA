# Diccionario de datos

## Propósito
Definir cada campo de datos del modelo de dominio de SOEA: nombre, tipo, descripción, si es obligatorio/opcional
 y valores permitidos. Copilot usa esto como fuente autorizada al generar clases de entidades,
esquemas de base de datos, lectores de Excel y DTOs de API.

## Alcance
Todas las entidades persistentes del dominio. Los campos derivados o calculados se indican como tales.

---

## Usuario

Entidad base para cuentas humanas del sistema.

| Campo | Tipo | Requerido | Descripción | Valores permitidos / Restricciones |
|---|---|---|---|---|
| `Id_usuario` | UUID | Sí | Identificador único de la cuenta | Generado automáticamente |
| `Nombre` | string | Sí | Nombre completo visible | — |
| `Email` | string | Sí | Correo institucional | Formato de correo válido, único |

---

## Administrador

Especialización de Usuario para gestión académica.

| Campo | Tipo | Requerido | Descripción | Valores permitidos / Restricciones |
|---|---|---|---|---|
| `Id_usuario` | UUID | Sí | PK/FK a Usuario | Debe existir en `Usuario.Id_usuario` |
| `Tipo_admin` | enum | Sí | Rol administrativo del usuario | `Programa`, `Admisiones`, `RedEdu` |

---

## Docente

Especialización de Usuario para docencia.

| Campo | Tipo | Requerido | Descripción | Valores permitidos / Restricciones |
|---|---|---|---|---|
| `Id_usuario` | UUID | Sí | PK/FK a Usuario | Debe existir en `Usuario.Id_usuario` |
| `Tipo_vinculacion` | string | Sí | Tipo de vinculación contractual | Catálogo institucional |

---

## Disponibilidad_Docente

Bloques de tiempo disponibles por docente.

| Campo | Tipo | Requerido | Descripción | Valores permitidos / Restricciones |
|---|---|---|---|---|
| `Id_disponibilidad` | UUID | Sí | Identificador único del bloque | Generado automáticamente |
| `Id_usuario` | UUID | Sí | FK a Docente | Debe existir en `Docente.Id_usuario` |
| `Dia_semana` | enum | Sí | Día de la semana | `Monday`–`Friday` |
| `Hora_inicio` | TimeOnly | Sí | Hora de inicio | 07:00–21:30 |
| `Hora_fin` | TimeOnly | Sí | Hora de fin | > `Hora_inicio`, ≤ 21:30 |

---

## Asignatura

Curso académico de la malla curricular.

| Campo | Tipo | Requerido | Descripción | Valores permitidos / Restricciones |
|---|---|---|---|---|
| `Id_asignatura` | UUID | Sí | Identificador único | Generado automáticamente |
| `Nombre` | string | Sí | Nombre de la asignatura | — |
| `Prog_academico` | string | Sí | Programa académico al que pertenece | Catálogo institucional |
| `Req_equipamiento` | string | No | Equipamiento requerido | Lista separada por comas o referencia a catálogo |
| `Tipo_de_clase` | enum | Sí | Tipo de clase | `Teorica`, `Laboratorio`, `Practica` |
| `Duracion` | decimal | Sí | Duración de la sesión en horas | > 0, ≤ 8 |
| `Num_semanas_en_lab` | int | No | Semanas que requieren laboratorio | ≥ 0 |
| `Creado_por_id_admin` | UUID | Sí | FK a Administrador | Debe existir en `Administrador.Id_usuario` |

---

## Grupo_de_estudiantes

Grupo de estudiantes asociado a una asignatura.

| Campo | Tipo | Requerido | Descripción | Valores permitidos / Restricciones |
|---|---|---|---|---|
| `Id_grupo` | UUID | Sí | Identificador único | Generado automáticamente |
| `Prog_academico` | string | Sí | Programa académico del grupo | Catálogo institucional |
| `Cohorte` | string | Sí | Cohorte o semestre asociado | Por ejemplo, "2025-1 / Sem 3" |
| `Num_estudiantes` | int | Sí | Número de estudiantes inscritos | > 0 |
| `Asignatura` | UUID | Sí | FK a Asignatura | Debe existir en `Asignatura.Id_asignatura` |
| `Creado_por_id_admin` | UUID | Sí | FK a Administrador | Debe existir en `Administrador.Id_usuario` |

---

## Espacio_Academico

Ubicación física o recurso virtual para dictar sesiones.

| Campo | Tipo | Requerido | Descripción | Valores permitidos / Restricciones |
|---|---|---|---|---|
| `Id_espacio` | UUID | Sí | Identificador único | Generado automáticamente |
| `Nombre` | string | Sí | Nombre o código del espacio | Por ejemplo, "Aula 204" |
| `Bloque` | string | No | Bloque/edificio | — |
| `Tipo` | enum | Sí | Tipo de espacio | `Aula`, `Laboratorio`, `Auditorio` |
| `Capacidad` | int | Sí | Ocupación máxima | > 0 |
| `Equipamiento` | string | No | Equipamiento disponible | Lista separada por comas o referencia a catálogo |
| `Es_virtual` | bool | Sí | Indica si el espacio es virtual | — |
| `Reservado_por_id_admin` | UUID | No | FK a Administrador que lo reservó | Debe existir en `Administrador.Id_usuario` |

---

## Sesion

Unidad programable: una ocurrencia de clase en un día y hora específicos.

| Campo | Tipo | Requerido | Descripción | Valores permitidos / Restricciones |
|---|---|---|---|---|
| `Id_sesion` | UUID | Sí | Identificador único | Generado automáticamente |
| `Id_grupo` | UUID | Sí | FK a Grupo_de_estudiantes | Debe existir en `Grupo_de_estudiantes.Id_grupo` |
| `Id_espacio` | UUID | No | FK a Espacio_Academico | Null cuando `Modalidad = Virtual` |
| `Id_docente` | UUID | Sí | FK a Docente | Debe existir en `Docente.Id_usuario` |
| `Dia_semana` | enum | Sí | Día de la semana | `Monday`–`Friday` |
| `Hora_inicio` | TimeOnly | Sí | Hora de inicio | 07:00–21:30 |
| `Hora_fin` | TimeOnly | Sí | Hora de fin | > `Hora_inicio`, ≤ 21:30 |
| `Modalidad` | enum | Sí | Modalidad de la sesión | `Presencial`, `Virtual` |
| `Semana_num` | int | Sí | Número de la semana del semestre | ≥ 1 |
| `Es_alternancia_virtual` | bool | Sí | Indica si la sesión es virtual por alternancia | — |
| `Creado_por_id_admin` | UUID | Sí | FK a Administrador | Debe existir en `Administrador.Id_usuario` |

---

## Preguntas abiertas

- ¿`Req_equipamiento` y `Equipamiento` deben normalizarse a una tabla de catálogo?
- ¿`Tipo_vinculacion` y `Tipo_de_clase` se deben restringir con listas institucionales rígidas?
- ¿Las sesiones virtuales deben persistir siempre con `Id_espacio = null`, aun si existe un `Espacio_Academico` virtual?
