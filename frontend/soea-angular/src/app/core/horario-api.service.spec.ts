import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { HorarioApiService, GenerarHorarioRequest } from './horario-api.service';
import { Asignatura, ConfiguracionAlgoritmo, Docente, Espacio, Grupo } from './models';

/**
 * Test de contrato: el body HTTP de /horario/generar debe llevar TODOS los campos que el
 * backend (GenerarHorarioRequest.cs) espera. Existe porque el mapeo perdió requisitosEspacio
 * en silencio durante meses sin que ningún test lo detectara (raíz del bug "todo cae en
 * Salon 201") — un campo nuevo que nadie mapee aquí debe romper este test, no descubrirse
 * en producción.
 */
describe('HorarioApiService — contrato con /horario/generar', () => {
  let service: HorarioApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(HorarioApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('envía requisitosEspacio del grupo, la ventana horaria de la asignatura y los pesos del GA', () => {
    const asignaturas: Asignatura[] = [{
      id: 'a1', codigo: 'C1', nombre: 'Bioquímica', alternancia: 'SinAlternancia',
      sesionesTeoriaPresencialSemana: 1, horasTeoriaPresencial: 2,
      sesionesTeoriaVirtualSemana: 0, horasTeoriaVirtual: 2,
      sesionesLaboratorioSemana: 0, horasLaboratorio: 2,
      sesionesLaboratorioSemestre: 0, programaId: 'p1',
      horaInicioMin: '08:00', horaFinMax: '12:00',
    }];
    const docentes: Docente[] = [];
    const espacios: Espacio[] = [{ id: 'e1', nombre: 'Lab Química', capacidad: 30, tipo: 'Laboratorio' }];
    const grupos: Grupo[] = [{
      id: 'g1', asignaturaId: 'a1', nombre: 'Grupo 1', estudiantesInscritos: 20, programaId: 'p1',
      requisitosEspacio: [
        { tipoSesion: 'TeoriaPresencial', espacioId: 'e1', tipoEspacio: 'Laboratorio', sesiones: 1 },
      ],
    }];
    const config: ConfiguracionAlgoritmo = {
      pobSize: 50, mutRate: 0.05, crossRate: 0.8, maxGen: 200,
      pesoErgo: 3, pesoTiempos: 2, pesoAlm: 3,
      pesoBalanceSemanas: 2, pesoPresencialFirst: 4, semilla: 2026,
    };

    service.generarHorario(asignaturas, docentes, espacios, config, '2026-1', undefined, grupos)
      .subscribe();

    const req = httpMock.expectOne(r => r.url.endsWith('/horario/generar'));
    const body = req.request.body as GenerarHorarioRequest;

    // El bug reportado: sin esto, RequisitosEspacio siempre llega vacío al backend y toda
    // sesión presencial cae en el primer espacio disponible del run.
    expect(body.grupos?.[0].requisitosEspacio).toEqual(grupos[0].requisitosEspacio);

    // HC-VH: sin esto, la ventana horaria de la asignatura es una restricción dura muerta.
    expect(body.asignaturas[0].horaInicioMin).toBe('08:00');
    expect(body.asignaturas[0].horaFinMax).toBe('12:00');

    // El backend lee PesoMaxHorasSeguidas, no PesoAlmuerzo — y nunca recibía semilla/balance/presencial-first.
    expect(body.configuracion?.pesoMaxHorasSeguidas).toBe(3);
    expect(body.configuracion?.pesoBalanceSemanas).toBe(2);
    expect(body.configuracion?.pesoPresencialFirst).toBe(4);
    expect(body.configuracion?.semilla).toBe(2026);

    req.flush({
      horarioId: 'h1', semestre: '2026-1', esFactible: true,
      puntajeFitness: 0, generaciones: 0, sesiones: [],
    });
  });

  it('con un grupo sin requisitosEspacio, envía una lista vacía en vez de omitir el campo', () => {
    const asignaturas: Asignatura[] = [];
    const grupos: Grupo[] = [{
      id: 'g1', asignaturaId: 'a1', nombre: 'Grupo 1', estudiantesInscritos: 20, programaId: 'p1',
    }];

    service.generarHorario(asignaturas, [], [], undefined, '2026-1', undefined, grupos).subscribe();

    const req = httpMock.expectOne(r => r.url.endsWith('/horario/generar'));
    const body = req.request.body as GenerarHorarioRequest;

    expect(body.grupos?.[0].requisitosEspacio).toEqual([]);

    req.flush({
      horarioId: 'h1', semestre: '2026-1', esFactible: true,
      puntajeFitness: 0, generaciones: 0, sesiones: [],
    });
  });
});
