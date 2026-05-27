import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Asignatura, Docente, Espacio } from './models';

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
  private base = 'http://localhost:5066/api';

  // ── Facultades y Programas ─────────────────────────────────────────────────────

  cargarFacultades(): Observable<any[]> {
    return this.http.get<any[]>(`${this.base}/facultades`);
  }

  cargarProgramas(): Observable<any[]> {
    return this.http.get<any[]>(`${this.base}/programas`);
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

  eliminarAsignatura(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/asignaturas/${id}`);
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
