# Scope

## Purpose
Definir qué es SOEA, qué problema resuelve y qué no cubre explícitamente.
Este es el primer documento que Copilot debe usar al generar código de alto nivel o responder preguntas arquitectónicas.

## Scope
Este documento cubre el contexto institucional, la declaración del problema, los objetivos del sistema y las exclusiones.

---

## System Overview

SOEA (Sistema de Optimización de Espacios Académicos) es un sistema de horario académico universitario diseñado para instituciones de educación superior colombianas que operan bajo un modelo de **alternancia** (híbrido presencial/virtual).

El objetivo principal es generar automáticamente horarios académicos factibles y optimizados que respeten las restricciones duras institucionales (espacios, capacidad, disponibilidad de docentes) y optimicen las preferencias blandas (horarios compactos, balance de carga, estabilidad de aulas).

---

## Problem Being Solved

Programar manualmente sesiones para cientos de cohortes en decenas de espacios físicos es:
- Lento (actualmente toma días o semanas por semestre)
- Propenso a errores (conflictos, asignaciones por encima de la capacidad, violaciones de reglas)
- Difícil de adaptar cuando ocurren cambios a mitad de semestre

SOEA automatiza este proceso mediante un pipeline de optimización de tres fases:
1. Graph Coloring — preasignación inicial de espacios de tiempo
2. Constraint Programming (OR-Tools CP-SAT) — imposición de factibilidad
3. Genetic Algorithm — optimización de restricciones blandas

---

## Business Objective

Producir un horario completo, libre de conflictos y optimizado para un semestre, exportable como JSON y consultable mediante una interfaz web basada en roles.

---

## In Scope

- Programación de sesiones académicas para cohortes de pregrado
- Asignación de alternancia Tipo A / Tipo B (ver `docs/business-rules/alternancia.md`)
- Asignación de espacios físicos (aulas, laboratorios)
- Validación de disponibilidad de docentes
- Ingesta de datos desde Excel (archivos de malla curricular y disponibilidad)
- Interfaz web basada en roles (Admin, Coordinator, Instructor, Student)
- Validación piloto con un subconjunto de programas (definido en `docs/business-rules/pilot-limits.md`)

---

## Out of Scope

- Programación de exámenes
- Gestión de contratos de docentes
- Gestión de matrículas de estudiantes
- Aplicación móvil
- Reserva de espacios en tiempo real
- Integración con sistemas SIS/ERP externos (fase futura)

---

## Pilot Context

El despliegue inicial apunta a un piloto limitado (ver `docs/business-rules/pilot-limits.md`) para validar la corrección antes de un despliegue a nivel institucional.

---

## Open Questions

- ¿Qué programas específicos están incluidos en el piloto?
- ¿Existe un número máximo de cohortes por semestre para la primera versión?
- ¿Las sesiones virtuales se programan en espacios físicos o se excluyen por completo?
