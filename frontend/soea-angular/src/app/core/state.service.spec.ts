import { StateService } from './state.service';

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
