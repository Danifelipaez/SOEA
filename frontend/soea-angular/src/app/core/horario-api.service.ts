import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { Asignatura, ConfiguracionAlgoritmo, Docente, Espacio, Grupo, HorarioBase, Sesion } from './models';
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
  pesoAlmuerzo:         number;
}

export interface SesionFijaApiDto {
  asignaturaId: string;
  docenteId?: string;
  espacioId?: string;
  dia: string;
  horaInicio: string;
  horaFin: string;
  duracionHoras: number;
  alternancia?: string;
  virtual: boolean;
}

export interface GrupoApiDto {
  id: string;
  nombre: string;
  codigo?: string;
  asignaturaId?: string;
  facultadId?: string;
  estudiantesInscritos: number;
  /** Franjas válidas para el grupo: "Matutino" | "Vespertino". Vacío = sin restricción (HC-G01). */
  disponibilidad: string[];
  disponibilidadUiJson?: string;
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
  docenteId?: string;
  creditos: number;
  horasSemanales: number;
  horasPorSesion: number;
  sesionesPorSemana: number;
  programaId?: string;
  alternancia?: string;
  esVirtual: boolean;
  espacioFijoId?: string;
  /** Prioridad de presencialidad (SC-PRES): 'Obligatoria' | 'Optativa' | 'Electiva'. */
  categoria?: string;
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
      asignaturaId: s.asignaturaId,
      docenteId:    s.docenteId,
      espacioId:    s.espacioId,
      dia:          s.dia,
      horaInicio:   s.horaInicio,
      horaFin:      s.horaFin,
      duracionHoras: s.duracionHoras,
      alternancia:  s.alternancia,
      virtual:      s.virtual,
    }));

    const gruposDto: GrupoApiDto[] | undefined = grupos?.map(g => ({
      id: g.id,
      nombre: g.nombre,
      codigo: g.codigo,
      asignaturaId: g.asignaturaId,
      facultadId: g.facultadId,
      estudiantesInscritos: g.estudiantesInscritos,
      disponibilidad: this.franjasDeGrupo(g.disponibilidadUiJson),
      disponibilidadUiJson: g.disponibilidadUiJson,
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
        esVirtual: false,
        espacioFijoId: a.espacioFijoId,
        categoria: a.categoria
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
      docenteId: s.docenteId || undefined,
      duracionHoras: s.duracionHoras ?? this.diffHoras(s.horaInicio, s.horaFin),
      alternancia: (s.alternancia as 'TipoA' | 'TipoB' | 'SinAlternancia') ?? 'SinAlternancia',
      semana: (s.semana === 'A' || s.semana === 'B') ? s.semana : undefined,
    }));
  }

  /**
   * Reduce la disponibilidad por día del grupo (JSON de la UI) a las franjas que entiende
   * HC-G01 en el backend: "Matutino" (06–13) y/o "Vespertino" (13–20).
   * Une las franjas de todos los días configurados. Si cubre ambas → devuelve [] (sin
   * restricción, evita un filtro inútil). Sin JSON → [] (grupo sin restricción de franja).
   */
  private franjasDeGrupo(json?: string): string[] {
    if (!json) return [];
    let disp: Record<string, any>;
    try { disp = JSON.parse(json); } catch { return []; }

    const set = new Set<string>();
    for (const dia of Object.keys(disp)) {
      const d = disp[dia];
      if (!d || d.noDisponible) continue;
      const tipo   = String(d.tipo ?? '').toLowerCase();
      const franja = String(d.franjaGeneral ?? '').toLowerCase();
      const tiene  = (s: string) => tipo.includes(s) || franja.includes(s);

      if (tiene('todo')) { set.add('Matutino'); set.add('Vespertino'); }
      else if (tiene('matutino')) set.add('Matutino');
      else if (tiene('vespertino') || tiene('nocturno')) set.add('Vespertino');
      else if (tiene('especific')) {
        const desde = d.desde ?? '06:00';
        const hasta = d.hasta ?? '22:00';
        if (desde < '13:00') set.add('Matutino');
        if (hasta > '13:00') set.add('Vespertino');
      } else { set.add('Matutino'); set.add('Vespertino'); }
    }
    return set.size >= 2 ? [] : [...set];
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
