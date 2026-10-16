import { Asignatura, DisponibilidadUi, Docente, Espacio, Grupo, Sesion, TipoSesionUi } from '../../core/models';
import { HORA_APERTURA, HORA_CIERRE, HORA_CIERRE_SABADO } from '../../core/jornada';

/** Representación visual de una sesión atómica multi-slot. */
export interface MergedSesion {
  key: string;
  sesiones: Sesion[];
  dia: string;
  horaInicio: string;
  horaFin: string;
  virtual: boolean;
  alternancia: string;
  semana?: 'A' | 'B';
  asignaturaId: string;
  /** Grupo (cohorte) dueño de la sesión — distingue dos grupos de la misma asignatura. */
  grupoId?: string;
  docenteId?: string;
  espacioId?: string;
  espacioIdHogar?: string;
  tipoFlujo?: 'Laboratorio' | 'AulaVirtual';
  /** Id de la pareja de alternancia; agrupa la presencial con su contraparte virtual. */
  parejaId?: string;
  /** Contraparte virtual de esta sesión, dibujada como sub-caja DENTRO de la misma celda:
   *  la materia que ocupa el aula la semana contraria se sigue dictando, en línea. */
  contraparte?: MergedSesion;
}

/** Una sesión ya ubicada en la cuadrícula (grid-row/grid-column resueltos) lista para el template. */
export interface TarjetaGrilla {
  key: string; m: MergedSesion;
  fila: string; col: string;
  /** Menos de 2 horas: la tarjeta solo muestra asignatura y grupo (ver plantilla). */
  g1h: boolean;
  texto: string;
}
/** Botón "+N" de un tramo con más de MAX_CARRILES-1 sesiones simultáneas. */
export interface GrupoMasGrilla { fila: string; items: MergedSesion[]; texto: string; }
/** Sesión que no se pudo ubicar en la cuadrícula (ver ubicarEnGrilla) — nunca se descarta. */
export interface SesionFuera { m: MergedSesion; motivo: string; texto: string; }
export interface DiaGrilla {
  valor: string; corto: string; etiqueta: string;
  tarjetas: TarjetaGrilla[]; mas: GrupoMasGrilla[];
  /** Fila donde empieza el bloque "Cerrado" — solo el sábado cierra antes que el resto. */
  filaCierre?: number;
}
export interface GrillaSemanal { dias: DiaGrilla[]; fuera: SesionFuera[]; total: number; }
/** Item intermedio de distribuirEnCarriles: la posición ya resuelta (ini/fin en minutos, fila0/
 *  fila1 en filas de grid) más la sesión que representa. */
export interface ItemGrilla extends PosicionGrilla { m: MergedSesion; }

/**
 * "Cálculo I · G1 (lunes 08:00–10:00)" — identifica una sesión en un mensaje de conflicto por
 * asignatura/grupo, día y hora, para que "Sesión 1"/"Sesión 2" no queden indistinguibles cuando
 * ambas son de la misma asignatura (p. ej. dos grupos distintos en el mismo horario).
 * Con programaById agrega el programa: el import nombra los grupos "BIOQUIMICA - Grupo 1" en
 * cada programa, así que sin él dos grupos distintos se leían igual en un aviso de choque.
 */
export function describirSesionConflicto(
  s: { asignaturaId: string; grupoId?: string; dia: string; horaInicio: string; horaFin: string },
  asignaturas: Asignatura[], grupos: Grupo[], programaById?: ReadonlyMap<string, { nombre: string }>
): string {
  const a = asignaturas.find(x => x.id === s.asignaturaId);
  const asig = a?.nombre ?? 'asignatura sin nombre';
  const grupo = s.grupoId ? grupos.find(g => g.id === s.grupoId)?.nombre : undefined;
  // "QUIMICA GENERAL · QUIMICA GENERAL - Grupo 4" repetía la asignatura: el nombre del grupo importado ya la trae.
  let nombre = !grupo ? asig : grupo.startsWith(asig) ? grupo : `${asig} · ${grupo}`;
  const programa = a && programaById?.get(a.programaId)?.nombre;
  if (programa) nombre += ` · ${programa}`;
  return `${nombre} (${s.dia} ${s.horaInicio}–${s.horaFin})`;
}

/** Espejo de horario-api.service.ts#diffHoras — usado solo como fallback cuando `duracionHoras`
 *  no viene poblada (FE11: sigue duplicado en varios sitios, fuera de alcance de este fix). */
export function diffHorasEntre(inicio: string, fin: string): number {
  const [hi, mi] = inicio.split(':').map(Number);
  const [hf, mf] = fin.split(':').map(Number);
  return Math.max(1, (hf * 60 + mf - (hi * 60 + mi)) / 60);
}

export const hhmm = (h: number): string => `${String(h).padStart(2, '0')}:00`;
export const HORAS_GRILLA: readonly string[] = Array.from({ length: HORA_CIERRE - HORA_APERTURA }, (_, i) => hhmm(HORA_APERTURA + i));
export const DIAS_GRILLA: readonly { valor: string; corto: string; etiqueta: string }[] = [
  { valor: 'lunes', corto: 'Lun', etiqueta: 'Lunes' },
  { valor: 'martes', corto: 'Mar', etiqueta: 'Martes' },
  { valor: 'miercoles', corto: 'Mié', etiqueta: 'Miércoles' },
  { valor: 'jueves', corto: 'Jue', etiqueta: 'Jueves' },
  { valor: 'viernes', corto: 'Vie', etiqueta: 'Viernes' },
  { valor: 'sabado', corto: 'Sáb', etiqueta: 'Sábado' },
];
/** Hora de cierre de la jornada para ese día — el sábado cierra antes que el resto de la semana. */
export function cierreDe(dia: string): number { return dia === 'sabado' ? HORA_CIERRE_SABADO : HORA_CIERRE; }

/**
 * FE5/FE16/FE17 auditoría: estas tres reglas vivían duplicadas —y una de las copias, distinta—
 * entre los diálogos de crear, editar y fijar sesión. Fuente única aquí: CrearSesionDialogComponent
 * (también en modo sesión fija) y EditarSesionDialogComponent las usan en vez de reimplementarlas.
 */

/** True si [newStart, newEnd) se solapa con el span horario de `s`, en índices de `horasDisponibles`. */
export function seSolapanHorarios(s: Sesion, newStart: number, newEnd: number, horasDisponibles: readonly string[]): boolean {
  const sStart = horasDisponibles.indexOf(s.horaInicio);
  if (sStart < 0) return false;
  const sDur = Math.max(1, Math.round(s.duracionHoras ?? diffHorasEntre(s.horaInicio, s.horaFin)));
  return newStart < sStart + sDur && sStart < newEnd;
}

/**
 * Espejo (cliente) de ModalidadSemanal.CompartenSemanaDeEspacio (backend): dos sesiones solo
 * chocan por aula si ocupan el aula alguna semana en común. Lo que no alterna la ocupa las DOS,
 * así que choca con todo; una pareja TipoA/TipoB no choca nunca entre sí. FE5: el diálogo de
 * Crear no aplicaba esta exención — bloqueaba una franja legítima (una TipoB en el mismo
 * bloque/aula que una TipoA ya existente) por no descartar el caso en que las semanas nunca
 * coinciden.
 */
export function nuncaCoexisteEnSemana(semanaA: Sesion['semana'], semanaB: Sesion['semana']): boolean {
  return !!semanaA && !!semanaB && semanaA !== semanaB;
}

/**
 * FE16 auditoría: los diálogos acotaban el sábado a las 13:00 pero casi ninguno comprobaba el
 * límite general de L-V — un `endIdx` fuera de
 * `horasDisponibles` daba `undefined` al indexar, y cada diálogo caía a `?? horaInicio`, dejando
 * pasar un "fin = inicio" silencioso (p. ej. "lunes 21:00–21:00") en vez de rechazar el horario.
 */
export function finDeJornadaOk(dia: string, endIdx: number, horasDisponibles: readonly string[]): boolean {
  if (endIdx > horasDisponibles.length) return false;
  if (dia === 'sabado' && endIdx > horasDisponibles.indexOf(hhmm(HORA_CIERRE_SABADO))) return false;
  return true;
}

/**
 * Espejo (cliente) de CalculadorEspaciosSesion.Candidatos: HC-S05 (requisito de espacio del
 * grupo, si lo hay) ∩ HC-S03 (tipo por defecto según TipoSesion cuando no hay requisito). FE17:
 * solo el diálogo de Crear lo aplicaba — Editar dejaba mover un laboratorio a un salón cualquiera,
 * o una sesión con aula fija de grupo a un aula distinta de la exigida.
 */
export function espaciosPermitidosPara(tipo: TipoSesionUi, grupo: Grupo | undefined, espacios: Espacio[]): Espacio[] {
  if (tipo === 'TeoriaVirtual') return [];
  const requisito = grupo?.requisitosEspacio?.find(r => r.tipoSesion === tipo);
  if (requisito?.espacioId) return espacios.filter(e => e.id === requisito.espacioId);
  if (requisito?.tipoEspacio) return espacios.filter(e => e.tipo === (requisito.tipoEspacio === 'Salon' ? 'Salón' : requisito.tipoEspacio));
  return tipo === 'Laboratorio' ? espacios.filter(e => e.tipo === 'Laboratorio') : espacios.filter(e => e.tipo !== 'Laboratorio');
}

/** Minutos desde medianoche de "HH:mm", o NaN si el formato no es válido. */
export function minutosDe(horaHHmm: string): number {
  const m = /^(\d{2}):(\d{2})$/.exec(horaHHmm ?? '');
  return m ? Number(m[1]) * 60 + Number(m[2]) : NaN;
}

export const hhmmDeMinutos = (m: number): string => `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;

/** Ventana declarada de un día en minutos [desde, hasta), o null si el día está marcado no disponible.
 *  Espejo de DisponibilidadSemanal.VentanaDe (backend): la franja general la decide el prefijo de la
 *  etiqueta; una franja específica ilegible o "Todo el día" no restringe. Una franja legible con
 *  desde >= hasta se devuelve tal cual, como en el backend: ventana vacía, ninguna clase cabe ese día. */
export function ventanaDelDia(d: { noDisponible?: boolean; tipo?: string; franjaGeneral?: string; desde?: string; hasta?: string }): { desde: number; hasta: number } | null {
  if (d.noDisponible) return null;
  if (d.tipo === 'Franja específica') {
    const desde = minutosDe(d.desde ?? ''), hasta = minutosDe(d.hasta ?? '');
    if (!Number.isNaN(desde) && !Number.isNaN(hasta)) return { desde, hasta };
  }
  const franja = (d.franjaGeneral ?? '').toLowerCase();
  if (franja.startsWith('matutino')) return { desde: 6 * 60, hasta: 12 * 60 };
  if (franja.startsWith('vespertino')) return { desde: 12 * 60, hasta: 18 * 60 };
  if (franja.startsWith('nocturno')) return { desde: 18 * 60, hasta: 22 * 60 };
  return { desde: HORA_APERTURA * 60, hasta: HORA_CIERRE * 60 };
}

/** conIds: ids de las sesiones con las que choca (mismo orden que choques) — permite listar cada
 *  par una sola vez en el resumen sin perder el aviso en ninguna de las dos sesiones. */
export interface AvisosDocente { choques: string[]; conIds: string[]; fueraDeDisponibilidad?: string; }

/**
 * Sesiones fuera de la disponibilidad declarada de su grupo (HC-G01), por id de sesión → motivo.
 * Espejo de Grupo.ObtenerDisponibilidadSemanal (backend, diaSinEntradaDisponible: false): JSON
 * nulo/vacío/{} = sin restricción; si declara algún día, un día ausente cuenta como cerrado.
 * El motor ya lo respeta al generar; esto avisa de horarios guardados con datos/reglas anteriores.
 */
export function fueraDisponibilidadGrupo(sesiones: Sesion[], grupos: Grupo[]): Map<string, string> {
  const fuera = new Map<string, string>();
  const dispPorGrupo = new Map<string, DisponibilidadUi>();
  for (const g of grupos) {
    try {
      const obj = g.disponibilidadUiJson ? JSON.parse(g.disponibilidadUiJson) : null;
      if (obj && typeof obj === 'object' && Object.keys(obj).length > 0) dispPorGrupo.set(g.id, obj);
    } catch { /* ilegible = sin restricción, igual que el backend */ }
  }
  for (const s of sesiones) {
    if (s.esContraparteVirtual || !s.grupoId) continue;
    const disp = dispPorGrupo.get(s.grupoId);
    if (!disp) continue;
    const declarado = disp[s.dia];
    const v = declarado ? ventanaDelDia(declarado) : null;
    if (v && minutosDe(s.horaInicio) >= v.desde && minutosDe(s.horaFin) <= v.hasta) continue;
    const dia = DIAS_GRILLA.find(d => d.valor === s.dia)?.etiqueta.toLowerCase() ?? s.dia;
    fuera.set(s.id, v
      ? `el grupo solo está disponible el ${dia} de ${hhmmDeMinutos(v.desde)} a ${hhmmDeMinutos(v.hasta)}`
      : `el grupo no está disponible el ${dia}`);
  }
  return fuera;
}

/**
 * Avisos del docente por id de sesión. NO bloquean: el docente está fuera de la generación (CR-08)
 * pero el import ya lo trae en el grupo, así que el horario generado puede dejarlo en dos clases a
 * la vez o fuera de su disponibilidad sin que nada lo impida — esto lo hace visible.
 *  - choques (HC-I01): otra clase del mismo docente que se solapa. Espejo de
 *    ValidadorRestriccionesDuras.ValidarDocentes: independiente de la semana (ALT-05 — la sesión que
 *    alterna se sigue dictando virtual la semana contraria, en la misma franja).
 *  - fueraDeDisponibilidad (HC-I02, degradada a aviso): la franja cae fuera de lo que declaró el docente.
 */
export function avisosDocentePorSesion(sesiones: Sesion[], docentes: Docente[], asignaturas: Asignatura[], grupos: Grupo[], programaById?: ReadonlyMap<string, { nombre: string }>): Map<string, AvisosDocente> {
  const avisos = new Map<string, AvisosDocente>();
  const de = (id: string) => avisos.get(id) ?? avisos.set(id, { choques: [], conIds: [] }).get(id)!;
  const docentePorId = new Map(docentes.map(d => [d.id, d]));
  const nombreDe = (id: string) => docentePorId.get(id)?.nombre ?? 'El docente';
  // La contraparte virtual derivada repite el id de su sesión: cada sesión cuenta una sola vez.
  const propias = sesiones.filter(s => s.docenteId && !s.esContraparteVirtual && minutosDe(s.horaFin) > minutosDe(s.horaInicio));

  const porDocenteDia = new Map<string, Sesion[]>();
  for (const s of propias) {
    const k = `${s.docenteId}|${s.dia}`;
    porDocenteDia.set(k, [...(porDocenteDia.get(k) ?? []), s]);
  }
  for (const lista of porDocenteDia.values()) {
    for (let i = 0; i < lista.length; i++) {
      for (let j = i + 1; j < lista.length; j++) {
        const a = lista[i], b = lista[j];
        if (!(minutosDe(a.horaInicio) < minutosDe(b.horaFin) && minutosDe(b.horaInicio) < minutosDe(a.horaFin))) continue;
        const nombre = nombreDe(a.docenteId!);
        de(a.id).choques.push(`${nombre} tiene otra clase a la misma hora: ${describirSesionConflicto(b, asignaturas, grupos, programaById)}.`);
        de(a.id).conIds.push(b.id);
        de(b.id).choques.push(`${nombre} tiene otra clase a la misma hora: ${describirSesionConflicto(a, asignaturas, grupos, programaById)}.`);
        de(b.id).conIds.push(a.id);
      }
    }
  }

  for (const s of propias) {
    const declarado = docentePorId.get(s.docenteId!)?.disponibilidad?.[s.dia];
    if (!declarado) continue; // día sin declarar = sin restricción (DisponibilidadSemanal.PermiteBloque)
    const v = ventanaDelDia(declarado);
    if (v && minutosDe(s.horaInicio) >= v.desde && minutosDe(s.horaFin) <= v.hasta) continue;
    const dia = DIAS_GRILLA.find(d => d.valor === s.dia)?.etiqueta.toLowerCase() ?? s.dia;
    de(s.id).fueraDeDisponibilidad = v
      ? `Fuera de la disponibilidad de ${nombreDe(s.docenteId!)}: el ${dia} solo está disponible de ${hhmmDeMinutos(v.desde)} a ${hhmmDeMinutos(v.hasta)}.`
      : `Fuera de la disponibilidad de ${nombreDe(s.docenteId!)}: marcó el ${dia} como no disponible.`;
  }
  return avisos;
}

/**
 * Choques de aula por id de sesión: dos clases presenciales en el mismo espacio, mismo día, con
 * franjas solapadas y alguna semana en común (ver nuncaCoexisteEnSemana). Un horario generado no
 * debería tener ninguno; sirve sobre todo para el modo borrador, donde las clases se agregan a mano.
 * Mismo formato que avisosDocentePorSesion (choques + conIds) para listar cada par una sola vez.
 */
export function choquesEspacioPorSesion(sesiones: Sesion[], espacios: Espacio[], asignaturas: Asignatura[], grupos: Grupo[], programaById?: ReadonlyMap<string, { nombre: string }>): Map<string, AvisosDocente> {
  const choques = new Map<string, AvisosDocente>();
  const de = (id: string) => choques.get(id) ?? choques.set(id, { choques: [], conIds: [] }).get(id)!;
  const nombreEspacio = new Map(espacios.map(e => [e.id, e.nombre]));
  const porEspacioDia = new Map<string, Sesion[]>();
  for (const s of sesiones) {
    if (s.virtual || s.esContraparteVirtual || !s.espacioId || !(minutosDe(s.horaFin) > minutosDe(s.horaInicio))) continue;
    const k = `${s.espacioId}|${s.dia}`;
    porEspacioDia.set(k, [...(porEspacioDia.get(k) ?? []), s]);
  }
  for (const lista of porEspacioDia.values()) {
    for (let i = 0; i < lista.length; i++) {
      for (let j = i + 1; j < lista.length; j++) {
        const a = lista[i], b = lista[j];
        if (nuncaCoexisteEnSemana(a.semana, b.semana)) continue;
        if (!(minutosDe(a.horaInicio) < minutosDe(b.horaFin) && minutosDe(b.horaInicio) < minutosDe(a.horaFin))) continue;
        const aula = nombreEspacio.get(a.espacioId!) ?? 'El espacio';
        de(a.id).choques.push(`${aula} ya está ocupado a esa hora por ${describirSesionConflicto(b, asignaturas, grupos, programaById)}.`);
        de(a.id).conIds.push(b.id);
        de(b.id).choques.push(`${aula} ya está ocupado a esa hora por ${describirSesionConflicto(a, asignaturas, grupos, programaById)}.`);
        de(b.id).conIds.push(a.id);
      }
    }
  }
  return choques;
}

/** Filas de resumen (una por sesión, cada par listado una sola vez) de un mapa de choques por sesión. */
export function filasDeChoques(sesiones: Sesion[], choques: Map<string, AvisosDocente>, asignaturas: Asignatura[], grupos: Grupo[], programaById?: ReadonlyMap<string, { nombre: string }>) {
  const listadas = new Set<string>();
  const filas: { s: Sesion; etiqueta: string; detalle: string }[] = [];
  for (const s of sesiones) {
    const av = choques.get(s.id);
    if (s.esContraparteVirtual || !av?.choques.length) continue;
    const nuevos = av.choques.filter((_, i) => !listadas.has(av.conIds[i]));
    listadas.add(s.id);
    if (nuevos.length) filas.push({ s, etiqueta: describirSesionConflicto(s, asignaturas, grupos, programaById), detalle: nuevos.join(' ') });
  }
  return filas;
}

/** Ubicación de una sesión en la cuadrícula: fila1 es exclusiva (grid-row: "fila0 / fila1"),
 *  fila 1 = HORA_APERTURA. */
export interface PosicionGrilla { ini: number; fin: number; fila0: number; fila1: number; }

/**
 * Ubica una sesión en la cuadrícula por minutos de inicio/fin — no por índice de celda, que es lo
 * que hacía que una sesión desalineada (media hora, fuera de jornada) desapareciera en silencio o
 * corriera las columnas del resto del día (ver auditoría UI 2026-09). Nunca descarta: cuando la
 * sesión no cabe, devuelve el motivo para que el llamador la liste aparte en vez de perderla.
 */
export function ubicarEnGrilla(dia: string, horaInicio: string, horaFin: string): PosicionGrilla | { motivo: string } {
  if (!DIAS_GRILLA.some(d => d.valor === dia)) return { motivo: `día fuera de la cuadrícula (${dia || 'sin día'})` };
  const ini = minutosDe(horaInicio), fin = minutosDe(horaFin);
  if (!Number.isFinite(ini) || !Number.isFinite(fin) || !(fin > ini)) return { motivo: 'horario inválido' };
  if (ini % 60 !== 0 || fin % 60 !== 0) return { motivo: 'no empieza o no termina en una hora en punto' };
  if (ini < HORA_APERTURA * 60) return { motivo: `empieza antes de ${hhmm(HORA_APERTURA)}` };
  if (fin > cierreDe(dia) * 60) return { motivo: `termina después del cierre de la jornada (${hhmm(cierreDe(dia))})` };
  return { ini, fin, fila0: ini / 60 - HORA_APERTURA + 1, fila1: fin / 60 - HORA_APERTURA + 1 };
}

export const MAX_CARRILES = 3;
export interface TarjetaEnCarril<T> { item: T; carril: number; carriles: number; }
export interface GrupoDesbordado<T> { ini: number; fin: number; items: T[]; }
export interface ResultadoCarriles<T> { tarjetas: TarjetaEnCarril<T>[]; mas: GrupoDesbordado<T>[]; }

/**
 * Reparte sesiones simultáneas en carriles (columnas) lado a lado, sin ocultar ni encimar ninguna
 * — reemplaza el rowspan por celda de inicio de la tabla anterior, que perdía sesiones escalonadas
 * o solapadas (ver auditoría UI 2026-09). "Interval partitioning" greedy: ordena por inicio y
 * asigna a cada sesión el primer carril cuyo último fin ya pasó — O(n log n). Si un racimo (grupo
 * de sesiones conectadas transitivamente por solape) necesita más carriles que `max`, los primeros
 * `max - 1` quedan como tarjetas y el resto se agrupa en un "+N" por tramo solapado (un grupo de
 * un solo elemento se dibuja como tarjeta normal — un "+1" no aporta nada).
 */
export function distribuirEnCarriles<T extends { ini: number; fin: number }>(
  items: readonly T[], max = MAX_CARRILES
): ResultadoCarriles<T> {
  const tarjetas: TarjetaEnCarril<T>[] = [];
  const desborde: { ini: number; fin: number; item: T }[] = [];
  const orden = [...items].sort((a, b) => a.ini - b.ini || b.fin - a.fin);

  for (let i = 0; i < orden.length; ) {
    // Racimo: sesiones conectadas por solape con fin EXCLUSIVO — 08-10 y 10-12 no se conectan.
    let j = i, finRacimo = -Infinity;
    while (j < orden.length && (j === i || orden[j].ini < finRacimo)) { finRacimo = Math.max(finRacimo, orden[j].fin); j++; }

    const finDeCarril: number[] = [];
    const racimo = orden.slice(i, j).map(item => {
      let carril = finDeCarril.findIndex(fin => fin <= item.ini);
      if (carril < 0) carril = finDeCarril.length;
      finDeCarril[carril] = item.fin;
      return { item, carril };
    });
    i = j;

    const k = finDeCarril.length;
    if (k <= max) { racimo.forEach(r => tarjetas.push({ ...r, carriles: k })); continue; }
    for (const r of racimo) {
      if (r.carril < max - 1) tarjetas.push({ ...r, carriles: max });
      else desborde.push({ ini: r.item.ini, fin: r.item.fin, item: r.item });
    }
  }

  // Agrupa el desborde en tramos solapados propios — dos racimos están separados en el tiempo por
  // construcción, así que un recorrido único (ordenado) basta, sin repetir el agrupamiento por racimo.
  desborde.sort((a, b) => a.ini - b.ini);
  const mas: GrupoDesbordado<T>[] = [];
  for (const d of desborde) {
    const ult = mas[mas.length - 1];
    if (ult && d.ini < ult.fin) { ult.items.push(d.item); ult.fin = Math.max(ult.fin, d.fin); }
    else mas.push({ ini: d.ini, fin: d.fin, items: [d.item] });
  }
  const solos = mas.filter(m => m.items.length === 1);
  solos.forEach(m => tarjetas.push({ item: m.items[0], carril: max - 1, carriles: max }));

  return { tarjetas, mas: mas.filter(m => m.items.length > 1) };
}

/**
 * El backend siempre reporta un MensajeError técnico ("status del solver: Infeasible", "HC-SEP",
 * "HC-ALT") pensado para quien lee el código, no para un coordinador académico — y el snackbar lo
 * reemplazaba por una frase genérica que tampoco ayuda ("No se encontró un horario factible para
 * N asignatura(s) en M espacio(s)."). Traduce motivoInfactibilidad (siempre real) a una guía
 * accionable en español simple, y si gruposEnConflicto trae Ids (diagnóstico opcional de Fase 2)
 * nombra los grupos reales en vez de un conteo vacío — nunca inventa una causa que el backend no
 * reportó; si no hay nada estructurado, cae a un mensaje genérico pero igual sin jerga interna.
 */
export function mensajeInfactibilidadAmigable(
  motivo: string | undefined,
  gruposEnConflictoIds: string[] | undefined,
  grupos: Grupo[],
  totalAsignaturas: number,
  totalEspacios: number,
  /** Solo para motivo Capacidad: catálogo con el que nombrar asignatura, programa y espacio. */
  capacidad?: { espacioLimitanteId?: string; espacios: Espacio[]; asignaturas: Asignatura[]; programaById: Map<string, { nombre: string }> }
): string {
  if (motivo === 'Capacidad') {
    const g = grupos.find(x => x.id === gruposEnConflictoIds?.[0]);
    const e = capacidad?.espacios.find(x => x.id === capacidad.espacioLimitanteId);
    if (g && e) {
      const asig = capacidad!.asignaturas.find(a => a.id === g.asignaturaId);
      const prog = capacidad!.programaById.get(asig?.programaId ?? g.programaId);
      return `El grupo ${g.nombre}${asig ? ` de la asignatura ${asig.nombre}` : ''}${prog ? ` del programa ${prog.nombre}` : ''} ` +
        `tiene ${g.estudiantesInscritos} estudiantes y el ${e.tipo.toLowerCase()} ${e.nombre} (el más grande que puede usar) ` +
        `solo tiene capacidad para ${e.capacidad}. Asígnele un espacio con más capacidad o reduzca el grupo.`;
    }
    return 'Un grupo tiene más estudiantes que la capacidad de los espacios que puede usar. Asígnele un espacio más grande o reduzca el grupo.';
  }
  const nombresConflicto = (gruposEnConflictoIds ?? [])
    .map(id => grupos.find(g => g.id === id)?.nombre)
    .filter((n): n is string => !!n);
  if (nombresConflicto.length > 0) {
    return `No se encontró un horario factible. Grupo(s) en conflicto: ${nombresConflicto.join(', ')} — revíselos en el catálogo (posible espacio compartido o pareja de alternancia) y vuelva a generar.`;
  }
  switch (motivo) {
    case 'Espacio':
      return 'No hay espacio físico suficiente para todas las sesiones presenciales en los horarios permitidos. Revise la disponibilidad de los grupos o libere algún espacio.';
    case 'VentanaHoraria':
      return 'Una o más asignaturas tienen una ventana horaria demasiado estrecha para todas sus sesiones. Revise la ventana horaria configurada en esas asignaturas.';
    case 'FranjaGrupo':
      return 'Uno o más grupos tienen más sesiones de las que caben en su disponibilidad horaria declarada. Revise la disponibilidad de esos grupos en el catálogo.';
    case 'Datos':
      return 'Faltan datos necesarios para generar el horario. Revise que haya asignaturas, docentes y espacios cargados.';
    case 'Timeout':
      return 'El sistema no pudo determinar si existe una solución a tiempo. Intente de nuevo, o reduzca el número de asignaturas o grupos en este intento.';
    default:
      return `No se encontró un horario factible para ${totalAsignaturas} asignatura(s) en ${totalEspacios} espacio(s). Pruebe con disponibilidad más flexible o menos requisitos de espacio fijo.`;
  }
}

/** Resultado de una validación de los diálogos de sesión. */
export interface Check { ok: boolean; texto: string; }
