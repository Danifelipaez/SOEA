import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { Asignatura, Docente, Espacio, Sesion } from './models';
import { StateService } from './state.service';

// ── Tipos del contrato con la API ──────────────────────────────────────────────

export interface GenerarHorarioRequest {
  semestre: string;
  asignaturas: AsignaturaApiDto[];
  docentes: DocenteApiDto[];
  espacios: EspacioApiDto[];
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
  sesiones: SesionApiDto[];
}

export interface SesionApiDto {
  id: string;
  asignaturaId: string;
  docenteId: string;
  espacioId?: string;
  dia: string;
  horaInicio: string;
  horaFin: string;
  alternancia: string;
  virtual: boolean;
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
    semestre = '2026-1'
  ): Observable<GenerarHorarioResponse> {
    const body: GenerarHorarioRequest = {
      semestre,
      asignaturas: asignaturas.map(a => ({
        id: a.id,
        nombre: a.nombre,
        docenteId: a.docenteId,
        creditos: a.sesionesPorSemana * a.horasPorSesion,
        horasSemanales: a.horasPorSesion * a.sesionesPorSemana,
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

  /** Mapea la respuesta del API al modelo Sesion[] del StateService. */
  mapearSesiones(apiSesiones: SesionApiDto[]): Sesion[] {
    return apiSesiones.map(s => ({
      id: s.id,
      asignaturaId: s.asignaturaId,
      docenteId: s.docenteId,
      dia: s.dia,
      horaInicio: s.horaInicio,
      horaFin: s.horaFin,
      espacioId: s.espacioId,
      virtual: s.virtual,
      alternancia: (s.alternancia as 'TipoA' | 'TipoB' | 'SinAlternancia') ?? 'SinAlternancia'
    }));
  }

  private manejarError(err: HttpErrorResponse): Observable<never> {
    // Si el backend devolvió el DTO GenerarHorarioResponse (con status 422)
    if (err.error && typeof err.error === 'object') {
      return throwError(() => err.error);
    }
    const mensaje = err.message ?? 'Error desconocido al conectar con el API.';
    return throwError(() => new Error(mensaje));
  }
}
