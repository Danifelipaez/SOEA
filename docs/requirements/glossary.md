# Glosario

## Propósito
Definir cada término del dominio usado en el código, la documentación y la interfaz de SOEA para que todos los colaboradores (y Copilot) usen un lenguaje consistente. En caso de duda, consulta este archivo antes de nombrar una clase, variable o endpoint.

## Alcance
Todos los términos específicos del dominio usados en el backend, el frontend y la documentación.

---

## Términos

| Término (ES) | Identificador de código (EN) | Definición |
|---|---|---|
| **Usuario** | `User` | Cuenta base del sistema con nombre y email; puede especializarse en Administrador o Docente |
| **Administrador** | `Administrator` | Usuario con responsabilidades de gestión académica (Programa, Admisiones o RedEdu) |
| **Docente** | `Teacher` | Usuario encargado de impartir sesiones; se asocia a disponibilidad y tipo de vinculación |
| **Disponibilidad docente** | `TeacherAvailability` | Bloques de tiempo en los que un docente puede dictar sesiones |
| **Asignatura** | `Subject` | Curso académico de la malla curricular con duración y tipo de clase definidos |
| **Grupo de estudiantes** | `StudentGroup` | Cohorte o grupo asociado a una asignatura y un programa académico |
| **Espacio académico** | `AcademicSpace` | Aula, laboratorio o recurso virtual donde puede ocurrir una sesión |
| **Sesión** | `Session` | Evento programado: un grupo, un docente, un horario y un espacio (opcional si es virtual) |
| **Modalidad** | `SessionModality` | Tipo de sesión: Presencial o Virtual |
| **Alternancia** | `Alternation` | Modelo híbrido en el que algunas sesiones ocurren virtualmente en semanas específicas |
| **Semana_num** | `WeekNumber` | Número de la semana del semestre aplicado a la sesión |
| **Tipo de alternancia** | `AlternanciaType` | Enum canónico del dominio; valores: `TypeA`, `TypeB`, `NonAlternating` |
| **Restricción dura** | `HardConstraint` | Regla que nunca debe violarse en un horario válido (ver `hard-constraints.md`) |
| **Restricción blanda** | `SoftConstraint` | Preferencia que debe optimizarse, pero puede relajarse (ver `soft-constraints.md`) |
| **Grafo de conflictos** | `ConflictGraph` | Grafo donde los nodos son sesiones y las aristas representan conflictos; se usa en la Fase 1 |
| **Fitness** | `Fitness` | Puntuación numérica que mide qué tan bien un horario satisface restricciones blandas |
| **OR-Tools / CP-SAT** | `OR-Tools / CP-SAT` | Biblioteca de optimización usada en la fase de programación por restricciones |
| **EPPlus** | `EPPlus` | Biblioteca .NET para leer/escribir archivos de Excel usada en la ingesta de datos |
| **EF Core** | `EF Core` | Entity Framework Core; ORM usado para acceso a datos |
| **UCTP** | `UCTP` | University Course Timetabling Problem; problema formal de optimización combinatoria |

---

## Convenciones de nomenclatura para el código

- Clases de entidad: sustantivo en singular en inglés (`User`, `Teacher`, `Session`, `StudentGroup`, `AcademicSpace`, `Subject`)
- Colecciones: sustantivo en plural en inglés (`Users`, `Teachers`, `Sessions`)
- Usa inglés para todos los identificadores de código; español solo para etiquetas visibles al usuario y títulos de documentos
- Usa la enumeración `AlternanciaType` con los valores `TypeA`, `TypeB` y `NonAlternating` (excepción explícita al uso estricto de inglés por ser término canónico del dominio)

---

## Preguntas abiertas

- ¿"Espacio académico" debe incluir enlaces virtuales o solo ubicaciones físicas?
- ¿`AlternanciaType` se almacena en el grupo o se deriva de `WeekNumber` y reglas institucionales?
