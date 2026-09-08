import { mensajeInfactibilidadAmigable } from './horario.component';
import { Grupo } from '../../core/models';

/**
 * El snackbar de infactibilidad reemplazaba CUALQUIER mensaje del backend por una frase
 * genérica ("No se encontró un horario factible para N asignatura(s) en M espacio(s)"), o —
 * si no matcheaba esa regex — mostraba el texto crudo del backend con jerga interna (HC-SEP,
 * HC-ALT, "Motivo: FranjaGrupo") que un coordinador académico no técnico no puede accionar.
 * mensajeInfactibilidadAmigable() traduce motivoInfactibilidad (siempre real, del backend) a
 * texto en español simple y, cuando gruposEnConflicto trae Ids, nombra los grupos reales en
 * vez de dar un conteo vacío — nunca inventa una causa que el backend no reportó.
 */
function grupo(overrides: Partial<Grupo> = {}): Grupo {
  return { id: 'g1', asignaturaId: 'a1', nombre: 'G1', estudiantesInscritos: 30, programaId: 'p1', ...overrides };
}

describe('mensajeInfactibilidadAmigable', () => {
  const grupos = [grupo({ id: 'g1', nombre: 'G1' }), grupo({ id: 'g2', nombre: 'G2' })];

  it('nombra los grupos reales cuando el backend trae gruposEnConflicto (sin jerga)', () => {
    const msg = mensajeInfactibilidadAmigable('Otro', ['g1', 'g2'], grupos, 5, 2);
    expect(msg).toContain('G1');
    expect(msg).toContain('G2');
    expect(msg).not.toMatch(/HC-SEP|HC-ALT|Infeasible/i);
  });

  it('Motivo Espacio da una guía accionable sin jerga interna', () => {
    const msg = mensajeInfactibilidadAmigable('Espacio', [], grupos, 5, 2);
    expect(msg).toMatch(/espacio/i);
    expect(msg).not.toMatch(/HC-|Infeasible/i);
  });

  it('Motivo FranjaGrupo menciona disponibilidad de grupo, no el código interno', () => {
    const msg = mensajeInfactibilidadAmigable('FranjaGrupo', [], grupos, 5, 2);
    expect(msg).toMatch(/disponibilidad/i);
    expect(msg).not.toMatch(/FranjaGrupo|HC-/i);
  });

  it('Motivo VentanaHoraria menciona la ventana horaria de la asignatura', () => {
    const msg = mensajeInfactibilidadAmigable('VentanaHoraria', [], grupos, 5, 2);
    expect(msg).toMatch(/ventana horaria/i);
  });

  it('Motivo Timeout invita a reintentar en vez de sonar como un error de datos', () => {
    const msg = mensajeInfactibilidadAmigable('Timeout', [], grupos, 5, 2);
    expect(msg).toMatch(/intente|de nuevo/i);
  });

  it('sin motivo reconocido y sin grupos, cae a un mensaje genérico pero sin jerga', () => {
    const msg = mensajeInfactibilidadAmigable(undefined, [], grupos, 5, 2);
    expect(msg.length).toBeGreaterThan(0);
    expect(msg).not.toMatch(/HC-|Infeasible|undefined/i);
  });

  it('un Id de gruposEnConflicto que ya no existe en el catálogo no rompe el mensaje', () => {
    const msg = mensajeInfactibilidadAmigable('Otro', ['id-borrado'], grupos, 5, 2);
    expect(msg.length).toBeGreaterThan(0);
    expect(msg).not.toContain('undefined');
  });
});
