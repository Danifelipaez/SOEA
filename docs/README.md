# Documentación SOEA

SOEA (Sistema de Optimización de Espacios Académicos) genera el horario semanal de la Universidad del Magdalena con alternancia presencial/virtual por semanas. A partir de asignaturas, grupos, docentes y aulas, un pipeline de 3 fases (coloración de grafos → CP-SAT → algoritmo genético) produce un horario que cumple todas las restricciones duras.

| Documento | Contenido |
|---|---|
| [architecture.md](architecture.md) | Capas, proyectos, servicios de dominio, API REST, configuración, cómo agregar una entidad |
| [domain.md](domain.md) | Glosario, entidades, alternancia, restricciones duras y blandas, esquema de BD |
| [algorithms.md](algorithms.md) | Pipeline de generación, las 3 fases, Semana B reactiva, validador, edición posterior |
| [frontend.md](frontend.md) | Recorrido de 5 pasos, estado, importación de Excel |
| [operacion.md](operacion.md) | Desarrollo local, migraciones, pruebas, CI/CD y producción en Azure |
| [pendientes.md](pendientes.md) | Bloqueantes, datos por confirmar con la coordinación y deuda técnica |

Las reglas para trabajar en el código (arquitectura no negociable, convenciones, comandos) están en [`CLAUDE.md`](../CLAUDE.md). Los documentos de planificación y auditoría ya cerrados se eliminaron; están en el historial de git.
