import { vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { EditarSesionDialogComponent } from './horario.component';
import { StateService } from '../../core/state.service';
import { HorarioApiService } from '../../core/horario-api.service';
import { PersistenciaService } from '../../core/persistencia.service';
import { Sesion } from '../../core/models';

/**
 * Mover una sesión (cambio de día/hora/espacio) pasa por POST /horario/reacomodar, que exige
 * el horarioId de la corrida que generó el horario. Si la página se recargó, ese id solo vive
 * en memoria (StateService.horarioId) y se pierde — reacomodar() ya tiene un guard para esto
 * (horario.component.ts), pero no tenía ningún test que lo ejercitara.
 */
function sesion(overrides: Partial<Sesion> = {}): Sesion {
  return {
    id: 's1', asignaturaId: 'a1', grupoId: 'g1', dia: 'lunes', horaInicio: '08:00', horaFin: '10:00',
    duracionHoras: 2, alternancia: 'SinAlternancia', virtual: false, docenteId: undefined, espacioId: 'e1',
    ...overrides,
  };
}

describe('EditarSesionDialogComponent — reacomodar sin horarioId', () => {
  let state: StateService;
  let horarioApi: { reacomodar: ReturnType<typeof vi.fn> };
  let component: EditarSesionDialogComponent;

  beforeEach(() => {
    horarioApi = { reacomodar: vi.fn() };
    const s = sesion();
    TestBed.configureTestingModule({
      imports: [EditarSesionDialogComponent],
      providers: [
        provideNoopAnimations(),
        { provide: HorarioApiService, useValue: horarioApi },
        { provide: PersistenciaService, useValue: {} },
        { provide: MatDialogRef, useValue: { close: vi.fn() } },
        {
          provide: MAT_DIALOG_DATA,
          useValue: {
            merged: s, sesion: s, asignaturas: [], docentes: [], espacios: [],
            sesiones: [s], programaById: new Map(), facultadById: new Map(),
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(EditarSesionDialogComponent);
    component = fixture.componentInstance;
    state = TestBed.inject(StateService);
    // No se setea state.horarioId — simula una recarga de página que perdió el id en memoria.
  });

  it('mover la sesión sin horarioId en memoria muestra el error y NUNCA llama a /reacomodar', () => {
    component.dia.set('martes'); // cambio de slot: dispara la rama reacomodar() de guardar()

    component.guardar();

    expect(horarioApi.reacomodar).not.toHaveBeenCalled();
    expect(component.errorServidor()).toMatch(/genera el horario de nuevo|no hay un horario/i);
    expect(component.guardando()).toBe(false);
  });

  it('con horarioId presente, mover la sesión sí llama a /reacomodar', () => {
    state.horarioId.set('h1');
    horarioApi.reacomodar.mockReturnValue({ subscribe: () => {} });
    component.dia.set('martes');

    component.guardar();

    expect(horarioApi.reacomodar).toHaveBeenCalledWith(expect.objectContaining({ horarioId: 'h1' }));
  });
});
