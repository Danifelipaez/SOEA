import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { avisosDocentePorSesion, EditarSesionDialogComponent } from './horario.component';
import { Asignatura, Docente, Grupo, Sesion } from '../../core/models';
import { StateService } from '../../core/state.service';

/**
 * Horario generado real (2026-09-29): el docente está fuera de la generación (CR-08), así que salió
 * un docente en dos aulas a la vez y 72/79 clases fuera de su disponibilidad sin ningún aviso.
 */
const asignaturas = [{ id: 'a1', nombre: 'Química General' }, { id: 'a2', nombre: 'Bioquímica' }] as Asignatura[];
const grupos = [{ id: 'g1', nombre: 'Grupo 1' }, { id: 'g2', nombre: 'Grupo 2' }] as Grupo[];
const docente = (disponibilidad: Record<string, unknown> = {}): Docente =>
  ({ id: 'd1', nombre: 'Alex Chimenty', cedula: '', maxHoras: 40, disponibilidad });
const sesion = (o: Partial<Sesion> = {}): Sesion => ({
  id: 's1', asignaturaId: 'a1', grupoId: 'g1', docenteId: 'd1', dia: 'jueves', horaInicio: '15:00', horaFin: '17:00',
  duracionHoras: 2, espacioId: 'e1', virtual: false, alternancia: 'SinAlternancia', ...o,
});
const otra = sesion({ id: 's2', asignaturaId: 'a2', grupoId: 'g2', horaInicio: '16:00', horaFin: '18:00', espacioId: 'e2' });

describe('avisosDocentePorSesion', () => {
  it('avisa el choque en las dos sesiones, nombrando asignatura y grupo de la otra', () => {
    const av = avisosDocentePorSesion([sesion(), otra], [docente()], asignaturas, grupos);
    expect(av.get('s1')?.choques).toEqual(['Alex Chimenty tiene otra clase a la misma hora: Bioquímica · Grupo 2 (jueves 16:00–18:00).']);
    expect(av.get('s2')?.choques[0]).toContain('Química General · Grupo 1 (jueves 15:00–17:00)');
  });

  it('no avisa clases consecutivas, de otro día ni la contraparte virtual de la misma sesión', () => {
    const av = avisosDocentePorSesion([
      sesion(),
      sesion({ id: 's2', horaInicio: '17:00', horaFin: '19:00' }),
      sesion({ id: 's3', dia: 'viernes' }),
      sesion({ virtual: true, esContraparteVirtual: true, espacioId: undefined }),
    ], [docente()], asignaturas, grupos);
    expect([...av.values()].flatMap(a => a.choques)).toEqual([]);
  });

  it('avisa fuera de disponibilidad: día cerrado, franja específica y franja general', () => {
    const casos: [Record<string, unknown>, string | undefined][] = [
      [{ jueves: { noDisponible: true } }, 'Fuera de la disponibilidad de Alex Chimenty: marcó el jueves como no disponible.'],
      [{ jueves: { noDisponible: false, tipo: 'Franja específica', desde: '14:00', hasta: '16:00' } }, 'Fuera de la disponibilidad de Alex Chimenty: el jueves solo está disponible de 14:00 a 16:00.'],
      [{ jueves: { noDisponible: false, tipo: 'Franja general', franjaGeneral: 'Matutino (06:00–12:00)' } }, 'Fuera de la disponibilidad de Alex Chimenty: el jueves solo está disponible de 06:00 a 12:00.'],
      [{ jueves: { noDisponible: false, tipo: 'Franja específica', desde: '14:00', hasta: '18:00' } }, undefined],
      [{ jueves: { noDisponible: false, tipo: 'Franja general', franjaGeneral: 'Todo el día (06:00–22:00)' } }, undefined],
      [{ lunes: { noDisponible: true } }, undefined], // el jueves no se declaró: sin restricción
    ];
    for (const [disp, esperado] of casos)
      expect(avisosDocentePorSesion([sesion()], [docente(disp)], asignaturas, grupos).get('s1')?.fueraDeDisponibilidad).toBe(esperado);
  });
});

describe('EditarSesionDialogComponent — avisos del docente', () => {
  function crear(editada: Sesion, sesiones: Sesion[], docentes: Docente[]) {
    TestBed.configureTestingModule({
      imports: [EditarSesionDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        {
          provide: MAT_DIALOG_DATA,
          useValue: {
            merged: { ...editada, key: editada.id, sesiones: [editada] },
            sesion: editada, asignaturas, docentes, espacios: [], sesiones,
            programaById: new Map(), facultadById: new Map(),
          },
        },
      ],
    });
    TestBed.inject(StateService).grupos.set(grupos);
    return TestBed.createComponent(EditarSesionDialogComponent).componentInstance;
  }
  const bloqueoDocente = (c: EditarSesionDialogComponent) => c.validaciones().find(v => v.texto.includes('ya tiene otra sesión'));

  it('el choque que la sesión ya traía de la generación se avisa y no bloquea', () => {
    const c = crear(sesion(), [sesion(), otra], [docente({ jueves: { noDisponible: true } })]);
    expect(bloqueoDocente(c)).toBeUndefined();
    expect(c.avisosDocente()).toEqual([
      'Alex Chimenty tiene otra clase a la misma hora: Bioquímica · Grupo 2 (jueves 16:00–18:00).',
      'Fuera de la disponibilidad de Alex Chimenty: marcó el jueves como no disponible.',
    ]);
  });

  it('un choque que introduce la edición sí bloquea (igual que el 409 del servidor)', () => {
    const c = crear(sesion({ horaInicio: '08:00', horaFin: '10:00' }), [sesion({ horaInicio: '08:00', horaFin: '10:00' }), otra], [docente()]);
    expect(bloqueoDocente(c)).toBeUndefined();
    c.horaInicio.set('16:00');
    expect(bloqueoDocente(c)?.ok).toBe(false);
    expect(c.avisosDocente()).toEqual([]); // ya lo muestra la validación: sin duplicarlo como aviso
  });
});
