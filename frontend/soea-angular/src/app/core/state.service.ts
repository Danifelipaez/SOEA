import { Injectable, signal } from '@angular/core';
import { Espacio, Docente, Asignatura, Sesion } from './models';

@Injectable({
  providedIn: 'root'
})
export class StateService {
  espacios = signal<Espacio[]>([]);
  docentes = signal<Docente[]>([]);
  asignaturas = signal<Asignatura[]>([]);
  sesiones = signal<Sesion[]>([]); // generated schedule

  constructor() {
    // Initial mock data if empty
  }

  addEspacio(e: Espacio) { this.espacios.update(v => [...v, e]); }
  updateEspacio(e: Espacio) { this.espacios.update(v => v.map(x => x.id === e.id ? e : x)); }
  deleteEspacio(id: string) { this.espacios.update(v => v.filter(x => x.id !== id)); }

  addDocente(d: Docente) { this.docentes.update(v => [...v, d]); }
  updateDocente(d: Docente) { this.docentes.update(v => v.map(x => x.id === d.id ? d : x)); }
  deleteDocente(id: string) { this.docentes.update(v => v.filter(x => x.id !== id)); }

  addAsignatura(a: Asignatura) { this.asignaturas.update(v => [...v, a]); }
  updateAsignatura(a: Asignatura) { this.asignaturas.update(v => v.map(x => x.id === a.id ? a : x)); }
  deleteAsignatura(id: string) { this.asignaturas.update(v => v.filter(x => x.id !== id)); }
  
  setSesiones(s: Sesion[]) { this.sesiones.set(s); }
}
