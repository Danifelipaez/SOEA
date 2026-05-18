import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Asignatura, Docente, Espacio } from './models';

@Injectable({ providedIn: 'root' })
export class PersistenciaService {
  private http = inject(HttpClient);
  private base = 'http://localhost:5066/api';

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

  guardarAsignatura(a: Asignatura): Observable<any> {
    return this.http.post<any>(`${this.base}/asignaturas`, {
      nombre: a.nombre,
      codigo: a.codigo || `${a.nombre.substring(0, 3).toUpperCase()}-AUTO`,
      horasPorSesion: a.horasPorSesion,
      sesionesPorSemana: a.sesionesPorSemana,
      sesionesLaboratorioSemestre: a.sesionesLaboratorioSemestre,
      programaId: a.programaId || '00000000-0000-0000-0000-000000000001'
    });
  }
}
