import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Asignatura, Docente, Espacio, Facultad, Programa, Sesion, TipoAlternanciaConfig } from './models';
import { environment } from '../../environments/environment';

export interface ImportMapping {
  tempId: string;
  newId: string;
}

export interface ImportSummary {
  facultades: number;
  programas: number;
  asignaturas: number;
  grupos: number;
  docentes: number;
}

export interface ImportResult {
  facultades: ImportMapping[];
  programas: ImportMapping[];
  asignaturas: ImportMapping[];
  grupos: ImportMapping[];
  docentes: ImportMapping[];
  summary: ImportSummary;
}

@Injectable({ providedIn: 'root' })
export class PersistenciaService {
  private http = inject(HttpClient);
  private base = environment.apiBaseUrl;

  // ── Facultades y Programas ─────────────────────────────────────────────────────

  cargarFacultades(): Observable<Facultad[]> {
    return this.http.get<Facultad[]>(`${this.base}/facultades`);
  }

  cargarProgramas(): Observable<Programa[]> {
    return this.http.get<Programa[]>(`${this.base}/programas`);
  }

  // ── Docentes ───────────────────────────────────────────────────────────────────

  cargarDocentes(): Observable<Docente[]> {
    return this.http.get<Docente[]>(`${this.base}/docentes`);
  }

  guardarDocente(d: Docente): Observable<Docente> {
    const body = { id: d.id, nombre: d.nombre, cedula: d.cedula, maxHoras: d.maxHoras, disponibilidad: d.disponibilidad };
    return this.http.post<Docente>(`${this.base}/docentes`, body);
  }

  actualizarDocente(d: Docente): Observable<Docente> {
    const body = { id: d.id, nombre: d.nombre, cedula: d.cedula, maxHoras: d.maxHoras, disponibilidad: d.disponibilidad };
    return this.http.put<Docente>(`${this.base}/docentes/${d.id}`, body);
  }

  eliminarDocenteBD(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/docentes/${id}`);
  }

  /** Grupos de docentes que probablemente son la misma persona (variantes de nombre). */
  detectarDuplicadosDocentes(): Observable<Docente[][]> {
    return this.http.get<Docente[][]>(`${this.base}/docentes/duplicados`);
  }

  /** Fusiona los duplicados en el canónico: reasigna sus asignaturas y los elimina de la BD. */
  fusionarDocentes(canonicoId: string, duplicadosIds: string[]): Observable<FusionDocentesResultado> {
    return this.http.post<FusionDocentesResultado>(`${this.base}/docentes/fusionar`,
      { canonicoId, duplicadosIds });
  }

  // ── Tipos de alternancia (catálogo editable, Inc. C) ────────────────────────────

  cargarTiposAlternancia(): Observable<TipoAlternanciaConfig[]> {
    return this.http.get<TipoAlternanciaConfig[]>(`${this.base}/tiposalternancia`);
  }

  crearTipoAlternancia(t: Partial<TipoAlternanciaConfig>): Observable<TipoAlternanciaConfig> {
    return this.http.post<TipoAlternanciaConfig>(`${this.base}/tiposalternancia`, t);
  }

  actualizarTipoAlternancia(t: TipoAlternanciaConfig): Observable<TipoAlternanciaConfig> {
    return this.http.put<TipoAlternanciaConfig>(`${this.base}/tiposalternancia/${t.id}`, t);
  }

  eliminarTipoAlternancia(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/tiposalternancia/${id}`);
  }

  // ── Espacios ───────────────────────────────────────────────────────────────────

  cargarEspacios(): Observable<Espacio[]> {
    return this.http.get<Espacio[]>(`${this.base}/espacios`);
  }

  guardarEspacio(e: Espacio): Observable<Espacio> {
    const body = { id: e.id, nombre: e.nombre, tipo: e.tipo, capacidad: e.capacidad, edificio: e.edificio, piso: e.piso };
    return this.http.post<Espacio>(`${this.base}/espacios`, body);
  }

  actualizarEspacio(e: Espacio): Observable<Espacio> {
    const body = { id: e.id, nombre: e.nombre, tipo: e.tipo, capacidad: e.capacidad, edificio: e.edificio, piso: e.piso };
    return this.http.put<Espacio>(`${this.base}/espacios/${e.id}`, body);
  }

  eliminarEspacioBD(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/espacios/${id}`);
  }

  // ── Asignaturas (usa el AsignaturasController existente) ──────────────────────

  cargarAsignaturas(): Observable<any[]> {
    return this.http.get<any[]>(`${this.base}/asignaturas`);
  }

  actualizarAsignatura(a: Asignatura): Observable<any> {
    const body = {
      nombre: a.nombre,
      codigo: a.codigo,
      horasPorSesion: a.horasPorSesion,
      sesionesPorSemana: a.sesionesPorSemana,
      sesionesLaboratorioSemestre: a.sesionesLaboratorioSemestre,
      programaId: a.programaId,
      // Se envía la alternancia visible en la UI como override explícito para
      // que el backend no la re-infiera por umbral y pise un tipo ya establecido.
      alternancia: a.alternancia,
      docenteId: a.docenteId ?? null,
      espacioFijoId: a.espacioFijoId ?? null
    };
    return this.http.put<any>(`${this.base}/asignaturas/${a.id}`, body);
  }

  actualizarAlternancia(id: string, alternancia: 'TipoA' | 'TipoB' | 'SinAlternancia'): Observable<void> {
    return this.http.patch<void>(`${this.base}/asignaturas/${id}/alternancia`, { alternancia });
  }

  eliminarAsignatura(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/asignaturas/${id}`);
  }

  // ── Horario: creación manual de sesión ────────────────────────────────────────

  crearSesionManual(payload: {
    asignaturaId: string;
    docenteId: string;
    espacioId: string | null;
    dia: string;
    horaInicio: string;
    duracionHoras: number;
    alternancia: string;
  }): Observable<Sesion[]> {
    return this.http.post<Sesion[]>(`${this.base}/horario/sesion-manual`, payload);
  }

  importarCurriculum(payload: any): Observable<ImportResult> {
    return this.http.post<ImportResult>(`${this.base}/import/curriculum`, payload);
  }

  importarExcel(file: File): Observable<ImportExcelStatsDto> {
    const form = new FormData();
    form.append('archivo', file, file.name);
    return this.http.post<ImportExcelStatsDto>(`${this.base}/import/excel`, form);
  }
}

export interface ImportExcelStatsDto {
  facultadesCreadas: number;
  programasCreados: number;
  docentesCreados: number;
  docentesActualizados: number;
  espaciosCreados: number;
  asignaturasCreadas: number;
  asignaturasActualizadas: number;
  gruposCreados: number;
  sesionesPersistidas: number;
  asignaturasSinDocente: number;
  advertencias: string[];
}

export interface FusionDocentesResultado {
  canonicoId: string;
  docentesEliminados: number;
  asignaturasReasignadas: number;
}
