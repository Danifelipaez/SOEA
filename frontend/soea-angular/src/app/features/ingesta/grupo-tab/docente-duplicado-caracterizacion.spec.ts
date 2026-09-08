import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { GrupoDialogComponent } from './grupo-tab.component';
import { StateService } from '../../../core/state.service';
import { Docente } from '../../../core/models';

/**
 * Test de CARACTERIZACIÓN, no de bug confirmado: GrupoDialogComponent no valida ni advierte si
 * el docente seleccionado ya dicta otro grupo de la MISMA asignatura — docenteOptions() lista
 * todos los docentes sin filtrar ni marcar ninguno. Puede ser intencional (un docente sí puede
 * dictar dos secciones de la misma materia) — este test documenta el comportamiento actual para
 * que una futura decisión de producto no lo descubra por accidente.
 */
function docente(overrides: Partial<Docente> = {}): Docente {
  return { id: 'd1', nombre: 'Doc 1', cedula: '1', maxHoras: 20, disponibilidad: {}, ...overrides };
}

describe('GrupoDialogComponent — docente duplicado entre grupos de la misma asignatura', () => {
  it('docenteOptions no excluye ni marca al docente ya asignado a otro grupo de la asignatura', () => {
    TestBed.configureTestingModule({
      imports: [GrupoDialogComponent],
      providers: [
        provideNoopAnimations(),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: { asignaturaId: 'a1' } },
      ],
    });
    const fixture = TestBed.createComponent(GrupoDialogComponent);
    const component = fixture.componentInstance;
    const state = TestBed.inject(StateService);
    state.docentes.set([docente({ id: 'd1', nombre: 'Doc 1' })]);
    // Grupo existente de la MISMA asignatura ya con d1 asignado.
    state.grupos.set([{ id: 'g1', asignaturaId: 'a1', nombre: 'G1', estudiantesInscritos: 20, programaId: 'p1', docenteId: 'd1' }]);

    const opciones = component.docenteOptions();

    expect(opciones.some(o => o.value === 'd1')).toBe(true);
    // Comportamiento actual: ninguna opción trae una marca/aviso de "ya asignado en esta materia".
    expect(opciones.find(o => o.value === 'd1')?.label).toBe('Doc 1');
  });
});
