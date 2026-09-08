import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { EditarSesionDialogComponent } from './horario.component';
import { Asignatura, Docente, Espacio, Grupo, Sesion } from '../../core/models';

/**
 * G7 (bug reportado "conflicto consigo misma al editar solo el docente"): validaciones() no
 * excluía la fila de la semana opuesta del mismo par de alternancia — dos sesiones que jamás
 * coexisten en una semana real (A vs B) se reportaban como conflicto de espacio/docente. Ver
 * nuncaCoexiste() en horario.component.ts.
 */
describe('EditarSesionDialogComponent — validaciones() y semana', () => {
  const sesion = (overrides: Partial<Sesion> = {}): Sesion => ({
    id: 's1', asignaturaId: 'a1', grupoId: 'g1', dia: 'lunes',
    horaInicio: '08:00', horaFin: '10:00', duracionHoras: 2,
    espacioId: 'e1', virtual: false, alternancia: 'TipoA', semana: 'A',
    ...overrides,
  });

  function crear(sesionEditada: Sesion, otras: Sesion[]) {
    const asignaturas: Asignatura[] = [];
    const docentes: Docente[] = [];
    const espacios: Espacio[] = [{ id: 'e1', nombre: 'Salón 1', capacidad: 30, tipo: 'Salón' }];
    const grupos: Grupo[] = [];
    TestBed.configureTestingModule({
      imports: [EditarSesionDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        {
          provide: MAT_DIALOG_DATA,
          useValue: {
            merged: {
              key: sesionEditada.id, sesiones: [sesionEditada], dia: sesionEditada.dia,
              horaInicio: sesionEditada.horaInicio, horaFin: sesionEditada.horaFin, duracionSlots: 2,
              virtual: sesionEditada.virtual, alternancia: sesionEditada.alternancia,
              semana: sesionEditada.semana, asignaturaId: sesionEditada.asignaturaId,
              grupoId: sesionEditada.grupoId, docenteId: sesionEditada.docenteId,
              espacioId: sesionEditada.espacioId,
            },
            sesion: sesionEditada,
            asignaturas, docentes, espacios,
            sesiones: [sesionEditada, ...otras],
            programaById: new Map(), facultadById: new Map(),
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(EditarSesionDialogComponent);
    return fixture.componentInstance;
  }

  const conflictoEspacio = (c: { ok: boolean; texto: string }[]) =>
    c.find(x => x.texto.includes('ocupado') || x.texto.includes('libre'));

  it('no reporta conflicto de espacio con la sesión de la semana opuesta del mismo par de alternancia', () => {
    const editada = sesion({ semana: 'A' });
    const opuesta = sesion({ id: 's2', asignaturaId: 'a2', grupoId: 'g2', semana: 'B' });
    const component = crear(editada, [opuesta]);

    const check = conflictoEspacio(component.validaciones());
    expect(check?.ok).toBe(true);
  });

  it('sí reporta conflicto de espacio cuando ambas sesiones son de la misma semana', () => {
    const editada = sesion({ semana: 'A' });
    const mismaSemana = sesion({ id: 's2', asignaturaId: 'a2', grupoId: 'g2', semana: 'A' });
    const component = crear(editada, [mismaSemana]);

    const check = conflictoEspacio(component.validaciones());
    expect(check?.ok).toBe(false);
  });

  it('sí reporta conflicto de espacio cuando la otra sesión no tiene semana asignada', () => {
    const editada = sesion({ semana: 'A' });
    const sinSemana = sesion({ id: 's2', asignaturaId: 'a2', grupoId: 'g2', semana: undefined });
    const component = crear(editada, [sinSemana]);

    const check = conflictoEspacio(component.validaciones());
    expect(check?.ok).toBe(false);
  });
});
