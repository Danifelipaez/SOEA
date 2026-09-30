/** Jornada institucional — espejo de SOEA.Domain/Services/GrillaInstitucional.cs. Única fuente en el frontend. */
export const HORA_APERTURA = 6;
export const HORA_CIERRE = 22;
export const HORA_CIERRE_SABADO = 14;

/**
 * Horas en que una misma aula puede usarse en la semana: lunes a viernes de apertura a cierre, y el sábado hasta
 * su cierre anterior. L-6 auditoría 2026-09-28: el KPI de ocupación usaba 16 h × 6 días = 96 h, contando el sábado
 * como jornada completa (real: 88 h), y por eso mostraba menos ocupación de la que hay.
 */
export const HORAS_AULA_SEMANA = 5 * (HORA_CIERRE - HORA_APERTURA) + (HORA_CIERRE_SABADO - HORA_APERTURA);
