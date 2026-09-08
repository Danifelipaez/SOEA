import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { AsignaturaDialogComponent } from './asignaturas-tab.component';

/**
 * El FormGroup de AsignaturaDialogComponent no tenía ningún validador cruzado entre
 * horaInicioMin/horaFinMax (HC-VH) — una ventana horaria invertida (ej. 14:00–08:00) se
 * guardaba sin ningún aviso en la UI; el único indicio llegaba después, como una infactibilidad
 * genérica de CP-SAT al generar el horario.
 */
describe('AsignaturaDialogComponent — ventana horaria invertida', () => {
  let component: AsignaturaDialogComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AsignaturaDialogComponent],
      providers: [
        provideNoopAnimations(),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: undefined },
      ],
    });
    const fixture = TestBed.createComponent(AsignaturaDialogComponent);
    component = fixture.componentInstance;
    component.form.patchValue({ facultadId: 'f1', programaId: 'p1', codigo: 'C1', nombre: 'N1' });
  });

  it('horaInicioMin posterior a horaFinMax deja el formulario inválido y bloquea Guardar', () => {
    component.form.patchValue({ horaInicioMin: '14:00', horaFinMax: '08:00' });

    expect(component.form.valid).toBe(false);
    expect(component.canSave()).toBe(false);
  });

  it('una ventana horaria correcta (inicio antes que fin) sigue siendo válida', () => {
    component.form.patchValue({ horaInicioMin: '08:00', horaFinMax: '14:00' });

    expect(component.form.valid).toBe(true);
    expect(component.canSave()).toBe(true);
  });

  it('sin ventana horaria declarada (ambos vacíos) sigue siendo válido', () => {
    component.form.patchValue({ horaInicioMin: '', horaFinMax: '' });

    expect(component.form.valid).toBe(true);
    expect(component.canSave()).toBe(true);
  });
});
