import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CIERRE_CHOQUES_AULAS, choquesAulasDetalle, mensajeInfactibilidadAmigable } from './horario.reglas';
import { HorarioApiService, GenerarHorarioRequest } from '../../core/horario-api.service';
import { Asignatura, ChoqueAulas, Grupo } from '../../core/models';

/**
 * Caso de producción (2026-10-28): el miércoles de 08:00 a 09:00 coincidían tres clases y había dos
 * salones. El aviso decía "Grupo(s) en conflicto: …" sin la hora, y nada avisaba antes de generar.
 */
const asig = (id: string, programaId: string): Asignatura =>
  ({ id, programaId, nombre: id, codigo: id } as Asignatura);
const grupo = (id: string, asignaturaId: string, nombre: string): Grupo =>
  ({ id, asignaturaId, programaId: '', nombre, estudiantesInscritos: 30 });

const grupos = [
  grupo('g1', 'fun', 'FUNDAMENTOS QUIMICA - Grupo 1'),
  grupo('g2', 'bio', 'BIOQUIMICA - Grupo 1'),
  grupo('g3', 'qg', 'QUIMICA GENERAL - Grupo 1'),
];
const catalogo = {
  asignaturas: [asig('fun', 'lq'), asig('bio', 'pes'), asig('qg', 'ing')],
  programaById: new Map([['lq', { nombre: 'LIC QUIMICA' }], ['pes', { nombre: 'INGENIERIA PESQUERA' }], ['ing', { nombre: 'FACULTAD DE INGENIERIA' }]]),
};
const choque: ChoqueAulas = { dia: 'miercoles', horaInicio: '08:00', horaFin: '09:00', clases: 3, aulas: 2, grupoIds: ['g1', 'g2', 'g3'] };

describe('choquesAulasDetalle', () => {
  it('nombra día, hora, conteos y cada grupo con su programa', () => {
    expect(choquesAulasDetalle([choque], grupos, catalogo)).toEqual([
      'Miércoles 08:00–09:00: 3 clases y solo 2 aulas — FUNDAMENTOS QUIMICA - Grupo 1 (LIC QUIMICA), ' +
      'BIOQUIMICA - Grupo 1 (INGENIERIA PESQUERA), QUIMICA GENERAL - Grupo 1 (FACULTAD DE INGENIERIA)',
    ]);
  });

  it('un grupo borrado después no rompe el aviso', () => {
    expect(choquesAulasDetalle([{ ...choque, grupoIds: ['borrado'] }], grupos, catalogo)[0]).toContain('un grupo que ya no existe');
  });
});

describe('mensajeInfactibilidadAmigable — motivo Espacio con choques', () => {
  it('usa la hora concreta en vez de la lista suelta de grupos', () => {
    const msg = mensajeInfactibilidadAmigable('Espacio', ['g1', 'g2', 'g3'], grupos, 3, 2, { espacios: [], ...catalogo }, [choque]);
    expect(msg).toContain('Miércoles 08:00–09:00: 3 clases y solo 2 aulas');
    expect(msg).toContain(CIERRE_CHOQUES_AULAS);
    expect(msg).not.toContain('Grupo(s) en conflicto');
  });

  it('sin choques sigue el mensaje de antes', () => {
    expect(mensajeInfactibilidadAmigable('Espacio', ['g1'], grupos, 3, 2)).toContain('Grupo(s) en conflicto');
  });
});

describe('HorarioApiService.choquesAulas', () => {
  it('envía a /horario/choques-aulas la misma petición que /horario/generar', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const service = TestBed.inject(HorarioApiService);
    const http = TestBed.inject(HttpTestingController);
    const args: Parameters<HorarioApiService['generarHorario']> = [catalogo.asignaturas, [],
      [{ id: 'e1', nombre: 'QUI', capacidad: 30, tipo: 'Salón' }], undefined, '2026-2', undefined, grupos];

    let recibido: ChoqueAulas[] | undefined;
    service.choquesAulas(...args).subscribe(c => recibido = c);
    service.generarHorario(...args).subscribe();

    const previo = http.expectOne(r => r.url.endsWith('/horario/choques-aulas'));
    const generar = http.expectOne(r => r.url.endsWith('/horario/generar'));
    expect(previo.request.method).toBe('POST');
    expect(previo.request.body as GenerarHorarioRequest).toEqual(generar.request.body);
    previo.flush([choque]);
    expect(recibido).toEqual([choque]);
    http.verify();
  });
});
