# Alcance

## Propósito
Definir qué es SOEA, qué problema resuelve y qué no cubre explícitamente.
Este es el primer documento que Copilot debe usar al generar código de alto nivel o responder preguntas arquitectónicas.

## Alcance
Este documento cubre el contexto institucional, la declaración del problema, los objetivos del sistema y las exclusiones.

---

## Panorama del sistema

SOEA (Sistema de Optimización de Espacios Académicos) es un sistema de horario académico universitario diseñado para instituciones de educación superior colombianas que operan bajo un modelo de **alternancia** (híbrido presencial/virtual).

El objetivo principal es generar automáticamente horarios académicos factibles y optimizados que respeten las restricciones duras institucionales (espacios, capacidad, disponibilidad de docentes) y optimicen las preferencias blandas (horarios compactos, balance de carga, estabilidad de aulas).

---

## Problema que se resuelve

Programar manualmente sesiones para cientos de cohortes en decenas de espacios físicos es:
- Lento (actualmente toma días o semanas por semestre)
- Propenso a errores (conflictos, asignaciones por encima de la capacidad, violaciones de reglas)
- Difícil de adaptar cuando ocurren cambios a mitad de semestre

SOEA automatiza este proceso mediante un pipeline de optimización de tres fases:
1. Coloreado de grafos — preasignación inicial de espacios de tiempo
2. Programación por restricciones (OR-Tools CP-SAT) — imposición de factibilidad
3. Algoritmo genético — optimización de restricciones blandas

---

## Objetivo de negocio

Producir un horario completo, libre de conflictos y optimizado para un semestre, exportable como JSON y consultable mediante una interfaz web basada en roles.

---

## Dentro del alcance

- Programación de sesiones académicas para cohortes de pregrado
- Asignación de alternancia Tipo A / Tipo B (ver `docs/business-rules/alternancia.md`)
- Asignación de espacios físicos (aulas, laboratorios)
- Validación de disponibilidad de docentes
- Ingesta de datos desde Excel (archivos de malla curricular y disponibilidad)
- Interfaz web basada en roles (Administrador, Coordinador, Docente, Estudiante)
- Validación piloto con un subconjunto de programas (definido en `docs/business-rules/pilot-limits.md`)

---

## Fuera del alcance

- Programación de exámenes
- Gestión de contratos de docentes
- Gestión de matrículas de estudiantes
- Aplicación móvil
- Reserva de espacios en tiempo real
- Integración con sistemas SIS/ERP externos (fase futura)

---

## Contexto del piloto

El despliegue inicial apunta a un piloto limitado (ver `docs/business-rules/pilot-limits.md`) para validar la corrección antes de un despliegue a nivel institucional.

---

## Preguntas abiertas

- ¿Qué programas específicos están incluidos en el piloto?
- ¿Existe un número máximo de cohortes por semestre para la primera versión?
- ¿Las sesiones virtuales se programan en espacios físicos o se excluyen por completo?
