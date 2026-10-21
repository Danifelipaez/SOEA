import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { GeneracionFallidaComponent } from './generacion-fallida.component';
import { StateService } from '../../core/state.service';
import { Asignatura, Grupo, Programa, Sesion } from '../../core/models';

/**
 * Con un horario ya guardado, un fallo al generar solo salía en un snackbar con los grupos en un
 * párrafo separado por «;». El aviso persistente los lista y lleva al catálogo ya filtrado.
 */
describe('GeneracionFallidaComponent', () => {
  function montar(conHorarioGuardado: boolean) {
    TestBed.configureTestingModule({ imports: [GeneracionFallidaComponent], providers: [provideRouter([])] });
    const state = TestBed.inject(StateService);
    state.programas.set([{ id: 'p1', nombre: 'LIC QUIMICA' } as Programa]);
    state.asignaturas.set([{ id: 'a', programaId: 'p1', nombre: 'FUNDAMENTOS QUIMICA', codigo: 'a',
      sesionesTeoriaPresencialSemana: 1, horasTeoriaPresencial: 3, sesionesTeoriaVirtualSemana: 0, horasTeoriaVirtual: 0,
      sesionesLaboratorioSemana: 0, horasLaboratorio: 0 } as Asignatura]);
    state.grupos.set([{ id: 'g', asignaturaId: 'a', programaId: 'p1', nombre: 'FUNDAMENTOS QUIMICA - Grupo 1', estudiantesInscritos: 30,
      disponibilidadUiJson: JSON.stringify({ miercoles: { tipo: 'Franja específica', desde: '06:00', hasta: '08:00' } }) } as Grupo]);
    state.sesiones.set(conHorarioGuardado ? [{ id: 's' } as Sesion] : []);
    state.setMotivoInfactibilidad('FranjaGrupo');
    state.setGruposEnConflicto(['g']);
    state.errorGeneracion.set('texto largo');
    const f = TestBed.createComponent(GeneracionFallidaComponent);
    f.detectChanges();
    return f.nativeElement as HTMLElement;
  }

  it('lista cada grupo con su programa y enlaza al catálogo filtrado', () => {
    const el = montar(false);
    expect(el.querySelector('li')?.textContent).toContain('FUNDAMENTOS QUIMICA - Grupo 1 (LIC QUIMICA): necesita 3 h');
    expect(el.querySelector('a')?.getAttribute('href')).toBe('/catalogo?estado=incompletas');
    expect(el.textContent).not.toContain('guardado anterior');
  });

  it('con un horario guardado aclara que ese horario no cambió', () => {
    expect(montar(true).textContent).toContain('El horario guardado anterior no cambió.');
  });
});
