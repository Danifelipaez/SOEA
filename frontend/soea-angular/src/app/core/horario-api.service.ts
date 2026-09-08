import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { Asignatura, ConfiguracionAlgoritmo, Docente, Espacio, Grupo, HorarioBase, RequisitoEspacio, Sesion } from './models';
import { environment } from '../../environments/environment';

// ── Tipos del contrato con la API ──────────────────────────────────────────────

export interface ConfiguracionAlgoritmoApiDto {
  tamañoPoblacion:      number;
  maxGeneraciones:      number;
  probabilidadMutacion: number;
  probabilidadCruce:    number;
  umbralConvergencia:   number;
  pesoErgo:             number;
  pesoTiempos:          number;
  /** Backend: SOEA.Application.../GenerarHorarioRequest.ConfiguracionAlgoritmoDto.PesoMaxHorasSeguidas. */
  pesoMaxHorasSeguidas: number;
  pesoBalanceSemanas?:  number;
  pesoPresencialFirst?: number;
  semilla?:             number;
}

export interface SesionFijaApiDto {
  id?: string;
  asignaturaId: string;
  docenteId?: string;
  espacioId?: string;
  dia: string;
  horaInicio: string;
  horaFin: string;
  duracionHoras: number;
  alternancia?: string;
  virtual: boolean;
  /** Laboratorio | AulaVirtual. Null/no reconocido → Laboratorio (default histórico del backend). */
  tipoFlujo?: string;
}

export interface GrupoApiDto {
  id: string;
  nombre: string;
  codigo?: string;
  asignaturaId?: string;
  facultadId?: string;
  /** Docente que dicta la asignatura para este grupo — semilla de Sesion.docenteId al generar. */
  docenteId?: string;
  estudiantesInscritos: number;
  /** JSON crudo por día (misma forma que Docente.disponibilidad); el backend deriva la ventana HC-G01. */
  disponibilidadUiJson?: string;
  /** Requisito de espacio por tipo de sesión (HC-S03/HC-S05). */
  requisitosEspacio?: RequisitoEspacio[];
}

export interface GenerarHorarioRequest {
  semestre: string;
  asignaturas: AsignaturaApiDto[];
  docentes: DocenteApiDto[];
  espacios: EspacioApiDto[];
  grupos?: GrupoApiDto[];
  configuracion?: ConfiguracionAlgoritmoApiDto;
  sesionesFijas?: SesionFijaApiDto[];
}

export interface AsignaturaApiDto {
  id: string;
  nombre: string;
  sesionesTeoriaPresencialSemana: number;
  horasTeoriaPresencial: number;
  sesionesTeoriaVirtualSemana: number;
  horasTeoriaVirtual: number;
  sesionesLaboratorioSemana: number;
  horasLaboratorio: number;
  programaId?: string;
  /** TipoA | TipoB | SinAlternancia — solo aplica al track de laboratorio. */
  alternancia?: string;
  /** Prioridad de presencialidad (SC-PRES): 'Obligatoria' | 'Optativa' | 'Electiva'. */
  categoria?: string;
  /** Candidata a ceder a alternancia si el algoritmo agota el espacio físico (cesión por saturación de espacio). */
  esCandidataAlternancia?: boolean;
  /** Ventana horaria HC-VH, formato "HH:mm". Ausente = sin restricción. */
  horaInicioMin?: string;
  horaFinMax?: string;
}

export interface DocenteApiDto {
  id: string;
  nombre: string;
  maxHoras: number;
  disponibilidad: Record<string, DisponibilidadDiaDto>;
}

export interface DisponibilidadDiaDto {
  noDisponible: boolean;
  tipo?: string;
  franjaGeneral?: string;
  desde?: string;
  hasta?: string;
}

export interface EspacioApiDto {
  id: string;
  nombre: string;
  capacidad: number;
  tipo?: string;
}

export interface GenerarHorarioResponse {
  horarioId: string;
  semestre: string;
  esFactible: boolean;
  puntajeFitness: number;
  generaciones: number;
  mensajeError?: string;
  motivoInfactibilidad?: string;
  /** Ids de grupo señalados por el diagnóstico opcional de Fase 2 como responsables de la
   *  infactibilidad — ver GenerarHorarioResponse.GruposEnConflicto en el backend. */
  gruposEnConflicto?: string[];
  logs?: string[];
  // alternancia y semana llegan como string desde JSON; mapearSesiones() los castea
  sesiones: (Omit<Sesion, 'alternancia' | 'semana'> & { alternancia: string; semana?: string })[];
}

export interface ReacomodarHorarioRequest {
  horarioId: string;
  sesionEditadaId: string;
  dia: string;
  horaInicio: string;
  espacioId?: string;
}

export interface ReacomodarHorarioResponse {
  esFactible: boolean;
  mensajeError?: string;
  advertencias: string[];
  sesiones: (Omit<Sesion, 'alternancia' | 'semana'> & { alternancia: string; semana?: string })[];
}

// ── Servicio ───────────────────────────────────────────────────────────────────

@Injectable({ providedIn: 'root' })
export class HorarioApiService {
  /** URL base del backend. Configurada por entorno (environment.ts / environment.prod.ts). */
  private readonly apiBase = environment.apiBaseUrl;

  private http = inject(HttpClient);

  /**
   * Llama al pipeline de 3 fases en el backend.
   * Construye el payload desde los datos actuales del StateService
   * y retorna las sesiones generadas listas para el StateService.
   */
  generarHorario(
    asignaturas: Asignatura[],
    docentes: Docente[],
    espacios: Espacio[],
    config?: ConfiguracionAlgoritmo,
    semestre = '2026-1',
    base?: HorarioBase,
    grupos?: Grupo[]
  ): Observable<GenerarHorarioResponse> {
    const sesionesFijas: SesionFijaApiDto[] | undefined = base?.sesiones.map(s => ({
      id:           s.id,
      asignaturaId: s.asignaturaId,
      docenteId:    s.docenteId,
      espacioId:    s.espacioId,
      dia:          s.dia,
      horaInicio:   s.horaInicio,
      horaFin:      s.horaFin,
      duracionHoras: s.duracionHoras,
      alternancia:  s.alternancia,
      virtual:      s.virtual,
      tipoFlujo:    s.tipoFlujo,
    }));

    const gruposDto: GrupoApiDto[] | undefined = grupos?.map(g => ({
      id: g.id,
      nombre: g.nombre,
      codigo: g.codigo,
      asignaturaId: g.asignaturaId,
      facultadId: g.facultadId,
      docenteId: g.docenteId,
      estudiantesInscritos: g.estudiantesInscritos,
      disponibilidadUiJson: g.disponibilidadUiJson,
      requisitosEspacio: g.requisitosEspacio ?? [],
    }));

    const body: GenerarHorarioRequest = {
      semestre,
      grupos: gruposDto?.length ? gruposDto : undefined,
      sesionesFijas: sesionesFijas?.length ? sesionesFijas : undefined,
      configuracion: config ? {
        tamañoPoblacion:      config.pobSize,
        maxGeneraciones:      config.maxGen,
        probabilidadMutacion: config.mutRate,
        probabilidadCruce:    config.crossRate,
        umbralConvergencia:   30,
        pesoErgo:             config.pesoErgo,
        pesoTiempos:          config.pesoTiempos,
        pesoMaxHorasSeguidas: config.pesoAlm,
        pesoBalanceSemanas:   config.pesoBalanceSemanas,
        pesoPresencialFirst:  config.pesoPresencialFirst,
        semilla:              config.semilla,
      } : undefined,
      asignaturas: asignaturas.map(a => ({
        id: a.id,
        nombre: a.nombre,
        sesionesTeoriaPresencialSemana: a.sesionesTeoriaPresencialSemana,
        horasTeoriaPresencial: a.horasTeoriaPresencial,
        sesionesTeoriaVirtualSemana: a.sesionesTeoriaVirtualSemana,
        horasTeoriaVirtual: a.horasTeoriaVirtual,
        sesionesLaboratorioSemana: a.sesionesLaboratorioSemana,
        horasLaboratorio: a.horasLaboratorio,
        programaId: a.programaId,
        alternancia: a.alternancia,
        horaInicioMin: a.horaInicioMin,
        horaFinMax: a.horaFinMax,
        categoria: a.categoria,
        esCandidataAlternancia: a.esCandidataAlternancia
      })),
      docentes: docentes.map(d => ({
        id: d.id,
        nombre: d.nombre,
        maxHoras: d.maxHoras,
        disponibilidad: d.disponibilidad ?? {}
      })),
      espacios: espacios.map(e => ({
        id: e.id,
        nombre: e.nombre,
        capacidad: e.capacidad,
        tipo: e.tipo
      }))
    };

    return this.http
      .post<GenerarHorarioResponse>(`${this.apiBase}/horario/generar`, body)
      .pipe(catchError(this.manejarError));
  }

  /**
   * P6: recupera el horario ya persistido para un semestre (última corrida generada), para
   * rehidratar la grilla tras un reload de página — antes esto no existía y el horario generado
   * solo vivía en memoria del navegador, así que un simple F5 lo vaciaba aunque siguiera intacto
   * en BD. null si aún no se ha generado ningún horario para ese semestre (404, no es un error).
   */
  obtenerActual(semestre = '2026-1'): Observable<GenerarHorarioResponse | null> {
    return this.http
      .get<GenerarHorarioResponse>(`${this.apiBase}/horario/actual`, { params: { semestre } })
      .pipe(catchError((err: HttpErrorResponse) => err.status === 404 ? of(null) : this.manejarError(err)));
  }

  /**
   * Petición 13: mueve una sesión ya generada a un nuevo (día, hora, espacio) sin regenerar el
   * horario completo. El backend recalcula solo la sesión editada y las que ahora chocan con ella.
   */
  reacomodar(request: ReacomodarHorarioRequest): Observable<ReacomodarHorarioResponse> {
    return this.http
      .post<ReacomodarHorarioResponse>(`${this.apiBase}/horario/reacomodar`, request)
      .pipe(catchError(this.manejarError));
  }

  /** Castea alternancia y semana de string a los tipos unión tipados. */
  mapearSesiones(sesiones: GenerarHorarioResponse['sesiones']): Sesion[] {
    return sesiones.map(s => ({
      ...s,
      docenteId: s.docenteId || undefined,
      duracionHoras: s.duracionHoras ?? this.diffHoras(s.horaInicio, s.horaFin),
      alternancia: (s.alternancia as 'TipoA' | 'TipoB' | 'SinAlternancia') ?? 'SinAlternancia',
      semana: (s.semana === 'A' || s.semana === 'B') ? s.semana : undefined,
    }));
  }

  private diffHoras(horaInicio: string, horaFin: string): number {
    const [hi, mi] = horaInicio.split(':').map(Number);
    const [hf, mf] = horaFin.split(':').map(Number);
    return Math.max(1, (hf * 60 + mf - (hi * 60 + mi)) / 60);
  }

  private manejarError(err: HttpErrorResponse): Observable<never> {
    // Fallo de red real (backend caído, CORS, sin conexión): Angular reporta status 0 y
    // err.error es un ProgressEvent — también `typeof === 'object'`, así que sin este chequeo
    // primero caía en la rama de abajo y se reenviaba el ProgressEvent crudo como si fuera el
    // payload 422 real, indistinguible en el componente de un 422 mal formado.
    if (err.status === 0) {
      return throwError(() => new Error('No se pudo conectar con el servidor. Verifique su conexión o que el backend esté disponible.'));
    }
    if (err.status === 400) {
      const errors = err.error?.errors;
      if (errors && typeof errors === 'object') {
        const msgs = (Object.values(errors) as string[][]).flat().join('; ');
        return throwError(() => new Error(msgs || 'Datos inválidos enviados al servidor.'));
      }
      const title = err.error?.title ?? err.error?.message ?? 'Solicitud inválida (400).';
      return throwError(() => new Error(title));
    }
    // 422: backend devolvió GenerarHorarioResponse con EsFactible=false
    if (err.error && typeof err.error === 'object') {
      return throwError(() => err.error);
    }
    return throwError(() => new Error(err.message ?? 'Error desconocido al conectar con el API.'));
  }
}
