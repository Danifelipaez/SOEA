import { clasesPendientes } from './horario.reglas';
import { Asignatura, Grupo, Sesion } from '../../core/models';

/** «Pendientes de ubicar» = lo que el catálogo pide para cada grupo menos lo que está en el horario. */
describe('clasesPendientes', () => {
  const a = { id: 'a', nombre: 'Química', sesionesTeoriaPresencialSemana: 2, horasTeoriaPresencial: 2,
    sesionesTeoriaVirtualSemana: 0, horasTeoriaVirtual: 0, sesionesLaboratorioSemana: 1, horasLaboratorio: 3 } as Asignatura;
  const grupos = [{ id: 'g', asignaturaId: 'a', nombre: 'G1' } as Grupo];
  const ses = (id: string, extra: Partial<Sesion>) => ({ id, grupoId: 'g', asignaturaId: 'a', virtual: false, ...extra }) as Sesion;

  it('cuenta por grupo y tipo, sin la contraparte virtual de una clase que alterna', () => {
    const sesiones = [
      ses('t1', { tipoFlujo: 'AulaVirtual' }),
      ses('t1', { tipoFlujo: 'AulaVirtual', virtual: true, esContraparteVirtual: true }),
    ];
    expect(clasesPendientes(sesiones, grupos, new Map([['a', a]]))).toEqual([
      { grupoId: 'g', asignaturaId: 'a', tipo: 'TeoriaPresencial', horas: 2, faltan: 1 },
      { grupoId: 'g', asignaturaId: 'a', tipo: 'Laboratorio', horas: 3, faltan: 1 },
    ]);
  });

  it('un grupo completo (o con una clase manual de más) no tiene pendientes', () => {
    const sesiones = [ses('t1', { tipoFlujo: 'AulaVirtual' }), ses('t2', { tipoFlujo: 'AulaVirtual' }),
      ses('t3', { tipoFlujo: 'AulaVirtual' }), ses('l1', { tipoFlujo: 'Laboratorio' })];
    expect(clasesPendientes(sesiones, grupos, new Map([['a', a]]))).toEqual([]);
  });
});
