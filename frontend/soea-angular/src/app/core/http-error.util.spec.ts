import { mensajeErrorHttp } from './http-error.util';

/**
 * mensajeErrorHttp es el helper central para extraer un mensaje legible de un HttpErrorResponse.
 * Cubre los 3 formatos reales del backend (string plano, ProblemDetails, envelope {error}) y los
 * casos límite que antes producían "[object Object]" o "undefined" en un snackbar visible al
 * coordinador académico (ver asignaturas-tab.component.ts onFileSelected, horario-api.service.ts
 * manejarError).
 */
describe('mensajeErrorHttp', () => {
  it('cuerpo string plano (BadRequest/NotFound con mensaje directo)', () => {
    expect(mensajeErrorHttp({ error: 'Asignatura no encontrada.' })).toBe('Asignatura no encontrada.');
  });

  it('ProblemDetails con detail', () => {
    expect(mensajeErrorHttp({ error: { detail: 'La ventana horaria es inválida.' } }))
      .toBe('La ventana horaria es inválida.');
  });

  it('ProblemDetails con solo title (sin detail) — no debe producir [object Object]', () => {
    const msg = mensajeErrorHttp({ error: { title: 'Formato de columna inválido' } });
    expect(msg).toBe('Formato de columna inválido');
    expect(msg).not.toContain('[object Object]');
  });

  it('envelope { error: "..." } (SesionesController/HorarioController)', () => {
    expect(mensajeErrorHttp({ error: { error: 'Conflicto de horario con otra sesión.' } }))
      .toBe('Conflicto de horario con otra sesión.');
  });

  it('cuerpo null cae a err.message', () => {
    expect(mensajeErrorHttp({ error: null, message: 'Http failure response' })).toBe('Http failure response');
  });

  it('cuerpo undefined y sin message cae al genérico, nunca a "undefined"', () => {
    const msg = mensajeErrorHttp({});
    expect(msg).toBe('Error desconocido');
    expect(msg).not.toContain('undefined');
  });

  it('objeto sin ninguno de los campos esperados cae al genérico, no a [object Object]', () => {
    const msg = mensajeErrorHttp({ error: { status: 500, foo: 'bar' } });
    expect(msg).toBe('Error desconocido');
    expect(msg).not.toContain('[object Object]');
  });

  it('cuerpo string vacío no se usa tal cual — cae a los demás campos', () => {
    const msg = mensajeErrorHttp({ error: '   ', message: 'fallback' });
    expect(msg).toBe('fallback');
  });

  it('quita la etiqueta de regla [HC-xx] que el backend deja al final del mensaje ya traducido', () => {
    const msg = mensajeErrorHttp({ error: 'El aula ya está ocupada. [HC-S01]' });
    expect(msg).toBe('El aula ya está ocupada.');
  });

  it('quita el prefijo DATOS: de un mensaje de error interno de datos y capitaliza la frase', () => {
    const msg = mensajeErrorHttp({ error: 'DATOS: la sesión quedó en un aula que ya no existe.' });
    expect(msg).toBe('La sesión quedó en un aula que ya no existe.');
  });

  it('quita el código de regla AL INICIO (convención real de ValidadorRestriccionesDuras) y capitaliza', () => {
    const msg = mensajeErrorHttp({ error: "HC-C01: solape en 'G1' — Sesión 1: X; Sesión 2: Y." });
    expect(msg).not.toMatch(/^HC-/);
    expect(msg).toBe("Solape en 'G1' — Sesión 1: X; Sesión 2: Y.");
  });

  it('un ProblemDetails genérico en inglés (400) se traduce, no se muestra tal cual', () => {
    const msg = mensajeErrorHttp({ status: 400, error: { title: 'One or more validation errors occurred.' } });
    expect(msg).not.toMatch(/validation errors/i);
    expect(msg).toMatch(/formulario/i);
  });

  it('un fallo de red real (status 0) se traduce en vez de mostrar "Http failure response…"', () => {
    const msg = mensajeErrorHttp({ status: 0, error: null, message: 'Http failure response for http://x: 0 Unknown Error' });
    expect(msg).not.toMatch(/http failure/i);
    expect(msg).toMatch(/conexión/i);
  });

  it('un 404 "Not Found" sin cuerpo se traduce a "recargue la página"', () => {
    const msg = mensajeErrorHttp({ status: 404, error: 'Not Found' });
    expect(msg).toMatch(/recargue/i);
  });

  it('un mensaje de negocio real no se toca aunque el status tenga mapeo (no todo 404 es "recargue")', () => {
    const msg = mensajeErrorHttp({ status: 404, error: 'El docente elegido ya no existe.' });
    expect(msg).toBe('El docente elegido ya no existe.');
  });
});
