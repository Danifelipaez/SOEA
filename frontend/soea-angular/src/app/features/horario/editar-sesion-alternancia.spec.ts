import { vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { EditarSesionDialogComponent } from './horario.component';
import { HorarioApiService } from '../../core/horario-api.service';
import { PersistenciaService } from '../../core/persistencia.service';
import { Sesion } from '../../core/models';

/**
 * NEW-8 (auditoría 2026-09-28): el diálogo "Editar sesión" ofrecía cambiar la alternancia de un
 * laboratorio, pero el cambio solo se aplicaba en memoria — ni /reacomodar ni el PATCH de docente
 * la reciben — y se perdía al recargar. La alternancia la fija el generador al emparejar sesiones
 * (ALT-05), así que el diálogo la muestra de solo lectura.
 */
describe('EditarSesionDialogComponent — alternancia de solo lectura', () => {
  const laboratorio: Sesion = {
    id: 's1', asignaturaId: 'a1', grupoId: 'g1', dia: 'lunes', horaInicio: '08:00', horaFin: '10:00',
    duracionHoras: 2, alternancia: 'TipoA', semana: 'A', virtual: false, espacioId: 'e1', tipoFlujo: 'Laboratorio',
  };
  let close: ReturnType<typeof vi.fn>;
  let persistencia: { asignarDocente: ReturnType<typeof vi.fn> };
  let horarioApi: { reacomodar: ReturnType<typeof vi.fn> };

  function crear() {
    close = vi.fn();
    persistencia = { asignarDocente: vi.fn().mockReturnValue(of({ advertencias: [] })) };
    horarioApi = { reacomodar: vi.fn() };
    TestBed.configureTestingModule({
      imports: [EditarSesionDialogComponent],
      providers: [
        provideNoopAnimations(),
        { provide: HorarioApiService, useValue: horarioApi },
        { provide: PersistenciaService, useValue: persistencia },
        { provide: MatDialogRef, useValue: { close } },
        {
          provide: MAT_DIALOG_DATA,
          useValue: {
            merged: laboratorio, sesion: laboratorio, asignaturas: [],
            docentes: [{ id: 'd1', nombre: 'Docente Uno', cedula: '', maxHoras: 40, disponibilidad: {} }],
            espacios: [], sesiones: [laboratorio], programaById: new Map(), facultadById: new Map(),
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(EditarSesionDialogComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('muestra la alternancia como texto, sin selector para cambiarla', () => {
    const fixture = crear();

    const bloque: HTMLElement = fixture.nativeElement.querySelector('[data-testid="alternancia-solo-lectura"]');
    expect(bloque.textContent).toContain('Semana A');
    expect(fixture.nativeElement.querySelectorAll('.seg-opt').length).toBe(0);
  });

  it('sin cambios de docente ni de franja no hay nada que guardar', () => {
    const fixture = crear();

    expect(fixture.componentInstance.hayCambios()).toBe(false);
  });

  it('guardar solo el docente conserva la alternancia y no llama a /reacomodar', () => {
    const fixture = crear();
    fixture.componentInstance.docenteId.set('d1');

    fixture.componentInstance.guardar();

    expect(persistencia.asignarDocente).toHaveBeenCalledWith('s1', 'd1');
    expect(horarioApi.reacomodar).not.toHaveBeenCalled();
    const resultado = close.mock.calls[0][0];
    expect(resultado.sesion).toMatchObject({ id: 's1', docenteId: 'd1', alternancia: 'TipoA', semana: 'A' });
  });
});
