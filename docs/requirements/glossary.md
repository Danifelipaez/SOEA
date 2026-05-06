# Glosario

## Propósito
Definir cada término del dominio usado en el código, la documentación y la interfaz de SOEA para que todos los colaboradores (y Copilot) usen un lenguaje consistente. En caso de duda, consulta este archivo antes de nombrar una clase, variable o endpoint.

## Alcance
Todos los términos específicos del dominio usados en el backend, el frontend y la documentación.

---

## Términos

| Term | Definition |
|---|---|
| **Session** | Evento académico programado: una asignatura + un docente + una cohorte + un espacio de tiempo + espacio opcional |
| **Cohort** | Grupo de estudiantes inscritos en el mismo programa académico y semestre (por ejemplo, "Ingeniería de Sistemas — Semestre 3") |
| **Space** | Ubicación física donde puede realizarse una sesión presencial (aula, laboratorio, auditorio) |
| **Espacio** | Equivalente en español de Space; se usa en los documentos institucionales originales |
| **Instructor** | Persona asignada para impartir una sesión (profesor, catedrático, asistente de docencia) |
| **Time Slot** | Unidad programable discreta: día + hora de inicio + hora de fin (por ejemplo, lunes 07:00–09:00) |
| **Schedule** | Asignación completa de todas las sesiones a espacios de tiempo y espacios para un semestre |
| **Alternancia** | Modelo híbrido de enseñanza: las cohortes alternan entre semanas presenciales y virtuales |
| **Type A (Tipo A)** | Cohorte en alternancia que asiste presencialmente en semanas impares y virtualmente en semanas pares |
| **Type B (Tipo B)** | Cohorte en alternancia que asiste presencialmente en semanas pares y virtualmente en semanas impares |
| **Hard Constraint** | Regla que nunca debe violarse en un horario válido (ver `hard-constraints.md`) |
| **Soft Constraint** | Preferencia que debe optimizarse, pero puede relajarse (ver `soft-constraints.md`) |
| **Conflict** | Dos sesiones que no pueden compartir el mismo espacio de tiempo (mismo docente, mismo espacio o misma cohorte) |
| **Conflict Graph** | Grafo donde los nodos son sesiones y las aristas representan conflictos; se usa en la Fase 1 |
| **Chromosome** | Codificación completa de un horario usada en el Algoritmo Genético (Fase 3) |
| **Fitness** | Puntuación numérica que mide qué tan bien un cromosoma satisface las restricciones blandas |
| **OR-Tools** | Biblioteca de optimización de código abierto de Google; se usa para Programación por Restricciones en la Fase 2 |
| **CP-SAT** | Solucionador de programación por restricciones de OR-Tools usado en la Fase 2 |
| **EPPlus** | Biblioteca .NET para leer/escribir archivos de Excel; se usa para la ingesta de datos |
| **EF Core** | Entity Framework Core; el ORM usado para acceso a datos |
| **UCTP** | University Course Timetabling Problem; el problema formal de optimización combinatoria que resuelve SOEA |
| **Pilot** | Despliegue inicial limitado que cubre un subconjunto de programas para validar el sistema antes del despliegue completo |
| **Block** | Sesión que abarca varias horas consecutivas (por ejemplo, un bloque de laboratorio de 3 horas en un solo día) |
| **Split Block** | Sesión cuyas horas se distribuyen en varios días |

---

## Convenciones de nomenclatura para el código

- Clases de entidad: sustantivo en singular (`Session`, `Cohort`, `Space`)
- Colecciones: sustantivo en plural (`Sessions`, `Cohorts`, `Spaces`)
- Usa inglés para todos los identificadores de código; español solo para etiquetas visibles al usuario y títulos de documentos
- Usa la enumeración `AlternanciaType` con los valores `TypeA`, `TypeB` y `NonAlternating`

---

## Preguntas abiertas

- ¿"Espacio" siempre es una sala física o también puede ser un enlace de reunión virtual?
- ¿Las "sesiones virtuales" deben asignarse a una entidad Space o excluirse de las restricciones de espacio?
