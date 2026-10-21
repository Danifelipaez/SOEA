import { TestBed } from '@angular/core/testing';
import { conteosFiltro, pasaFiltrosHorario, ClavesSesion } from './horario.reglas';
import { StateService } from '../../core/state.service';
import { Sesion } from '../../core/models';

describe('Filtro de horario por facetas', () => {
  const quimica: ClavesSesion = { facultad: 'f1', programa: 'p1', asignatura: 'a1', grupo: 'g1', docente: 'd1' };
  const biologia: ClavesSesion = { facultad: 'f1', programa: 'p2', asignatura: 'a2', grupo: 'g2', docente: 'd2' };

  it('dentro de una dimensión basta con una; entre dimensiones se exigen todas', () => {
    const dosGrupos = [{ dim: 'grupo' as const, id: 'g1' }, { dim: 'grupo' as const, id: 'g2' }];
    expect(pasaFiltrosHorario(quimica, dosGrupos)).toBe(true);
    expect(pasaFiltrosHorario(biologia, dosGrupos)).toBe(true);
    expect(pasaFiltrosHorario(biologia, [...dosGrupos, { dim: 'docente', id: 'd1' }])).toBe(false);
  });

  it('el conteo de una dimensión ignora sus propios chips y respeta los demás', () => {
    const conteos = conteosFiltro([quimica, biologia, quimica], [{ dim: 'programa', id: 'p1' }]);
    expect(conteos.programa.get('p2')).toBe(1);      // se puede sumar el otro programa
    expect(conteos.grupo.get('g1')).toBe(2);
    expect(conteos.grupo.has('g2')).toBe(false);     // vaciaría la cuadrícula: no se ofrece
  });
});

describe('Clases fijas para las pruebas', () => {
  const sesion = (o: Partial<Sesion>): Sesion => ({
    id: 'x', asignaturaId: 'a1', grupoId: 'g1', dia: 'lunes', horaInicio: '08:00', horaFin: '10:00',
    duracionHoras: 2, virtual: false, alternancia: 'SinAlternancia', tipoFlujo: 'AulaVirtual', ...o,
  });

  it('una clase fija reemplaza a la del punto de partida del mismo grupo y tipo, no se suma', () => {
    localStorage.clear();
    const state = TestBed.inject(StateService);
    state.horariosBases.set([{ id: 'b', nombre: 'Base', creadoEn: '', sesiones: [sesion({ id: 'base1' }), sesion({ id: 'base2', grupoId: 'g2' })] }]);
    state.seleccionarBase('b');
    state.fijarClase(sesion({ id: 'pin', dia: 'martes' }));

    expect(state.sesionesFijasParaGenerar().map(s => s.id).sort()).toEqual(['base2', 'pin']);
    state.alternarClaseFija('pin');
    expect(state.sesionesFijasParaGenerar().map(s => s.id).sort()).toEqual(['base1', 'base2']);
  });
});
