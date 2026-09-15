import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { HorarioComponent } from './horario.component';
import { StateService } from '../../core/state.service';
import { HorarioApiService } from '../../core/horario-api.service';
import { PersistenciaService } from '../../core/persistencia.service';
import { CatalogoService } from '../../core/catalogo.service';

/**
 * FE9 auditoría: importarHorario() ahora descarta las filas con un `dia` fuera de la grilla
 * canónica (p. ej. "domingo") en vez de inyectarlas tal cual — antes entraban a state.sesiones()
 * sin que la grilla pudiera ubicarlas en ninguna celda (pérdida de datos silenciosa). También
 * limpia `horarioId`, porque las sesiones importadas no corresponden a ningún horario persistido.
 */
function jsonFile(contenido: unknown): File {
  return new File([JSON.stringify(contenido)], 'horario.json', { type: 'application/json' });
}

async function esperarHasta(condicion: () => boolean, timeoutMs = 1000): Promise<void> {
  const inicio = Date.now();
  while (!condicion()) {
    if (Date.now() - inicio > timeoutMs) throw new Error('Timeout esperando la condición.');
    await new Promise(resolve => setTimeout(resolve, 10));
  }
}

describe('HorarioComponent.importarHorario — sesión con día inválido', () => {
  let state: StateService;
  let component: HorarioComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HorarioComponent],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        { provide: HorarioApiService, useValue: { obtenerActual: () => of(null) } },
        { provide: PersistenciaService, useValue: {} },
        { provide: CatalogoService, useValue: { cargarTodo: () => of(undefined) } },
      ],
    });
    const fixture = TestBed.createComponent(HorarioComponent);
    component = fixture.componentInstance;
    state = TestBed.inject(StateService);
  });

  it('una sesión con día inválido se descarta; la válida entra y horarioId se limpia', async () => {
    state.horarioId.set('horario-anterior');
    const sesiones = [
      { id: 's1', asignaturaId: 'a1', dia: 'lunes', horaInicio: '08:00', horaFin: '10:00', duracionHoras: 2 },
      { id: 's2', asignaturaId: 'a1', dia: 'domingo', horaInicio: '08:00', horaFin: '10:00', duracionHoras: 2 },
    ];
    const input = document.createElement('input');
    input.type = 'file';
    const file = jsonFile(sesiones);
    Object.defineProperty(input, 'files', { value: [file] });

    component.importarHorario({ target: input } as unknown as Event);
    await esperarHasta(() => state.sesiones().length > 0);

    expect(state.sesiones()).toHaveLength(1);
    expect(state.sesiones().some(s => s.dia === 'domingo')).toBe(false);
    expect(state.horarioId()).toBeNull();
  });
});
