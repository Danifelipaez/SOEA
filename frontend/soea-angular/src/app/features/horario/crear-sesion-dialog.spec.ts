import { vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { CrearSesionDialogComponent } from './horario.component';
import { StateService } from '../../core/state.service';
import { PersistenciaService } from '../../core/persistencia.service';
import { Asignatura, Grupo } from '../../core/models';

/**
 * P0-3/P0-5 auditoría: el mismo diálogo crea una sesión manual (al horario vigente, que exige
 * horarioId) o una sesión fija para un horario base (se devuelve sin tocar el servidor, con grupo).
 */
describe('CrearSesionDialogComponent', () => {
  let persistencia: { crearSesionManual: ReturnType<typeof vi.fn> };
  let dialogRef: { close: ReturnType<typeof vi.fn> };

  function abrir(modoFija: boolean): CrearSesionDialogComponent {
    persistencia = { crearSesionManual: vi.fn().mockReturnValue({ subscribe: () => {} }) };
    dialogRef = { close: vi.fn() };
    const asignatura = { id: 'a1', nombre: 'Cálculo I', programaId: 'p1', sesionesTeoriaPresencialSemana: 0,
      sesionesTeoriaVirtualSemana: 1, horasTeoriaVirtual: 2, sesionesLaboratorioSemana: 0 } as Asignatura;
    const grupo = { id: 'g1', nombre: 'G1', asignaturaId: 'a1' } as Grupo;
    TestBed.configureTestingModule({
      imports: [CrearSesionDialogComponent],
      providers: [
        provideNoopAnimations(),
        { provide: PersistenciaService, useValue: persistencia },
        { provide: MatDialogRef, useValue: dialogRef },
        {
          provide: MAT_DIALOG_DATA,
          useValue: { asignaturas: [asignatura], docentes: [], espacios: [], grupos: [grupo], sesiones: [], programaById: new Map(), modoFija },
        },
      ],
    });
    const c = TestBed.createComponent(CrearSesionDialogComponent).componentInstance;
    // Teoría virtual (la única que ofrece la asignatura): no necesita aula.
    c.asignaturaId = 'a1'; c.onAsignaturaChange('a1');
    c.grupoId = 'g1'; c.onGrupoChange();
    c.dia = 'martes'; c.horaInicio = '08:00'; c.recheck();
    return c;
  }

  it('en modo fija devuelve la sesión con su grupo y no llama al servidor', () => {
    const c = abrir(true);

    c.crear();

    expect(persistencia.crearSesionManual).not.toHaveBeenCalled();
    expect(dialogRef.close).toHaveBeenCalledWith([expect.objectContaining({
      asignaturaId: 'a1', grupoId: 'g1', dia: 'martes', horaInicio: '08:00', horaFin: '10:00',
      duracionHoras: 2, virtual: true, tipoFlujo: 'AulaVirtual', docenteId: undefined,
    })]);
  });

  it('sin horario generado muestra el error y no llama al servidor', () => {
    const c = abrir(false);

    c.crear();

    expect(persistencia.crearSesionManual).not.toHaveBeenCalled();
    expect(c.errorServidor()).toMatch(/genere el horario/i);
  });

  it('con horario generado envía el horarioId y docente null si el grupo no tiene', () => {
    const c = abrir(false);
    TestBed.inject(StateService).horarioId.set('h1');

    c.crear();

    expect(persistencia.crearSesionManual).toHaveBeenCalledWith(
      expect.objectContaining({ horarioId: 'h1', grupoId: 'g1', docenteId: null, espacioId: null }));
  });
});
