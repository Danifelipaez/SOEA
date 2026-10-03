import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { ImportRevisionDialogComponent } from './import-revision-dialog.component';
import { FilaCurriculum, PersistenciaService, RevisionImport } from '../../core/persistencia.service';

const fila = (n: number, o: Partial<FilaCurriculum> = {}): FilaCurriculum => ({
  fila: n, facultad: 'EDUCACION', programa: 'LIC QUIMICA', asignatura: 'FUNDAMENTOS QUIMICA', codigo: '', tipoEspacio: '',
  espacio: 'QUI', duracion: '2', dia: 'MIERCOLES', hora: '06:00', final: '08:00', docente: 'ANA', grupo: '1', ...o,
});

function crear(revision: RevisionImport, persistencia: Partial<PersistenciaService>) {
  const close = vi.fn();
  TestBed.configureTestingModule({
    imports: [ImportRevisionDialogComponent],
    providers: [
      { provide: MAT_DIALOG_DATA, useValue: revision },
      { provide: MatDialogRef, useValue: { close } },
      { provide: PersistenciaService, useValue: persistencia },
    ],
  });
  return { c: TestBed.createComponent(ImportRevisionDialogComponent).componentInstance, close };
}

describe('ImportRevisionDialogComponent', () => {
  const revision: RevisionImport = {
    filas: [fila(2, { duracion: '3' }), fila(3, { facultad: '' }), fila(4)],
    incoherencias: [
      { fila: 2, campo: 'duracion', esError: true, mensaje: 'La duración dice 3 h…' },
      { fila: 3, campo: 'facultad', esError: true, mensaje: 'Falta la facultad.' },
    ],
    avisos: [],
  };

  it('muestra solo las filas con problemas; editar o borrar obliga a comprobar antes de importar', () => {
    const revisarFilas = vi.fn().mockReturnValue(of({ filas: [], incoherencias: [], avisos: [] }));
    const { c } = crear(revision, { revisarFilas });
    expect(c.filasConProblemas().map(f => f.fila)).toEqual([2, 3]);
    expect(c.errores()).toBe(2);

    c.editar(2, 'duracion', '2');
    c.borrar(3);
    expect(c.sucio()).toBe(true);
    expect(c.borradas()).toEqual([3]);

    c.comprobar();
    expect(revisarFilas).toHaveBeenCalledWith([fila(2), fila(4)]);
    expect(c.errores()).toBe(0);
    expect(c.sucio()).toBe(false);
    expect(c.filasConProblemas().map(f => f.fila)).toEqual([2]); // sigue visible, ya corregida
  });

  it('deshacer recupera las filas borradas sin perder las ediciones', () => {
    const { c } = crear(revision, {});
    c.editar(2, 'duracion', '2');
    c.borrar(3);
    c.deshacerBorrado();
    expect(c.filas()).toEqual([fila(2), fila(3, { facultad: '' }), fila(4)]);
  });

  it('si el servidor rechaza el import (422), muestra los nuevos problemas en vez de cerrar', () => {
    const importarFilas = vi.fn().mockReturnValue(throwError(() => ({
      status: 422, error: { incoherencias: [{ fila: 4, campo: '', esError: true, mensaje: 'Fila repetida' }] },
    })));
    const { c, close } = crear(revision, { importarFilas });
    c.importar();
    expect(close).not.toHaveBeenCalled();
    expect(c.incoherenciasDe(4)[0].mensaje).toBe('Fila repetida');
  });
});
