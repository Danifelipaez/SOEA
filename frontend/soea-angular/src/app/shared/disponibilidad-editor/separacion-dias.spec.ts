import { errorSeparacionDias } from './disponibilidad-editor.component';

/** HC-SEP en la captura: n sesiones del mismo tipo exigen n días disponibles con uno libre de por medio. */
function disp(...disponibles: string[]) {
  const out: Record<string, any> = {};
  ['lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado']
    .forEach(d => out[d] = disponibles.includes(d) ? { noDisponible: false, tipo: 'Franja general' } : { noDisponible: true });
  return out;
}

describe('errorSeparacionDias', () => {
  it('1 sesión o sin disponibilidad declarada: sin requisito', () => {
    expect(errorSeparacionDias(disp('lunes'), 1)).toBeNull();
    expect(errorSeparacionDias({}, 2)).toBeNull();
  });

  it('2 sesiones con un solo día disponible: error', () => {
    expect(errorSeparacionDias(disp('lunes'), 2)).toContain('al menos 2 días');
  });

  it('2 sesiones en días consecutivos no basta; con un día de por medio sí', () => {
    expect(errorSeparacionDias(disp('lunes', 'martes'), 2)).not.toBeNull();
    expect(errorSeparacionDias(disp('lunes', 'miercoles'), 2)).toBeNull();
  });

  it('día sin entrada cuenta como disponible, igual que el backend (import de Excel)', () => {
    expect(errorSeparacionDias({ lunes: { noDisponible: false } }, 3)).toBeNull();
  });

  it('3 sesiones: lun-mar-mié no alcanza, lun-mié-vie sí; 4 nunca', () => {
    expect(errorSeparacionDias(disp('lunes', 'martes', 'miercoles'), 3)).not.toBeNull();
    expect(errorSeparacionDias(disp('lunes', 'miercoles', 'viernes'), 3)).toBeNull();
    expect(errorSeparacionDias(disp('lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado'), 4)).toContain('como máximo 3');
  });
});
