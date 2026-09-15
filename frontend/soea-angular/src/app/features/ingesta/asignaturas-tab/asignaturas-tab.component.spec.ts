import { vi } from 'vitest';
import { throwError } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { AsignaturasTabComponent } from './asignaturas-tab.component';
import { StateService } from '../../../core/state.service';
import { Asignatura, Grupo } from '../../../core/models';

/**
 * Aviso visual de datos incompletos en un grupo (sin requisito de espacio o sin disponibilidad
 * horaria declarada) y de conflicto reportado por el backend al fallar la generación del
 * horario (StateService.gruposEnConflicto). Ambos deben propagarse a la fila de la asignatura
 * cuando está colapsada — de lo contrario un grupo problemático queda invisible.
 */
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

function grupo(overrides: Partial<Grupo> = {}): Grupo {
  return {
    id: 'g1', asignaturaId: 'a1', nombre: 'G1', estudiantesInscritos: 30, programaId: 'p1',
    disponibilidadUiJson: JSON.stringify({ lunes: { noDisponible: false } }),
    requisitosEspacio: [{ tipoSesion: 'TeoriaPresencial', tipoEspacio: 'Salon', sesiones: 1 }],
    ...overrides
  };
}

describe('AsignaturasTabComponent — avisos de grupo incompleto y en conflicto', () => {
  let state: StateService;
  let component: AsignaturasTabComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AsignaturasTabComponent],
      providers: [provideNoopAnimations()],
    });
    const fixture = TestBed.createComponent(AsignaturasTabComponent);
    component = fixture.componentInstance;
    state = TestBed.inject(StateService);
    state.setAsignaturas([asignatura()]);
  });

  it('un grupo completo (espacio + disponibilidad) no está incompleto', () => {
    expect(component.grupoIncompleto(grupo())).toBe(false);
  });

  it('un grupo sin requisito de espacio está incompleto', () => {
    expect(component.grupoIncompleto(grupo({ requisitosEspacio: [] }))).toBe(true);
  });

  it('un grupo sin disponibilidad declarada está incompleto', () => {
    expect(component.grupoIncompleto(grupo({ disponibilidadUiJson: undefined }))).toBe(true);
  });

  it('un grupo con disponibilidad declarada pero 0 días disponibles está incompleto', () => {
    const disp = JSON.stringify({
      lunes: { noDisponible: true }, martes: { noDisponible: true }, miercoles: { noDisponible: true },
      jueves: { noDisponible: true }, viernes: { noDisponible: true }, sabado: { noDisponible: true },
    });
    expect(component.grupoIncompleto(grupo({ disponibilidadUiJson: disp }))).toBe(true);
  });

  it('grupoEnConflicto refleja StateService.gruposEnConflicto', () => {
    const g = grupo({ id: 'g9' });
    expect(component.grupoEnConflicto(g)).toBe(false);
    state.setGruposEnConflicto(['g9']);
    expect(component.grupoEnConflicto(g)).toBe(true);
  });

  it('asignaturaIncompleta es true si CUALQUIER grupo propio está incompleto (colapsado o no)', () => {
    state.grupos.set([grupo({ id: 'g1' }), grupo({ id: 'g2', requisitosEspacio: [] })]);
    expect(component.asignaturaIncompleta(asignatura())).toBe(true);
  });

  it('asignaturaIncompleta es false cuando todos los grupos propios están completos', () => {
    state.grupos.set([grupo({ id: 'g1' }), grupo({ id: 'g2' })]);
    expect(component.asignaturaIncompleta(asignatura())).toBe(false);
  });

  it('asignaturaEnConflicto es true si alguno de sus grupos está en la lista de conflicto', () => {
    state.grupos.set([grupo({ id: 'g1' }), grupo({ id: 'g2' })]);
    state.setGruposEnConflicto(['g2']);
    expect(component.asignaturaEnConflicto(asignatura())).toBe(true);
  });

  it('asignaturaEnConflicto es false si el conflicto pertenece a otra asignatura', () => {
    state.grupos.set([grupo({ id: 'g1' })]);
    state.setGruposEnConflicto(['g-de-otra-asignatura']);
    expect(component.asignaturaEnConflicto(asignatura())).toBe(false);
  });
});

/**
 * El borde rojo por sí solo no le dice al usuario QUÉ falta ni CÓMO corregirlo — sobre todo en
 * la fila de asignatura colapsada, donde no hay ninguna otra pista visible. tituloGrupo/
 * tituloAsignatura alimentan el atributo `title` (tooltip nativo) con el motivo real, derivado
 * de los mismos campos que ya deciden el color — nunca texto inventado sobre por qué falló CP-SAT.
 */
describe('AsignaturasTabComponent — tooltip explicando el aviso (title)', () => {
  let state: StateService;
  let component: AsignaturasTabComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AsignaturasTabComponent],
      providers: [provideNoopAnimations()],
    });
    const fixture = TestBed.createComponent(AsignaturasTabComponent);
    component = fixture.componentInstance;
    state = TestBed.inject(StateService);
    state.setAsignaturas([asignatura()]);
  });

  it('un grupo completo no tiene tooltip', () => {
    expect(component.tituloGrupo(grupo())).toBe('');
  });

  it('el tooltip de un grupo sin requisito de espacio lo menciona explícitamente', () => {
    expect(component.tituloGrupo(grupo({ requisitosEspacio: [] }))).toMatch(/aula/i);
  });

  it('el tooltip de un grupo sin disponibilidad declarada lo menciona explícitamente', () => {
    expect(component.tituloGrupo(grupo({ disponibilidadUiJson: undefined }))).toMatch(/disponibilidad/i);
  });

  it('el tooltip de un grupo en conflicto explica que el backend lo señaló al fallar la generación', () => {
    const g = grupo({ id: 'g9' });
    state.setGruposEnConflicto(['g9']);
    expect(component.tituloGrupo(g)).toMatch(/generar|horario|conflicto/i);
  });

  it('un grupo incompleto Y en conflicto combina ambos motivos en el tooltip', () => {
    const g = grupo({ id: 'g9', requisitosEspacio: [] });
    state.setGruposEnConflicto(['g9']);
    const titulo = component.tituloGrupo(g);
    expect(titulo).toMatch(/aula/i);
    expect(titulo).toMatch(/generar|horario|conflicto/i);
  });

  it('el tooltip de la asignatura colapsada nombra los grupos incompletos (no solo dice "hay un problema")', () => {
    state.grupos.set([grupo({ id: 'g1' }), grupo({ id: 'g2', nombre: 'G2', requisitosEspacio: [] })]);
    expect(component.tituloAsignatura(asignatura())).toContain('G2');
  });

  it('el tooltip de la asignatura colapsada nombra los grupos en conflicto', () => {
    state.grupos.set([grupo({ id: 'g1' }), grupo({ id: 'g2', nombre: 'G2' })]);
    state.setGruposEnConflicto(['g2']);
    expect(component.tituloAsignatura(asignatura())).toContain('G2');
  });

  it('una asignatura sin problemas no tiene tooltip', () => {
    state.grupos.set([grupo({ id: 'g1' })]);
    expect(component.tituloAsignatura(asignatura())).toBe('');
  });
});

/**
 * Sección "Grupos sin asignatura válida": antes de esto un grupo huérfano (asignaturaId de una
 * asignatura ya eliminada) era completamente invisible en /catalogo — la tabla principal se arma
 * vía getGruposByAsignatura, que nunca lo lista bajo ninguna fila. Reusa openGrupoDialog/
 * deleteGrupo tal cual (sin cambios): openGrupoDialog(undefined, g) ya es idéntico al flujo de
 * edición existente cuando se pasa un grupo.
 */
describe('AsignaturasTabComponent — sección de grupos sin asignatura', () => {
  let state: StateService;
  let fixture: ReturnType<typeof TestBed.createComponent<AsignaturasTabComponent>>;
  let component: AsignaturasTabComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AsignaturasTabComponent],
      providers: [provideNoopAnimations()],
    });
    fixture = TestBed.createComponent(AsignaturasTabComponent);
    component = fixture.componentInstance;
    state = TestBed.inject(StateService);
    state.setAsignaturas([asignatura()]);
  });

  it('sección oculta cuando no hay grupos huérfanos', () => {
    state.grupos.set([grupo({ id: 'g1' })]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('sin asignatura válida');
  });

  it('muestra una fila por cada grupo huérfano', () => {
    state.grupos.set([grupo({ id: 'g1', asignaturaId: 'a-borrada', nombre: 'Huérfano 1' })]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('1 grupo(s) cuya asignatura fue eliminada');
    expect(fixture.nativeElement.textContent).toContain('Huérfano 1');
  });

  it('"Editar" en un huérfano llama a openGrupoDialog(undefined, g)', () => {
    const huerfano = grupo({ id: 'g1', asignaturaId: 'a-borrada' });
    state.grupos.set([huerfano]);
    const spy = vi.spyOn(component, 'openGrupoDialog').mockImplementation(() => {});
    fixture.detectChanges();

    fixture.nativeElement.querySelector('.tabla-huerfanos .ic-edit').click();

    expect(spy).toHaveBeenCalledWith(undefined, huerfano);
  });

  it('"Eliminar" en un huérfano llama a deleteGrupo(g)', () => {
    const huerfano = grupo({ id: 'g1', asignaturaId: 'a-borrada' });
    state.grupos.set([huerfano]);
    const spy = vi.spyOn(component, 'deleteGrupo').mockImplementation(() => {});
    fixture.detectChanges();

    fixture.nativeElement.querySelector('.tabla-huerfanos .ic-del').click();

    expect(spy).toHaveBeenCalledWith(huerfano);
  });
});

/**
 * onFileSelected extraía el mensaje de error del import de Excel con lógica ad-hoc
 * (err?.error?.detail ?? err?.error ?? err?.message) en vez de reusar mensajeErrorHttp (ya
 * importado en este archivo) — un ProblemDetails con solo `.title` (sin `.detail`, formato
 * habitual de ASP.NET para errores de validación de columnas) caía en `err?.error`, un objeto,
 * que al interpolarse en el string del snackbar produce literalmente "[object Object]".
 */
describe('AsignaturasTabComponent — mensaje de error al importar Excel', () => {
  let state: StateService;
  let component: AsignaturasTabComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AsignaturasTabComponent],
      providers: [provideNoopAnimations()],
    });
    const fixture = TestBed.createComponent(AsignaturasTabComponent);
    component = fixture.componentInstance;
    state = TestBed.inject(StateService);
    state.setAsignaturas([]);
  });

  function importar(errorBody: any) {
    vi.spyOn(component.persistencia, 'importarExcel').mockReturnValue(throwError(() => errorBody));
    const openSpy = vi.spyOn(component.snackBar, 'open').mockReturnValue({} as any);
    const file = new File(['contenido'], 'curriculum.xlsx');
    const input = { files: [file], value: '' } as unknown as HTMLInputElement;
    component.onFileSelected({ target: input } as unknown as Event);
    return openSpy;
  }

  it('un ProblemDetails con solo title (sin detail) no produce "[object Object]" en el snackbar', () => {
    const openSpy = importar({ error: { title: 'Formato de columna inválido' } });

    const mensaje = openSpy.mock.calls[0][0] as string;
    expect(mensaje).not.toContain('[object Object]');
    expect(mensaje).toContain('Formato de columna inválido');
  });

  it('un cuerpo de error string plano se muestra tal cual', () => {
    const openSpy = importar({ error: 'Archivo vacío.' });

    expect(openSpy.mock.calls[0][0]).toContain('Archivo vacío.');
  });
});
