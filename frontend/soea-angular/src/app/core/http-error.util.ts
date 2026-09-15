// Convención real del backend (ValidadorRestriccionesDuras y la mayoría de servicios): el código
// de regla va al INICIO ("HC-C01: solape en…", "DATOS: la sesión…") — los tests de esos archivos
// exigen StartsWith("HC-…"), así que la limpieza se hace aquí, no reformateando el backend.
// Un puñado de mensajes más nuevos lo dejan como sufijo entre corchetes al final; se cubren ambos.
const PREFIJO_REGLA = /^\s*(?:HC-[A-Z0-9]+|DATOS):\s*/i;
const SUFIJO_REGLA = /\s*\[(?:HC-[A-Z0-9]+|DATOS)\]\s*$/i;

/** Quita el código de regla técnico (soporte, no para la coordinadora) de uno u otro extremo del
 *  mensaje. Si se quitó un prefijo, la frase queda en minúscula ("solape en…") — se capitaliza.
 *  Exportada: horario.component.ts la reusa sobre `err.mensajeError` de un 422 (GenerarHorarioResponse),
 *  que llega tal cual del backend sin pasar por mensajeErrorHttp/manejarError. */
export function limpiarEtiquetaInterna(msg: string): string {
  const sinPrefijo = msg.replace(PREFIJO_REGLA, '');
  const limpio = sinPrefijo.replace(SUFIJO_REGLA, '').trim();
  if (!limpio) return '';
  return sinPrefijo !== msg ? limpio.charAt(0).toUpperCase() + limpio.slice(1) : limpio;
}

/** Frases por defecto de ASP.NET/Angular que llegan en inglés cuando no hay un mensaje de
 *  negocio (ProblemDetails genérico, fallo de red) — no se muestran tal cual a la coordinadora. */
const FRASES_GENERICAS = [
  /^an error occurred/i, /^one or more validation errors/i, /^not found$/i,
  /^http failure response/i, /^bad request$/i, /^unauthorized$/i, /^internal server error$/i,
];

function mensajePorEstado(status: number | undefined): string | undefined {
  switch (status) {
    case 0: return 'No se pudo conectar con el sistema. Revise su conexión e intente de nuevo.';
    case 400: return 'El servidor no aceptó los datos enviados. Revise el formulario.';
    case 404: return 'Este dato ya no existe. Recargue la página.';
    case 409: return 'No se pudo guardar: hay un cruce de horario.';
    case 500: return 'Ocurrió un error inesperado en el servidor. Intente de nuevo.';
    default: return undefined;
  }
}

/**
 * Extrae un mensaje legible de un error HTTP del backend.
 * Soporta cuerpos string (BadRequest/NotFound con mensaje plano), application/problem+json
 * (ProblemDetails con detail/title) y el envelope { error: "..." } usado por SesionesController
 * y HorarioController (404/409/400/500 de asignar-docente y sesión-manual).
 *
 * Quita las etiquetas de regla que el backend deja al final ("[HC-C01]", "DATOS:") — son para
 * soporte técnico, no para la coordinadora — y cuando el mensaje es un texto genérico en inglés
 * (ProblemDetails por defecto de ASP.NET, o el error de red crudo de Angular) lo reemplaza por una
 * frase en español según el código HTTP, en vez de mostrarlo tal cual.
 */
export function mensajeErrorHttp(err: any): string {
  const cuerpo = err?.error;
  let msg: string = (typeof cuerpo === 'string' && cuerpo.trim()) ? cuerpo
    : cuerpo?.error ?? cuerpo?.detail ?? cuerpo?.title ?? err?.message ?? 'Error desconocido';
  msg = limpiarEtiquetaInterna(msg) || 'Error desconocido';
  if (FRASES_GENERICAS.some(r => r.test(msg))) return mensajePorEstado(err?.status) ?? msg;
  return msg;
}
