import { Component, input, computed, inject } from '@angular/core';
import { CommonModule, TitleCasePipe } from '@angular/common';
import { StateService } from '../../core/state.service';
import { Sesion } from '../../core/models';

interface MergedSesion {
  key: string;
  dia: string;
  horaInicio: string;
  horaFin: string;
  duracionSlots: number;
  virtual: boolean;
  alternancia: string;
  semana?: 'A' | 'B';
  asignaturaId: string;
  docenteId: string;
  espacioId?: string;
  espacioIdHogar?: string;
}

/**
 * Grilla de horario read-only (sin drag & drop).
 * Reutilizada por HorarioDocenteComponent y cualquier vista que necesite
 * mostrar una matriz semana-franja sin interacción de arrastre.
 */
@Component({
  selector: 'app-horario-grid',
  standalone: true,
  imports: [CommonModule, TitleCasePipe],
  template: `
    <div class="matrix-scroll">
      <table class="horario-matrix">
        <thead>
          <tr>
            <th class="time-col">Hora</th>
            @for (dia of dias; track dia) {
              <th>{{ dia | titlecase }}</th>
            }
          </tr>
        </thead>
        <tbody>
          @for (franja of franjas; track franja) {
            <tr>
              <td class="time-cell">{{ franja }}</td>
              @for (dia of dias; track dia) {
                @if (!isCoveredByMergedPrior(dia, franja)) {
                  <td class="matrix-cell"
                      [attr.rowspan]="getMergedRowspan(dia, franja)"
                      [class.out-of-hours]="isOutOfHours(dia, franja)">
                    <div class="cell-zone">
                      @for (m of getMergedCellSesiones(dia, franja); track m.key) {
                        <div class="session-card"
                             [class.presencial]="!m.virtual"
                             [class.virtual]="m.virtual"
                             [class.tipo-a]="esTipoA(m)">
                          @if (esTipoA(m)) {
                            <div class="tipo-a-badge">Tipo A</div>
                          }
                          <div class="card-context">{{ getContextLabel(m) }}</div>
                          <div class="card-title">{{ getCardTitle(m) }}</div>
                          <div class="card-sub">{{ getCardSub(m) }}</div>
                          <div class="card-duration">{{ m.horaInicio }} – {{ m.horaFin }}</div>
                          <div class="card-badges">
                            @if (m.virtual) {
                              <span class="badge-virtual">Virtual</span>
                            }
                            @if (m.semana) {
                              <span class="badge-semana">S.{{ m.semana }}</span>
                            }
                            @if (m.alternancia !== 'SinAlternancia' && !m.semana) {
                              <span class="badge-alt">{{ m.alternancia }}</span>
                            }
                            @if (getGrupoLabel(m); as g) {
                              <span class="badge-grupo">{{ g }}</span>
                            }
                          </div>
                        </div>
                      }
                    </div>
                  </td>
                }
              }
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
  styles: [`
    .matrix-scroll { overflow-x: auto; }
    .horario-matrix { width: 100%; border-collapse: collapse; min-width: 820px; table-layout: fixed; }
    .horario-matrix th, .horario-matrix td { border: 1px solid #e0e0e0; padding: 4px; text-align: center; vertical-align: top; }
    .time-col { width: 72px; }
    .time-cell { font-size: 12px; font-weight: 500; color: #757575; vertical-align: middle; white-space: nowrap; }
    .matrix-cell { height: 72px; position: relative; }
    .out-of-hours { background: repeating-linear-gradient(45deg, #f5f5f5, #f5f5f5 8px, #eeeeee 8px, #eeeeee 16px); }
    .cell-zone { min-height: 64px; height: 100%; display: flex; flex-direction: column; gap: 3px; padding: 2px; }
    .session-card { padding: 6px 8px; border-radius: 4px; text-align: left; font-size: 11px; position: relative; box-shadow: 0 1px 3px rgba(0,0,0,.15); flex: 1; }
    .presencial { background-color: #e8f5e9; border-left: 4px solid #388e3c; }
    .virtual    { background-color: #fafafa;  border-left: 4px solid #9e9e9e; }
    .tipo-a            { border-left-color: #f57c00; }
    .tipo-a.presencial { background-color: #fff8e1; }
    .card-context  { font-size: 9px; color: #9e9e9e; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; margin-bottom: 1px; }
    .card-title    { font-weight: 600; margin-bottom: 2px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .card-sub      { color: #616161; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .card-duration { color: #9e9e9e; font-size: 10px; margin-top: 2px; }
    .card-badges   { display: flex; gap: 4px; margin-top: 3px; flex-wrap: wrap; }
    .badge-virtual { padding: 1px 5px; background: #e0e0e0; border-radius: 10px; font-size: 9px; }
    .badge-semana  { padding: 1px 5px; background: #ede7f6; color: #512da8; border-radius: 10px; font-size: 9px; font-weight: 600; }
    .badge-alt     { padding: 1px 5px; background: #e3f2fd; color: #1565c0; border-radius: 10px; font-size: 9px; }
    .badge-grupo   { padding: 1px 5px; background: #e8f5e9; color: #2e7d32; border-radius: 10px; font-size: 9px; font-weight: 600; }
    .tipo-a-badge  { position: absolute; top: 2px; right: 2px; font-size: 9px; background: #ff9800; color: white; padding: 1px 4px; border-radius: 2px; }
  `]
})
export class HorarioGridComponent {
  /** Sesiones ya pre-filtradas (por docente, por espacio, o todas). */
  sesiones        = input<Sesion[]>([]);
  activeWeek      = input<'A' | 'B'>('A');
  /** undefined = no filtrar por espacio (p. ej. vista docente). */
  filtroEspacioId = input<string | undefined>(undefined);
  /** true → card muestra nombre del espacio en lugar del docente. */
  modoDocente     = input(false);

  private state = inject(StateService);

  readonly dias = ['lunes','martes','miercoles','jueves','viernes','sabado'];
  readonly franjas = [
    '06:00','07:00','08:00','09:00','10:00','11:00','12:00',
    '13:00','14:00','15:00','16:00','17:00','18:00','19:00','20:00'
  ];

  // ── Computed map ──────────────────────────────────────────────────────────────

  mergedByCell = computed(() => {
    const spaceId = this.filtroEspacioId();
    const week    = this.activeWeek();
    const map     = new Map<string, MergedSesion[]>();

    for (const s of this.sesiones()) {
      if (!this.perteneceAlEspacio(s, spaceId)) continue;
      if (!this.visibleEnSemana(s, week)) continue;

      const dur = Math.max(1, Math.round(s.duracionHoras ?? this.diffH(s.horaInicio, s.horaFin)));
      const m: MergedSesion = {
        key: s.id, dia: s.dia,
        horaInicio: s.horaInicio, horaFin: s.horaFin,
        duracionSlots: dur, virtual: s.virtual,
        alternancia: s.alternancia, semana: s.semana,
        asignaturaId: s.asignaturaId, docenteId: s.docenteId,
        espacioId: s.espacioId, espacioIdHogar: s.espacioIdHogar,
      };
      const cid = this.cellId(s.dia, s.horaInicio);
      if (!map.has(cid)) map.set(cid, []);
      map.get(cid)!.push(m);
    }
    return map;
  });

  coveredCells = computed(() => {
    const covered = new Set<string>();
    for (const list of this.mergedByCell().values()) {
      for (const m of list) {
        if (m.duracionSlots <= 1) continue;
        const startIdx = this.franjas.indexOf(m.horaInicio);
        for (let k = 1; k < m.duracionSlots; k++) {
          const idx = startIdx + k;
          if (idx < this.franjas.length) covered.add(this.cellId(m.dia, this.franjas[idx]));
        }
      }
    }
    return covered;
  });

  private outOfHoursCells = computed(() => {
    const set = new Set<string>();
    const limit = this.franjas.indexOf('13:00');
    this.franjas.forEach((f, i) => { if (i >= limit) set.add(this.cellId('sabado', f)); });
    return set;
  });

  // ── Helpers de celda ──────────────────────────────────────────────────────────

  cellId(dia: string, franja: string): string { return `g-${dia}-${franja.replace(':', '')}`; }

  getMergedCellSesiones(dia: string, franja: string): MergedSesion[] {
    return this.mergedByCell().get(this.cellId(dia, franja)) ?? [];
  }

  isCoveredByMergedPrior(dia: string, franja: string): boolean {
    return this.coveredCells().has(this.cellId(dia, franja));
  }

  getMergedRowspan(dia: string, franja: string): number {
    const list = this.getMergedCellSesiones(dia, franja);
    return list.length === 0 ? 1 : Math.max(...list.map(m => m.duracionSlots));
  }

  isOutOfHours(dia: string, franja: string): boolean {
    return this.outOfHoursCells().has(this.cellId(dia, franja));
  }

  esTipoA(m: MergedSesion): boolean { return m.alternancia === 'TipoA'; }

  getCardTitle(m: MergedSesion): string {
    return this.state.asignaturaById().get(m.asignaturaId)?.nombre ?? 'Desconocida';
  }

  getCardSub(m: MergedSesion): string {
    if (this.modoDocente()) {
      const eid = m.espacioId ?? m.espacioIdHogar;
      return this.state.espacioById().get(eid ?? '')?.nombre ?? (m.virtual ? 'Virtual' : '—');
    }
    return this.state.docenteById().get(m.docenteId)?.nombre ?? '';
  }

  getContextLabel(m: MergedSesion): string {
    const asig = this.state.asignaturaById().get(m.asignaturaId);
    if (!asig) return '';
    const prog = this.state.programaById().get(asig.programaId);
    if (!prog) return '';
    const fac = this.state.facultadById().get(prog.facultadId);
    return fac ? `${fac.nombre} · ${prog.nombre}` : prog.nombre;
  }

  getGrupoLabel(m: MergedSesion): string | null {
    const asig = this.state.asignaturaById().get(m.asignaturaId);
    return asig?.grupoNumero ? `G${asig.grupoNumero}` : null;
  }

  // ── Filtros internos ──────────────────────────────────────────────────────────

  private perteneceAlEspacio(s: Sesion, spaceId: string | undefined): boolean {
    if (!spaceId) return true;
    if (s.espacioId === spaceId) return true;
    if (s.espacioIdHogar) return s.espacioIdHogar === spaceId;
    return s.virtual;
  }

  private visibleEnSemana(s: Sesion, week: 'A' | 'B'): boolean {
    if (s.semana) return s.semana === week;
    if (s.alternancia === 'SinAlternancia') return true;
    return week === 'A' ? s.alternancia === 'TipoA' : s.alternancia === 'TipoB';
  }

  private diffH(horaInicio: string, horaFin: string): number {
    const [hi, mi] = horaInicio.split(':').map(Number);
    const [hf, mf] = horaFin.split(':').map(Number);
    return Math.max(1, (hf * 60 + mf - (hi * 60 + mi)) / 60);
  }
}
