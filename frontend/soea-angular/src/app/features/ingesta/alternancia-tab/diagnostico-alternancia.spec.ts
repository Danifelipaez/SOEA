import { diagnosticarAlternancia } from './diagnostico-alternancia';
import { Asignatura, CriterioCesionAlternancia, Grupo } from '../../../core/models';

const asig = (id: string, o: Partial<Asignatura> = {}): Asignatura => ({
  id, codigo: id, nombre: id, alternancia: 'SinAlternancia', programaId: 'p', esCandidataAlternancia: true,
  sesionesTeoriaPresencialSemana: 1, horasTeoriaPresencial: 2, sesionesTeoriaVirtualSemana: 0, horasTeoriaVirtual: 2,
  sesionesLaboratorioSemana: 0, horasLaboratorio: 2, sesionesLaboratorioSemestre: 0, ...o,
});
const grupo = (id: string, asignaturaId: string, o: Partial<Grupo> = {}): Grupo =>
  ({ id, asignaturaId, nombre: id, estudiantesInscritos: 20, programaId: 'p', ...o });
const criterios = (activo = true): CriterioCesionAlternancia[] => [{ id: 'c', criterio: 'Elegible', orden: 1, activo }];

describe('diagnosticarAlternancia', () => {
  const asignaturas = [asig('a1'), asig('a2')];
  const grupos = [grupo('g1', 'a1'), grupo('g2', 'a2')];

  it('dos teorías de asignaturas y grupos distintos forman pareja con "Todas" y "Solo salones"', () => {
    expect(diagnosticarAlternancia(asignaturas, grupos, [], criterios(), 'Todos').hayPareja).toBe(true);
    expect(diagnosticarAlternancia(asignaturas, grupos, [], criterios(), 'Salon').hayPareja).toBe(true);
  });

  it('"Solo laboratorios" sin labs elegibles avisa que es imposible', () => {
    const d = diagnosticarAlternancia(asignaturas, grupos, [], criterios(), 'Laboratorio');
    expect(d.hayPareja).toBe(false);
    expect(d.motivo).toContain('laboratorio');
  });

  it('sin criterio activo, o con duraciones distintas, no hay pareja', () => {
    expect(diagnosticarAlternancia(asignaturas, grupos, [], criterios(false), 'Todos').hayPareja).toBe(false);
    const distintas = [asig('a1'), asig('a2', { horasTeoriaPresencial: 3 })];
    expect(diagnosticarAlternancia(distintas, grupos, [], criterios(), 'Todos').motivo).toContain('misma duración');
  });

  it('una teoría con requisito de laboratorio cuenta como laboratorio', () => {
    const conLab = grupos.map(g => ({ ...g, requisitosEspacio: [{ tipoSesion: 'TeoriaPresencial' as const, tipoEspacio: 'Laboratorio' as const, sesiones: 1 }] }));
    expect(diagnosticarAlternancia(asignaturas, conLab, [], criterios(), 'Laboratorio').hayPareja).toBe(true);
    expect(diagnosticarAlternancia(asignaturas, conLab, [], criterios(), 'Salon').hayPareja).toBe(false);
  });
});
