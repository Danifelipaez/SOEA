# Pendientes y decisiones abiertas

> Consolidado el 2026-10-14 a partir de las auditorías (2026-05, 2026-09-14, 2026-09-28) y los planes ya cerrados, que se eliminaron de `docs/` (siguen en el historial de git). Verificar cada punto contra el código antes de actuar.

## Bloqueantes para uso real

| ID | Pendiente | Tipo |
|---|---|---|
| SEC-2 | La API es pública y anónima (`DELETE`/`POST` sobre todo el catálogo). Falta restringir el acceso: lista de IP de la universidad o Easy Auth / cuenta Microsoft | Decisión + infraestructura |
| SEC-3 | Activar `httpsOnly=true` en `soea-api` | Infraestructura |
| PUB | `PublicarHorarioService` y el paso `/publicar` (impedir publicar con violaciones > 0) | Desarrollo |
| PROD | El horario guardado en prod (2026-10-03) viola la disponibilidad de grupo: hay que regenerarlo tras desplegar la corrección de "día sin entrada = cerrado" | Operación |

## Datos que debe confirmar la coordinación (no inventarlos)

- Capacidad exacta de cada espacio. El importador asume 30 si falta (también 30 estudiantes por grupo y 40 h / franja matutina por docente) y lo avisa.
- Prioridad de los cursos (1/2/3) y la lista definitiva de asignaturas TipoA/TipoB.
- Duración por asignatura (horas por sesión y sesiones por semana); en particular, laboratorios de 3 h o de 2 h, porque con las ventanas actuales los laboratorios se llenan.
- Rango de la jornada (hoy L–V 06–22, sábado 06–14).
- Semántica de la alternancia por parejas (HC-ALT), marcada `VERIFICA` en el código.
- HC-T02 (laboratorios antes de 19:30) y HC-T04 (receso de mediodía): sin implementar hasta tener confirmación.

## Deuda técnica

| ID | Pendiente |
|---|---|
| L-1 | El generador agenda el catálogo que envía el navegador, no el de la BD |
| DUP-1 | Las reglas de docente están en tres sitios (asignar docente, sesión manual, reacomodar) más el espejo del frontend (`horario.reglas.ts`) |
| L-2 | No está definido qué significa `Docente.BloquesDisponibles`: se llena desde el Excel con las clases que el docente ya tiene, pero se usa como disponibilidad |
| L-8 | La UI no permite elegir el semestre (el backend ya admite varios) |
| REAC | `/reacomodar` no comparte el limitador de concurrencia ni el plazo total de `/generar` |
| IMP | El importador marca toda aula fija como requisito de Laboratorio, también en teoría. Hay que decidir la semántica antes de filtrar en la UI |
| MAN | El diálogo "Agregar clase" permite una sesión manual TipoA/TipoB sin pareja, lo que choca con ALT-05 |
| DB | Esquema: `horarios.sesion_ids` en JSON sin FK, JSON en columnas `text`, unicidad por nombre; columnas muertas (`Sesion.EsBloque`, `EstaDividida`) y `PesoBalanceSemanas` obsoleto en el contrato |
| OPS-1 | `Database.Migrate()` corre al arrancar el API; falta un health check que toque la BD |
| SEC-5 | Licencia de EPPlus: declararla como organización es una decisión legal de la universidad |
| FUS | Ejecutar `docs/scripts/Fase2_FusionAsignaturasDuplicadas.sql` sobre los duplicados heredados, si siguen existiendo |
| UI | Vista de reporte de conflictos en el frontend |
