# Especificación de Requisitos de Software (SRS)
**Última actualización:** 2026-05-16

## Propósito
Requisitos funcionales y no funcionales de alto nivel para SOEA.
Este documento es la fuente autorizada de lo que el sistema debe hacer.
Copilot usa esto al generar casos de uso, endpoints de API y casos de prueba.

## Alcance
Todos los requisitos para el motor de optimización del backend, la ingesta de datos, la API y el frontend.

---

## Requisitos funcionales

### FR-01 — Ingesta de datos desde Excel
El sistema deberá aceptar archivos Excel que contengan:
- Datos de malla curricular (asignaturas, horas por semana, cohortes)
- Disponibilidad de docentes (bloques de tiempo por docente y por día)
- Inventario de espacios (capacidad, tipo, equipamiento)

### FR-02 — Generación de horarios
El sistema deberá producir un horario completo para un semestre a partir de los datos de entrada,
usando un pipeline de tres fases (coloreado de grafos → CP → algoritmo genético).

### FR-03 — Cumplimiento de restricciones duras
El horario generado no deberá violar ninguna restricción dura definida en
`docs/business-rules/hard-constraints.md`.

### FR-04 — Optimización de restricciones blandas
El sistema deberá optimizar las restricciones blandas (ver `docs/business-rules/soft-constraints.md`)
mediante la fase del Algoritmo Genético.

### FR-05 — Exportación JSON
El sistema deberá exportar el horario final como un documento JSON estructurado
(el formato se define en `docs/data/json-output-spec.md`).

### FR-06 — Interfaz web basada en roles
El sistema deberá proporcionar una interfaz web con vistas y permisos diferenciados para:
Administrador, Coordinador, Docente y Estudiante (ver `docs/requirements/stakeholders.md`).

### FR-07 — Informe de validación del horario
El sistema deberá producir un informe de validación que liste cualquier violación
restante de restricciones blandas después de la optimización, con una puntuación de severidad.

### FR-08 — Soporte de alternancia
El sistema deberá asignar las sesiones de acuerdo con las reglas de alternancia (Tipo A / Tipo B),
según se define en `docs/business-rules/alternancia.md`.

---

## Requisitos no funcionales

### NFR-01 — Rendimiento
El pipeline de optimización deberá completarse en menos de 10 minutos para una carga semestral estándar
(hasta 200 cohortes, 50 docentes, 30 espacios).

### NFR-02 — Corrección
Todas las restricciones duras deben cumplirse (cero violaciones) en la salida final.

### NFR-03 — Usabilidad
Los usuarios no técnicos (Coordinadores) deben poder revisar y entender el horario generado
sin capacitación adicional más allá de una sesión de inducción de 30 minutos.

### NFR-04 — Mantenibilidad
La base de código deberá seguir las convenciones de Clean Architecture descritas en
`docs/architecture/architecture-overview.md`.

### NFR-05 — Seguridad
El control de acceso basado en roles deberá impedir que los usuarios accedan a datos fuera de su alcance.

---

## Preguntas abiertas

- ¿Cuál es el número máximo exacto de sesiones por semestre para dimensionar los objetivos de rendimiento?
- ¿La regeneración de horarios debería ser posible a mitad de semestre (reoptimización parcial)?
