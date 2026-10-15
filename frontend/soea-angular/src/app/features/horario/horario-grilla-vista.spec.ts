import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { HorarioComponent } from './horario.component';
import { StateService } from '../../core/state.service';
import { HorarioApiService } from '../../core/horario-api.service';
import { PersistenciaService } from '../../core/persistencia.service';
import { CatalogoService } from '../../core/catalogo.service';
import { Espacio, Sesion } from '../../core/models';

/**
 * grilla() reemplaza la tabla con rowspan (mergedByCell/coveredCells) que ocultaba sesiones
 * simultáneas/escalonadas y descuadraba columnas (auditoría UI 2026-09, ver grilla-carriles.spec.ts
 * para las funciones puras). Estos tests cubren el ensamblado completo: filtro por espacio/semana,
 * pliegue de contraparte y las dos vistas de la alternancia — con datos reales pasados por StateService,
 * no solo los items sintéticos de distribuirEnCarriles().
 */
function sesion(overrides: Partial<Sesion> = {}): Sesion {
  return {
    id: 's1', asignaturaId: 'a1', dia: 'lunes', horaInicio: '08:00', horaFin: '10:00',
    duracionHoras: 2, alternancia: 'SinAlternancia', virtual: false,
    ...overrides,
  };
}
function espacio(): Espacio { return { id: 'e1', nombre: 'Salón 1', capacidad: 30, tipo: 'Salón' }; }

function crearComponente(): HorarioComponent {
  TestBed.configureTestingModule({
    imports: [HorarioComponent],
    providers: [
      provideNoopAnimations(),
      { provide: HorarioApiService, useValue: {} },
      { provide: PersistenciaService, useValue: {} },
      { provide: CatalogoService, useValue: {} },
    ],
  });
  const fixture = TestBed.createComponent(HorarioComponent);
  const component = fixture.componentInstance;
  // Sin fixture.detectChanges(): evita disparar ngOnInit → syncFromBackend() (llamada HTTP real).
  const state = TestBed.inject(StateService);
  state.espacios.set([espacio()]);
  component.activeSpace.set(espacio());
  return component;
}

describe('HorarioComponent.grilla() — vista virtual con desborde', () => {
  it('6 sesiones simultáneas: ninguna se pierde entre tarjetas y el grupo "+N"', () => {
    const component = crearComponente();
    const state = TestBed.inject(StateService);
    component.selectVirtual();
    const sesiones = Array.from({ length: 6 }, (_, i) => sesion({
      id: `v${i}`, asignaturaId: `a${i}`, virtual: true, espacioId: undefined,
    }));
    state.setSesiones(sesiones);

    const dia = component.grilla().dias.find(d => d.valor === 'lunes')!;
    const enTarjetas = dia.tarjetas.length;
    const enMas = dia.mas.reduce((n, m) => n + m.items.length, 0);
    expect(enTarjetas + enMas).toBe(6);
    expect(enTarjetas).toBe(2); // MAX_CARRILES=3 → 2 carriles como tarjeta, 1 reservado al "+N"
    expect(dia.mas).toHaveLength(1);
    expect(dia.mas[0].items).toHaveLength(4);
  });
});

describe('HorarioComponent.grilla() — pliegue de contraparte de alternancia', () => {
  // GenerarHorarioService siempre etiqueta la contraparte con la semana OPUESTA a su presencial
  // (ver GenerarHorarioService.cs:754), así que en una vista de una sola semana normalmente nunca
  // coinciden — el pliegue solo importa cuando SÍ coexisten en la misma vista (p. ej. un import a
  // mano con datos inconsistentes). Por eso aquí se fuerza `semana: 'A'` en ambas: no es el dato
  // real que produce el backend, pero es exactamente el caso que sesionesVista() debe resolver sin
  // duplicar ni perder nada — el resto de los tests de este archivo sí usan semanas opuestas.
  it('presencial y contraparte coexistiendo en la misma vista y franja: una sola tarjeta con .contraparte', () => {
    const component = crearComponente();
    const state = TestBed.inject(StateService);
    const presencial = sesion({
      id: 'p1', dia: 'martes', horaInicio: '14:00', horaFin: '16:00',
      alternancia: 'TipoA', semana: 'A', parejaId: 'par1', espacioId: 'e1', virtual: false,
    });
    const contraparte = sesion({
      id: 'p1', dia: 'martes', horaInicio: '14:00', horaFin: '16:00',
      alternancia: 'TipoA', semana: 'A', parejaId: 'par1', espacioIdHogar: 'e1',
      virtual: true, espacioId: undefined, esContraparteVirtual: true,
    });
    state.setSesiones([presencial, contraparte]);

    const dia = component.grilla().dias.find(d => d.valor === 'martes')!;
    expect(dia.tarjetas).toHaveLength(1);
    expect(dia.tarjetas[0].m.contraparte).toBeTruthy();
    expect(dia.tarjetas[0].m.contraparte!.virtual).toBe(true);
  });

  it('contraparte con otra hora de fin: no se pliega, queda como tarjeta propia (no se pierde)', () => {
    const component = crearComponente();
    const state = TestBed.inject(StateService);
    const presencial = sesion({
      id: 'p2', dia: 'martes', horaInicio: '14:00', horaFin: '16:00',
      alternancia: 'TipoA', semana: 'A', parejaId: 'par2', espacioId: 'e1', virtual: false,
    });
    const contraparteDesfasada = sesion({
      id: 'p2', dia: 'martes', horaInicio: '14:00', horaFin: '17:00', // fin distinto: no coincide exacto
      alternancia: 'TipoA', semana: 'A', parejaId: 'par2', espacioIdHogar: 'e1',
      virtual: true, espacioId: undefined, esContraparteVirtual: true,
    });
    state.setSesiones([presencial, contraparteDesfasada]);

    const dia = component.grilla().dias.find(d => d.valor === 'martes')!;
    const total = dia.tarjetas.length + dia.mas.reduce((n, m) => n + m.items.length, 0);
    expect(total).toBe(2);
    expect(dia.tarjetas.every(t => !t.m.contraparte)).toBe(true);
  });

  it('contraparte cuya presencial no está visible en esta semana: se muestra sola, nunca se pierde', () => {
    const component = crearComponente();
    const state = TestBed.inject(StateService);
    // Caso real del backend: presencial en semana A, contraparte derivada en la B opuesta —
    // en la vista de Semana B la presencial queda fuera del filtro, pero la contraparte sigue
    // siendo una tarjeta (antes se descartaba en silencio si no encontraba pareja, ver HC:485).
    const presencial = sesion({
      id: 'p3', dia: 'jueves', horaInicio: '10:00', horaFin: '12:00',
      alternancia: 'TipoA', semana: 'A', parejaId: 'par3', espacioId: 'e1', virtual: false,
    });
    const contraparte = sesion({
      id: 'p3', dia: 'jueves', horaInicio: '10:00', horaFin: '12:00',
      alternancia: 'TipoA', semana: 'B', parejaId: 'par3', espacioIdHogar: 'e1',
      virtual: true, espacioId: undefined, esContraparteVirtual: true,
    });
    state.setSesiones([presencial, contraparte]);
    component.selectWeek('B');

    const dia = component.grilla().dias.find(d => d.valor === 'jueves')!;
    expect(dia.tarjetas).toHaveLength(1);
    expect(dia.tarjetas[0].m.virtual).toBe(true);
    expect(dia.tarjetas[0].m.contraparte).toBeFalsy();
  });
});

describe('HorarioComponent.grilla() — alternancia: horario completo y semana siguiente', () => {
  // Datos tal como los arma el backend: X (TipoA) e Y (TipoB) comparten jueves 15:00 en e1; cada
  // una tiene su fila presencial y su contraparte virtual derivada en la semana opuesta.
  function pareja() {
    const x = sesion({ id: 'x', asignaturaId: 'ax', grupoId: 'gx', dia: 'jueves', horaInicio: '15:00', horaFin: '17:00',
      alternancia: 'TipoA', semana: 'A', parejaId: 'par', espacioId: 'e1' });
    const xVirtual = { ...x, semana: 'B' as const, virtual: true, espacioId: undefined, espacioIdHogar: 'e1', esContraparteVirtual: true };
    const y = sesion({ id: 'y', asignaturaId: 'ay', grupoId: 'gy', dia: 'jueves', horaInicio: '15:00', horaFin: '17:00',
      alternancia: 'TipoB', semana: 'B', parejaId: 'par', espacioId: 'e1' });
    const yVirtual = { ...y, semana: 'A' as const, virtual: true, espacioId: undefined, espacioIdHogar: 'e1', esContraparteVirtual: true };
    const fija = sesion({ id: 'fija', dia: 'miercoles', espacioId: 'e1', alternancia: 'SinAlternancia', semana: undefined });
    return [x, xVirtual, y, yVirtual, fija];
  }

  it('horario completo: todo el horario, y la tarjeta que alterna dice quién usa el espacio la próxima semana', () => {
    const component = crearComponente();
    TestBed.inject(StateService).setSesiones(pareja());

    const jueves = component.grilla().dias.find(d => d.valor === 'jueves')!;
    expect(jueves.tarjetas).toHaveLength(1);
    expect(jueves.tarjetas[0].m.asignaturaId).toBe('ax');
    expect(jueves.tarjetas[0].m.contraparte?.asignaturaId).toBe('ay');
    expect(component.grilla().dias.find(d => d.valor === 'miercoles')!.tarjetas).toHaveLength(1);
  });

  it('semana siguiente: solo las parejas que alternan, el resto vacío', () => {
    const component = crearComponente();
    TestBed.inject(StateService).setSesiones(pareja());
    component.selectWeek('B');

    const jueves = component.grilla().dias.find(d => d.valor === 'jueves')!;
    expect(jueves.tarjetas).toHaveLength(1);
    expect(jueves.tarjetas[0].m.asignaturaId).toBe('ay');
    expect(jueves.tarjetas[0].m.contraparte?.asignaturaId).toBe('ax');
    expect(component.grilla().dias.find(d => d.valor === 'miercoles')!.tarjetas).toHaveLength(0);
    expect(component.grilla().total).toBe(1);
  });
});
