import { StateService } from './state.service';
import { Asignatura, Grupo, Sesion } from './models';

/**
 * gruposEnConflicto: el backend (GenerarHorarioResponse.GruposEnConflicto) solo llega a existir
 * cuando la generación de horario falla — antes de esto el StateService no tenía dónde guardar
 * esos Ids para que /catalogo pudiera resaltar los grupos responsables.
 */
describe('StateService — gruposEnConflicto', () => {
  it('empieza vacío', () => {
    const state = new StateService();
    expect(state.gruposEnConflicto()).toEqual([]);
  });

  it('setGruposEnConflicto reemplaza la lista', () => {
    const state = new StateService();
    state.setGruposEnConflicto(['g1', 'g2']);
    expect(state.gruposEnConflicto()).toEqual(['g1', 'g2']);
  });

  it('setGruposEnConflicto([]) limpia un conflicto previo (nueva generación exitosa)', () => {
    const state = new StateService();
    state.setGruposEnConflicto(['g1']);
    state.setGruposEnConflicto([]);
    expect(state.gruposEnConflicto()).toEqual([]);
  });

  it('gruposEnConflictoSet expone un Set para lookup O(1) por id', () => {
    const state = new StateService();
    state.setGruposEnConflicto(['g1', 'g2']);
    expect(state.gruposEnConflictoSet().has('g1')).toBe(true);
    expect(state.gruposEnConflictoSet().has('g3')).toBe(false);
  });

  it('el grupo deja de estar marcado al editarlo (o su asignatura) después del fallo', () => {
    const state = new StateService();
    state.asignaturas.set([{ id: 'a', nombre: 'Química', horasTeoriaPresencial: 3 } as Asignatura]);
    state.grupos.set([{ id: 'g1', asignaturaId: 'a', disponibilidadUiJson: '{"miercoles":{"desde":"06:00","hasta":"08:00"}}' } as Grupo,
      { id: 'g2', asignaturaId: 'b' } as Grupo]);
    state.setGruposEnConflicto(['g1', 'g2']);

    // Renombrar no corrige nada: sigue marcado.
    state.grupos.update(gs => gs.map(g => ({ ...g, nombre: 'otro' })));
    expect(state.gruposEnConflictoSet().size).toBe(2);

    state.grupos.update(gs => gs.map(g => g.id === 'g1' ? { ...g, disponibilidadUiJson: '{"miercoles":{"desde":"06:00","hasta":"09:00"}}' } : g));
    expect([...state.gruposEnConflictoSet()]).toEqual(['g2']);
    expect(state.gruposEnConflicto()).toEqual(['g1', 'g2']); // el aviso del intento fallido no cambia

    state.setGruposEnConflicto(['g1']);
    state.asignaturas.update(as => as.map(a => ({ ...a, horasTeoriaPresencial: 2 })));
    expect(state.gruposEnConflictoSet().size).toBe(0);
  });
});

/**
 * gruposHuerfanos: un Grupo cuyo asignaturaId no resuelve a ninguna Asignatura del catálogo
 * (por ejemplo, porque la asignatura fue eliminada mientras el grupo seguía existiendo). Antes
 * de esto eran invisibles en /catalogo — la tabla se arma vía getGruposByAsignatura, que nunca
 * los lista bajo ninguna fila. Derivado 100% en el cliente: GET /api/grupos ya trae todos los
 * grupos, incluidos los huérfanos, sin filtrar.
 */
describe('StateService — gruposHuerfanos', () => {
  it('vacío sin grupos', () => {
    const state = new StateService();
    expect(state.gruposHuerfanos()).toEqual([]);
  });

  it('vacío cuando todos los grupos resuelven a una asignatura existente', () => {
    const state = new StateService();
    state.setAsignaturas([{
      id: 'a1', codigo: 'C1', nombre: 'Cálculo I', alternancia: 'SinAlternancia',
      sesionesTeoriaPresencialSemana: 1, horasTeoriaPresencial: 2,
      sesionesTeoriaVirtualSemana: 0, horasTeoriaVirtual: 2,
      sesionesLaboratorioSemana: 0, horasLaboratorio: 2, sesionesLaboratorioSemestre: 0,
      programaId: 'p1',
    }]);
    state.grupos.set([{ id: 'g1', asignaturaId: 'a1', nombre: 'G1', estudiantesInscritos: 30, programaId: 'p1' }]);
    expect(state.gruposHuerfanos()).toEqual([]);
  });

  it('excluye un grupo con asignaturaId vacío (no asignado a propósito, no huérfano)', () => {
    const state = new StateService();
    state.grupos.set([{ id: 'g1', asignaturaId: '', nombre: 'G1', estudiantesInscritos: 30, programaId: 'p1' }]);
    expect(state.gruposHuerfanos()).toEqual([]);
  });

  it('incluye un grupo cuyo asignaturaId no está en asignaturas()', () => {
    const state = new StateService();
    const huerfano = { id: 'g1', asignaturaId: 'a-borrada', nombre: 'G1', estudiantesInscritos: 30, programaId: 'p1' };
    state.grupos.set([huerfano]);
    expect(state.gruposHuerfanos()).toEqual([huerfano]);
  });
});

/**
 * fijasSobre: lo que se manda como fijo al probar sobre un horario. Una clase fija reemplaza a la de
 * su misma franja o, con el grupo completo, a otra del mismo tipo; si al horario le falta esa clase
 * (una pendiente), se suma sin quitar ninguna.
 */
describe('StateService — fijasSobre', () => {
  const ses = (id: string, horaInicio: string): Sesion => ({
    id, asignaturaId: 'a', grupoId: 'g', dia: 'lunes', horaInicio, horaFin: '', duracionHoras: 2,
    virtual: false, alternancia: 'SinAlternancia', tipoFlujo: 'AulaVirtual',
  });
  const nuevo = (sesionesSemana: number) => {
    const state = new StateService();
    state.asignaturas.set([{ id: 'a', nombre: 'A', sesionesTeoriaPresencialSemana: sesionesSemana } as Asignatura]);
    return state;
  };

  it('con el grupo completo, la fija reemplaza a una clase del mismo tipo', () => {
    const state = nuevo(2);
    state.clasesFijas.set([{ sesion: ses('f', '14:00'), activa: true }]);
    expect(state.fijasSobre([ses('x', '08:00'), ses('y', '10:00')]).map(s => s.id)).toEqual(['y', 'f']);
  });

  it('prefiere la de su misma franja', () => {
    const state = nuevo(2);
    state.clasesFijas.set([{ sesion: ses('f', '10:00'), activa: true }]);
    expect(state.fijasSobre([ses('x', '08:00'), ses('y', '10:00')]).map(s => s.id)).toEqual(['x', 'f']);
  });

  it('una clase movida reemplaza a la que estaba en su franja de origen, no a otra del grupo', () => {
    const state = nuevo(2);
    state.clasesFijas.set([{ sesion: ses('f', '14:00'), activa: true, origen: { dia: 'lunes', horaInicio: '10:00' } }]);
    expect(state.fijasSobre([ses('x', '08:00'), ses('y', '10:00')]).map(s => s.id)).toEqual(['x', 'f']);
  });

  it('dos fijas del mismo grupo no se reemplazan entre sí ni dejan una clase de más', () => {
    const state = nuevo(2);
    state.clasesFijas.set([
      { sesion: ses('f1', '10:00'), activa: true, origen: { dia: 'lunes', horaInicio: '08:00' } },
      { sesion: ses('f2', '16:00'), activa: true, origen: { dia: 'lunes', horaInicio: '10:00' } },
    ]);
    expect(state.fijasSobre([ses('x', '08:00'), ses('y', '10:00')]).map(s => s.id)).toEqual(['f1', 'f2']);
  });

  it('a un horario con una pendiente le suma la fija sin quitar nada; las apagadas no cuentan', () => {
    const state = nuevo(2);
    state.clasesFijas.set([{ sesion: ses('f', '14:00'), activa: true }, { sesion: ses('off', '16:00'), activa: false }]);
    expect(state.fijasSobre([ses('x', '08:00')]).map(s => s.id)).toEqual(['x', 'f']);
  });
});
