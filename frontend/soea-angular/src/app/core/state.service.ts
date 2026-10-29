import { Injectable, signal, computed } from '@angular/core';
import { Facultad, Programa, Espacio, Docente, Grupo, Asignatura, Sesion, HorarioBase, FiltroHorario, ClaseFija, TipoEspacioAlternancia, ChoqueAulas } from './models';
import { nuevoId } from './id.util';
import { SEMESTRE_POR_DEFECTO } from './semestre';
import { porNombre, ordenar } from './orden';

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

  readonly gruposByAsignatura = computed(() => {
    const m = new Map<string, Grupo[]>();
    for (const g of this.grupos()) {
      const list = m.get(g.asignaturaId);
      if (list) list.push(g); else m.set(g.asignaturaId, [g]);
    }
    for (const list of m.values()) list.sort(porNombre);
    return m;
  });

  getGruposByAsignatura(asignaturaId: string): Grupo[] {
    return this.gruposByAsignatura().get(asignaturaId) ?? [];
  }

  /** Grupos cuyo asignaturaId no resuelve a ninguna Asignatura del catálogo (p. ej. la asignatura
   *  fue eliminada mientras el grupo seguía existiendo). Un asignaturaId vacío es "sin asignar a
   *  propósito", no huérfano. */
  readonly gruposHuerfanos = computed(() =>
    this.grupos().filter(g => g.asignaturaId && !this.asignaturaById().has(g.asignaturaId)));

  // ── Color por asignatura (petición 12) ──────────────────────────────────────
  // Rampa fija coherente con --alt-a/--alt-b/--alt-lab de styles.css. Hash determinístico
  // del id: misma asignatura → mismo color en toda la sesión, sin persistir nada nuevo.
  private static readonly PALETA_ASIGNATURA = [
    '#5980a6', '#a8825a', '#6f8f6a', '#8a6fa0', '#a0645f', '#5fa0a0', '#a0955f', '#6f7fa0'
  ];

  colorDeAsignatura(id: string): string {
    let hash = 0;
    for (let i = 0; i < id.length; i++) hash = (hash * 31 + id.charCodeAt(i)) >>> 0;
    return StateService.PALETA_ASIGNATURA[hash % StateService.PALETA_ASIGNATURA.length];
  }

  // Todas las listas del catálogo se guardan en orden alfabético (ordenar): así cada listado,
  // selector y chip de la app sale ordenado sin que cada vista tenga que recordarlo.

  // ── Facultades ───────────────────────────────────────────────────────────────
  addFacultad(f: Facultad)      { this.facultades.update(v => ordenar([...v, f])); }
  updateFacultad(f: Facultad)   { this.facultades.update(v => ordenar(v.map(x => x.id === f.id ? f : x))); }
  deleteFacultad(id: string)    { this.facultades.update(v => v.filter(x => x.id !== id)); }

  // ── Programas ────────────────────────────────────────────────────────────────
  addPrograma(p: Programa)      { this.programas.update(v => ordenar([...v, p])); }
  updatePrograma(p: Programa)   { this.programas.update(v => ordenar(v.map(x => x.id === p.id ? p : x))); }
  deletePrograma(id: string)    { this.programas.update(v => v.filter(x => x.id !== id)); }

  // ── Espacios ─────────────────────────────────────────────────────────────────
  addEspacio(e: Espacio)        { this.espacios.update(v => ordenar([...v, e])); }
  updateEspacio(e: Espacio)     { this.espacios.update(v => ordenar(v.map(x => x.id === e.id ? e : x))); }
  deleteEspacio(id: string)     { this.espacios.update(v => v.filter(x => x.id !== id)); }

  // ── Docentes ─────────────────────────────────────────────────────────────────
  addDocente(d: Docente)        { this.docentes.update(v => ordenar([...v, d])); }
  updateDocente(d: Docente)     { this.docentes.update(v => ordenar(v.map(x => x.id === d.id ? d : x))); }
  deleteDocente(id: string)     { this.docentes.update(v => v.filter(x => x.id !== id)); }

  // ── Grupos ───────────────────────────────────────────────────────────────────
  addGrupo(g: Grupo)            { this.grupos.update(v => ordenar([...v, g])); }
  updateGrupo(g: Grupo)         { this.grupos.update(v => ordenar(v.map(x => x.id === g.id ? g : x))); }
  deleteGrupo(id: string)       { this.grupos.update(v => v.filter(x => x.id !== id)); }

  // ── Asignaturas ──────────────────────────────────────────────────────────────
  addAsignatura(a: Asignatura)      { this.asignaturas.update(v => ordenar([...v, a])); }
  updateAsignatura(a: Asignatura)   { this.asignaturas.update(v => ordenar(v.map(x => x.id === a.id ? a : x))); }
  deleteAsignatura(id: string)      {
    this.grupos.update(v => v.filter(g => g.asignaturaId !== id));
    this.asignaturas.update(v => v.filter(x => x.id !== id));
  }
  /** Reemplaza el listado completo (útil para importación masiva desde Excel). */
  setAsignaturas(list: Asignatura[]) { this.asignaturas.set(ordenar(list)); }

  // ── Sesiones y Logs (resultado del algoritmo) ──────────────────────────────
  /** Semestre con el que se genera y se rehidrata el horario (L-8). */
  semestre = signal<string>(SEMESTRE_POR_DEFECTO);
  executionLogs = signal<string[]>([]);
  /** Id del Horario persistido por la última generación exitosa (P5: lo necesita /reacomodar). */
  horarioId = signal<string | null>(null);
  /** FE6/FE13 auditoría: CatalogoService.cargarTodo() intenta rehidratar el horario persistido
   *  junto al resto del catálogo, pero un fallo ahí no debe tumbar esa carga — se guarda aquí en
   *  vez de lanzarse, para que el componente que lo necesite (HorarioComponent) lo muestre. */
  errorHorarioActual = signal<string | null>(null);

  setSesiones(s: Sesion[])       { this.sesiones.set(s); }
  /**
   * Aplica una edición a TODAS las filas que comparten `id`. Una sesión que no alterna tiene
   * UNA sola fila; una que alterna tiene su fila presencial más la contraparte virtual derivada,
   * y ambas comparten `id` a propósito. Día, hora, docente y alternancia se sincronizan en todas.
   * `espacioId` es la excepción: una fila virtual siempre debe quedar en null (regla 9,
   * CLAUDE.md) aunque el usuario haya editado el espacio presencial; ese valor se refleja en
   * `espacioIdHogar` para que la contraparte virtual siga apuntando a su aula.
   * `semana` NUNCA se sincroniza: es lo que distingue la fila presencial de su contraparte.
   * Reemplazar con el objeto completo colapsaba ambas filas en una copia idéntica, perdiendo esa
   * distinción — la sesión "duplicada" que se veía en el grid tras mover un día/hora.
   */
  updateSesion(s: Sesion) {
    this.sesiones.update(v => v.map(x => x.id !== s.id ? x : {
      ...x,
      docenteId: s.docenteId,
      dia: s.dia,
      horaInicio: s.horaInicio,
      horaFin: s.horaFin,
      alternancia: s.alternancia,
      espacioId: x.virtual ? undefined : s.espacioId,
      espacioIdHogar: s.espacioId ?? x.espacioIdHogar,
    }));
  }
  setExecutionLogs(logs: string[]) { this.executionLogs.set(logs); }

  /** Filtro de la vista de /horario (solo cambia lo que se ve, nunca el horario). Aquí para que
   *  sobreviva al cambio de pestaña; ponytail: se pierde con F5. */
  filtrosHorario = signal<FiltroHorario[]>([]);
  filtroAtenuar = signal(false);

  /**
   * Ids de grupo que el backend señaló como responsables de una infactibilidad (diagnóstico
   * opcional de Fase 2, ver GenerarHorarioResponse.GruposEnConflicto). Vacío mientras no haya
   * fallado ninguna generación, o tras una generación exitosa (ver horario.component.ts).
   */
  gruposEnConflicto = signal<string[]>([]);
  /** Los que siguen marcados en el catálogo: un grupo deja de estarlo en cuanto se edita él o su
   *  asignatura después del fallo (antes la marca seguía hasta volver a generar, aunque ya estuviera
   *  corregido). Si el cambio no bastó, el borde de "datos incompletos" lo sigue señalando. */
  readonly gruposEnConflictoSet = computed(() =>
    new Set(this.gruposEnConflicto().filter(id => this.firmaGrupo(id) === this.firmasConflicto.get(id))));
  /** Grupo + asignatura como estaban al fallar la generación. Se escribe junto con gruposEnConflicto. */
  private firmasConflicto = new Map<string, string>();
  /** Solo lo que puede hacer fallar a un grupo (disponibilidad, aforo, aula, horas y ventana de su
   *  asignatura): renombrarlo o marcar su asignatura como candidata a alternar no lo da por corregido. */
  private firmaGrupo(id: string): string {
    const g = this.grupos().find(x => x.id === id);
    if (!g) return '';
    const a = this.asignaturaById().get(g.asignaturaId);
    return JSON.stringify([g.asignaturaId, g.disponibilidadUiJson, g.estudiantesInscritos, g.requisitosEspacio,
      a && [a.sesionesTeoriaPresencialSemana, a.horasTeoriaPresencial, a.sesionesTeoriaVirtualSemana, a.horasTeoriaVirtual,
        a.sesionesLaboratorioSemana, a.horasLaboratorio, a.horaInicioMin, a.horaFinMax]]);
  }
  setGruposEnConflicto(ids: string[]) {
    this.firmasConflicto = new Map(ids.map(id => [id, this.firmaGrupo(id)]));
    this.gruposEnConflicto.set(ids);
  }

  /**
   * M6 auditoría: causa de infactibilidad (GenerarHorarioResponse.MotivoInfactibilidad) del último
   * intento — junto con gruposEnConflicto, persiste fuera de /horario para que /revisar y /publicar
   * puedan mostrar la misma guía accionable sin que se pierda al navegar (antes solo vivía en un
   * signal local de HorarioComponent, que se destruye al salir de la ruta).
   */
  motivoInfactibilidad = signal<string | undefined>(undefined);
  /** Texto ya traducido (sin jerga) del último intento fallido de generar; '' tras uno exitoso.
   *  Lo pintan /horario y /revisar (GeneracionFallidaComponent), sobrevive a la navegación. */
  errorGeneracion = signal('');
  setMotivoInfactibilidad(motivo: string | undefined) { this.motivoInfactibilidad.set(motivo); }
  /** Con motivo Capacidad: el espacio de mayor aforo que el grupo en conflicto podía usar. */
  espacioLimitanteId = signal<string | undefined>(undefined);
  /** Con motivo Espacio: horas con más clases obligadas que aulas en el último intento fallido. */
  choquesAulas = signal<ChoqueAulas[]>([]);
  readonly contextoCapacidad = computed(() => ({
    espacioLimitanteId: this.espacioLimitanteId(), espacios: this.espacios(),
    asignaturas: this.asignaturas(), programaById: this.programaById()
  }));

  // ── Horarios base ────────────────────────────────────────────────────────────
  horariosBases       = signal<HorarioBase[]>(this.cargarBasesLocalStorage());
  baseSeleccionadaId  = signal<string | null>(null);

  guardarHorarioBase(nombre: string): HorarioBase {
    const base: HorarioBase = {
      id: nuevoId(),
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

  // ── Pruebas de horario ───────────────────────────────────────────────────────
  /** Clases fijadas a mano para probar combinaciones: al generar (o probar) quedan en su franja y
   *  el resto se acomoda alrededor. `activa` deja apagar una sin perderla. Se guardan en este
   *  navegador, como los puntos de partida. */
  clasesFijas = signal<ClaseFija[]>(leerLocal('soea_clases_fijas', []));
  /** Resultado de "Probar sin guardar": se muestra en lugar del horario guardado hasta guardarlo o
   *  descartarlo. null = no hay prueba abierta. */
  prueba = signal<Sesion[] | null>(null);

  fijarClase(sesion: Sesion, origen?: ClaseFija['origen']) { this.guardarFijas([...this.clasesFijas(), { sesion, activa: true, origen }]); }
  alternarClaseFija(id: string) { this.guardarFijas(this.clasesFijas().map(c => c.sesion.id === id ? { ...c, activa: !c.activa } : c)); }
  quitarClaseFija(id: string) { this.guardarFijas(this.clasesFijas().filter(c => c.sesion.id !== id)); }
  private guardarFijas(v: ClaseFija[]) { this.clasesFijas.set(v); escribirLocal('soea_clases_fijas', v); }

  /** Lo que se manda como fijo al generar: el punto de partida elegido más las clases fijas activas. */
  readonly sesionesFijasParaGenerar = computed<Sesion[]>(() => this.fijasSobre(this.baseSeleccionada()?.sesiones ?? []));

  /** Cuántas clases de ese tipo pide la asignatura de la sesión por semana (0 si ya no existe). */
  topeClasesDelTipo(s: Sesion): number {
    const a = this.asignaturaById().get(s.asignaturaId);
    if (!a) return 0;
    return s.tipoFlujo === 'Laboratorio' ? a.sesionesLaboratorioSemana
      : s.virtual ? a.sesionesTeoriaVirtualSemana : a.sesionesTeoriaPresencialSemana;
  }

  /**
   * `base` (punto de partida, o el horario guardado al buscar lugar para las pendientes) más las
   * clases fijas activas. Una fija reemplaza a la clase de la base del mismo grupo, asignatura y tipo
   * que estaba en su franja de origen (la que se movió), si no a la de su misma franja, y si no a
   * otra de ellas cuando el grupo ya tiene todas las de ese tipo (si no, quedaría con una de más: el
   * servidor suma la fija que no reemplaza nada). Si a la base le falta esa clase (una pendiente), la
   * fija se suma sin quitar ninguna. Nunca reemplaza a otra fija.
   */
  fijasSobre(base: readonly Sesion[]): Sesion[] {
    const res = base.filter(s => !s.esContraparteVirtual);
    const fijas = new Set<Sesion>();
    const enFranja = (s: Sesion, franja?: { dia: string; horaInicio: string }) =>
      !!franja && s.dia === franja.dia && s.horaInicio === franja.horaInicio;
    for (const { sesion: f, origen } of this.clasesFijas().filter(c => c.activa)) {
      const delTipo = res.filter(s => claveTipoSesion(s) === claveTipoSesion(f));
      const mismas = delTipo.filter(s => !fijas.has(s));
      const reemplazada = mismas.find(s => enFranja(s, origen)) ?? mismas.find(s => enFranja(s, f))
        ?? (delTipo.length >= this.topeClasesDelTipo(f) ? mismas[0] : undefined);
      if (reemplazada) res.splice(res.indexOf(reemplazada), 1);
      res.push(f);
      fijas.add(f);
    }
    return res;
  }

  // ── Alternancia ──────────────────────────────────────────────────────────────
  /** Qué sesiones pueden alternar según el espacio que usan (pestaña Alternancia). */
  tipoEspacioAlternancia = signal<TipoEspacioAlternancia>(leerLocal('soea_tipo_espacio_alternancia', 'Todos'));
  setTipoEspacioAlternancia(v: TipoEspacioAlternancia) { this.tipoEspacioAlternancia.set(v); escribirLocal('soea_tipo_espacio_alternancia', v); }

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

/** Grupo + asignatura + tipo de sesión: dos sesiones con la misma clave son intercambiables al fijar. */
export function claveTipoSesion(s: Sesion): string {
  return `${s.grupoId}|${s.asignaturaId}|${s.tipoFlujo ?? ''}|${s.virtual}`;
}

function leerLocal<T>(clave: string, porDefecto: T): T {
  try { const raw = localStorage.getItem(clave); return raw ? JSON.parse(raw) : porDefecto; } catch { return porDefecto; }
}
function escribirLocal(clave: string, valor: unknown) {
  try { localStorage.setItem(clave, JSON.stringify(valor)); } catch { /* cuota excedida o almacenamiento bloqueado */ }
}
