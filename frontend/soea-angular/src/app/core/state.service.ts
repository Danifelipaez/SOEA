import { Injectable, signal, computed } from '@angular/core';
import { Facultad, Programa, Espacio, Docente, Grupo, Asignatura, Sesion, ConfiguracionAlgoritmo, CONFIGURACION_DEFECTO, HorarioBase } from './models';

@Injectable({
  providedIn: 'root'
})
export class StateService {
  // ── Entidades maestras ───────────────────────────────────────────────────────
  facultades  = signal<Facultad[]>([]);
  programas   = signal<Programa[]>([]);
  espacios    = signal<Espacio[]>([]);
  docentes    = signal<Docente[]>([]);
  grupos      = signal<Grupo[]>([]);
  asignaturas = signal<Asignatura[]>([]);
  sesiones    = signal<Sesion[]>([]);

  // ── Lookup Maps computados — O(1) en lugar de .find() ───────────────────────
  readonly facultadById    = computed(() => new Map(this.facultades().map(f  => [f.id,  f])));
  readonly programaById    = computed(() => new Map(this.programas().map(p   => [p.id,  p])));
  readonly espacioById     = computed(() => new Map(this.espacios().map(e    => [e.id,  e])));
  readonly docenteById     = computed(() => new Map(this.docentes().map(d    => [d.id,  d])));
  readonly asignaturaById  = computed(() => new Map(this.asignaturas().map(a => [a.id,  a])));

  readonly programasByFacultad = computed(() => {
    const m = new Map<string, Programa[]>();
    for (const p of this.programas()) {
      const list = m.get(p.facultadId);
      if (list) list.push(p); else m.set(p.facultadId, [p]);
    }
    return m;
  });

  readonly asignaturasByPrograma = computed(() => {
    const m = new Map<string, Asignatura[]>();
    for (const a of this.asignaturas()) {
      const list = m.get(a.programaId);
      if (list) list.push(a); else m.set(a.programaId, [a]);
    }
    return m;
  });

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

  // ── Grupos ───────────────────────────────────────────────────────────────────
  addGrupo(g: Grupo)            { this.grupos.update(v => [...v, g]); }
  updateGrupo(g: Grupo)         { this.grupos.update(v => v.map(x => x.id === g.id ? g : x)); }
  deleteGrupo(id: string)       { this.grupos.update(v => v.filter(x => x.id !== id)); }

  // ── Asignaturas ──────────────────────────────────────────────────────────────
  addAsignatura(a: Asignatura)      { this.asignaturas.update(v => [...v, a]); }
  updateAsignatura(a: Asignatura)   { this.asignaturas.update(v => v.map(x => x.id === a.id ? a : x)); }
  deleteAsignatura(id: string)      { this.asignaturas.update(v => v.filter(x => x.id !== id)); }
  /** Reemplaza el listado completo (útil para importación masiva desde Excel). */
  setAsignaturas(list: Asignatura[]) { this.asignaturas.set(list); }

  // ── Configuración del algoritmo (Developer Dashboard) ───────────────────────
  configuracionAlgoritmo = signal<ConfiguracionAlgoritmo>(CONFIGURACION_DEFECTO);
  setConfiguracionAlgoritmo(c: ConfiguracionAlgoritmo) { this.configuracionAlgoritmo.set(c); }

  // ── Sesiones y Logs (resultado del algoritmo) ──────────────────────────────
  executionLogs = signal<string[]>([]);

  setSesiones(s: Sesion[])       { this.sesiones.set(s); }
  updateSesion(s: Sesion)        { this.sesiones.update(v => v.map(x => x.id === s.id ? s : x)); }
  /**
   * Mueve una sesión a un nuevo día/franja conservando la modalidad, semana y espacio de
   * CADA fila. Las filas A y B de una misma sesión comparten `id` y ocupan el mismo horario
   * (el horario es idéntico en ambas semanas; solo cambia presencial↔virtual), por lo que el
   * movimiento aplica a todas sus filas a la vez sin colapsarlas en un único objeto.
   */
  moverSesion(id: string, dia: string, horaInicio: string, horaFin: string) {
    this.sesiones.update(v => v.map(x =>
      x.id === id ? { ...x, dia, horaInicio, horaFin } : x));
  }
  setExecutionLogs(logs: string[]) { this.executionLogs.set(logs); }

  // ── Horarios base ────────────────────────────────────────────────────────────
  horariosBases       = signal<HorarioBase[]>(this.cargarBasesLocalStorage());
  baseSeleccionadaId  = signal<string | null>(null);

  guardarHorarioBase(nombre: string): HorarioBase {
    const base: HorarioBase = {
      id: crypto.randomUUID(),
      nombre: nombre.trim(),
      creadoEn: new Date().toISOString(),
      sesiones: this.sesiones(),
    };
    this.horariosBases.update(v => [...v, base]);
    this.persistirBasesLocalStorage();
    return base;
  }

  eliminarHorarioBase(id: string) {
    this.horariosBases.update(v => v.filter(b => b.id !== id));
    if (this.baseSeleccionadaId() === id) this.baseSeleccionadaId.set(null);
    this.persistirBasesLocalStorage();
  }

  seleccionarBase(id: string | null) { this.baseSeleccionadaId.set(id); }

  readonly baseSeleccionada = computed(() =>
    this.horariosBases().find(b => b.id === this.baseSeleccionadaId()) ?? null);

  private cargarBasesLocalStorage(): HorarioBase[] {
    try {
      const raw = localStorage.getItem('soea_horarios_base');
      return raw ? JSON.parse(raw) : [];
    } catch { return []; }
  }

  private persistirBasesLocalStorage() {
    try {
      localStorage.setItem('soea_horarios_base', JSON.stringify(this.horariosBases()));
    } catch { /* cuota excedida — ignorar */ }
  }

  // ── Helpers derivados (delegan a los Maps computados) ───────────────────────
  getFacultadById(id: string): Facultad | undefined {
    return this.facultadById().get(id);
  }

  getProgramaById(id: string): Programa | undefined {
    return this.programaById().get(id);
  }

  getProgramasByFacultad(facultadId: string): Programa[] {
    return this.programasByFacultad().get(facultadId) ?? [];
  }

  getAsignaturasByPrograma(programaId: string): Asignatura[] {
    return this.asignaturasByPrograma().get(programaId) ?? [];
  }
}
