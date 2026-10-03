import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { avisosDocentePorSesion, choquesEspacioPorSesion, EditarSesionDialogComponent, fueraDisponibilidadGrupo } from './horario.component';
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

describe('fueraDisponibilidadGrupo', () => {
  const conDisp = (json: unknown) => [{ id: 'g1', nombre: 'Grupo 1', disponibilidadUiJson: json === undefined ? undefined : JSON.stringify(json) }] as Grupo[];

  it('día sin entrada = cerrado; JSON vacío o ausente = sin restricción (espejo del backend)', () => {
    const casos: [unknown, string | undefined][] = [
      [{ lunes: { noDisponible: false, tipo: 'Franja específica', desde: '06:00', hasta: '08:00' } }, 'el grupo no está disponible el jueves'],
      [{ jueves: { noDisponible: true } }, 'el grupo no está disponible el jueves'],
      [{ jueves: { noDisponible: false, tipo: 'Franja específica', desde: '15:00', hasta: '16:00' } }, 'el grupo solo está disponible el jueves de 15:00 a 16:00'],
      [{ jueves: { noDisponible: false, tipo: 'Franja específica', desde: '15:00', hasta: '17:00' } }, undefined],
      [{}, undefined],
      [undefined, undefined],
    ];
    for (const [json, esperado] of casos)
      expect(fueraDisponibilidadGrupo([sesion()], conDisp(json)).get('s1')).toBe(esperado);
  });

  it('ignora la contraparte virtual derivada', () => {
    const disp = conDisp({ lunes: { noDisponible: true } });
    expect(fueraDisponibilidadGrupo([sesion({ esContraparteVirtual: true })], disp).size).toBe(0);
  });
});

describe('choquesEspacioPorSesion (modo borrador)', () => {
  const espacios = [{ id: 'e1', nombre: 'Lab 1', tipo: 'Laboratorio', capacidad: 30 }] as any[];

  it('avisa dos clases presenciales solapadas en el mismo espacio, en ambas sesiones', () => {
    const ch = choquesEspacioPorSesion([sesion(), sesion({ id: 's2', grupoId: 'g2', horaInicio: '16:00', horaFin: '18:00' })], espacios, asignaturas, grupos);
    expect(ch.get('s1')?.choques[0]).toBe('Lab 1 ya está ocupado a esa hora por Química General · Grupo 2 (jueves 16:00–18:00).');
    expect(ch.get('s2')?.conIds).toEqual(['s1']);
  });

  it('no avisa consecutivas, otro espacio, virtuales ni semanas que nunca coinciden', () => {
    const ch = choquesEspacioPorSesion([
      sesion({ semana: 'A' }),
      sesion({ id: 's2', horaInicio: '17:00', horaFin: '19:00' }),
      sesion({ id: 's3', espacioId: 'e2' }),
      sesion({ id: 's4', virtual: true }),
      sesion({ id: 's5', semana: 'B' }),
    ], espacios, asignaturas, grupos);
    expect([...ch.values()].flatMap(a => a.choques)).toEqual([]);
  });
});
