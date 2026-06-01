import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { Asignatura, ConfiguracionAlgoritmo, Docente, Espacio, Sesion } from './models';

// ── Tipos del contrato con la API ──────────────────────────────────────────────

export interface ConfiguracionAlgoritmoApiDto {
  tamañoPoblacion:      number;
  maxGeneraciones:      number;
  probabilidadMutacion: number;
  probabilidadCruce:    number;
  umbralConvergencia:   number;
  pesoErgo:             number;
  pesoTiempos:          number;
  pesoAlmuerzo:         number;
}

export interface GenerarHorarioRequest {
  semestre: string;
  asignaturas: AsignaturaApiDto[];
  docentes: DocenteApiDto[];
  espacios: EspacioApiDto[];
  configuracion?: ConfiguracionAlgoritmoApiDto;
}

export interface AsignaturaApiDto {
  id: string;
  nombre: string;
  docenteId?: string;
  creditos: number;
  horasSemanales: number;
  horasPorSesion: number;
  sesionesPorSemana: number;
  programaId?: string;
  alternancia?: string;
  esVirtual: boolean;
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
  logs?: string[];
  // alternancia y semana llegan como string desde JSON; mapearSesiones() los castea
  sesiones: (Omit<Sesion, 'alternancia' | 'semana'> & { alternancia: string; semana?: string })[];
}

// ── Servicio ───────────────────────────────────────────────────────────────────

@Injectable({ providedIn: 'root' })
export class HorarioApiService {
  /** URL base del backend. Cambia aquí si el API corre en otro puerto. */
  private readonly apiBase = 'http://localhost:5066/api';

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
    semestre = '2026-1'
  ): Observable<GenerarHorarioResponse> {
    const body: GenerarHorarioRequest = {
      semestre,
      configuracion: config ? {
        tamañoPoblacion:      config.pobSize,
        maxGeneraciones:      config.maxGen,
        probabilidadMutacion: config.mutRate,
        probabilidadCruce:    config.crossRate,
        umbralConvergencia:   30,
        pesoErgo:             config.pesoErgo,
        pesoTiempos:          config.pesoTiempos,
        pesoAlmuerzo:         config.pesoAlm,
      } : undefined,
      asignaturas: asignaturas.map(a => ({
        id: a.id,
        nombre: a.nombre,
        docenteId: a.docenteId,
        creditos: (a.sesionesPorSemana || 0) * (a.horasPorSesion || 0),
        horasSemanales: (a.horasPorSesion || 0) * (a.sesionesPorSemana || 0),
        horasPorSesion: a.horasPorSesion,
        sesionesPorSemana: a.sesionesPorSemana,
        programaId: a.programaId,
        alternancia: a.alternancia,
        esVirtual: false
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

  /** Castea alternancia y semana de string a los tipos unión tipados. */
  mapearSesiones(sesiones: GenerarHorarioResponse['sesiones']): Sesion[] {
    return sesiones.map(s => ({
      ...s,
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
