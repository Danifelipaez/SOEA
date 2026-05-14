# Diccionario de datos

## Propósito
Definir cada campo de datos del modelo de dominio de SOEA: nombre, tipo, descripción, si es obligatorio/opcional
y valores permitidos. Copilot usa esto como fuente autorizada al generar clases de entidades,
esquemas de base de datos, lectores de Excel y DTOs de API.

## Alcance
Todas las entidades persistentes del dominio. Los campos derivados o calculados se indican como tales.

---

## Sesión

La unidad programable central: una ocurrencia de una asignatura para una cohorte.

| Campo | Tipo | Requerido | Descripción | Valores permitidos / Restricciones |
|---|---|---|---|---|
| `Id` | GUID | Sí | Identificador único | Generado automáticamente |
| `AsignaturaId` | GUID | Sí | Referencia a Asignatura | Debe existir en la tabla Asignatura |
| `GrupoId` | GUID | No | Referencia a Grupo (null para sesiones de todo el programa) | Debe existir en la tabla Grupo, o null |
| `DocenteId` | GUID | Sí | Referencia a Docente | Debe existir en la tabla Docente |
| `EspacioId` | GUID | No | Espacio asignado (null si es virtual) | Debe existir en la tabla Espacio |
| `BloqueTiempoId` | GUID | Sí | Espacio de tiempo asignado | Debe existir en la tabla BloqueTiempo |
| `Alternancia` | Enum | Sí | Tipo de alternancia de la cohorte | `TipoA`, `TipoB`, `SinAlternancia` |
| `Modalidad` | Enum | Sí | Presencial o virtual | `Presencial`, `Virtual` |
| `Estado` | Enum | Sí | Estado de programación | `Pendiente`, `Asignado`, `Conflicto` |
| `HorasDuracion` | decimal | Sí | Duración de la sesión en horas | > 0, ≤ 8 |
| `EsBloque` | bool | Sí | Indica si es una sesión en bloque continuo | — |
| `EsBloqueDividido` | bool | Sí | Indica si las horas están divididas en varios días | No puede ser true si EsBloque es true |

---

## Grupo

Agrupación opcional de estudiantes para programación de sesiones (piloto de química).
Las sesiones pueden asignarse a un Grupo específico o a todo el programa (GrupoId = null).

| Campo | Tipo | Requerido | Descripción | Valores permitidos |
|---|---|---|---|---|
| `Id` | GUID | Sí | Identificador único | Generado automáticamente |
| `Nombre` | string | Sí | Nombre visible del grupo | Por ejemplo, "Química Lab A", "Grupo 1" |
| `ProgramaId` | GUID | Sí | Programa académico | Debe existir en la tabla Programa |
| `Semestre` | int | Sí | Número de semestre académico | 1–10 |
| `EstudiantesInscritos` | int | Sí | Número de estudiantes inscritos | > 0 |
| `Alternancia` | Enum | Sí | Tipo A, Tipo B o no alternante | `TipoA`, `TipoB`, `SinAlternancia` |

---

## Espacio

Ubicación física para las sesiones.

| Campo | Tipo | Requerido | Descripción | Valores permitidos |
|---|---|---|---|---|
| `Id` | GUID | Sí | Identificador único | Generado automáticamente |
| `Nombre` | string | Sí | Nombre o código del espacio | Por ejemplo, "Aula 204", "Lab Química" |
| `Tipo` | Enum | Sí | Tipo de espacio | `Salon`, `Laboratorio`, `Auditorio` |
| `Capacidad` | int | Sí | Ocupación máxima | > 0 |
| `Edificio` | string | No | Nombre o código del edificio | — |
| `Piso` | int | No | Número del piso | — |

---

## Docente

Persona que imparte sesiones.

| Campo | Tipo | Requerido | Descripción | Valores permitidos |
|---|---|---|---|---|
| `Id` | GUID | Sí | Identificador único | Generado automáticamente |
| `NombreCompleto` | string | Sí | Nombre completo | — |
| `Correo` | string | Sí | Correo institucional | Formato de correo válido |
| `MaximoHorasSemanales` | decimal | Sí | Máximo de horas de docencia contratadas por semana | > 0 |
| `Disponibilidad` | list of BloqueTiempo | Sí | Espacios de tiempo disponibles | Ver BloqueTiempo |

---

## Espacio de tiempo

Bloque de tiempo discreto programable.

| Campo | Tipo | Requerido | Descripción | Valores permitidos |
|---|---|---|---|---|
| `Id` | GUID | Sí | Identificador único | Generado automáticamente |
| `DiaDeSemana` | Enum | Sí | Día de la semana | `Lunes`–`Viernes` |
| `HoraInicio` | TimeOnly | Sí | Hora de inicio | 07:00–21:30 |
| `HoraFin` | TimeOnly | Sí | Hora de fin | > HoraInicio, ≤ 21:30 |

---

## Asignatura

Una asignatura o curso académico de la malla curricular.

| Campo | Tipo | Requerido | Descripción | Valores permitidos |
|---|---|---|---|---|
| `Id` | GUID | Sí | Identificador único | Generado automáticamente |
| `Nombre` | string | Sí | Nombre de la asignatura | — |
| `Codigo` | string | Sí | Código institucional de la asignatura | Alfanumérico |
| `HorasPorSesion` | int | Sí | Duración de cada sesión en horas | > 0 |
| `SesionesPorSemana` | int | Sí | Número de veces que se dicta a la semana | > 0 |
| `SesionesLaboratorioSemestre` | int | Sí | Cantidad de sesiones de lab en el semestre | ≥ 0 |
| `Alternancia` | Enum | Calculado | Derivado de SesionesLab (8=TipoA, >8=TipoB) | `TipoA`, `TipoB`, `SinAlternancia` |
| `ProgramaId` | GUID | Sí | Programa académico al que pertenece | — |

---

## Horario

La salida completa del horario para un semestre.

| Campo | Tipo | Requerido | Descripción |
|---|---|---|---|
| `Id` | GUID | Sí | Identificador único |
| `Semestre` | string | Sí | Por ejemplo, "2025-1" |
| `GeneradoEn` | DateTime | Sí | Marca temporal de generación |
| `Estado` | Enum | Sí | `Borrador`, `Publicado`, `Archivado` |
| `Sesiones` | list of Sesion | Sí | Todas las sesiones asignadas |
| `ViolacionesRestriccionesDuras` | int | Calculado | Debe ser 0 para un horario válido |
| `PuntajeFitness` | decimal | Calculado | Más bajo es mejor; proviene de la Fase 3 |

---

## Preguntas abiertas

- ¿`Docente.Disponibilidad` debe almacenarse como una tabla de unión separada o incrustada como JSON?
- ¿`Asignatura.HorasSemanales` es lo mismo que `Sesion.HorasDuracion × sesiones por semana`?
- ¿Existen asignaturas con horas variables que cambian según la cohorte?
