# Glosario

## Propósito
Definir cada término del dominio usado en el código, la documentación y la interfaz de SOEA para que todos los colaboradores (y Copilot) usen un lenguaje consistente. En caso de duda, consulta este archivo antes de nombrar una clase, variable o endpoint.

## Alcance
Todos los términos específicos del dominio usados en el backend, el frontend y la documentación.

---

## Términos

| Término | Definición |
|---|---|
| **Usuario** | Cuenta base del sistema con nombre y email; puede especializarse en Administrador o Docente |
| **Administrador** | Usuario con responsabilidades de gestión académica (Programa, Admisiones o RedEdu) |
| **Docente** | Usuario encargado de impartir sesiones; se asocia a disponibilidad y tipo de vinculación |
| **Disponibilidad docente** | Bloques de tiempo en los que un docente puede dictar sesiones |
| **Asignatura** | Curso académico de la malla curricular con duración y tipo de clase definidos |
| **Grupo de estudiantes** | Cohorte o grupo asociado a una asignatura y un programa académico |
| **Espacio académico** | Aula, laboratorio o recurso virtual donde puede ocurrir una sesión |
| **Sesión** | Evento programado: un grupo, un docente, un horario y un espacio (opcional si es virtual) |
| **Modalidad** | Tipo de sesión: Presencial o Virtual |
| **Alternancia** | Modelo híbrido en el que algunas sesiones ocurren virtualmente en semanas específicas |
| **Semana_num** | Número de la semana del semestre aplicado a la sesión |
| **AlternanciaType** | Conjunto canónico del dominio: `TypeA`, `TypeB`, `NonAlternating` |
| **Restricción dura** | Regla que nunca debe violarse en un horario válido (ver `hard-constraints.md`) |
| **Restricción blanda** | Preferencia que debe optimizarse, pero puede relajarse (ver `soft-constraints.md`) |
| **Grafo de conflictos** | Grafo donde los nodos son sesiones y las aristas representan conflictos; se usa en la Fase 1 |
| **Fitness** | Puntuación numérica que mide qué tan bien un horario satisface restricciones blandas |
| **OR-Tools / CP-SAT** | Biblioteca de optimización usada en la fase de programación por restricciones |
| **EPPlus** | Biblioteca .NET para leer/escribir archivos de Excel usada en la ingesta de datos |
| **EF Core** | Entity Framework Core; ORM usado para acceso a datos |
| **UCTP** | University Course Timetabling Problem; problema formal de optimización combinatoria |

---

## Convenciones de nomenclatura para el código

- Clases de entidad: sustantivo en singular (`Usuario`, `Docente`, `Sesion`, `GrupoDeEstudiantes`, `EspacioAcademico`, `Asignatura`)
- Colecciones: sustantivo en plural (`Usuarios`, `Docentes`, `Sesiones`)
- Usa la enumeración `AlternanciaType` con los valores `TypeA`, `TypeB` y `NonAlternating`

---

## Preguntas abiertas

- ¿"Espacio académico" debe incluir enlaces virtuales o solo ubicaciones físicas?
- ¿`AlternanciaType` se almacena en el grupo o se deriva de `Semana_num` y reglas institucionales?
