import { vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';
import { AlternanciaTabComponent } from './alternancia-tab.component';
import { StateService } from '../../../core/state.service';
import { PersistenciaService } from '../../../core/persistencia.service';
import { Asignatura, CriterioCesionAlternancia } from '../../../core/models';

function asignatura(overrides: Partial<Asignatura> = {}): Asignatura {
  return {
    id: 'a1', codigo: 'COD1', nombre: 'Cálculo I',
    alternancia: 'SinAlternancia',
    sesionesTeoriaPresencialSemana: 2, horasTeoriaPresencial: 2,
    sesionesTeoriaVirtualSemana: 0, horasTeoriaVirtual: 2,
    sesionesLaboratorioSemana: 0, horasLaboratorio: 2,
    sesionesLaboratorioSemestre: 0,
    programaId: 'p1',
    ...overrides
  };
}

function criterio(overrides: Partial<CriterioCesionAlternancia> = {}): CriterioCesionAlternancia {
  return { id: 'c1', criterio: 'Electiva', orden: 1, activo: true, ...overrides };
}

describe('AlternanciaTabComponent', () => {
  let persistencia: {
    cargarCriteriosCesion: ReturnType<typeof vi.fn>;
    actualizarCriterioCesion: ReturnType<typeof vi.fn>;
    actualizarElegibilidadAlternancia: ReturnType<typeof vi.fn>;
  };
  let state: StateService;

  beforeEach(async () => {
    persistencia = {
      cargarCriteriosCesion: vi.fn().mockReturnValue(of([
        criterio({ id: 'c1', criterio: 'Electiva', orden: 1 }),
        criterio({ id: 'c2', criterio: 'Elegible', orden: 2 }),
      ])),
      actualizarCriterioCesion: vi.fn().mockReturnValue(of([])),
      actualizarElegibilidadAlternancia: vi.fn().mockReturnValue(of(undefined)),
    };

    await TestBed.configureTestingModule({
      imports: [AlternanciaTabComponent],
      providers: [
        provideNoopAnimations(),
        { provide: PersistenciaService, useValue: persistencia },
      ],
    }).compileComponents();

    state = TestBed.inject(StateService);
    state.setAsignaturas([
      asignatura({ id: 'a1', nombre: 'Cálculo I' }),
      asignatura({ id: 'a2', nombre: 'Química Orgánica', esCandidataAlternancia: true }),
    ]);
  });

  it('marca una asignatura como candidata y persiste el cambio', () => {
    const fixture = TestBed.createComponent(AlternanciaTabComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    const fila = component.filasFiltradas().find(f => f.asignatura.id === 'a1')!;
    component.cambiarCandidatura(fila, true);

    expect(persistencia.actualizarElegibilidadAlternancia).toHaveBeenCalledWith('a1', true);
    expect(state.asignaturas().find(a => a.id === 'a1')?.esCandidataAlternancia).toBe(true);
  });

  it('carga los criterios ordenados por orden', () => {
    persistencia.cargarCriteriosCesion.mockReturnValue(of([
      criterio({ id: 'c2', criterio: 'Elegible', orden: 2 }),
      criterio({ id: 'c1', criterio: 'Electiva', orden: 1 }),
    ]));
    const fixture = TestBed.createComponent(AlternanciaTabComponent);
    fixture.detectChanges();

    const criterios = fixture.componentInstance.criterios();
    expect(criterios.map(c => c.id)).toEqual(['c1', 'c2']);
  });

  it('activa/desactiva un criterio', () => {
    const fixture = TestBed.createComponent(AlternanciaTabComponent);
    fixture.detectChanges();
    const c = fixture.componentInstance.criterios()[0];

    fixture.componentInstance.toggleCriterioActivo(c, false);

    expect(persistencia.actualizarCriterioCesion).toHaveBeenCalledWith(c.id, { activo: false });
  });

  it('moverCriterio intercambia el orden con el vecino y respeta los límites', () => {
    const fixture = TestBed.createComponent(AlternanciaTabComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    const [primero, segundo] = component.criterios();

    component.moverCriterio(primero, -1); // ya es el primero: no debe llamar al backend
    expect(persistencia.actualizarCriterioCesion).not.toHaveBeenCalled();

    component.moverCriterio(primero, 1); // intercambia con el segundo
    expect(persistencia.actualizarCriterioCesion).toHaveBeenCalledWith(primero.id, { orden: segundo.orden });

    component.moverCriterio(segundo, 1); // ya es el último: no debe llamar de nuevo
    expect(persistencia.actualizarCriterioCesion).toHaveBeenCalledTimes(1);
  });
});
