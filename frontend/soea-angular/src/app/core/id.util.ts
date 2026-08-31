/**
 * Genera un id único para entidades nuevas del lado del cliente.
 *
 * G5 (bug reportado "no deja guardar"): `crypto.randomUUID()` solo existe en contextos seguros
 * (HTTPS o localhost) — en un contexto inseguro (p. ej. http:// sobre una IP de LAN, un escenario
 * real de prueba en esta app) lanza `TypeError`, y el botón de guardar se ve como si no hiciera
 * nada. `crypto.getRandomValues` sí está disponible en más contextos; con eso se arma un UUID v4
 * a mano como fallback.
 */
export function nuevoId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    try { return crypto.randomUUID(); } catch { /* contexto inseguro: cae al fallback */ }
  }
  const bytes = new Uint8Array(16);
  if (typeof crypto !== 'undefined' && typeof crypto.getRandomValues === 'function') {
    crypto.getRandomValues(bytes);
  } else {
    for (let i = 0; i < bytes.length; i++) bytes[i] = Math.floor(Math.random() * 256);
  }
  bytes[6] = (bytes[6] & 0x0f) | 0x40; // versión 4
  bytes[8] = (bytes[8] & 0x3f) | 0x80; // variante RFC 4122
  const hex = Array.from(bytes, b => b.toString(16).padStart(2, '0'));
  return `${hex.slice(0, 4).join('')}-${hex.slice(4, 6).join('')}-${hex.slice(6, 8).join('')}-` +
         `${hex.slice(8, 10).join('')}-${hex.slice(10, 16).join('')}`;
}
