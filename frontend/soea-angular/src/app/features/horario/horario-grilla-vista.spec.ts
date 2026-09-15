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
 * pliegue de contraparte y el caso "siempre" de Semana B — con datos reales pasados por StateService,
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

describe('HorarioComponent.grilla() — Semana B: lo que no alterna se ve ocupado', () => {
  it('una sesión SinAlternancia se muestra en gris ("siempre") en vez de dejar el aula libre', () => {
    const component = crearComponente();
    const state = TestBed.inject(StateService);
    // hayAlternancia() exige al menos una sesión con parejaId para que el selector de semana exista.
    const conPareja = sesion({ id: 'x1', dia: 'lunes', espacioId: 'e1', parejaId: 'parX', semana: 'A' });
    const contraparteX = sesion({
      id: 'x1', dia: 'lunes', parejaId: 'parX', semana: 'B', virtual: true, espacioId: undefined, espacioIdHogar: 'e1', esContraparteVirtual: true,
    });
    const fija = sesion({ id: 'fija', dia: 'miercoles', espacioId: 'e1', alternancia: 'SinAlternancia', semana: undefined });
    state.setSesiones([conPareja, contraparteX, fija]);
    expect(component.hayAlternancia()).toBe(true);

    component.selectWeek('B');

    const dia = component.grilla().dias.find(d => d.valor === 'miercoles')!;
    expect(dia.tarjetas).toHaveLength(1);
    expect(dia.tarjetas[0].siempre).toBe(true);
  });

  it('en Semana A la misma sesión no se marca "siempre" (solo aplica en B)', () => {
    const component = crearComponente();
    const state = TestBed.inject(StateService);
    const fija = sesion({ id: 'fija', dia: 'miercoles', espacioId: 'e1', alternancia: 'SinAlternancia', semana: undefined });
    state.setSesiones([fija]);

    const dia = component.grilla().dias.find(d => d.valor === 'miercoles')!;
    expect(dia.tarjetas[0].siempre).toBe(false);
  });
});
