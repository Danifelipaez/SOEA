import { TestBed } from '@angular/core/testing';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { GrupoDialogComponent, GrupoDialogData } from './grupo-tab.component';
import { StateService } from '../../../core/state.service';
import { Asignatura, Grupo } from '../../../core/models';

/**
 * asignaturaFija asumía que grupo.asignaturaId siempre resuelve a una Asignatura real del
 * catálogo. Para un grupo huérfano (asignaturaId apunta a una asignatura ya eliminada) eso es
 * falso: asignaturaCtx() da undefined y el diálogo mostraba "Grupo de undefined" sin ningún
 * select para reasignarlo. El fix cae al modo cascada completo cuando el id no resuelve.
 */
function asignatura(overrides: Partial<Asignatura> = {}): Asignatura {
  return {
    id: 'a1', codigo: 'COD1', nombre: 'Cálculo I', alternancia: 'SinAlternancia',
    sesionesTeoriaPresencialSemana: 2, horasTeoriaPresencial: 2,
    sesionesTeoriaVirtualSemana: 0, horasTeoriaVirtual: 2,
    sesionesLaboratorioSemana: 0, horasLaboratorio: 2, sesionesLaboratorioSemestre: 0,
    programaId: 'p1',
    ...overrides
  };
}

function grupo(overrides: Partial<Grupo> = {}): Grupo {
  return { id: 'g1', asignaturaId: 'a1', nombre: 'G1', estudiantesInscritos: 30, programaId: 'p1', ...overrides };
}

function crear(data: GrupoDialogData) {
  TestBed.configureTestingModule({
    imports: [GrupoDialogComponent],
    providers: [
      provideNoopAnimations(),
      { provide: MatDialogRef, useValue: { close: () => {} } },
      { provide: MAT_DIALOG_DATA, useValue: data },
    ],
  });
  const state = TestBed.inject(StateService);
  state.setAsignaturas([asignatura()]);
  const fixture = TestBed.createComponent(GrupoDialogComponent);
  return fixture.componentInstance;
}

describe('GrupoDialogComponent — asignaturaFija con grupo huérfano', () => {
  it('grupo huérfano (asignaturaId no resuelve): cae al modo cascada completo', () => {
    const component = crear({ grupo: grupo({ asignaturaId: 'a-borrada' }) });

    expect(component.asignaturaFija).toBe('');
    expect(component.tiposRequisito()).toEqual([]);
  });

  it('grupo normal (asignaturaId resuelve): mantiene el modo contextual (regresión)', () => {
    const component = crear({ grupo: grupo({ asignaturaId: 'a1' }) });

    expect(component.asignaturaFija).toBe('a1');
    expect(component.asignaturaCtx()?.nombre).toBe('Cálculo I');
  });

  it('creación desde la fila de una asignatura (sin grupo): usa data.asignaturaId (regresión)', () => {
    const component = crear({ asignaturaId: 'a1', programaId: 'p1' });

    expect(component.asignaturaFija).toBe('a1');
  });
});
