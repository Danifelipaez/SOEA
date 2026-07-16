# Mapeo de roles y flujos — rediseño frontend SOEA

> Fecha: 2026-07-16. Complementa `docs/REQUISITOS_FRONTEND.md` (catálogo de endpoints y popups por pantalla); este documento define roles, journey y agrupación por flujo. Es el paso 1 del rediseño acordado: mapeo → sitemap/IA → wireframes → implementación.

## Contexto

El objetivo del rediseño es reconstruir el frontend con foco en flujo de uso, agrupación de funcionalidades y mejor UX/UI, en vez de seguir agrupando pantallas por entidad CRUD (Asignaturas/Docentes/Espacios/Grupos como tabs sueltos, dashboards duplicados, etc.).

Hallazgos verificados en código que fuerzan las decisiones de scope:

- **No existe autenticación ni roles en el backend** (sin JWT, sin `[Authorize]`, sin entidad `Usuario`/`Rol`). Confirmado en código, no solo en CLAUDE.md.
- **`Docente` y `Estudiante` son entidades de catálogo**, no actores del sistema — no hay ninguna pantalla, ruta ni flujo dirigido a ellos como usuarios finales.
- `docs/REQUISITOS_FRONTEND.md` (§ fuera de alcance) marca explícitamente **"Vista de horario por docente (descartada)"**, aunque hoy existe `/horario-docente` sin guard que la ligue a un docente real.
- `dashboard-developer` expone tuning interno del algoritmo genético (mutación, cruce, pesos, logs) sin ningún control de acceso — y `REQUISITOS_FRONTEND.md §2` ya especifica esos mismos parámetros como un popup colapsable dentro de "Generar Horario", no como pantalla aparte.
- `dashboard-admin` (KPIs de ocupación/carga docente) no tiene nada en el código que lo distinga de un uso normal del Coordinador — es de solo lectura y depende de que ya existan sesiones generadas.

**Decisiones de alcance confirmadas con el usuario:**

1. Un solo rol operativo — no se modelan Admin/Developer como roles de producto separados; hoy son solo vistas dentro del mismo flujo.
2. `/horario-docente` sale del alcance del rediseño (se sigue lo que ya decía `REQUISITOS_FRONTEND.md`).
3. Los parámetros del algoritmo genético (`dashboard-developer`) se funden como sección avanzada/colapsable dentro del flujo de generación.
4. Los KPIs de `dashboard-admin` son un paso de revisión dentro del mismo flujo del Coordinador, no una vista de otro rol.

## Rol único: Coordinador Académico

Es quien hoy usa el sistema de punta a punta (en la práctica, el perfil de "Rosa" referenciado en `CLAUDE.md`/`domain.md` como fuente de verdad de datos). Carga el catálogo del semestre, genera el horario, resuelve conflictos, asigna docentes y revisa resultados. `Docente` y `Estudiante` son datos que administra, no cuentas que usan el sistema. Login/roles quedan explícitamente pendientes (bloqueados por el backend) y fuera de este rediseño.

## Mapa de flujos (journey único, en orden)

### 1. Preparar catálogo del semestre

*Pantallas hoy: Ingesta (tabs Asignaturas/Docentes/Espacios/Grupos) + Tipos de Alternancia*

- Cargar por Excel (`POST /import/excel`, detecta modo automático) **o** crear/editar manualmente cada entidad.
- Sub-flujo Docentes: detectar y fusionar duplicados (`GET /docentes/duplicados` → `POST /docentes/fusionar`).
- Configurar catálogo de "tipos de alternancia" (hoy pantalla aparte, colgada de dashboard-developer vía botón "← Developer" — con el Developer disuelto, esto necesita una nueva casa; candidato natural: dentro de este mismo flujo de catálogo, como configuración de Asignaturas). **[Pregunta abierta (a)]**
- Asignar tipo de alternancia por asignatura — hoy existe **dos veces**: edición individual dentro del popup de Asignatura, y edición masiva en `configuracion-alternancia`. Ambas son el mismo dato (`PATCH /asignaturas/{id}/alternancia`); son candidatas a fusionarse en una sola vista con modo "fila por fila" en la siguiente fase de sitemap.
- Limitación conocida: el import Excel solo soporta 1 bloque de sesiones por asignatura, colapsa tracks múltiples (teoría/virtual/laboratorio) — el shape legado está documentado en `Asignatura.cs` ("un solo bloque de sesiones", pre-desglose por tipo de sesión). Afecta el contrato de este flujo; decidir si se corrige antes o después del rediseño de UI.
- **Salida:** catálogo completo → habilita el flujo 2.

### 2. Generar horario

*Pantalla hoy: Horario (botón Generar) + dashboard-developer (parámetros, fusionado aquí)*

- Opcional: definir "horario base" (sesiones fijas que CP-SAT no puede mover).
- Opcional/avanzado: ajustar hiperparámetros del algoritmo genético (población, mutación, cruce, pesos de soft constraints) — sección colapsable, no bloquea el flujo (todo tiene default).
- Click Generar → pipeline de 3 fases (GraphColoring → CP-SAT → Genético) server-side.
- Resultado: grid semanal si es factible; si no, mensaje de error + logs de ejecución (los logs crudos de `dashboard-developer` se muestran aquí solo cuando hay un problema, no como panel permanente).
- **Depende de:** flujo 1 completo (la pantalla ya valida esto hoy y redirige a Ingesta si falta algo — mantener ese guard).

### 3. Ajustar y resolver conflictos

*Pantalla hoy: Horario (edición inline sobre el grid ya generado)*

- Editar una sesión existente: día/hora/espacio/alternancia/semana, con validación de conflictos en vivo.
- Asignar/cambiar/desasignar docente de una sesión (`PATCH /sesiones/{id}/docente`) — 409 si hay solape duro de horario del docente; advertencias blandas (disponibilidad, carga horaria) no bloquean.
- Crear sesión manual sobre un horario ya generado, sin re-optimizar (`POST /horario/sesion-manual`).
- Guardar el horario actual como "base" reutilizable en la próxima generación.
- Nota de alcance: "Exportar/Importar horario como JSON" existe hoy pero parece un mecanismo de respaldo/debug más que una necesidad real del Coordinador. **[Pregunta abierta (b)]**

### 4. Revisar resultado

*Pantalla hoy: dashboard-admin, fusionado como paso del mismo flujo*

- KPIs: % ocupación, sesiones presenciales/virtuales, franjas ociosas por espacio, carga por docente (Normal/Alerta/Límite).
- Es de solo lectura — el punto natural es *después* de generar/ajustar, no una pantalla aislada en el nav principal.

### 5. Publicar (futuro, hoy bloqueado)

- No existe `PublicarHorarioService` en el backend todavía. La UI debe dejar esta acción deshabilitada/bloqueada hasta que exista — no se diseña a fondo en este rediseño, solo se reserva el lugar en el journey.

## Fuera de alcance (confirmado)

- `/horario-docente` como pantalla independiente — se elimina.
- Roles múltiples con login/guard (Admin/Coordinador/Docente/Estudiante) — bloqueado por falta de JWT en backend; no se diseña ahora.
- Autoservicio de Docente/Estudiante ("ver mi horario") — no existe hoy ni se modela en este rediseño.

## Preguntas abiertas — RESUELTAS (2026-07-16)

- **(a)** Tipos de alternancia → vive **dentro del flujo de catálogo** (paso 1), junto a Asignaturas; se fusiona con la asignación masiva de `configuracion-alternancia` en una sola vista.
- **(b)** Exportar/Importar horario JSON → **se mantiene pero oculto como opción avanzada** (menú secundario en la vista de horario, fuera del flujo principal).

## Verificación de este mapeo

- Journey de 5 pasos **confirmado por el usuario** como reflejo del proceso real (2026-07-16).
- Las dos preguntas abiertas quedaron resueltas arriba — el mapeo está listo como base para el sitemap.

## Próximo paso (no incluido en este mapeo)

Con este journey de 5 pasos como base: sitemap/IA nueva (rutas agrupadas por flujo en vez de por entidad) y luego wireframes de baja fidelidad.
