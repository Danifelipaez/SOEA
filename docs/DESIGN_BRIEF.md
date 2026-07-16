# SOEA — Brief de diseño para el nuevo frontend

> Documento autocontenido para diseñar el rediseño del frontend de SOEA. No requiere acceso al código. Acompañado de dos anexos: `MAPEO_FLUJOS_FRONTEND.md` (journey y decisiones de alcance, validado por el usuario) y `REQUISITOS_FRONTEND.md` (inventario exacto de formularios, campos y comportamiento de API por pantalla).

## 1. Qué es SOEA

SOEA (Sistema de Optimización de Espacios Académicos) genera horarios semanales universitarios para la Universidad del Magdalena, con un modelo de **alternancia bi-semanal**: cada asignatura alterna semanas presenciales y virtuales (Semana A / Semana B). El usuario carga el catálogo del semestre (asignaturas, docentes, espacios, grupos), ejecuta un optimizador server-side de 3 fases, y ajusta el horario resultante a mano.

**Problema del frontend actual:** las pantallas están agrupadas por entidad CRUD (tabs de Asignaturas/Docentes/Espacios/Grupos, dos dashboards separados, pantallas de configuración huérfanas) en vez de por flujo de trabajo. El rediseño reorganiza todo alrededor del journey real del usuario.

## 2. Usuario único: Coordinadora Académica

Una sola persona usa el sistema de punta a punta (perfil real: coordinadora académica, no técnica, trabaja en PC de oficina). No hay login ni roles — `Docente` y `Estudiante` son datos que ella administra, no cuentas. Diseñar para **una experiencia de operador único, desktop-first**.

## 3. Journey (validado — la IA nueva se organiza alrededor de esto)

Cinco pasos en orden, con dependencias reales entre ellos:

| # | Paso | Qué hace | Estado |
|---|------|----------|--------|
| 1 | **Preparar catálogo** | Cargar Excel o crear a mano asignaturas/docentes/espacios/grupos; fusionar docentes duplicados; configurar tipos de alternancia | Habilita el paso 2 |
| 2 | **Generar horario** | Opcional: fijar sesiones base y ajustar parámetros avanzados (colapsable). Click Generar → espera al optimizador → grid semanal o error con logs | Requiere catálogo completo |
| 3 | **Ajustar** | Editar sesiones inline sobre el grid (día/hora/espacio/semana), asignar docentes, crear sesiones manuales, guardar como horario base | Sobre horario generado |
| 4 | **Revisar** | KPIs de solo lectura: % ocupación de espacios, presencial/virtual, franjas ociosas, carga por docente (Normal/Alerta/Límite) | Sobre horario generado |
| 5 | **Publicar** | **Bloqueado** — el backend aún no lo soporta. Reservar el lugar en el journey con la acción deshabilitada | Futuro |

Decisiones ya tomadas (no re-abrir):
- Los parámetros del algoritmo genético van como **sección avanzada/colapsable dentro de Generar**, no como pantalla aparte.
- Los KPIs son **un paso del mismo flujo**, no un dashboard de otro rol.
- El catálogo de tipos de alternancia vive **dentro del paso 1** (junto a Asignaturas); la asignación de alternancia por asignatura se unifica en **una sola vista** (hoy está duplicada en dos pantallas).
- Exportar/Importar horario como JSON: **se mantiene pero oculto** como opción avanzada (menú secundario), fuera del flujo principal.
- Fuera de alcance: vista de horario por docente, login/roles, autoservicio de estudiantes.

La forma exacta del nav (stepper lineal vs. sidebar por secciones vs. híbrido) está **abierta a exploración de diseño** — la restricción es que la agrupación siga el journey, no las entidades.

## 4. Conceptos visuales clave del dominio

Estos conceptos son el corazón del producto y necesitan un lenguaje visual claro:

- **Grid semanal**: lunes a sábado × horas 06:00–22:00. Es la vista central del producto (pasos 2 y 3). Cada celda pinta una sesión con: asignatura, docente (puede estar sin asignar), espacio (o "virtual"), y su alternancia.
- **Semana A / Semana B**: todo horario es bi-semanal. Cada sesión pertenece a la semana "A" o "B". El grid necesita resolver cómo se ven ambas semanas (toggle, vista lado a lado, superposición…) — decisión de diseño abierta.
- **Presencial vs. virtual**: una sesión virtual es sincrónica online, ocupa la misma franja horaria pero sin espacio físico (conserva referencia al laboratorio "hogar" de su contraparte presencial). Necesita distinción visual inmediata.
- **Tipos de alternancia con color**: cada tipo (TipoA, TipoB, SinAlternancia + tipos personalizados) tiene un color hex configurable por el usuario (default `#607d8b`). El color identifica la asignatura/patrón en el grid.
- **Carga docente en 3 estados**: Normal / Alerta / Límite (semáforo respecto a sus horas máximas).
- **Disponibilidad por día**: docentes y grupos declaran disponibilidad lunes–sábado con 3 modos por día: no disponible, franja preset (Todo el día, Oficina, Matutino, Vespertino, Nocturno) o franja específica desde/hasta. Es un selector recurrente — merece un componente propio.

## 5. Patrones de feedback y error (contratos del backend)

- **Generación**: operación larga server-side (hasta ~2 min). Necesita estado de espera claro. Si no es factible: mensaje de error + logs de ejecución en lugar del grid (los logs solo aparecen cuando hay problema, no como panel permanente).
- **Conflicto duro (409)**: al asignar docente con solape de horario → error bloqueante dentro del popup, no se cierra.
- **Violación de restricción (422)**: al crear sesión manual inválida → mensaje de la restricción violada como error bloqueante en el popup.
- **Advertencias blandas**: disponibilidad/carga del docente → la acción SÍ se guarda; mostrar aviso no bloqueante después.
- **Import Excel**: al terminar muestra un resumen de contadores (creados/actualizados por entidad) + lista de advertencias.

## 6. Restricciones técnicas

- **Stack**: Angular 21 con Angular Material + CDK ya instalados (Material como base del sistema de componentes es lo natural); chart.js disponible para los KPIs.
- **Idioma de la UI**: español.
- **Plataforma**: desktop-first (uso en PC de oficina); no hay requisito móvil.
- El detalle exacto de cada formulario (campos, obligatorios, selects y sus opciones literales) está en el anexo `REQUISITOS_FRONTEND.md` — usarlo como fuente de verdad al diseñar popups y forms.

## 7. Entregable esperado del trabajo de diseño

1. Propuesta de IA/sitemap: navegación agrupada por el journey de 5 pasos.
2. Wireframes/diseños de las vistas clave, en orden de importancia: **grid semanal de horario** (con semana A/B y edición inline), **flujo de catálogo** (tabs + import Excel + duplicados), **flujo de generación** (con parámetros colapsables y estados de espera/error), **vista de KPIs**.
3. Sistema de componentes base (Material-friendly): el selector de disponibilidad por día, la celda de sesión del grid, los popups de edición, los estados de feedback (§5).
