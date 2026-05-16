import { Injectable, signal, computed } from '@angular/core';
import { Facultad, Programa, Espacio, Docente, Asignatura, Sesion } from './models';

@Injectable({
  providedIn: 'root'
})
export class StateService {
  // ── Entidades maestras ───────────────────────────────────────────────────────
  facultades = signal<Facultad[]>([]);
  programas  = signal<Programa[]>([]);
  espacios   = signal<Espacio[]>([]);
  docentes   = signal<Docente[]>([]);
  asignaturas = signal<Asignatura[]>([]);
  sesiones   = signal<Sesion[]>([]);

  // ── Facultades ───────────────────────────────────────────────────────────────
  addFacultad(f: Facultad)      { this.facultades.update(v => [...v, f]); }
  updateFacultad(f: Facultad)   { this.facultades.update(v => v.map(x => x.id === f.id ? f : x)); }
  deleteFacultad(id: string)    { this.facultades.update(v => v.filter(x => x.id !== id)); }

  // ── Programas ────────────────────────────────────────────────────────────────
  addPrograma(p: Programa)      { this.programas.update(v => [...v, p]); }
  updatePrograma(p: Programa)   { this.programas.update(v => v.map(x => x.id === p.id ? p : x)); }
  deletePrograma(id: string)    { this.programas.update(v => v.filter(x => x.id !== id)); }

  // ── Espacios ─────────────────────────────────────────────────────────────────
  addEspacio(e: Espacio)        { this.espacios.update(v => [...v, e]); }
  updateEspacio(e: Espacio)     { this.espacios.update(v => v.map(x => x.id === e.id ? e : x)); }
  deleteEspacio(id: string)     { this.espacios.update(v => v.filter(x => x.id !== id)); }

  // ── Docentes ─────────────────────────────────────────────────────────────────
  addDocente(d: Docente)        { this.docentes.update(v => [...v, d]); }
  updateDocente(d: Docente)     { this.docentes.update(v => v.map(x => x.id === d.id ? d : x)); }
  deleteDocente(id: string)     { this.docentes.update(v => v.filter(x => x.id !== id)); }

  // ── Asignaturas ──────────────────────────────────────────────────────────────
  addAsignatura(a: Asignatura)      { this.asignaturas.update(v => [...v, a]); }
  updateAsignatura(a: Asignatura)   { this.asignaturas.update(v => v.map(x => x.id === a.id ? a : x)); }
  deleteAsignatura(id: string)      { this.asignaturas.update(v => v.filter(x => x.id !== id)); }
  /** Reemplaza el listado completo (útil para importación masiva desde Excel). */
  setAsignaturas(list: Asignatura[]) { this.asignaturas.set(list); }

  // ── Sesiones y Logs (resultado del algoritmo) ──────────────────────────────
  executionLogs = signal<string[]>([]);

  setSesiones(s: Sesion[])       { this.sesiones.set(s); }
  updateSesion(s: Sesion)        { this.sesiones.update(v => v.map(x => x.id === s.id ? s : x)); }
  setExecutionLogs(logs: string[]) { this.executionLogs.set(logs); }

  // ── Helpers derivados ────────────────────────────────────────────────────────
  getFacultadById(id: string): Facultad | undefined {
    return this.facultades().find(f => f.id === id);
  }

  getProgramaById(id: string): Programa | undefined {
    return this.programas().find(p => p.id === id);
  }

  getProgramasByFacultad(facultadId: string): Programa[] {
    return this.programas().filter(p => p.facultadId === facultadId);
  }

  getAsignaturasByPrograma(programaId: string): Asignatura[] {
    return this.asignaturas().filter(a => a.programaId === programaId);
  }
}
