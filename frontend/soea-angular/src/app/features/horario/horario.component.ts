import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatMenuModule } from '@angular/material/menu';
import { RouterModule } from '@angular/router';
import { StateService } from '../../core/state.service';
import { HorarioApiService } from '../../core/horario-api.service';
import { PersistenciaService } from '../../core/persistencia.service';
import { CatalogoService } from '../../core/catalogo.service';
import { Asignatura, Docente, Espacio, Grupo, Sesion, TipoSesionUi, tipoFlujoDesde, esVirtualDesde } from '../../core/models';
import { nuevoId } from '../../core/id.util';
import { mensajeErrorHttp, limpiarEtiquetaInterna } from '../../core/http-error.util';
import { SearchableSelectComponent, SearchableOption } from '../../shared/searchable-select/searchable-select.component';

/** Representación visual de una sesión atómica multi-slot. */
interface MergedSesion {
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
interface TarjetaGrilla {
  key: string; m: MergedSesion;
  fila: string; col: string;
  /** No alterna, mostrada en gris en Semana B: ocupa el aula todas las semanas. */
  siempre: boolean;
  /** Menos de 2 horas: la tarjeta solo muestra asignatura y grupo (ver plantilla). */
  g1h: boolean;
  texto: string;
}
/** Botón "+N" de un tramo con más de MAX_CARRILES-1 sesiones simultáneas. */
interface GrupoMasGrilla { fila: string; items: MergedSesion[]; texto: string; }
/** Sesión que no se pudo ubicar en la cuadrícula (ver ubicarEnGrilla) — nunca se descarta. */
interface SesionFuera { m: MergedSesion; motivo: string; texto: string; }
interface DiaGrilla {
  valor: string; corto: string; etiqueta: string;
  tarjetas: TarjetaGrilla[]; mas: GrupoMasGrilla[];
  /** Fila donde empieza el bloque "Cerrado" — solo el sábado cierra antes que el resto. */
  filaCierre?: number;
}
interface GrillaSemanal { dias: DiaGrilla[]; fuera: SesionFuera[]; total: number; }
/** Item intermedio de distribuirEnCarriles: la posición ya resuelta (ini/fin en minutos, fila0/
 *  fila1 en filas de grid) más la sesión que representa. */
interface ItemGrilla extends PosicionGrilla { m: MergedSesion; }

/**
 * "Cálculo I · G1 (lunes 08:00–10:00)" — identifica una sesión en un mensaje de conflicto por
 * asignatura/grupo, día y hora, para que "Sesión 1"/"Sesión 2" no queden indistinguibles cuando
 * ambas son de la misma asignatura (p. ej. dos grupos distintos en el mismo horario).
 */
function describirSesionConflicto(
  s: { asignaturaId: string; grupoId?: string; dia: string; horaInicio: string; horaFin: string },
  asignaturas: Asignatura[], grupos: Grupo[]
): string {
  const asig = asignaturas.find(a => a.id === s.asignaturaId)?.nombre ?? 'asignatura sin nombre';
  const grupo = s.grupoId ? grupos.find(g => g.id === s.grupoId)?.nombre : undefined;
  const nombre = grupo ? `${asig} · ${grupo}` : asig;
  return `${nombre} (${s.dia} ${s.horaInicio}–${s.horaFin})`;
}

/** Espejo de horario-api.service.ts#diffHoras — usado solo como fallback cuando `duracionHoras`
 *  no viene poblada (FE11: sigue duplicado en varios sitios, fuera de alcance de este fix). */
function diffHorasEntre(inicio: string, fin: string): number {
  const [hi, mi] = inicio.split(':').map(Number);
  const [hf, mf] = fin.split(':').map(Number);
  return Math.max(1, (hf * 60 + mf - (hi * 60 + mi)) / 60);
}

/** Jornada institucional — espejo de SOEA.Domain/Services/GrillaInstitucional.cs. Única fuente:
 *  no declarar el rango horario en ningún otro sitio del componente ni de sus diálogos. */
export const HORA_APERTURA = 6;
export const HORA_CIERRE = 22;
export const HORA_CIERRE_SABADO = 14;
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
function minutosDe(horaHHmm: string): number {
  const m = /^(\d{2}):(\d{2})$/.exec(horaHHmm ?? '');
  return m ? Number(m[1]) * 60 + Number(m[2]) : NaN;
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
  totalEspacios: number
): string {
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

@Component({
  selector: 'app-horario',
  standalone: true,
  imports: [CommonModule, DatePipe, FormsModule, MatDialogModule, MatSnackBarModule, RouterModule, MatMenuModule],
  template: `
    <div class="hz-head">
      <div class="hz-head-l"><span class="soea-tag">Paso 2</span><h1 class="hz-title">Horario semanal</h1></div>
      <div class="hz-head-r">
        <span class="text-muted adv-menu" [matMenuTriggerFor]="avanzadoMenu" title="Más opciones">
          <span class="tag tag-outline" style="font-size:9px">Más opciones</span> ⭳ ⭱ ⋯
        </span>
        <mat-menu #avanzadoMenu="matMenu">
          <button mat-menu-item (click)="exportarHorario()" [disabled]="state.sesiones().length === 0">⭳ Descargar copia del horario</button>
          <button mat-menu-item (click)="importarInput.click()">⭱ Abrir copia guardada</button>
        </mat-menu>
        <input #importarInput type="file" accept=".json" style="display:none" (change)="importarHorario($event)">
      </div>
    </div>

    <div class="blueprint elev-md frame">
      <i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>

      <!-- toolbar -->
      <div class="toolbar">
        @if (hayAlternancia()) {
          <span class="tb-lbl" id="lbl-semana">Semana</span>
          <div class="seg" role="radiogroup" aria-labelledby="lbl-semana">
            <label class="seg-opt" role="radio" [attr.aria-checked]="semanaVista() === 'A'" [class.on]="semanaVista() === 'A'" (click)="selectWeek('A')">Semana A · horario completo</label>
            <label class="seg-opt" role="radio" [attr.aria-checked]="semanaVista() === 'B'" [class.on]="semanaVista() === 'B'" (click)="selectWeek('B')">Semana B · lo que alterna (el resto en gris)</label>
          </div>
        }
        <div class="legend">
          <span><span class="lg-box pres"></span> Presencial</span>
          <span><span class="lg-box virt"></span> ⌁ Virtual</span>
        </div>
        <div class="tb-right">
          <button class="btn btn-secondary" (click)="abrirSesionFija()">＋ Fijar una clase</button>
          @if (state.sesiones().length > 0) {
            <button class="btn btn-secondary" (click)="abrirCrearSesion()" [disabled]="loadingBackend()">＋ Sesión manual</button>
            <button class="btn btn-secondary" (click)="guardarComoBase()">Guardar como punto de partida</button>
          }
          <button class="btn btn-primary" (click)="generarHorario()" [disabled]="loadingBackend() || generandoHorario()">▶ Generar horario</button>
        </div>
      </div>

      <!-- leyenda de colores por asignatura (petición 12) -->
      @if (leyendaAsignaturas().length > 0) {
        <div class="asig-legend">
          @for (item of leyendaAsignaturas(); track item.id) {
            <span class="asig-legend-item"><span class="lg-box" [style.background]="item.color"></span>{{ item.nombre }}</span>
          }
        </div>
      }

      <!-- Estado C (HF-3): sin solución. El registro técnico del backend (Fase 1/2/3, HC-…, ms)
           nunca se muestra en pantalla — solo se puede copiar para adjuntar a un caso de soporte. -->
      @if (state.sesiones().length === 0 && state.executionLogs().length > 0) {
        <div class="fail">
          <div class="errb"><b>✕ Horario no generado.</b> {{ mensajeInfactible() }}</div>
          <button type="button" class="btn-link" (click)="copiarDetalleSoporte()">Copiar detalle para soporte</button>
        </div>
      }

      @if (state.espacios().length > 0) {
        <div class="grid-area">
          <div class="grid-main">
            <!-- selector de espacio -->
            <div class="space-sel">
              @for (esp of state.espacios(); track esp.id) {
                <button class="chip space-chip" [class.on]="!modoVirtual() && activeSpace()?.id === esp.id" (click)="selectSpace(esp)">{{ esp.nombre }}</button>
              }
              <button class="chip space-chip" [class.on]="modoVirtual()" (click)="selectVirtual()">⌁ Virtual (sin espacio)</button>
            </div>

            @if (!backendReady()) {
              <div class="soft backend-alert">No hay conexión con el sistema. <button class="btn btn-secondary" style="font-size:12px;padding:3px 10px" (click)="syncFromBackend()" [disabled]="loadingBackend()">Reintentar</button></div>
            }

            @if (grilla().fuera.length > 0) {
              <div class="soft fuera" role="region" aria-label="Sesiones que no se pueden ubicar en la cuadrícula">
                <b>{{ grilla().fuera.length }} sesión(es) que no se pueden ubicar en la cuadrícula:</b>
                <ul>
                  @for (f of grilla().fuera; track f.m.key) {
                    <li>
                      <button type="button" class="btn-link" (click)="abrirEditarSesion(f.m)">{{ getAsignaturaName(f.m) }}{{ grupoSuffix(f.m) }}</button>
                      — {{ f.motivo }}
                    </li>
                  }
                </ul>
              </div>
            }

            @if (state.sesiones().length === 0) {
              <p class="vacio">Todavía no hay horario. Pulse «Generar horario» o abra una copia guardada desde «Más opciones».</p>
            } @else if (grilla().total === 0) {
              <p class="vacio">
                @if (modoVirtual()) {
                  No hay sesiones virtuales{{ hayAlternancia() ? ' en la Semana ' + semanaVista() : '' }}.
                } @else {
                  {{ activeSpace()?.nombre }} no tiene sesiones{{ hayAlternancia() ? ' en la Semana ' + semanaVista() : '' }}.
                }
              </p>
            }

            <div class="wk-scroll">
              <div class="wk">
                @for (d of grilla().dias; track d.valor; let i = $index) {
                  <div class="wk-dhdr" [style.grid-column]="i + 2">{{ d.corto }}</div>
                }
                <div class="wk-horas" [style.grid-template-rows]="filasCss" aria-hidden="true">
                  @for (h of horas; track h) { <span>{{ h.slice(0,2) }}</span> }
                </div>
                @for (d of grilla().dias; track d.valor; let i = $index) {
                  <div class="wk-dia" role="group" [attr.aria-label]="d.etiqueta" [style.grid-column]="i + 2" [style.grid-template-rows]="filasCss">
                    @if (d.filaCierre) {
                      <div class="wk-cerrado" [style.grid-row]="d.filaCierre + ' / -1'" aria-hidden="true">Cerrado</div>
                    }
                    @for (t of d.tarjetas; track t.key) {
                      <button type="button" class="gcell" [class.gvirt]="t.m.virtual" [class.gsiempre]="t.siempre" [class.g1h]="t.g1h"
                              [style.grid-row]="t.fila" [style.grid-column]="t.col"
                              [style.background]="t.siempre ? null : gcellBg(t.m)" [style.border-left-color]="altColor(t.m)"
                              [title]="t.texto" [attr.aria-label]="t.texto" (click)="abrirEditarSesion(t.m)">
                        <span class="s">@if (t.m.virtual) { ⌁ }{{ getAsignaturaName(t.m) }}</span>
                        <span class="g">{{ t.siempre ? 'todas las semanas' : (grupoSuffix(t.m).slice(3) || '—') }}</span>
                        <span class="det">
                          <span [class.nodoc]="!t.m.docenteId">{{ t.m.docenteId ? getDocenteName(t.m) : 'sin docente' }}</span>
                          <span class="t">{{ celdaEspacio(t.m) }} · {{ t.m.horaInicio }}–{{ t.m.horaFin }}</span>
                          @if (t.m.contraparte; as otra) { <span class="gsub">⌁ {{ getAsignaturaName(otra) }}{{ grupoSuffix(otra) }} · virtual</span> }
                        </span>
                      </button>
                    }
                    @for (x of d.mas; track x.fila) {
                      <button type="button" class="gmas" [style.grid-row]="x.fila" style="grid-column: 5 / span 2"
                              [matMenuTriggerFor]="masMenu" [matMenuTriggerData]="{ lista: x.items }" [attr.aria-label]="x.texto">+{{ x.items.length }}</button>
                    }
                  </div>
                }
              </div>
            </div>
            <div class="grid-foot text-muted">⋯ jornada {{ horaAperturaTexto }} – {{ horaCierreTexto }} (Sáb: {{ horaAperturaTexto }} – {{ horaCierreSabadoTexto }}) ⋯</div>

            <mat-menu #masMenu="matMenu">
              <ng-template matMenuContent let-lista="lista">
                @for (m of lista; track m.key) {
                  <button mat-menu-item (click)="abrirEditarSesion(m)" [title]="describir(m)">{{ getAsignaturaName(m) }}{{ grupoSuffix(m) }} · {{ m.horaInicio }}–{{ m.horaFin }}</button>
                }
              </ng-template>
            </mat-menu>
          </div>

          <!-- panel lateral: horarios base -->
          @if (state.horariosBases().length > 0) {
            <div class="blueprint side">
              <i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>
              <div class="pophd" style="font-size:13px">Puntos de partida</div>
              <div class="side-bd">
                @for (base of state.horariosBases(); track base.id) {
                  <div class="base-item" [class.on]="state.baseSeleccionadaId() === base.id">
                    <label class="radio" style="flex:1">
                      <input type="radio" name="base" [checked]="state.baseSeleccionadaId() === base.id" (click)="toggleBase(base.id)">
                      <span class="dot"></span>
                      <span class="base-info"><span class="base-name">{{ base.nombre }}</span><span class="text-muted base-meta">{{ base.sesiones.length }} ses · {{ base.creadoEn | date:'dd/MM HH:mm' }}</span></span>
                    </label>
                    <button type="button" class="material-icons ic-del" (click)="eliminarBase(base.id)" [attr.aria-label]="'Eliminar ' + base.nombre">delete</button>
                  </div>
                }
                @if (state.baseSeleccionada(); as base) {
                  <div class="okb" style="font-size:11.5px">✓ Al generar se respetarán las {{ base.sesiones.length }} clases fijadas en "{{ base.nombre }}"</div>
                }
              </div>
            </div>
          }
        </div>
      } @else {
        <div class="empty-state">
          <p>No hay datos cargados. Ve a <a routerLink="/catalogo"><strong>Catálogo</strong></a> para comenzar.</p>
        </div>
      }
    </div>
  `,
  styles: [`
    .hz-head { display: flex; align-items: flex-end; justify-content: space-between; gap: 12px; margin-bottom: 16px; flex-wrap: wrap; }
    .hz-head-l { display: flex; align-items: baseline; gap: 12px; }
    .soea-tag { font: 600 11px var(--font-heading); letter-spacing: .1em; text-transform: uppercase; background: var(--color-accent); color: #fff; padding: 4px 10px; }
    .hz-title { margin: 0; font-size: 26px; }
    .adv-menu { display: inline-flex; align-items: center; gap: 8px; font-size: 12px; border: 1px solid var(--color-divider); padding: 5px 10px; cursor: pointer; }

    .frame { background: var(--color-bg); }
    .toolbar { display: flex; align-items: center; gap: 16px; padding: 12px 22px; border-bottom: 1px solid var(--color-divider); flex-wrap: wrap; }
    .tb-lbl { font: 600 12px var(--font-heading); letter-spacing: .08em; text-transform: uppercase; color: var(--color-neutral-700); }
    .legend { display: flex; gap: 18px; align-items: center; font-size: 12px; color: var(--color-neutral-700); }
    .legend span { display: flex; gap: 7px; align-items: center; }
    .lg-box { width: 24px; height: 14px; border: 1px solid var(--color-neutral-700); border-left: 4px solid var(--color-accent); }
    .lg-box.virt { border-style: dashed; background: repeating-linear-gradient(-45deg, transparent 0 3px, color-mix(in srgb, var(--color-accent) 18%, transparent) 3px 5px); }
    .asig-legend { display: flex; gap: 14px; align-items: center; flex-wrap: nowrap; overflow-x: auto; padding: 2px 0 8px; font-size: 11.5px; color: var(--color-neutral-700); }
    .asig-legend-item { display: flex; gap: 6px; align-items: center; white-space: nowrap; flex: 0 0 auto; }
    .asig-legend-item .lg-box { width: 14px; height: 14px; border-left-width: 1px; flex: 0 0 auto; }
    .tb-right { margin-left: auto; display: flex; gap: 10px; flex-wrap: wrap; }

    .fail { padding: 16px 22px; display: flex; flex-direction: column; gap: 6px; align-items: flex-start; }

    .grid-area { padding: 18px 22px; display: flex; gap: 18px; align-items: flex-start; }
    .grid-main { flex: 1; min-width: 0; }
    .space-sel { display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 14px; }
    .space-chip { cursor: pointer; }
    .space-chip.on { background: var(--color-accent); color: #fff; border-color: var(--color-accent); }
    .backend-alert { margin-bottom: 12px; display: flex; align-items: center; gap: 10px; }

    .fuera { margin-bottom: 14px; }
    .fuera ul { margin: 6px 0 0; padding-left: 18px; }
    .fuera li { margin-bottom: 2px; }
    .btn-link { border: 0; background: none; padding: 0; color: var(--color-accent); text-decoration: underline; cursor: pointer; font: inherit; }
    .vacio { color: var(--color-neutral-700); font-size: 13px; padding: 18px 4px; margin: 0 0 12px; }

    /* Cuadrícula por carriles (ver ubicarEnGrilla/distribuirEnCarriles): cada tarjeta se posiciona
     * por grid-row/grid-column ya resueltos en TypeScript — sin rowspan, así que ninguna sesión
     * simultánea/escalonada queda oculta ni descuadra las columnas siguientes (auditoría UI 2026-09). */
    .wk-scroll { overflow: auto; max-height: calc(100vh - 220px); min-height: 420px; }
    .wk { --fila: 46px; display: grid; grid-template-columns: 44px repeat(6, minmax(112px, 1fr)); grid-template-rows: auto auto; column-gap: 4px; min-width: 760px; }
    .wk-dhdr { grid-row: 1; position: sticky; top: 0; z-index: 3; background: var(--color-bg);
               text-align: center; font: 600 13px var(--font-heading); letter-spacing: .04em; color: var(--color-neutral-800); padding-bottom: 4px; }
    .wk-horas { grid-row: 2; grid-column: 1; display: grid; position: sticky; left: 0; z-index: 2; background: var(--color-bg); }
    .wk-horas span { font-size: 11px; color: var(--color-neutral-700); text-align: right; padding-right: 6px; }
    .wk-dia { grid-row: 2; display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); column-gap: 3px; position: relative;
              background: repeating-linear-gradient(to bottom, var(--color-neutral-300) 0 1px, transparent 1px var(--fila)); }
    .wk-cerrado { grid-column: 1 / -1; background: repeating-linear-gradient(-45deg, var(--color-neutral-100) 0 6px, var(--color-neutral-300) 6px 7px);
                  font-size: 11px; color: var(--color-neutral-700); text-align: center; padding-top: 6px; }
    .gcell { margin: 1px 0; min-width: 0; min-height: 0; display: flex; flex-direction: column; gap: 1px; text-align: left; font: 11px/1.25 inherit; color: var(--color-text);
             border: 1px solid var(--color-neutral-800); border-left-width: 5px; background: var(--color-bg); padding: 3px 6px; overflow: hidden; cursor: pointer; }
    .gcell:hover { box-shadow: var(--shadow-sm); }
    .gcell:focus-visible { outline: 2px solid var(--color-accent); outline-offset: 1px; }
    .gvirt { border-style: dashed; background: repeating-linear-gradient(-45deg, var(--color-bg) 0 5px, color-mix(in srgb, var(--color-accent) 12%, transparent) 5px 8px); }
    .gcell .s { font: 600 12.5px/1.15 var(--font-heading); letter-spacing: .01em; display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 2; overflow: hidden; }
    .gcell .g, .gcell .det > span { display: block; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; color: var(--color-neutral-700); font-size: 10px; }
    .gcell .nodoc { color: var(--err-bd); font-weight: 500; }
    .gcell.g1h .s { -webkit-line-clamp: 1; } .gcell.g1h .det { display: none; }
    .gsiempre { background: var(--color-neutral-100); border-color: var(--color-neutral-500); border-left-color: var(--color-neutral-500) !important; }
    .gcell .gsub { margin-top: 2px; padding: 2px 4px; border: 1px dashed var(--color-neutral-500);
                   background: repeating-linear-gradient(-45deg, var(--color-bg) 0 5px, color-mix(in srgb, var(--color-accent) 12%, transparent) 5px 8px);
                   color: var(--color-neutral-700); font-size: 10px; line-height: 1.2; }
    .gmas { margin: 1px 0; border: 1px dashed var(--color-neutral-700); background: var(--color-neutral-100);
            font: 600 13px var(--font-heading); color: var(--color-neutral-800); cursor: pointer; }
    .gmas:focus-visible { outline: 2px solid var(--color-accent); outline-offset: 1px; }
    .grid-foot { text-align: center; font-size: 11px; margin-top: 8px; }

    .side { width: 250px; flex: none; background: var(--color-surface); }
    .side-bd { padding: 11px 13px; display: flex; flex-direction: column; gap: 8px; }
    .base-item { display: flex; align-items: center; gap: 6px; padding: 5px 7px; border: 1px solid var(--color-divider); }
    .base-item.on { border-color: var(--color-accent); background: var(--color-accent-100); }
    .base-info { display: flex; flex-direction: column; }
    .base-name { font-size: 13px; } .base-meta { font-size: 11px; }

    .empty-state { text-align: center; padding: 56px 32px; color: var(--color-neutral-600); }
  `]
})
export class HorarioComponent implements OnInit {
  state = inject(StateService);
  dialog = inject(MatDialog);
  snackBar = inject(MatSnackBar);
  horarioApi = inject(HorarioApiService);
  persistencia = inject(PersistenciaService);
  catalogo = inject(CatalogoService);

  readonly horas = HORAS_GRILLA;
  /** grid-template-rows compartido por la columna de horas y cada día — una fila de grid por
   *  hora de jornada, único lugar donde se traduce HORAS_GRILLA.length a CSS. */
  readonly filasCss = `repeat(${HORAS_GRILLA.length}, var(--fila))`;
  readonly horaAperturaTexto = hhmm(HORA_APERTURA);
  readonly horaCierreTexto = hhmm(HORA_CIERRE);
  readonly horaCierreSabadoTexto = hhmm(HORA_CIERRE_SABADO);

  activeSpace = signal<Espacio | null>(null);
  /** Chip "Virtual (sin espacio)" (petición 8 UI): aísla las teorías virtuales en su propia
   *  vista en vez de dejarlas dispersas/duplicadas entre los chips de espacio físico. */
  modoVirtual = signal(false);
  private readonly ESPACIO_VIRTUAL = '__virtual__';
  activeWeek = signal<'A' | 'B'>('A');
  loadingBackend = signal(false);
  /** Evita disparar dos POST /horario/generar concurrentes por doble-click antes de que
   *  llegue la primera respuesta — generarHorario() no compartía loadingBackend (ese solo lo
   *  toca syncFromBackend). */
  generandoHorario = signal(false);
  backendReady = signal(false);
  /** Guía accionable (sin jerga interna) del banner persistente de infactibilidad — ver
   *  mensajeInfactibilidadAmigable(). Vacío mientras no haya fallado ninguna generación, o
   *  tras una generación exitosa. */
  mensajeInfactible = signal('');

  ngOnInit() { this.syncFromBackend(); }
  constructor() { const espacios = this.state.espacios(); if (espacios.length > 0) this.activeSpace.set(espacios[0]); }

  selectSpace(esp: Espacio) { this.modoVirtual.set(false); this.activeSpace.set(esp); }
  selectVirtual() { this.modoVirtual.set(true); }
  selectWeek(week: 'A' | 'B') { this.activeWeek.set(week); }
  altColor(m: MergedSesion): string { return m.alternancia === 'TipoA' ? '#5980a6' : m.alternancia === 'TipoB' ? '#a8825a' : '#8a8f94'; }

  /** Fondo de .gcell por asignatura (petición 12). Las virtuales conservan el rayado de
   *  .gvirt (indica modalidad, no asignatura) — null deja que gane la regla CSS de la clase. */
  gcellBg(m: MergedSesion): string | null {
    if (m.virtual) return null;
    return `color-mix(in srgb, ${this.state.colorDeAsignatura(m.asignaturaId)} 20%, white)`;
  }

  leyendaAsignaturas = computed(() => {
    const ids = new Set(this.state.sesiones().map(s => s.asignaturaId));
    return [...ids]
      .map(id => ({ id, nombre: this.state.asignaturaById().get(id)?.nombre ?? 'Asignatura eliminada', color: this.state.colorDeAsignatura(id) }))
      .sort((a, b) => a.nombre.localeCompare(b.nombre));
  });

  syncFromBackend() {
    this.loadingBackend.set(true);
    this.catalogo.cargarTodo().subscribe({
      next: () => {
        this.loadingBackend.set(false);
        this.backendReady.set(true);
        const espacios = this.state.espacios();
        const current = this.activeSpace();
        if (!current || !espacios.find(e => e.id === current.id)) this.activeSpace.set(espacios[0] ?? null);
        // FE6/FE13 auditoría: la rehidratación del horario (P6) ahora vive en
        // CatalogoService.cargarTodo() — única fuente, ver ficha allí — para que /revisar
        // también la reciba tras un F5. Un fallo real ahí no tumba el resto del catálogo; se
        // guarda en errorHorarioActual y se muestra aquí, que es donde el operador actúa sobre él.
        const errorHorario = this.state.errorHorarioActual();
        if (errorHorario) {
          this.snackBar.open('No se pudo recuperar el último horario. Puede generarlo de nuevo.', 'Cerrar', { duration: 6000, panelClass: ['snack-error'] });
        }
      },
      error: () => {
        this.loadingBackend.set(false);
        this.backendReady.set(false);
        this.snackBar.open('No se pudo conectar con el sistema. Revise su conexión e intente de nuevo.', 'Cerrar', { duration: 5000, panelClass: ['snack-error'] });
      }
    });
  }

  private aMerged(s: Sesion): MergedSesion {
    return {
      // Comparten `id` la presencial y su contraparte virtual (ver state.service.ts) — cuando
      // ambas terminan como tarjetas propias (la contraparte no se plegó, ver sesionesVista())
      // necesitan una clave distinta para no chocar en el @for ni en el mat-menu del "+N".
      key: `${s.id}|${s.semana ?? ''}|${s.virtual ? 'v' : 'p'}`,
      sesiones: [s], dia: s.dia, horaInicio: s.horaInicio, horaFin: s.horaFin,
      virtual: s.virtual, alternancia: s.alternancia, semana: s.semana, asignaturaId: s.asignaturaId,
      grupoId: s.grupoId, docenteId: s.docenteId, espacioId: s.espacioId, espacioIdHogar: s.espacioIdHogar,
      tipoFlujo: s.tipoFlujo, parejaId: s.parejaId
    };
  }

  private sesionPerteneceAlEspacio(s: Sesion, spaceId: string | undefined): boolean {
    if (spaceId === this.ESPACIO_VIRTUAL) return s.virtual;
    if (!spaceId) return true;
    if (s.espacioId === spaceId) return true;
    if (s.espacioIdHogar) return s.espacioIdHogar === spaceId;
    // Antes caía aquí "return s.virtual", lo que duplicaba las virtuales sin hogar en TODOS los
    // chips físicos. Ahora tienen su propia vista dedicada (chip "Virtual (sin espacio)").
    return false;
  }

  /** Hay alternancia activa cuando alguna sesión tiene pareja. Sin ella la Semana B no existe
   *  y el selector ni siquiera se muestra: la Semana A es el horario completo. */
  hayAlternancia = computed(() => this.state.sesiones().some(s => !!s.parejaId));

  /** La semana efectivamente mostrada: si ya no hay alternancia (p. ej. tras regenerar sin
   *  parejas) no se queda pegada en B sin selector visible para volver a A. */
  semanaVista = computed<'A' | 'B'>(() => this.hayAlternancia() ? this.activeWeek() : 'A');

  /** Semana A: todo lo que no es exclusivamente B. Semana B: todo lo que no es exclusivamente A
   *  — incluye lo que no alterna (`semana` vacío/ausente), que la grilla pinta en gris como
   *  "ocupa el aula todas las semanas" en vez de dejar el aula viéndose libre (antes solo se
   *  mostraba lo emparejado y el resto de las aulas ocupadas parecían disponibles). */
  private sesionVisibleEnSemana(s: Sesion): boolean {
    return this.semanaVista() === 'B' ? s.semana !== 'A' : s.semana !== 'B';
  }

  /** Sesiones visibles en el chip/semana actual, ya plegadas con su contraparte de alternancia.
   *  Reemplaza el mapa por celda de inicio (mergedByCell/coveredCells) de la tabla anterior. */
  sesionesVista = computed<MergedSesion[]>(() => {
    const spaceId = this.modoVirtual() ? this.ESPACIO_VIRTUAL : this.activeSpace()?.id;
    const visibles = this.state.sesiones().filter(s => this.sesionPerteneceAlEspacio(s, spaceId) && this.sesionVisibleEnSemana(s));

    // La contraparte virtual se pliega como sub-caja DENTRO de la tarjeta presencial solo si
    // coincide exactamente en pareja + día + hora con una presencial visible — antes se indexaba
    // solo por parejaId (un Map que sobrescribía si había más de una franja) y una contraparte sin
    // presencial visible se descartaba sin más (`continue`), perdiéndola de la grilla sin aviso.
    const clave = (s: { parejaId?: string; dia: string; horaInicio: string; horaFin: string }) =>
      `${s.parejaId}|${s.dia}|${s.horaInicio}|${s.horaFin}`;
    const contrapartes = new Map<string, Sesion>();
    for (const s of visibles) if (s.esContraparteVirtual && s.parejaId) contrapartes.set(clave(s), s);

    const plegadas = new Set<string>();
    const resultado: MergedSesion[] = [];
    for (const s of visibles) {
      if (s.esContraparteVirtual) continue;
      const merged = this.aMerged(s);
      const k = s.parejaId ? clave(s) : undefined;
      const otra = k ? contrapartes.get(k) : undefined;
      if (otra) { merged.contraparte = this.aMerged(otra); plegadas.add(k!); }
      resultado.push(merged);
    }
    // Contrapartes que no se plegaron (su presencial no está visible en esta vista/semana, o no
    // coincidió exactamente en franja): tarjeta propia en vez de perderse.
    for (const [k, s] of contrapartes) if (!plegadas.has(k)) resultado.push(this.aMerged(s));
    return resultado;
  });

  /** Presenciales sin aula ni aula de origen: `sesionPerteneceAlEspacio` las excluye de TODOS los
   *  chips de espacio físico (no pertenecen a ninguno), así que sin este parche desaparecían de
   *  la grilla sin aviso. grilla() las suma a la lista "fuera" de cualquier vista de aula. */
  private sesionesSinAula = computed<Sesion[]>(() =>
    this.state.sesiones().filter(s => !s.virtual && !s.espacioId && !s.espacioIdHogar && this.sesionVisibleEnSemana(s)));

  /** Cuadrícula por carriles (ver distribuirEnCarriles/ubicarEnGrilla): posiciona cada sesión por
   *  minutos de inicio/fin y reparte las simultáneas en columnas lado a lado — reemplaza la tabla
   *  con rowspan que ocultaba sesiones escalonadas o solapadas y descuadraba columnas (auditoría
   *  UI 2026-09). Nada se pierde: lo que no cabe en el rango horario va a `fuera`.
   */
  grilla = computed<GrillaSemanal>(() => {
    const dias: DiaGrilla[] = DIAS_GRILLA.map(d => ({
      valor: d.valor, corto: d.corto, etiqueta: d.etiqueta, tarjetas: [], mas: [],
      filaCierre: d.valor === 'sabado' ? HORA_CIERRE_SABADO - HORA_APERTURA + 1 : undefined,
    }));
    const diaIdx = new Map(dias.map((d, i) => [d.valor, i]));
    const fuera: SesionFuera[] = [];
    const porDia = new Map<string, ItemGrilla[]>();

    for (const m of this.sesionesVista()) {
      const pos = ubicarEnGrilla(m.dia, m.horaInicio, m.horaFin);
      if ('motivo' in pos) { fuera.push({ m, motivo: pos.motivo, texto: this.describir(m) }); continue; }
      const lista = porDia.get(m.dia) ?? []; lista.push({ m, ...pos }); porDia.set(m.dia, lista);
    }
    if (!this.modoVirtual()) {
      for (const s of this.sesionesSinAula()) {
        const m = this.aMerged(s);
        fuera.push({ m, motivo: 'sin aula asignada', texto: this.describir(m) });
      }
    }

    let total = 0;
    for (const [diaValor, items] of porDia) {
      const idx = diaIdx.get(diaValor); if (idx === undefined) continue;
      total += items.length;
      const { tarjetas, mas } = distribuirEnCarriles(items);
      dias[idx].tarjetas = tarjetas.map(({ item, carril, carriles }) => {
        const ancho = 6 / carriles;
        return {
          key: item.m.key, m: item.m,
          fila: `${item.fila0} / ${item.fila1}`,
          col: `${carril * ancho + 1} / span ${ancho}`,
          siempre: this.semanaVista() === 'B' && !item.m.semana,
          g1h: (item.fila1 - item.fila0) < 2,
          texto: this.describir(item.m),
        };
      });
      dias[idx].mas = mas.map(grupo => ({
        fila: `${grupo.ini / 60 - HORA_APERTURA + 1} / ${grupo.fin / 60 - HORA_APERTURA + 1}`,
        items: grupo.items.map(x => x.m),
        texto: `${grupo.items.length} sesiones más en ese horario — clic para verlas`,
      }));
    }
    return { dias, fuera, total };
  });

  /** Descripción completa de una sesión — tooltip y aria-label de su tarjeta, y texto de la lista
   *  "fuera de la cuadrícula". No se abrevia: es la única forma de leer el dato completo, ya que
   *  la tarjeta trunca según el alto disponible. */
  describir(m: MergedSesion): string {
    const base = describirSesionConflicto(m, this.state.asignaturas(), this.state.grupos());
    const ctx = this.getContextLabel(m);
    const docente = m.docenteId ? this.getDocenteName(m) : 'sin docente';
    const espacio = this.celdaEspacio(m);
    const semana = m.semana === 'A' ? ' · Semana A' : m.semana === 'B' ? ' · Semana B' : '';
    const siempre = this.semanaVista() === 'B' && !m.semana ? ' · ocupa el aula todas las semanas' : '';
    const contraparte = m.contraparte ? ` · alterna con ${this.getAsignaturaName(m.contraparte)}${this.grupoSuffix(m.contraparte)} (virtual)` : '';
    return `${base}${ctx ? ' · ' + ctx : ''} · ${docente} · ${espacio}${semana}${siempre}${contraparte}. Clic para editar.`;
  }

  getAsignaturaName(merged: MergedSesion): string { return this.state.asignaturaById().get(merged.asignaturaId)?.nombre ?? 'Desconocida'; }
  getDocenteName(merged: MergedSesion): string { return merged.docenteId ? (this.state.docenteById().get(merged.docenteId)?.nombre ?? '') : ''; }
  celdaEspacio(merged: MergedSesion): string {
    if (merged.virtual) {
      const hogar = merged.espacioIdHogar ? this.state.espacioById().get(merged.espacioIdHogar)?.nombre : null;
      return hogar ? `aula ${hogar}` : 'virtual';
    }
    return merged.espacioId ? (this.state.espacioById().get(merged.espacioId)?.nombre ?? '') : '—';
  }

  getContextLabel(merged: MergedSesion): string {
    const asig = this.state.asignaturaById().get(merged.asignaturaId);
    if (!asig) return '';
    const prog = this.state.programaById().get(asig.programaId);
    return prog?.nombre ?? '';
  }
  grupoSuffix(merged: MergedSesion): string {
    // G3 (bug reportado "no se muestran todos los grupos"): antes usaba Asignatura.grupoNumero
    // (campo legado de import, no el grupo real) — dos grupos de la misma asignatura se
    // pintaban idénticos. Ahora usa el grupo real de la sesión (Sesion.grupoId).
    const nombre = merged.grupoId && this.state.grupos().find(g => g.id === merged.grupoId)?.nombre;
    return nombre ? ` · ${nombre}` : '';
  }

  abrirEditarSesion(merged: MergedSesion) {
    const ref = this.dialog.open(EditarSesionDialogComponent, {
      width: '300px', maxHeight: '92vh',
      data: {
        merged, sesion: merged.sesiones[0], asignaturas: this.state.asignaturas(), docentes: this.state.docentes(),
        espacios: this.state.espacios(), sesiones: this.state.sesiones(), programaById: this.state.programaById(), facultadById: this.state.facultadById()
      } satisfies EditarSesionDialogData
    });
    ref.afterClosed().subscribe((result: EditarSesionResult | undefined) => {
      if (!result) return;
      // Petición 13: un cambio de día/hora/espacio ya refrescó el StateService completo desde la
      // respuesta de /reacomodar (puede haber movido otras sesiones en conflicto); solo un cambio
      // de docente/alternancia/semana sigue siendo una mutación local puntual.
      if (result.sesion) this.state.updateSesion(result.sesion);
      if (result.advertencias?.length) this.snackBar.open(`Sesión actualizada con avisos: ${result.advertencias.join(' · ')}`, 'Cerrar', { duration: 8000 });
      else this.snackBar.open('Sesión actualizada.', '', { duration: 2500 });
    });
  }

  /** El registro técnico (Fase 1/2/3, HC-…, milisegundos) nunca se pinta en pantalla — la
   *  coordinadora no puede accionarlo — pero sigue disponible para pegarlo en un caso de soporte. */
  copiarDetalleSoporte() {
    const texto = this.state.executionLogs().join('\n');
    navigator.clipboard?.writeText(texto).then(
      () => this.snackBar.open('Detalle copiado al portapapeles.', '', { duration: 3000 }),
      () => this.snackBar.open('No se pudo copiar. Seleccione el texto manualmente.', 'Cerrar', { duration: 4000, panelClass: ['snack-error'] })
    );
  }

  exportarHorario() {
    const sesiones = this.state.sesiones();
    const blob = new Blob([JSON.stringify({ version: 1, exportadoEn: new Date().toISOString(), sesiones }, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = `horario-soea-${new Date().toISOString().slice(0, 10)}.json`; a.click();
    URL.revokeObjectURL(url);
    this.snackBar.open(`Horario exportado (${sesiones.length} sesiones).`, '', { duration: 3000 });
  }

  importarHorario(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = (e) => {
      try {
        const json = JSON.parse(e.target!.result as string);
        const crudo = json.sesiones ?? json;
        if (!Array.isArray(crudo)) throw new Error('El archivo no es una copia de horario válida.');
        // FE9 auditoría: antes se inyectaba cualquier fila tal cual — una sesión con un `dia` fuera
        // de la grilla (p. ej. "domingo") entraba al state pero la grilla nunca la podía dibujar:
        // pérdida de datos silenciosa que el conteo del snackbar tampoco reflejaba.
        const diasValidos = new Set(DIAS_GRILLA.map(d => d.valor));
        const validas = crudo.filter((s: any) => diasValidos.has(s?.dia) && typeof s?.horaInicio === 'string' && typeof s?.horaFin === 'string');
        const descartadas = crudo.length - validas.length;
        this.state.setSesiones(validas);
        // Esta vista es solo local: las sesiones importadas no corresponden a ningún horario
        // persistido en BD. Antes horarioId seguía apuntando al horario viejo, así que la
        // siguiente edición (reacomodar/asignar docente) volvía a pedirle al backend ESE horario
        // y su respuesta pisaba el import en silencio. Al limpiarlo, esas acciones piden
        // regenerar en vez de borrar el import sin avisar.
        this.state.horarioId.set(null);
        const detalle = descartadas > 0 ? ` (${descartadas} no se cargaron: día u hora no válidos).` : '.';
        this.snackBar.open(
          `Horario abierto: ${validas.length} clases${detalle} Solo se ve en este equipo; no reemplaza el horario guardado.`,
          'Cerrar', { duration: 7000 });
      } catch (err: any) {
        this.snackBar.open('No se pudo leer el archivo. Use una copia descargada desde SOEA.', 'Cerrar', { duration: 6000, panelClass: ['snack-error'] });
      }
      (event.target as HTMLInputElement).value = '';
    };
    reader.readAsText(file);
  }

  guardarComoBase() {
    const nombre = window.prompt('Nombre del punto de partida:', `Base ${new Date().toLocaleDateString('es-CO')}`);
    if (!nombre?.trim()) return;
    const base = this.state.guardarHorarioBase(nombre);
    this.snackBar.open(`Horario base "${base.nombre}" guardado (${base.sesiones.length} sesiones).`, 'Cerrar', { duration: 4000 });
  }

  toggleBase(id: string) { this.state.seleccionarBase(this.state.baseSeleccionadaId() === id ? null : id); }
  eliminarBase(id: string) {
    const base = this.state.horariosBases().find(b => b.id === id);
    if (!base || !window.confirm(`¿Eliminar el punto de partida "${base.nombre}"?`)) return;
    this.state.eliminarHorarioBase(id);
    this.snackBar.open(`Base "${base.nombre}" eliminada.`, '', { duration: 3000 });
  }

  generarHorario() {
    if (this.generandoHorario()) return;
    if (!this.backendReady()) { this.snackBar.open('No hay conexión con el sistema; no se puede generar ahora.', 'Cerrar', { duration: 4000 }); return; }
    if (this.state.asignaturas().length === 0 || this.state.espacios().length === 0 || this.state.docentes().length === 0) {
      this.snackBar.open('Carga asignaturas, docentes y espacios antes de generar el horario.', 'Cerrar', { duration: 4000 });
      return;
    }
    const asignaturas = this.state.asignaturas();
    this.generandoHorario.set(true);
    const dialogRef = this.dialog.open(ProgressDialogComponent, { disableClose: true, width: '340px' });
    // Sin panel de parámetros en la UI (decisión de producto): siempre se generan con los valores
    // por defecto del servidor — ver ConfiguracionOptimizacion en GenerarHorarioService.
    this.horarioApi.generarHorario(asignaturas, this.state.docentes(), this.state.espacios(), undefined, '2026-1', this.state.baseSeleccionada() ?? undefined, this.state.grupos())
      .subscribe({
        next: (respuesta) => {
          this.generandoHorario.set(false);
          dialogRef.close();
          const sesiones = this.horarioApi.mapearSesiones(respuesta.sesiones);
          this.state.setSesiones(sesiones);
          this.state.setExecutionLogs(respuesta.logs || []);
          this.state.horarioId.set(respuesta.horarioId);
          // Una generación exitosa invalida cualquier aviso de un intento previo.
          this.state.setGruposEnConflicto([]);
          this.state.setMotivoInfactibilidad(undefined);
          this.mensajeInfactible.set('');
          this.snackBar.open(`Horario generado con ${sesiones.length} clases.`, 'Cerrar', { duration: 6000 });
        },
        error: (err: any) => {
          this.generandoHorario.set(false);
          dialogRef.close();
          // Un 422 (GenerarHorarioResponse) pasa tal cual desde manejarError — mensajeError no
          // pasó por mensajeErrorHttp, así que puede traer el código de regla crudo (HC-SEP…).
          const mensaje = limpiarEtiquetaInterna(err.mensajeError || err.message || err.error || 'Error desconocido');
          if (err.logs && Array.isArray(err.logs)) this.state.setExecutionLogs(err.logs);
          const gruposEnConflicto = Array.isArray(err.gruposEnConflicto) ? err.gruposEnConflicto : [];
          this.state.setGruposEnConflicto(gruposEnConflicto);
          this.state.setMotivoInfactibilidad(err.motivoInfactibilidad);
          // Antes solo se traducía si el mensaje crudo contenía "factible"/"infeasible" — un fallo
          // distinto (p. ej. una violación de reglas post-GA) se colaba tal cual, con jerga interna.
          // Con motivo o grupos estructurados ya hay suficiente señal para traducir igual.
          const esInfeasible = !!err.motivoInfactibilidad || gruposEnConflicto.length > 0 || /factible|infeasible/i.test(mensaje);
          const texto = esInfeasible
            ? mensajeInfactibilidadAmigable(err.motivoInfactibilidad, gruposEnConflicto, this.state.grupos(), asignaturas.length, this.state.espacios().length)
            : mensaje;
          // El banner persistente de /horario (a diferencia del snackbar) sigue visible hasta la
          // próxima generación — siempre con el mismo texto limpio, nunca vacío ni con jerga.
          this.mensajeInfactible.set(texto);
          this.snackBar.open(texto, 'Cerrar', { duration: 9000, panelClass: ['snack-error'] });
        }
      });
  }

  abrirCrearSesion() {
    const ref = this.dialog.open(CrearSesionDialogComponent, {
      width: '300px', maxHeight: '92vh',
      data: { asignaturas: this.state.asignaturas(), docentes: this.state.docentes(), espacios: this.state.espacios(), grupos: this.state.grupos(), sesiones: this.state.sesiones(), programaById: this.state.programaById() }
    });
    ref.afterClosed().subscribe((nuevas: Sesion[] | undefined) => {
      if (!nuevas?.length) return;
      this.state.sesiones.update(prev => [...prev, ...nuevas]);
      // "N filas" era jerga interna del modelo de datos: al coordinador solo le importa que la
      // sesión quedó creada.
      this.snackBar.open('Sesión creada.', 'Cerrar', { duration: 5000 });
    });
  }

  abrirSesionFija() {
    // Mismo diálogo que "crear sesión" (grupo, tipo y duración de la asignatura) en modo fija: las
    // comprobaciones se hacen contra la base seleccionada y nada va al servidor hasta generar.
    const ref = this.dialog.open(CrearSesionDialogComponent, {
      width: '300px', maxHeight: '92vh',
      data: { asignaturas: this.state.asignaturas(), docentes: this.state.docentes(), espacios: this.state.espacios(), grupos: this.state.grupos(), sesiones: this.state.baseSeleccionada()?.sesiones ?? [], programaById: this.state.programaById(), modoFija: true }
    });
    ref.afterClosed().subscribe((nuevas: Sesion[] | undefined) => {
      const sesion = nuevas?.[0];
      if (!sesion) return;
      const nombre = window.prompt('Nombre del punto de partida:', 'Sesiones fijas');
      if (!nombre?.trim()) return;
      // Se agrega la sesión fija a un horario base nuevo (restricción de igualdad para CP-SAT).
      const previa = this.state.baseSeleccionada();
      const sesiones = previa ? [...previa.sesiones, sesion] : [sesion];
      const antes = this.state.sesiones();
      this.state.setSesiones(sesiones);
      const base = this.state.guardarHorarioBase(nombre);
      this.state.setSesiones(antes);
      this.state.seleccionarBase(base.id);
      this.snackBar.open(`Clase fijada en "${base.nombre}" (${base.sesiones.length} en total).`, 'Cerrar', { duration: 5000 });
    });
  }
}

// ═══ Diálogo: Editar sesión (REQUISITOS §2/§3 — incluye asignar docente con 409) ═══
interface Check { ok: boolean; texto: string; }

interface EditarSesionDialogData {
  merged: MergedSesion; sesion: Sesion; asignaturas: Asignatura[]; docentes: Docente[]; espacios: Espacio[];
  sesiones: Sesion[]; programaById: Map<string, { id: string; nombre: string; facultadId: string }>; facultadById: Map<string, { id: string; nombre: string }>;
}
/** `sesion` ausente = un cambio de día/hora/espacio ya refrescó el StateService completo (P5). */
interface EditarSesionResult { sesion?: Sesion; advertencias: string[]; }

@Component({
  selector: 'app-editar-sesion-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule],
  template: `
    <div class="pophd">Editar sesión <button type="button" class="pop-close" (click)="cancelar()" aria-label="Cerrar">✕</button></div>
    <div class="popbd" style="max-height:80vh;overflow:auto">
      <div class="text-muted" style="font-size:12px">{{ asignatura()?.nombre }} · {{ data.sesion.duracionHoras }}h {{ data.sesion.virtual ? '· virtual' : '· presencial' }}</div>

      <div class="dfield"><label>Docente</label>
        <select class="input" [ngModel]="docenteId()" (ngModelChange)="docenteId.set($event)">
          <option value="">— Sin asignar —</option>
          @for (d of data.docentes; track d.id) { <option [value]="d.id">{{ d.nombre }}</option> }
        </select>
        @if (hayCambioDocente()) { <small class="text-muted" style="font-size:11px">Si el docente ya tiene clase a esa hora no se podrá guardar; si está fuera de su disponibilidad, verá un aviso.</small> }
      </div>

      <div class="dfield"><label>Día · Hora inicio</label>
        <div style="display:flex;gap:6px">
          <select class="input" style="flex:1" [ngModel]="dia()" (ngModelChange)="dia.set($event)">
            @for (d of diasOpciones; track d.valor) { <option [value]="d.valor">{{ d.etiqueta }}</option> }
          </select>
          <select class="input" style="width:80px" [ngModel]="horaInicio()" (ngModelChange)="horaInicio.set($event)">
            @for (h of horasDisponibles; track h) { <option [value]="h">{{ h }}</option> }
          </select>
        </div>
      </div>

      <div class="dfield"><label>Espacio</label>
        <select class="input" [ngModel]="espacioId()" (ngModelChange)="espacioId.set($event)">
          <option value="">— Sin espacio (virtual) —</option>
          @for (e of espaciosDisponibles(); track e.id) { <option [value]="e.id">{{ e.nombre }}</option> }
        </select>
      </div>

      @if (esLaboratorio()) {
        <div class="dfield"><label>Alternancia</label>
          <div class="seg" style="align-self:flex-start">
            <label class="seg-opt" [class.on]="alternancia()==='TipoA'" (click)="alternancia.set('TipoA')">Semana A</label>
            <label class="seg-opt" [class.on]="alternancia()==='TipoB'" (click)="alternancia.set('TipoB')">Semana B</label>
            <label class="seg-opt" [class.on]="alternancia()==='SinAlternancia'" (click)="alternancia.set('SinAlternancia')">Todas las semanas</label>
          </div>
        </div>
      }

      @for (c of validaciones(); track c.texto) {
        <div [class]="c.ok ? 'okb' : 'errb'">{{ c.ok ? '✓' : '✕' }} {{ c.texto }}</div>
      }
      @for (w of advertencias(); track w) { <div class="soft"><b>⚠ Aviso (no bloquea):</b> {{ w }}</div> }
      @if (errorServidor()) { <div class="errb"><b>✕ {{ errorServidor() }}</b></div> }

      <div class="popfoot">
        <button class="btn btn-secondary" (click)="cancelar()" [disabled]="guardando()">Cancelar</button>
        <button class="btn btn-primary" [disabled]="!puedeGuardar() || guardando()" (click)="guardar()">{{ guardando() ? 'Guardando…' : 'Guardar' }}</button>
      </div>
    </div>
  `
})
export class EditarSesionDialogComponent {
  private dialogRef = inject(MatDialogRef<EditarSesionDialogComponent>);
  readonly data: EditarSesionDialogData = inject(MAT_DIALOG_DATA);
  private persistencia = inject(PersistenciaService);
  private horarioApi = inject(HorarioApiService);
  private state = inject(StateService);
  private readonly orig = this.data.merged;

  docenteId = signal(this.orig.docenteId ?? '');
  dia = signal(this.orig.dia);
  horaInicio = signal(this.orig.horaInicio);
  espacioId = signal(this.orig.espacioId ?? '');
  alternancia = signal<'TipoA' | 'TipoB' | 'SinAlternancia'>(this.orig.alternancia as any);
  /** Derivada, no editable: TipoA ocupa el aula en la semana A, TipoB en la B, y lo que no
   *  alterna la ocupa en las dos. Elegirla a mano permitía estados que el motor no puede producir. */
  semana = computed<'A' | 'B' | undefined>(() =>
    this.alternancia() === 'TipoA' ? 'A' : this.alternancia() === 'TipoB' ? 'B' : undefined);
  guardando = signal(false);
  advertencias = signal<string[]>([]);
  errorServidor = signal('');

  asignatura = computed(() => this.data.asignaturas.find(a => a.id === this.orig.asignaturaId));
  esLaboratorio = computed(() => this.data.sesion.tipoFlujo === 'Laboratorio');
  private tipoSesionActual = computed<TipoSesionUi>(() =>
    this.esLaboratorio() ? 'Laboratorio' : this.data.sesion.virtual ? 'TeoriaVirtual' : 'TeoriaPresencial');
  /** FE17 auditoría: antes listaba data.espacios sin filtrar — permitía mover un laboratorio a
   *  un salón cualquiera, o una sesión con aula fija de grupo a un aula distinta de la exigida. */
  espaciosDisponibles = computed(() => {
    const grupo = this.orig.grupoId ? this.state.grupos().find(g => g.id === this.orig.grupoId) : undefined;
    return espaciosPermitidosPara(this.tipoSesionActual(), grupo, this.data.espacios);
  });
  hayCambioDocente = computed(() => this.docenteId() !== (this.orig.docenteId ?? ''));
  /** Petición 13: mover día/hora/espacio ya no es una mutación local — pasa por /reacomodar. */
  hayCambioSlot = computed(() =>
    this.dia() !== this.orig.dia || this.horaInicio() !== this.orig.horaInicio || this.espacioId() !== (this.orig.espacioId ?? ''));
  hayCambios = computed(() =>
    this.hayCambioDocente() || this.hayCambioSlot() ||
    this.alternancia() !== (this.orig.alternancia as string));

  readonly horasDisponibles = HORAS_GRILLA;
  readonly diasOpciones = DIAS_GRILLA;

  validaciones = computed<Check[]>(() => {
    const dia = this.dia(), inicio = this.horaInicio(), espacioId = this.espacioId(), docenteId = this.docenteId();
    const semanaActual = this.semana();
    const sesionId = this.data.sesion.id, dur = this.data.sesion.duracionHoras ?? 2;
    const chks: Check[] = [];
    if (!dia || !inicio) return chks;
    const startIdx = this.horasDisponibles.indexOf(inicio), endIdx = startIdx + Math.round(dur);
    // FE16 auditoría: antes solo se acotaba el sábado — un fin de jornada fuera de rango en L-V
    // pasaba sin aviso y horaFinNueva caía a "?? inicio" (un "fin = inicio" silencioso).
    if (!finDeJornadaOk(dia, endIdx, this.horasDisponibles)) {
      chks.push({ ok: false, texto: dia === 'sabado' ? `Sábado solo tiene jornada hasta las ${hhmm(HORA_CIERRE_SABADO)}` : 'La sesión no cabe en la jornada de ese día.' });
    }
    const horaFinNueva = this.horasDisponibles[endIdx] ?? inicio;
    const sesion1 = () => describirSesionConflicto(
      { asignaturaId: this.orig.asignaturaId, grupoId: this.orig.grupoId, dia, horaInicio: inicio, horaFin: horaFinNueva },
      this.data.asignaturas, this.state.grupos());
    const sesion2 = (s: Sesion) => describirSesionConflicto(s, this.data.asignaturas, this.state.grupos());

    if (espacioId && !this.data.sesion.virtual) {
      const conflicto = this.data.sesiones.find(s => s.id !== sesionId && s.espacioId === espacioId && s.dia === dia && !s.virtual && !nuncaCoexisteEnSemana(s.semana, semanaActual) && seSolapanHorarios(s, startIdx, endIdx, this.horasDisponibles));
      const nombre = this.data.espacios.find(e => e.id === espacioId)?.nombre ?? espacioId;
      const texto = conflicto
        ? `${nombre} ya está ocupado en esa franja — Sesión 1: ${sesion1()}; Sesión 2: ${sesion2(conflicto)}. ` +
          'Elija otro espacio o cambie el horario de una de las dos sesiones.'
        : `${nombre} está libre`;
      chks.push({ ok: !conflicto, texto });
    }
    if (docenteId) {
      const conflicto = this.data.sesiones.find(s => s.id !== sesionId && s.docenteId === docenteId && s.dia === dia && !nuncaCoexisteEnSemana(s.semana, semanaActual) && seSolapanHorarios(s, startIdx, endIdx, this.horasDisponibles));
      const nombre = this.data.docentes.find(d => d.id === docenteId)?.nombre ?? 'El docente';
      const texto = conflicto
        ? `${nombre} ya tiene otra sesión en esa franja — Sesión 1: ${sesion1()}; Sesión 2: ${sesion2(conflicto)}. ` +
          'Elija otro docente o cambie el horario de una de las dos sesiones.'
        : `${nombre} está libre en esa franja`;
      chks.push({ ok: !conflicto, texto });
    }
    return chks;
  });

  conflictosDuros = computed(() => this.validaciones().some(c => !c.ok));
  puedeGuardar = computed(() => this.hayCambios() && !this.conflictosDuros() && !this.guardando());

  guardar() {
    if (!this.puedeGuardar()) return;
    this.guardando.set(true); this.errorServidor.set(''); this.advertencias.set([]);
    if (this.hayCambioDocente()) {
      this.persistencia.asignarDocente(this.data.sesion.id, this.docenteId() || null).subscribe({
        next: (resp) => {
          this.advertencias.set(resp.advertencias ?? []);
          // FE4 auditoría: el PATCH de docente ya quedó comprometido en el backend en este punto.
          // Antes, si el reacomodo que sigue fallaba, el store seguía mostrando el docente viejo
          // — divergía de lo que ya había en BD sin que nada lo avisara. Se refleja de inmediato.
          this.state.updateSesion({ ...this.data.sesion, docenteId: this.docenteId() || undefined });
          this.continuar();
        },
        error: (err: any) => {
          this.guardando.set(false);
          const msg = mensajeErrorHttp(err);
          this.errorServidor.set(err?.status === 409 ? `Cruce de horario: ${msg}` : msg);
        }
      });
    } else { this.continuar(); }
  }

  private continuar() {
    if (this.hayCambioSlot()) this.reacomodar();
    else this.commitLocal();
  }

  /** Petición 13: mover día/hora/espacio ya no muta memoria — pasa por /reacomodar y refresca
   * el StateService completo desde la respuesta (puede haber liberado y reubicado otras sesiones). */
  private reacomodar() {
    const horarioId = this.state.horarioId();
    if (!horarioId) {
      this.guardando.set(false);
      this.errorServidor.set('Primero genere el horario para poder mover clases.');
      return;
    }
    this.horarioApi.reacomodar({
      horarioId,
      sesionEditadaId: this.data.sesion.id,
      dia: this.dia(),
      horaInicio: this.horaInicio(),
      espacioId: this.espacioId() || undefined
    }).subscribe({
      next: (resp) => {
        if (!resp.esFactible) {
          this.guardando.set(false);
          this.errorServidor.set(resp.mensajeError || 'No se pudo mover la clase a ese horario.');
          return;
        }
        this.state.setSesiones(this.horarioApi.mapearSesiones(resp.sesiones));
        // FE1 auditoría: ReacomodarHorarioRequest no lleva alternancia — el backend no la toca en
        // este endpoint. Antes, cambiar alternancia Y mover la sesión a la vez descartaba la
        // alternancia en silencio (solo commitLocal() la aplicaba). Se aplica aquí el mismo parche
        // local para que el comportamiento no dependa de si también hubo un cambio de slot.
        if (this.alternancia() !== (this.orig.alternancia as string)) {
          const movida = this.state.sesiones().find(s => s.id === this.data.sesion.id);
          if (movida) this.state.updateSesion({ ...movida, alternancia: this.alternancia() });
        }
        const avisos = [...this.advertencias(), ...resp.advertencias];
        this.guardando.set(false);
        this.dialogRef.close({ advertencias: avisos } satisfies EditarSesionResult);
      },
      error: (err: any) => {
        this.guardando.set(false);
        // ERR3 auditoría: manejarError() de HorarioApiService reenvía el body 422 (esFactible:false)
        // TAL CUAL (no anidado bajo `.error`) — err.mensajeError se comprueba antes por eso;
        // mensajeErrorHttp cubre el resto de formas (ProblemDetails, { error }, Error de red).
        this.errorServidor.set(err?.mensajeError ?? mensajeErrorHttp(err));
      }
    });
  }

  private commitLocal() {
    const updated: Sesion = {
      ...this.data.sesion, docenteId: this.docenteId() || undefined,
      alternancia: this.alternancia(), semana: this.semana()
    };
    // Si sólo hubo aviso blando, se dejó ver 1.2s antes de cerrar.
    if (this.advertencias().length) { setTimeout(() => { this.guardando.set(false); this.dialogRef.close({ sesion: updated, advertencias: this.advertencias() }); }, 1200); }
    else { this.guardando.set(false); this.dialogRef.close({ sesion: updated, advertencias: [] }); }
  }

  cancelar() { this.dialogRef.close(); }
  // FE5 auditoría: overlaps/nuncaCoexiste vivían duplicados aquí y en CrearSesionDialogComponent
  // — ahora son las funciones de módulo seSolapanHorarios/nuncaCoexisteEnSemana, usadas por
  // validaciones() arriba. addH/diffH quedaban sin uso salvo ese overlaps().
}

// ═══ Diálogo: Crear sesión manual (REQUISITOS §2) ═══
/** `modoFija`: la sesión va a un horario base (se devuelve a quien abrió el diálogo) en vez de crearse en el servidor. */
interface DialogData { asignaturas: Asignatura[]; docentes: Docente[]; espacios: Espacio[]; grupos: Grupo[]; sesiones: Sesion[]; programaById: Map<string, { id: string; nombre: string }>; modoFija?: boolean; }

@Component({
  selector: 'app-crear-sesion-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatSnackBarModule, SearchableSelectComponent],
  template: `
    <div class="pophd">{{ modoFija ? 'Fijar clase antes de generar' : 'Agregar clase' }} <button type="button" class="pop-close" (click)="cancelar()" aria-label="Cerrar">✕</button></div>
    <div class="popbd" style="max-height:80vh;overflow:auto">
      <div class="dfield"><label>Asignatura <span class="rq">*</span></label>
        <app-searchable-select [(ngModel)]="asignaturaId" (ngModelChange)="onAsignaturaChange($event)"
                                [options]="asignaturaOptions" placeholder="— Seleccione —"></app-searchable-select>
      </div>

      @if (asignaturaSeleccionada()) {
        <div class="dfield"><label>Grupo <span class="rq">*</span></label>
          <app-searchable-select [(ngModel)]="grupoId" (ngModelChange)="onGrupoChange()"
                                  [options]="grupoOptions()" placeholder="— Seleccione grupo —"></app-searchable-select>
        </div>
        <div class="dfield"><label>Tipo de sesión <span class="rq">*</span></label>
          <div class="seg" style="flex-wrap:wrap">
            @for (t of tiposDisponibles(); track t.tipo) { <label class="seg-opt" [class.on]="tipoSesion() === t.tipo" (click)="setTipoSesion(t.tipo)">{{ t.label }}</label> }
          </div>
        </div>
        <div class="text-muted" style="font-size:12px">👤 {{ docenteDelGrupo() }} · ⏱ {{ duracionSeleccionada() }}h por sesión (fijo)</div>
      }

      <div class="dfield"><label>Día <span class="rq">*</span></label>
        <select class="input" [(ngModel)]="dia" (ngModelChange)="recheck()">
          <option value="">— Seleccione —</option>
          @for (d of dias; track d.valor) { <option [value]="d.valor">{{ d.etiqueta }}</option> }
        </select>
      </div>
      <div style="display:flex;gap:8px">
        <div class="dfield" style="flex:1"><label>Inicio <span class="rq">*</span></label>
          <select class="input" [(ngModel)]="horaInicio" (ngModelChange)="recheck()">
            <option value="">—</option>
            @for (h of horasDisponibles; track h) { <option [value]="h">{{ h }}</option> }
          </select>
        </div>
        <div class="dfield" style="flex:1"><label>Espacio {{ tipoSesion() === 'TeoriaVirtual' ? '(virtual)' : '' }}</label>
          @if (tipoSesion() === 'TeoriaVirtual') { <div class="selval text-muted">Sin espacio físico</div> }
          @else {
            <app-searchable-select [(ngModel)]="espacioId" (ngModelChange)="recheck()" [disabled]="espacioFijoBloqueado()"
                                    [options]="espacioOptions()" placeholder="— Seleccione —"></app-searchable-select>
          }
        </div>
      </div>

      @if (tipoSesion() === 'Laboratorio') {
        <div class="dfield"><label>Alternancia <span class="rq">*</span></label>
          <div class="seg" style="flex-wrap:wrap">
            <label class="seg-opt" [class.on]="alternancia==='TipoA'" (click)="alternancia='TipoA'; recheck()">TipoA</label>
            <label class="seg-opt" [class.on]="alternancia==='TipoB'" (click)="alternancia='TipoB'; recheck()">Semana B</label>
            <label class="seg-opt" [class.on]="alternancia==='SinAlternancia'" (click)="alternancia='SinAlternancia'; recheck()">Todas las semanas</label>
          </div>
        </div>
      }

      @if (asignaturaId && dia && horaInicio && (espacioId || tipoSesion() === 'TeoriaVirtual')) {
        @for (c of checks(); track c.texto) { <div [class]="c.ok ? 'okb' : 'errb'">{{ c.ok ? '✓' : '✕' }} {{ c.texto }}</div> }
      }
      @if (errorServidor()) { <div class="errb"><b>✕ {{ errorServidor() }}</b></div> }
      @if (modoFija) {
        <p class="text-muted" style="font-size:11.5px;margin:0;border-top:1px dashed var(--color-neutral-300);padding-top:8px">Esta clase quedará fija; al generar, el resto del horario se acomoda alrededor.</p>
      }

      <div class="popfoot">
        <button class="btn btn-secondary" (click)="cancelar()" [disabled]="guardando()">Cancelar</button>
        <button class="btn btn-primary" [disabled]="!puedeCrear() || guardando()" (click)="crear()">{{ guardando() ? 'Creando…' : modoFija ? 'Fijar sesión' : 'Crear' }}</button>
      </div>
    </div>
  `
})
export class CrearSesionDialogComponent {
  private dialogRef = inject(MatDialogRef<CrearSesionDialogComponent>);
  private data: DialogData = inject(MAT_DIALOG_DATA);
  private persistencia = inject(PersistenciaService);
  private state = inject(StateService);
  readonly modoFija = !!this.data.modoFija;

  asignaturaId = ''; dia = ''; horaInicio = ''; espacioId = '';
  alternancia: 'TipoA' | 'TipoB' | 'SinAlternancia' = 'SinAlternancia';
  guardando = signal(false); errorServidor = signal('');

  readonly dias = DIAS_GRILLA;
  readonly horasDisponibles = HORAS_GRILLA;

  asignaturaSeleccionada = signal<Asignatura | undefined>(undefined);
  tipoSesion = signal<TipoSesionUi>('Laboratorio');
  espacioFijoBloqueado = signal(false);
  checks = signal<Check[]>([]);
  checksOk = signal(false);

  // item 9: lista plana buscable (sin optgroup por programa) — el programa va como "sub".
  readonly asignaturaOptions: SearchableOption[] = this.data.asignaturas
    .map(a => ({
      value: a.id,
      label: a.nombre + (a.codigo ? ` (${a.codigo})` : ''),
      sub: this.data.programaById.get(a.programaId)?.nombre
    }))
    .sort((a, b) => a.label.localeCompare(b.label));

  espacioOptions = computed<SearchableOption[]>(() =>
    this.espaciosDisponibles().map(e => ({ value: e.id, label: e.nombre })));

  // Fase 2: el docente se deriva del GRUPO (la misma asignatura la dictan docentes distintos).
  grupoId = '';
  grupoIdSel = signal('');
  grupoOptions = computed<SearchableOption[]>(() => {
    const asigId = this.asignaturaSeleccionada()?.id;
    if (!asigId) return [];
    return this.data.grupos.filter(g => g.asignaturaId === asigId)
      .map(g => ({ value: g.id, label: g.nombre, sub: g.docenteId ? this.nombreDocente(g.docenteId) : 'sin docente' }));
  });
  private docenteIdDelGrupo(): string {
    return this.data.grupos.find(g => g.id === this.grupoIdSel())?.docenteId ?? '';
  }
  docenteDelGrupo(): string {
    const id = this.docenteIdDelGrupo();
    return id ? this.nombreDocente(id) : '— sin docente en el grupo —';
  }
  onGrupoChange() { this.grupoIdSel.set(this.grupoId); this.espacioId = ''; this.recheck(); }

  tiposDisponibles = computed<{ tipo: TipoSesionUi; label: string }[]>(() => {
    const a = this.asignaturaSeleccionada();
    if (!a) return [];
    const list: { tipo: TipoSesionUi; label: string }[] = [];
    if (a.sesionesTeoriaPresencialSemana > 0) list.push({ tipo: 'TeoriaPresencial', label: 'Teoría presencial' });
    if (a.sesionesTeoriaVirtualSemana > 0) list.push({ tipo: 'TeoriaVirtual', label: 'Teoría virtual' });
    if (a.sesionesLaboratorioSemana > 0) list.push({ tipo: 'Laboratorio', label: 'Laboratorio' });
    return list;
  });

  duracionSeleccionada = computed(() => {
    const a = this.asignaturaSeleccionada();
    if (!a) return 2;
    switch (this.tipoSesion()) { case 'TeoriaVirtual': return a.horasTeoriaVirtual; case 'Laboratorio': return a.horasLaboratorio; default: return a.horasTeoriaPresencial; }
  });

  // FE17: espejo (cliente) de CalculadorEspaciosSesion.Candidatos — ahora la función de módulo
  // compartida espaciosPermitidosPara, también usada por EditarSesionDialogComponent.
  espaciosDisponibles = computed(() =>
    espaciosPermitidosPara(this.tipoSesion(), this.data.grupos.find(g => g.id === this.grupoIdSel()), this.data.espacios));

  puedeCrear = computed(() => !!this.asignaturaId && !!this.grupoIdSel() && !!this.dia && !!this.horaInicio && (!!this.espacioId || this.tipoSesion() === 'TeoriaVirtual') && this.checksOk() && !this.guardando());

  onAsignaturaChange(id: string) {
    const a = this.data.asignaturas.find(x => x.id === id);
    this.asignaturaSeleccionada.set(a);
    // Reiniciar el grupo: sus opciones dependen de la asignatura elegida.
    this.grupoId = ''; this.grupoIdSel.set('');
    this.tipoSesion.set(a && a.sesionesTeoriaPresencialSemana > 0 ? 'TeoriaPresencial' : a && a.sesionesLaboratorioSemana > 0 ? 'Laboratorio' : a && a.sesionesTeoriaVirtualSemana > 0 ? 'TeoriaVirtual' : 'TeoriaPresencial');
    this.aplicarReglasTipo(); this.recheck();
  }
  setTipoSesion(tipo: TipoSesionUi) { this.tipoSesion.set(tipo); this.aplicarReglasTipo(); this.recheck(); }

  private aplicarReglasTipo() {
    const a = this.asignaturaSeleccionada(), tipo = this.tipoSesion();
    if (tipo === 'Laboratorio' && a?.alternancia && a.alternancia !== 'SinAlternancia') this.alternancia = a.alternancia as 'TipoA' | 'TipoB';
    else this.alternancia = 'SinAlternancia';
    this.espacioFijoBloqueado.set(false);
    if (tipo === 'TeoriaVirtual') this.espacioId = '';
  }

  recheck() {
    const a = this.asignaturaSeleccionada(), dur = this.duracionSeleccionada(), esVirtual = this.tipoSesion() === 'TeoriaVirtual';
    const chks: Check[] = []; let ok = true;
    if (!a || !this.dia || !this.horaInicio || (!this.espacioId && !esVirtual)) { this.checks.set([]); this.checksOk.set(false); return; }
    const startIdx = this.horasDisponibles.indexOf(this.horaInicio), endIdx = startIdx + dur;
    // FE16 auditoría: antes solo se acotaba el sábado — ver mismo fix en EditarSesionDialogComponent.
    if (!finDeJornadaOk(this.dia, endIdx, this.horasDisponibles)) {
      chks.push({ ok: false, texto: this.dia === 'sabado' ? `Sábado solo tiene jornada hasta las ${hhmm(HORA_CIERRE_SABADO)}` : 'La sesión no cabe en la jornada de ese día.' });
      ok = false;
    }
    const horaFinNueva = this.horasDisponibles[endIdx] ?? this.horaInicio;
    const sesion1 = () => describirSesionConflicto(
      { asignaturaId: this.asignaturaId, grupoId: this.grupoIdSel(), dia: this.dia, horaInicio: this.horaInicio, horaFin: horaFinNueva },
      this.data.asignaturas, this.data.grupos);
    const sesion2 = (s: Sesion) => describirSesionConflicto(s, this.data.asignaturas, this.data.grupos);
    // FE5 auditoría: una sesión nueva TipoA/TipoB comparte semana con su propio par — la exención
    // de "nunca coexiste" debe considerar la semana que ESTA sesión ocuparía, igual que Editar.
    const semanaActual: Sesion['semana'] = this.alternancia === 'TipoA' ? 'A' : this.alternancia === 'TipoB' ? 'B' : undefined;

    const docenteId = this.docenteIdDelGrupo();
    if (docenteId) {
      const conflictoDocente = this.data.sesiones.find(s => s.docenteId === docenteId && s.dia === this.dia && !nuncaCoexisteEnSemana(s.semana, semanaActual) && seSolapanHorarios(s, startIdx, endIdx, this.horasDisponibles));
      if (conflictoDocente) ok = false;
      const texto = conflictoDocente
        ? `El docente ya tiene otra sesión en esa franja — Sesión 1: ${sesion1()}; Sesión 2: ${sesion2(conflictoDocente)}. ` +
          'Elija otro docente o cambie el horario de una de las dos sesiones.'
        : 'El docente está libre en esa franja';
      chks.push({ ok: !conflictoDocente, texto });
    }
    if (!esVirtual) {
      // FE5 auditoría: faltaba la exención de semana que Editar ya tenía — una TipoB nueva en el
      // mismo bloque/aula que una TipoA existente es válida (nunca comparten semana), pero esto
      // la bloqueaba igual por mirar solo solape de horario.
      const conflictoEspacio = this.data.sesiones.find(s => s.espacioId === this.espacioId && s.dia === this.dia && !s.virtual && !nuncaCoexisteEnSemana(s.semana, semanaActual) && seSolapanHorarios(s, startIdx, endIdx, this.horasDisponibles));
      if (conflictoEspacio) ok = false;
      const espNombre = this.data.espacios.find(e => e.id === this.espacioId)?.nombre ?? this.espacioId;
      const texto = conflictoEspacio
        ? `${espNombre} ya está ocupado en esa franja — Sesión 1: ${sesion1()}; Sesión 2: ${sesion2(conflictoEspacio)}. ` +
          'Elija otro espacio o cambie el horario de una de las dos sesiones.'
        : `${espNombre} está libre en esa franja`;
      chks.push({ ok: !conflictoEspacio, texto });
    }
    this.checks.set(chks); this.checksOk.set(ok);
  }

  crear() {
    if (!this.puedeCrear()) return;
    const a = this.asignaturaSeleccionada()!, tipo = this.tipoSesion(), duracion = this.duracionSeleccionada();
    // Grupo sin docente → null: '' no es un Guid y el servidor lo rechazaba con 400.
    const docenteId = this.docenteIdDelGrupo() || null;
    const espacioId = tipo === 'TeoriaVirtual' ? null : (this.espacioId || null);
    if (this.modoFija) {
      const [hh, mm] = this.horaInicio.split(':').map(Number);
      const sesion: Sesion = {
        id: nuevoId(), asignaturaId: a.id, grupoId: this.grupoIdSel(), docenteId: docenteId ?? undefined,
        dia: this.dia, horaInicio: this.horaInicio, duracionHoras: duracion,
        horaFin: `${String(hh + duracion).padStart(2, '0')}:${String(mm).padStart(2, '0')}`,
        espacioId: espacioId ?? undefined, virtual: esVirtualDesde(tipo), alternancia: this.alternancia, tipoFlujo: tipoFlujoDesde(tipo)
      };
      this.dialogRef.close([sesion]);
      return;
    }
    // P0-5 auditoría: la sesión se agrega al horario vigente; sin uno generado no hay dónde guardarla.
    const horarioId = this.state.horarioId();
    if (!horarioId) { this.errorServidor.set('Genere el horario antes de agregar clases.'); return; }
    this.guardando.set(true); this.errorServidor.set('');
    this.persistencia.crearSesionManual({
      horarioId, asignaturaId: a.id, docenteId, espacioId,
      // R2 auditoría: el diálogo ya exige elegir grupo (puedeCrear()) — antes se descartaba aquí.
      grupoId: this.grupoIdSel() || null,
      dia: this.dia, horaInicio: this.horaInicio, duracionHoras: duracion, alternancia: this.alternancia,
      tipoFlujo: tipoFlujoDesde(tipo), esVirtual: esVirtualDesde(tipo)
    }).subscribe({
      next: (sesiones: Sesion[]) => { this.guardando.set(false); this.dialogRef.close(sesiones); },
      error: (err: any) => { this.guardando.set(false); this.errorServidor.set(mensajeErrorHttp(err)); }
    });
  }

  cancelar() { this.dialogRef.close(); }
  // FE5 auditoría: overlaps/diffH vivían duplicados aquí y en EditarSesionDialogComponent — ahora
  // son las funciones de módulo seSolapanHorarios/diffHorasEntre, usadas por recheck() arriba.
  nombreDocente(id?: string): string { return this.data.docentes.find(d => d.id === id)?.nombre ?? '—'; }
}

// ═══ Diálogo de progreso (HF-3 · B — generando) ═══
// FE15 auditoría: antes mostraba "Fase 1/3 · GraphColoring" → "2/3 · CP-SAT" → "3/3 · Algoritmo
// genético" avanzando con setTimeout a los 2s y 10s fijos, sin relación con el progreso real del
// backend (que puede tardar segundos o, en un caso patológico, mucho más — ver PERF1). Le mostraba
// al coordinador (no técnico) jerga de motor y una barra que mentía sobre cuánto faltaba. Sin
// progreso real que reportar (el backend no transmite eventos de avance), es más honesto un
// indicador indeterminado que no finge saber en qué fase va.
@Component({
  selector: 'app-progress-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule],
  template: `
    <div class="popbd" style="align-items:center;text-align:center;gap:13px;padding:26px 22px">
      <div class="spinner"></div>
      <div class="opt">GENERANDO EL HORARIO…</div>
      <div class="prog"><i></i></div>
      <div class="text-muted" style="font-size:12px">Puede tardar hasta 2 min. No cierres la ventana.</div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .spinner { width: 44px; height: 44px; border: 3px solid var(--color-accent-200); border-top-color: var(--color-accent); border-radius: 50%; animation: soea-spin .9s linear infinite; }
    @keyframes soea-spin { to { transform: rotate(360deg); } }
    .opt { font: 600 15px var(--font-heading); letter-spacing: .06em; color: var(--color-accent); }
    .prog { width: 100%; height: 12px; border: 1px solid var(--color-neutral-700); position: relative; overflow: hidden; }
    .prog > i { position: absolute; inset: 0 auto 0 0; width: 30%; background: var(--color-accent); animation: soea-prog 2.4s ease-in-out infinite; }
    @keyframes soea-prog { 0% { width: 12%; } 50% { width: 70%; } 100% { width: 92%; } }
  `]
})
export class ProgressDialogComponent {}
