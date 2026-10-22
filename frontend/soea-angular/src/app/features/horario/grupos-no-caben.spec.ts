import { gruposQueNoCaben, mensajeInfactibilidadAmigable } from './horario.reglas';
import { Asignatura, Grupo } from '../../core/models';

/**
 * Generar fallaba con "Uno o más grupos tienen más sesiones de las que caben…" sin decir cuáles,
 * aunque el backend sí los conoce; y el filtro "Con datos incompletos" del catálogo no los marcaba.
 * gruposQueNoCaben() es el espejo de los pre-checks HC-G01 del motor (motivo FranjaGrupo).
 */
const disp = (dia: string, desde: string, hasta: string) =>
  JSON.stringify({ [dia]: { noDisponible: false, tipo: 'Franja específica', desde, hasta } });

function asignatura(id: string, programaId: string, horas: number, sesiones = 1): Asignatura {
  return {
    id, programaId, nombre: 'QUIMICA INORGANICA', codigo: id,
    sesionesTeoriaPresencialSemana: sesiones, horasTeoriaPresencial: horas,
    sesionesTeoriaVirtualSemana: 0, horasTeoriaVirtual: 2, sesionesLaboratorioSemana: 0, horasLaboratorio: 2,
  } as Asignatura;
}
const grupo = (id: string, asignaturaId: string, programaId: string, json?: string): Grupo =>
  ({ id, asignaturaId, programaId, nombre: 'QUIMICA INORGANICA - Grupo 1', estudiantesInscritos: 30, disponibilidadUiJson: json });

describe('gruposQueNoCaben', () => {
  const asigs = new Map([['a3', asignatura('a3', 'p1', 3)], ['a2', asignatura('a2', 'p2', 2)], ['a2x2', asignatura('a2x2', 'p2', 2, 2)]]);

  it('marca la clase de 3 h en una ventana de 2 h (caso real del import)', () => {
    const m = gruposQueNoCaben([grupo('g', 'a3', 'p1', disp('martes', '16:00', '18:00'))], asigs);
    expect(m.get('g')).toContain('3 h');
  });

  it('no marca una clase que cabe exacta', () => {
    expect(gruposQueNoCaben([grupo('g', 'a2', 'p2', disp('martes', '16:00', '18:00'))], asigs).size).toBe(0);
  });

  it('suma OK pero ningún día tiene las horas seguidas', () => {
    const json = JSON.stringify({ lunes: { tipo: 'Franja específica', desde: '07:00', hasta: '08:00' }, martes: { tipo: 'Franja específica', desde: '07:00', hasta: '08:00' } });
    expect(gruposQueNoCaben([grupo('g', 'a2', 'p2', json)], asigs).get('g')).toContain('seguidas');
  });

  it('dos sesiones de 2 h en un único día de 2 h no caben (suma)', () => {
    expect(gruposQueNoCaben([grupo('g', 'a2x2', 'p2', disp('martes', '16:00', '18:00'))], asigs).get('g')).toContain('4 h');
  });

  it('sin disponibilidad declarada no restringe', () => {
    expect(gruposQueNoCaben([grupo('g', 'a3', 'p1')], asigs).size).toBe(0);
  });
});

describe('mensajeInfactibilidadAmigable · FranjaGrupo', () => {
  it('nombra cada grupo con su programa, aunque se llamen igual, y por qué no cabe', () => {
    const asigs = [asignatura('a3', 'p1', 3), asignatura('a2', 'p2', 2)];
    const grupos = [grupo('g1', 'a3', 'p1', disp('martes', '16:00', '18:00')), grupo('g2', 'a2', 'p2', disp('jueves', '12:00', '14:00'))];
    const programaById = new Map([['p1', { nombre: 'LIC QUIMICA' }], ['p2', { nombre: 'BIOLOGÍA' }]]);
    const msg = mensajeInfactibilidadAmigable('FranjaGrupo', ['g1'], grupos, 2, 2, { espacios: [], asignaturas: asigs, programaById });
    expect(msg).toContain('QUIMICA INORGANICA - Grupo 1 (LIC QUIMICA)');
    expect(msg).not.toContain('BIOLOGÍA');
    expect(msg).toContain('3 h');
    expect(msg).not.toMatch(/FranjaGrupo|HC-|bloque/i);
  });
});
