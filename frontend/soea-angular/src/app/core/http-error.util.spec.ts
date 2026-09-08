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
});
