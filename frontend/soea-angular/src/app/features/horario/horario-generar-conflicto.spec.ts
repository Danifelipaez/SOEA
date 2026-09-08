import { vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError, Subject } from 'rxjs';
import { HorarioComponent } from './horario.component';
import { StateService } from '../../core/state.service';
import { HorarioApiService } from '../../core/horario-api.service';
import { PersistenciaService } from '../../core/persistencia.service';
import { CatalogoService } from '../../core/catalogo.service';
import { Asignatura, Docente, Espacio } from '../../core/models';

/**
 * generarHorario() es el único lugar que conoce la respuesta cruda del backend — StateService
 * no puede llenarse solo (ver state.service.spec.ts). Confirma que una generación fallida guarda
 * GruposEnConflicto para que /catalogo pueda resaltar esos grupos, y que una generación exitosa
 * limpia cualquier conflicto de un intento previo (si no, un aviso de una corrida vieja se queda
 * pegado para siempre).
 */
function asignatura(): Asignatura {
  return {
    id: 'a1', codigo: 'COD1', nombre: 'Cálculo I', alternancia: 'SinAlternancia',
    sesionesTeoriaPresencialSemana: 1, horasTeoriaPresencial: 2,
    sesionesTeoriaVirtualSemana: 0, horasTeoriaVirtual: 2,
    sesionesLaboratorioSemana: 0, horasLaboratorio: 2, sesionesLaboratorioSemestre: 0,
    programaId: 'p1',
  };
}
function docente(): Docente { return { id: 'd1', nombre: 'Doc', cedula: '1', maxHoras: 20, disponibilidad: {} }; }
function espacio(): Espacio { return { id: 'e1', nombre: 'Salón 1', capacidad: 30, tipo: 'Salón' }; }

describe('HorarioComponent.generarHorario() — StateService.gruposEnConflicto', () => {
  let state: StateService;
  let horarioApi: { generarHorario: ReturnType<typeof vi.fn>; mapearSesiones: ReturnType<typeof vi.fn> };
  let component: HorarioComponent;

  beforeEach(() => {
    horarioApi = { generarHorario: vi.fn(), mapearSesiones: vi.fn((s: unknown[]) => s) };
    TestBed.configureTestingModule({
      imports: [HorarioComponent],
      providers: [
        provideNoopAnimations(),
        { provide: HorarioApiService, useValue: horarioApi },
        { provide: PersistenciaService, useValue: {} },
        { provide: CatalogoService, useValue: {} },
      ],
    });
    const fixture = TestBed.createComponent(HorarioComponent);
    component = fixture.componentInstance;
    // Sin fixture.detectChanges(): evita disparar ngOnInit → syncFromBackend() (llamada HTTP real).
    component.backendReady.set(true);
    state = TestBed.inject(StateService);
    state.setAsignaturas([asignatura()]);
    state.docentes.set([docente()]);
    state.espacios.set([espacio()]);
  });

  it('generación infactible con grupos responsables guarda sus Ids en el StateService', () => {
    horarioApi.generarHorario.mockReturnValue(throwError(() => ({
      esFactible: false, mensajeError: 'infeasible', gruposEnConflicto: ['g1', 'g2'],
    })));

    component.generarHorario();

    expect(state.gruposEnConflicto()).toEqual(['g1', 'g2']);
  });

  it('generación infactible sin grupos responsables deja la lista vacía', () => {
    horarioApi.generarHorario.mockReturnValue(throwError(() => ({
      esFactible: false, mensajeError: 'infeasible',
    })));

    component.generarHorario();

    expect(state.gruposEnConflicto()).toEqual([]);
  });

  it('una generación exitosa limpia un conflicto reportado por un intento previo', () => {
    state.setGruposEnConflicto(['g-viejo']);
    horarioApi.generarHorario.mockReturnValue(of({
      horarioId: 'h1', semestre: '2026-1', esFactible: true, puntajeFitness: 10, generaciones: 5,
      sesiones: [],
    }));

    component.generarHorario();

    expect(state.gruposEnConflicto()).toEqual([]);
  });
});

/**
 * El banner persistente de infactibilidad (visible en /horario hasta la próxima generación,
 * a diferencia del snackbar que desaparece a los 9s) mostraba SIEMPRE el mismo texto genérico
 * ("No se encontró un horario factible con el catálogo actual") y volcaba los logs crudos del
 * backend debajo — con jerga interna (HC-SEP, HC-ALT, "status del solver: Infeasible") que un
 * coordinador no técnico no puede accionar. mensajeInfactible() debe llevar la misma guía
 * accionable que ya usa el snackbar (mensajeInfactibilidadAmigable), y limpiarse al tener éxito.
 */
describe('HorarioComponent.generarHorario() — banner persistente (mensajeInfactible)', () => {
  let state: StateService;
  let horarioApi: { generarHorario: ReturnType<typeof vi.fn>; mapearSesiones: ReturnType<typeof vi.fn> };
  let component: HorarioComponent;

  beforeEach(() => {
    horarioApi = { generarHorario: vi.fn(), mapearSesiones: vi.fn((s: unknown[]) => s) };
    TestBed.configureTestingModule({
      imports: [HorarioComponent],
      providers: [
        provideNoopAnimations(),
        { provide: HorarioApiService, useValue: horarioApi },
        { provide: PersistenciaService, useValue: {} },
        { provide: CatalogoService, useValue: {} },
      ],
    });
    const fixture = TestBed.createComponent(HorarioComponent);
    component = fixture.componentInstance;
    component.backendReady.set(true);
    state = TestBed.inject(StateService);
    state.setAsignaturas([asignatura()]);
    state.docentes.set([docente()]);
    state.espacios.set([espacio()]);
  });

  it('generación infactible llena mensajeInfactible() sin la jerga interna del backend', () => {
    horarioApi.generarHorario.mockReturnValue(throwError(() => ({
      esFactible: false, mensajeError: 'El modelo no tiene solución factible (status del solver: Infeasible) HC-SEP HC-ALT',
      motivoInfactibilidad: 'FranjaGrupo',
    })));

    component.generarHorario();

    expect(component.mensajeInfactible()).toMatch(/disponibilidad/i);
    expect(component.mensajeInfactible()).not.toMatch(/HC-|Infeasible/i);
  });

  it('una generación exitosa limpia mensajeInfactible() de un intento previo', () => {
    component.mensajeInfactible.set('mensaje viejo');
    horarioApi.generarHorario.mockReturnValue(of({
      horarioId: 'h1', semestre: '2026-1', esFactible: true, puntajeFitness: 10, generaciones: 5,
      sesiones: [],
    }));

    component.generarHorario();

    expect(component.mensajeInfactible()).toBe('');
  });
});

/**
 * generarHorario() no marcaba ningún estado de "en curso" propio — el botón del template solo
 * se deshabilita con loadingBackend(), una señal que solo toca syncFromBackend() (otro flujo).
 * Dos clics rápidos antes de que llegue la respuesta disparan dos POST concurrentes; la última
 * respuesta en llegar "gana" sobre sesiones/horarioId/gruposEnConflicto sin ningún aviso.
 */
describe('HorarioComponent.generarHorario() — doble envío', () => {
  let state: StateService;
  let horarioApi: { generarHorario: ReturnType<typeof vi.fn>; mapearSesiones: ReturnType<typeof vi.fn> };
  let component: HorarioComponent;

  beforeEach(() => {
    horarioApi = { generarHorario: vi.fn(), mapearSesiones: vi.fn((s: unknown[]) => s) };
    TestBed.configureTestingModule({
      imports: [HorarioComponent],
      providers: [
        provideNoopAnimations(),
        { provide: HorarioApiService, useValue: horarioApi },
        { provide: PersistenciaService, useValue: {} },
        { provide: CatalogoService, useValue: {} },
      ],
    });
    const fixture = TestBed.createComponent(HorarioComponent);
    component = fixture.componentInstance;
    component.backendReady.set(true);
    state = TestBed.inject(StateService);
    state.setAsignaturas([asignatura()]);
    state.docentes.set([docente()]);
    state.espacios.set([espacio()]);
  });

  it('dos llamadas seguidas antes de que responda el backend solo disparan un POST', () => {
    const enVuelo = new Subject<any>(); // nunca emite: simula la petición "en curso"
    horarioApi.generarHorario.mockReturnValue(enVuelo.asObservable());

    component.generarHorario();
    component.generarHorario();

    expect(horarioApi.generarHorario).toHaveBeenCalledTimes(1);
  });

  it('tras terminar (éxito o error) un nuevo clic sí dispara otra generación', () => {
    horarioApi.generarHorario.mockReturnValue(of({
      horarioId: 'h1', semestre: '2026-1', esFactible: true, puntajeFitness: 10, generaciones: 5,
      sesiones: [],
    }));

    component.generarHorario();
    component.generarHorario();

    expect(horarioApi.generarHorario).toHaveBeenCalledTimes(2);
  });
});
