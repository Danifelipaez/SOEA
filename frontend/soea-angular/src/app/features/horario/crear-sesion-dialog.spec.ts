import { vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { CrearSesionDialogComponent } from './crear-sesion-dialog.component';
import { StateService } from '../../core/state.service';
import { PersistenciaService } from '../../core/persistencia.service';
import { Asignatura, Docente, Grupo } from '../../core/models';

/**
 * P0-5 auditoría: la sesión manual se agrega al horario vigente (exige horarioId). También abre ya en un
 * grupo y tipo para «Elegir hora» de una clase pendiente de ubicar, y bloquea lo que el servidor rechazaría.
 */
describe('CrearSesionDialogComponent', () => {
  let persistencia: { crearSesionManual: ReturnType<typeof vi.fn> };
  let dialogRef: { close: ReturnType<typeof vi.fn> };

  function abrir(docentes: Docente[] = [], disponibilidadGrupo?: object): CrearSesionDialogComponent {
    persistencia = { crearSesionManual: vi.fn().mockReturnValue({ subscribe: () => {} }) };
    dialogRef = { close: vi.fn() };
    const asignatura = { id: 'a1', nombre: 'Cálculo I', programaId: 'p1', sesionesTeoriaPresencialSemana: 0,
      sesionesTeoriaVirtualSemana: 1, horasTeoriaVirtual: 2, sesionesLaboratorioSemana: 0 } as Asignatura;
    const grupo = { id: 'g1', nombre: 'G1', asignaturaId: 'a1', docenteId: docentes[0]?.id,
      disponibilidadUiJson: disponibilidadGrupo && JSON.stringify(disponibilidadGrupo) } as Grupo;
    TestBed.configureTestingModule({
      imports: [CrearSesionDialogComponent],
      providers: [
        provideNoopAnimations(),
        { provide: PersistenciaService, useValue: persistencia },
        { provide: MatDialogRef, useValue: dialogRef },
        {
          provide: MAT_DIALOG_DATA,
          useValue: { asignaturas: [asignatura], docentes, espacios: [], grupos: [grupo], sesiones: [], programaById: new Map(),
            prefill: { asignaturaId: 'a1', grupoId: 'g1', tipo: 'TeoriaVirtual' } },
        },
      ],
    });
    const c = TestBed.createComponent(CrearSesionDialogComponent).componentInstance;
    // Abre ya en el grupo y en teoría virtual (no necesita aula): solo falta la hora.
    c.dia = 'martes'; c.horaInicio = '08:00'; c.recheck();
    return c;
  }

  it('bloquea una hora fuera de la disponibilidad del grupo', () => {
    const c = abrir([], { martes: { tipo: 'Franja específica', desde: '14:00', hasta: '18:00' } });

    expect(c.checksOk()).toBe(false);
    expect(c.checks()[0].texto).toContain('Fuera del horario del grupo');

    c.horaInicio = '14:00'; c.recheck();
    expect(c.checksOk()).toBe(true);
  });

  it('sin horario generado muestra el error y no llama al servidor', () => {
    const c = abrir();

    c.crear();

    expect(persistencia.crearSesionManual).not.toHaveBeenCalled();
    expect(c.errorServidor()).toMatch(/genere el horario/i);
  });

  it('con horario generado envía el horarioId y docente null si el grupo no tiene', () => {
    const c = abrir();
    TestBed.inject(StateService).horarioId.set('h1');

    c.crear();

    expect(persistencia.crearSesionManual).toHaveBeenCalledWith(
      expect.objectContaining({ horarioId: 'h1', grupoId: 'g1', docenteId: null, espacioId: null }));
  });

  it('avisa si la clase cae fuera de la disponibilidad del docente del grupo, sin bloquear', () => {
    const disponibilidad = { martes: { noDisponible: false, tipo: 'Franja específica', desde: '14:00', hasta: '18:00' } };
    const c = abrir([{ id: 'd1', nombre: 'Ana Pérez', cedula: '', maxHoras: 40, disponibilidad }]);

    expect(c.avisos()).toEqual(['Fuera de la disponibilidad de Ana Pérez: el martes solo está disponible de 14:00 a 18:00.']);
    expect(c.checksOk()).toBe(true);

    c.horaInicio = '14:00'; c.recheck();
    expect(c.avisos()).toEqual([]);
  });
});
