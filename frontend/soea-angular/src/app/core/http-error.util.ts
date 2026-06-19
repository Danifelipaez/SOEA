/**
 * Extrae un mensaje legible de un error HTTP del backend.
 * Soporta cuerpos string (BadRequest/NotFound con mensaje plano) y
 * application/problem+json (ProblemDetails con detail/title).
 */
export function mensajeErrorHttp(err: any): string {
  const cuerpo = err?.error;
  if (typeof cuerpo === 'string' && cuerpo.trim()) return cuerpo;
  return cuerpo?.detail ?? cuerpo?.title ?? err?.message ?? 'Error desconocido';
}
