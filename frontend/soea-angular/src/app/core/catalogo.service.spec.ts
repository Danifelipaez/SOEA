import { vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { CatalogoService, normalizarClavesDia, normalizarClavesDiaJson } from './catalogo.service';
import { PersistenciaService } from './persistencia.service';
import { StateService } from './state.service';
import { Grupo } from './models';

/**
 * G6 (bug reportado "al crear un grupo desde esa pestaña, no se guarda, no crea grupo"):
 * guardar() escribía el StateService de forma optimista ANTES del HTTP y nunca revertía si el
 * POST/PUT fallaba. El grupo se veía en la tabla, el snackbar de error se iba a los 4s, y al
 * recargar desde BD ya no estaba — "se creó… y luego no está" desde el punto de vista del
 * usuario, aunque el backend nunca lo hubiera persistido.
 */
describe('CatalogoService — reversión en error', () => {
  let persistencia: { guardarGrupo: ReturnType<typeof vi.fn>; actualizarGrupo: ReturnType<typeof vi.fn> };
  let catalogo: CatalogoService;
  let state: StateService;

  const grupo = (overrides: Partial<Grupo> = {}): Grupo => ({
    id: 'g1', asignaturaId: 'a1', nombre: 'G1', estudiantesInscritos: 30, programaId: 'p1',
    ...overrides,
  });

  beforeEach(() => {
    persistencia = { guardarGrupo: vi.fn(), actualizarGrupo: vi.fn() };
    TestBed.configureTestingModule({
      providers: [{ provide: PersistenciaService, useValue: persistencia }],
    });
    catalogo = TestBed.inject(CatalogoService);
    state = TestBed.inject(StateService);
  });

  it('grupo nuevo: si el POST falla, no queda en el state', () => {
    persistencia.guardarGrupo.mockReturnValue(throwError(() => new Error('400 asignatura no existe')));

    catalogo.guardar('grupo', grupo()).subscribe({ error: () => {} });

    expect(state.grupos().some(g => g.id === 'g1')).toBe(false);
  });

  it('grupo nuevo: si el POST funciona, queda en el state', () => {
    persistencia.guardarGrupo.mockReturnValue(of(grupo()));

    catalogo.guardar('grupo', grupo()).subscribe();

    expect(state.grupos().some(g => g.id === 'g1')).toBe(true);
  });

  it('grupo existente: si el PUT falla, el state vuelve al valor anterior a la edición', () => {
    state.addGrupo(grupo({ nombre: 'Original' }));
    catalogo.marcarEnBd('grupo', 'g1');
    persistencia.actualizarGrupo.mockReturnValue(throwError(() => new Error('500')));

    catalogo.guardar('grupo', grupo({ nombre: 'Editado' })).subscribe({ error: () => {} });

    expect(state.grupos().find(g => g.id === 'g1')?.nombre).toBe('Original');
  });

  it('grupo existente: si el PUT funciona, el state queda con la edición', () => {
    state.addGrupo(grupo({ nombre: 'Original' }));
    catalogo.marcarEnBd('grupo', 'g1');
    persistencia.actualizarGrupo.mockReturnValue(of(grupo({ nombre: 'Editado' })));

    catalogo.guardar('grupo', grupo({ nombre: 'Editado' })).subscribe();

    expect(state.grupos().find(g => g.id === 'g1')?.nombre).toBe('Editado');
  });
});

/**
 * L-3 (auditoría 2026-09-28): el import de Excel guarda la disponibilidad por grupo con claves
 * "Martes"/"Miercoles"/"Sábado"; el editor y el chequeo de "sin días disponibles" leen "martes"…
 * y con claves capitalizadas todos los días aparecían cerrados (y guardar los destruía).
 */
describe('CatalogoService — claves de día de la disponibilidad', () => {
  const importado = JSON.stringify({
    Lunes: { noDisponible: false, tipo: 'Franja específica', desde: '08:00', hasta: '10:00' },
    'Miércoles': { noDisponible: false, tipo: 'Franja específica', desde: '08:00', hasta: '10:00' },
    'Sábado': { noDisponible: false, tipo: 'Franja específica', desde: '07:00', hasta: '09:00' },
  });

  it('normalizarClavesDiaJson pasa a minúscula y sin tilde, conservando el contenido', () => {
    const obj = JSON.parse(normalizarClavesDiaJson(importado)!);

    expect(Object.keys(obj).sort()).toEqual(['lunes', 'miercoles', 'sabado']);
    expect(obj.miercoles.desde).toBe('08:00');
  });

  it('no toca claves que no son un día, ni un JSON inválido, ni el vacío', () => {
    expect(normalizarClavesDia({ Otra: 1, MARTES: 2 })).toEqual({ Otra: 1, martes: 2 });
    expect(normalizarClavesDiaJson('no es json')).toBe('no es json');
    expect(normalizarClavesDiaJson(undefined)).toBeUndefined();
  });

  it('un grupo que llega de la API con claves capitalizadas queda normalizado en el state', () => {
    const persistencia = { guardarGrupo: vi.fn() };
    TestBed.configureTestingModule({ providers: [{ provide: PersistenciaService, useValue: persistencia }] });
    const catalogo = TestBed.inject(CatalogoService);
    const state = TestBed.inject(StateService);
    persistencia.guardarGrupo.mockReturnValue(of({
      id: 'g9', asignaturaId: 'a1', nombre: 'G9', estudiantesInscritos: 20, programaId: 'p1',
      disponibilidadUiJson: importado,
    }));

    catalogo.guardar('grupo', { id: 'g9', asignaturaId: 'a1', nombre: 'G9', estudiantesInscritos: 20, programaId: 'p1' }).subscribe();

    const claves = Object.keys(JSON.parse(state.grupos().find(g => g.id === 'g9')!.disponibilidadUiJson!));
    expect(claves).toContain('miercoles');
    expect(claves).not.toContain('Miércoles');
  });
});
