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
      docenteId: 'd1',
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

    // G1 (bug reportado "docente no se importa"): sin esto, toda sesión generada nace con
    // docenteId null aunque el grupo tenga docente en BD.
    expect(body.grupos?.[0].docenteId).toBe('d1');

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

/**
 * manejarError trataba CUALQUIER `err.error` de tipo objeto como el payload 422 real
 * (GenerarHorarioResponse con EsFactible=false) — pero un fallo de red real (backend caído,
 * CORS) también llega con status 0 y `err.error` como un ProgressEvent, que también es
 * `typeof === 'object'`. Sin distinguir por status, un caso de red se reenvía como si fuera
 * el 422 real; horario.component.ts no encuentra .mensajeError/.message/.error en un
 * ProgressEvent y cae a "Error desconocido" — indistinguible de un 422 mal formado.
 */
describe('HorarioApiService — manejarError', () => {
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

  it('un 422 real (payload GenerarHorarioResponse) sigue pasando el objeto tal cual', () => {
    let recibido: any;
    service.generarHorario([], [], []).subscribe({ error: (e) => (recibido = e) });

    const req = httpMock.expectOne(r => r.url.endsWith('/horario/generar'));
    req.flush(
      { esFactible: false, mensajeError: 'No se encontró un horario factible.' },
      { status: 422, statusText: 'Unprocessable Entity' }
    );

    expect(recibido).toEqual({ esFactible: false, mensajeError: 'No se encontró un horario factible.' });
  });

  it('un error de red real (status 0) produce un Error con mensaje distinguible, no el ProgressEvent crudo', () => {
    let recibido: any;
    service.generarHorario([], [], []).subscribe({ error: (e) => (recibido = e) });

    const req = httpMock.expectOne(r => r.url.endsWith('/horario/generar'));
    req.error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });

    expect(recibido).toBeInstanceOf(Error);
    expect(recibido.mensajeError).toBeUndefined();
    // No debe colapsar en el genérico indistinguible del 422 real mal formado.
    expect(recibido.message).not.toBe('Error desconocido al conectar con el API.');
    expect(recibido.message).toMatch(/conectar|conexión|servidor/i);
  });
});

/**
 * P6: rehidrata la grilla de /horario tras un reload leyendo el horario ya persistido en vez de
 * dejarla vacía. Un 404 (aún no se generó ningún horario para el semestre) es un estado normal,
 * no un error — nunca debe llegar como `error` al subscriber.
 */
describe('HorarioApiService — obtenerActual', () => {
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

  it('devuelve el horario cuando el backend tiene uno persistido para el semestre', () => {
    let recibido: any;
    service.obtenerActual('2026-1').subscribe(r => (recibido = r));

    const req = httpMock.expectOne(r => r.url.endsWith('/horario/actual') && r.params.get('semestre') === '2026-1');
    req.flush({ horarioId: 'h1', semestre: '2026-1', esFactible: true, puntajeFitness: 100, generaciones: 0, sesiones: [] });

    expect(recibido.horarioId).toBe('h1');
  });

  it('un 404 (aún no se ha generado ningún horario) se resuelve como null, no como error', () => {
    let recibido: any = 'sin-resolver';
    let fallo = false;
    service.obtenerActual('2026-1').subscribe({ next: r => (recibido = r), error: () => (fallo = true) });

    const req = httpMock.expectOne(r => r.url.endsWith('/horario/actual'));
    req.flush(null, { status: 404, statusText: 'Not Found' });

    expect(fallo).toBe(false);
    expect(recibido).toBeNull();
  });
});
