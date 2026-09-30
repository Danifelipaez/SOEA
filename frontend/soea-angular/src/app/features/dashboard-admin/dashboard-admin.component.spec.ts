import { TestBed } from '@angular/core/testing';
import { DashboardAdminComponent } from './dashboard-admin.component';
import { StateService } from '../../core/state.service';
import { CatalogoService } from '../../core/catalogo.service';
import { Docente, Sesion } from '../../core/models';

/**
 * G4 (bug reportado "error en el conteo de horas por docente"): state.sesiones() trae una fila
 * por (sesión, semana) — las filas A y B de una misma sesión comparten `id` y solo cambian de
 * modalidad (docs/algorithms.md, StateService.updateSesion). docentesData() sumaba ambas filas
 * sin deduplicar por id → cada sesión contaba el doble en las horas del docente.
 */
describe('DashboardAdminComponent — carga docente', () => {
  function crear() {
    TestBed.configureTestingModule({
      imports: [DashboardAdminComponent],
      providers: [{ provide: CatalogoService, useValue: { cargarTodo: () => ({ subscribe: () => {} }) } }],
    });
    const fixture = TestBed.createComponent(DashboardAdminComponent);
    const state = TestBed.inject(StateService);
    return { fixture, component: fixture.componentInstance, state };
  }

  const docente = (overrides: Partial<Docente> = {}): Docente =>
    ({ id: 'd1', nombre: 'Ana', cedula: '123', maxHoras: 10, disponibilidad: {}, ...overrides });

  // Una sesión real de 2h, replicada en semana A y B (mismo id) — el shape real que emite el
  // backend (GenerarHorarioService.ConstruirSesionesDto, una fila por AsignacionSemanal).
  const filaA = (overrides: Partial<Sesion> = {}): Sesion => ({
    id: 's1', asignaturaId: 'a1', docenteId: 'd1', dia: 'lunes',
    horaInicio: '07:00', horaFin: '09:00', duracionHoras: 2, virtual: false,
    alternancia: 'SinAlternancia', semana: 'A', ...overrides,
  });
  const filaB = (overrides: Partial<Sesion> = {}): Sesion => ({
    ...filaA(overrides), semana: 'B',
  });

  it('una sesión de 2h (filas A+B) reporta 2h, no 4h', () => {
    const { component, state } = crear();
    state.docentes.set([docente()]);
    state.sesiones.set([filaA(), filaB()]);

    const fila = component.docentesData().find(d => d.docente === 'Ana');

    expect(fila?.horasAsignadas).toBe(2);
  });

  it('dos sesiones distintas de 2h (con sus filas A+B) reportan 4h, no 8h', () => {
    const { component, state } = crear();
    state.docentes.set([docente()]);
    state.sesiones.set([
      filaA({ id: 's1' }), filaB({ id: 's1' }),
      filaA({ id: 's2', horaInicio: '10:00', horaFin: '12:00' }),
      filaB({ id: 's2', horaInicio: '10:00', horaFin: '12:00' }),
    ]);

    const fila = component.docentesData().find(d => d.docente === 'Ana');

    expect(fila?.horasAsignadas).toBe(4);
  });

  it('sin maxHoras declarado, el default es 40 (igual que el resto de la app)', () => {
    const { component, state } = crear();
    state.docentes.set([docente({ maxHoras: undefined as any })]);
    state.sesiones.set([filaA(), filaB()]);

    const fila = component.docentesData().find(d => d.docente === 'Ana');

    expect(fila?.maxHoras).toBe(40);
  });
});

/**
 * L-6 (auditoría 2026-09-28): el KPI de ocupación dividía entre 16 h × 6 días = 96 h por aula, contando el sábado
 * como jornada completa; el aula real tiene 88 h a la semana (lunes–viernes 06:00–22:00 y sábado hasta las 14:00),
 * así que la ocupación salía menor de lo real.
 */
describe('DashboardAdminComponent — ocupación de aulas', () => {
  it('una aula con 44 h presenciales está al 50 % (88 h semanales), no al 46 % (96 h)', () => {
    TestBed.configureTestingModule({
      imports: [DashboardAdminComponent],
      providers: [{ provide: CatalogoService, useValue: { cargarTodo: () => ({ subscribe: () => {} }) } }],
    });
    const component = TestBed.createComponent(DashboardAdminComponent).componentInstance;
    const state = TestBed.inject(StateService);
    state.espacios.set([{ id: 'e1', nombre: 'Aula', capacidad: 30, tipo: 'Salón' }]);
    // 11 sesiones presenciales de 4 h = 44 h
    state.sesiones.set(Array.from({ length: 11 }, (_, i) => ({
      id: `s${i}`, asignaturaId: 'a1', dia: 'lunes', horaInicio: '07:00', horaFin: '11:00',
      duracionHoras: 4, virtual: false, alternancia: 'SinAlternancia', semana: 'A',
    } as Sesion)));

    expect(component.ocupacionPct()).toBe(50);
  });
});
