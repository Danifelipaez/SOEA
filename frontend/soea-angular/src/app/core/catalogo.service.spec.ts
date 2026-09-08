import { vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { CatalogoService } from './catalogo.service';
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
