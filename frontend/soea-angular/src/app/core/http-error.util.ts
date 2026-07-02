/**
 * Extrae un mensaje legible de un error HTTP del backend.
 * Soporta cuerpos string (BadRequest/NotFound con mensaje plano), application/problem+json
 * (ProblemDetails con detail/title) y el envelope { error: "..." } usado por SesionesController
 * y HorarioController (404/409/400/500 de asignar-docente y sesión-manual).
 */
export function mensajeErrorHttp(err: any): string {
  const cuerpo = err?.error;
  if (typeof cuerpo === 'string' && cuerpo.trim()) return cuerpo;
  return cuerpo?.error ?? cuerpo?.detail ?? cuerpo?.title ?? err?.message ?? 'Error desconocido';
}
