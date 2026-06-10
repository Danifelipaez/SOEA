import { Component, inject, signal, computed, OnInit, OnDestroy } from '@angular/core';
import { CommonModule, TitleCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatIconModule } from '@angular/material/icon';
import {
  CdkDragDrop, DragDropModule, CdkDropList
} from '@angular/cdk/drag-drop';
import { RouterModule } from '@angular/router';
import { StateService } from '../../core/state.service';
import { HorarioApiService } from '../../core/horario-api.service';
import { PersistenciaService } from '../../core/persistencia.service';
import { CatalogoService } from '../../core/catalogo.service';
import { Asignatura, Docente, Espacio, Sesion } from '../../core/models';

/** Representación visual de una sesión atómica multi-slot. */
interface MergedSesion {
  key: string;
  sesiones: Sesion[];     // siempre length=1 con el nuevo backend; se conserva la forma para drag-drop
  dia: string;
  horaInicio: string;
  horaFin: string;
  duracionSlots: number;
  virtual: boolean;
  alternancia: string;
  /** Semana del ciclo de alternancia ('A'/'B'). Presente desde el modelo bi-semanal. */
  semana?: 'A' | 'B';
  asignaturaId: string;
  docenteId: string;
  espacioId?: string;
  /** Lab de origen (donde es presencial); usado para ubicar la fila virtual en su lab. */
  espacioIdHogar?: string;
}

@Component({
  selector: 'app-horario',
  standalone: true,
  imports: [
    CommonModule, TitleCasePipe, MatButtonModule, MatDialogModule, MatSnackBarModule,
    DragDropModule, RouterModule, MatProgressSpinnerModule, MatIconModule
  ],
  template: `
    <div class="horario-container">
      <div class="header-actions">
        <h1 class="page-title text-primary">Horario</h1>
        <div class="header-buttons">
          @if (state.sesiones().length > 0) {
            <button mat-stroked-button color="primary" (click)="abrirCrearSesion()" [disabled]="loadingBackend()">
              <mat-icon>add_circle_outline</mat-icon> Crear sesión
            </button>
          }
          <button mat-flat-button color="primary" class="primary-button" (click)="generarHorario()" [disabled]="loadingBackend()">
            <mat-icon>auto_awesome</mat-icon> Generar Horario
          </button>
        </div>
      </div>

      @if (state.espacios().length > 0) {
        <!-- Selector de espacio -->
        <div class="space-selector">
          @for (esp of state.espacios(); track esp.id) {
            <button class="pill-button"
                    [class.active]="activeSpace()?.id === esp.id"
                    (click)="selectSpace(esp)">
              {{ esp.nombre }}
            </button>
          }
        </div>

        <!-- Selector de semana A/B -->
        <div class="week-selector">
          <span class="week-label">Semana</span>
          <button class="pill-button week-btn"
                  [class.active]="activeWeek() === 'A'"
                  (click)="selectWeek('A')">
            <span class="week-letter">A</span>
            <span class="week-sub">pares</span>
          </button>
          <button class="pill-button week-btn"
                  [class.active]="activeWeek() === 'B'"
                  (click)="selectWeek('B')">
            <span class="week-letter">B</span>
            <span class="week-sub">impares</span>
          </button>
          @if (activeWeek() === 'A') {
            <span class="week-desc">TipoA presencial · TipoB virtual</span>
          }
          @if (activeWeek() === 'B') {
            <span class="week-desc">TipoB presencial · TipoA virtual</span>
          }
        </div>

        @if (!backendReady()) {
          <div class="backend-alert">
            <mat-icon>cloud_off</mat-icon>
            <span>Sin conexión al backend. Carga datos desde la API para continuar.</span>
            <button mat-stroked-button (click)="syncFromBackend()" [disabled]="loadingBackend()">Reintentar</button>
          </div>
        }

        <!-- Matriz de horario -->
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

                        <div class="cell-drop-zone"
                             cdkDropList
                             [id]="cellId(dia, franja)"
                             [cdkDropListData]="{ dia: dia, franja: franja }"
                             [cdkDropListConnectedTo]="allCellIds()"
                             [cdkDropListDisabled]="isOutOfHours(dia, franja)"
                             (cdkDropListDropped)="drop($event)">

                          @for (merged of getMergedCellSesiones(dia, franja); track merged.key) {
                            <div class="session-card"
                                 cdkDrag
                                 [cdkDragData]="merged"
                                 [cdkDragDisabled]="esTipoA(merged)"
                                 [class.presencial]="!merged.virtual"
                                 [class.virtual]="merged.virtual"
                                 [class.tipo-a]="esTipoA(merged)">

                              @if (esTipoA(merged)) {
                                <div class="tipo-a-badge">Tipo A</div>
                              }
                              <div class="card-title">{{ getAsignaturaName(merged) }}</div>
                              <div class="card-sub">{{ getDocenteName(merged) }}</div>
                              <div class="card-duration">{{ merged.horaInicio }} – {{ merged.horaFin }}</div>
                              <div class="card-badges">
                                @if (merged.virtual) {
                                  <span class="badge-virtual">Virtual</span>
                                }
                                @if (merged.semana) {
                                  <span class="badge-semana">S.{{ merged.semana }}</span>
                                }
                                @if (merged.alternancia !== 'SinAlternancia' && !merged.semana) {
                                  <span class="badge-alt">{{ merged.alternancia }}</span>
                                }
                              </div>

                              <!-- Placeholder durante el arrastre -->
                              <div *cdkDragPlaceholder class="drag-placeholder"></div>
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
      } @else {
        <div class="empty-state">
          <mat-icon>event_busy</mat-icon>
          <p>No hay datos cargados. Ve a <strong>Ingesta de Datos</strong> para comenzar.</p>
          <button mat-stroked-button routerLink="/ingesta">Ir a Ingesta</button>
        </div>
      }
    </div>
  `,
  styles: [`
    .horario-container { padding: 16px; background: white; border-radius: 8px; border: 1px solid #e0e0e0; min-height: 500px; }
    .header-actions { display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px; }
    .header-buttons { display: flex; gap: 10px; align-items: center; }
    .page-title { margin: 0; font-weight: 500; font-size: 24px; }
    .space-selector { display: flex; gap: 8px; margin-bottom: 24px; overflow-x: auto; padding-bottom: 8px; flex-wrap: wrap; }
    .pill-button { padding: 8px 16px; border-radius: 20px; border: 1px solid #e0e0e0; background: white; cursor: pointer; transition: 0.2s; white-space: nowrap; }
    .pill-button.active { background: #1976d2; color: white; border-color: #1976d2; }
    .week-selector { display: flex; gap: 8px; align-items: center; margin-bottom: 16px; flex-wrap: wrap; }
    .week-label { font-size: 11px; letter-spacing: 0.08em; text-transform: uppercase; color: #757575; }
    .week-btn { display: flex; flex-direction: column; align-items: center; padding: 6px 18px; min-width: 72px; }
    .week-letter { font-size: 15px; font-weight: 700; line-height: 1; }
    .week-sub { font-size: 9px; color: #9e9e9e; letter-spacing: 0.05em; margin-top: 1px; }
    .pill-button.week-btn.active .week-sub { color: rgba(255,255,255,0.75); }
    .week-desc { font-size: 11px; color: #9e9e9e; align-self: center; margin-left: 4px; font-style: italic; }
    .backend-alert {
      display: flex; align-items: center; gap: 12px; padding: 8px 12px; margin-bottom: 16px;
      border: 1px solid #ffe0b2; border-radius: 8px; background: #fff3e0; color: #8d6e63;
    }
    .backend-alert mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .matrix-scroll { overflow-x: auto; }
    .horario-matrix { width: 100%; border-collapse: collapse; min-width: 820px; table-layout: fixed; }
    .horario-matrix th, .horario-matrix td { border: 1px solid #e0e0e0; padding: 4px; text-align: center; vertical-align: top; }
    .time-col { width: 72px; }
    .time-cell { font-size: 12px; font-weight: 500; color: #757575; vertical-align: middle; white-space: nowrap; }
    .matrix-cell { height: 72px; position: relative; }
    .out-of-hours { background: repeating-linear-gradient(45deg, #f5f5f5, #f5f5f5 8px, #eeeeee 8px, #eeeeee 16px); }

    /* Drop-zone: must fill the cell so the user can drop onto it */
    .cell-drop-zone { min-height: 64px; height: 100%; display: flex; flex-direction: column; gap: 3px; padding: 2px; }

    /* Highlight when dragging over */
    .cell-drop-zone.cdk-drop-list-dragging { background: #e3f2fd; border-radius: 4px; }
    .cell-drop-zone.cdk-drop-list-receiving { background: #f3e5f5; border-radius: 4px; }

    .session-card {
      padding: 6px 8px; border-radius: 4px; text-align: left; font-size: 11px;
      cursor: grab; position: relative; box-shadow: 0 1px 3px rgba(0,0,0,0.15);
      user-select: none; flex: 1;
    }
    .session-card.cdk-drag-animating { transition: transform 250ms cubic-bezier(0,0,0.2,1); }
    .session-card.cdk-drag-disabled { cursor: not-allowed; opacity: .85; }
    .cdk-drag-preview {
      box-shadow: 0 6px 20px rgba(0,0,0,0.25); border-radius: 4px;
      font-size: 11px; padding: 6px 8px; background: white; opacity: .95;
    }
    .drag-placeholder { background: rgba(25,118,210,0.12); border: 2px dashed #1976d2; border-radius: 4px; min-height: 36px; }

    .presencial { background-color: #e8f5e9; border-left: 4px solid #388e3c; }
    .virtual    { background-color: #fafafa;  border-left: 4px solid #9e9e9e; }
    .tipo-a            { border-left-color: #f57c00; }
    .tipo-a.presencial { background-color: #fff8e1; }

    .card-title    { font-weight: 600; margin-bottom: 2px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .card-sub      { color: #616161; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .card-duration { color: #9e9e9e; font-size: 10px; margin-top: 2px; }
    .card-badges   { display: flex; gap: 4px; margin-top: 3px; flex-wrap: wrap; }
    .badge-virtual { padding: 1px 5px; background: #e0e0e0; border-radius: 10px; font-size: 9px; }
    .badge-semana  { padding: 1px 5px; background: #ede7f6; color: #512da8; border-radius: 10px; font-size: 9px; font-weight: 600; }
    .badge-alt     { padding: 1px 5px; background: #e3f2fd; color: #1565c0; border-radius: 10px; font-size: 9px; }
    .tipo-a-badge  { position: absolute; top: 2px; right: 2px; font-size: 9px; background: #ff9800; color: white; padding: 1px 4px; border-radius: 2px; }

    .empty-state { text-align: center; padding: 64px 32px; color: #757575; }
    .empty-state mat-icon { font-size: 64px; width: 64px; height: 64px; margin-bottom: 16px; }
    .empty-state p { font-size: 16px; margin-bottom: 16px; }
  `]
})
export class HorarioComponent implements OnInit {
  state     = inject(StateService);
  dialog    = inject(MatDialog);
  snackBar  = inject(MatSnackBar);
  horarioApi = inject(HorarioApiService);
  persistencia = inject(PersistenciaService);
  catalogo  = inject(CatalogoService);

  dias = ['lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado'];
  franjas = [
    '06:00','07:00','08:00','09:00','10:00','11:00','12:00',
    '13:00','14:00','15:00','16:00','17:00','18:00','19:00','20:00'
  ];

  activeSpace = signal<Espacio | null>(null);
  activeWeek = signal<'A' | 'B'>('A');
  loadingBackend = signal(false);
  backendReady = signal(false);

  ngOnInit() {
    this.syncFromBackend();
  }

  constructor() {
    const espacios = this.state.espacios();
    if (espacios.length > 0) this.activeSpace.set(espacios[0]);
  }

  selectSpace(esp: Espacio) { this.activeSpace.set(esp); }
  selectWeek(week: 'A' | 'B') { this.activeWeek.set(week); }

  syncFromBackend() {
    this.loadingBackend.set(true);
    this.catalogo.cargarTodo().subscribe({
      next: () => {
        this.loadingBackend.set(false);
        this.backendReady.set(true);

        const espacios = this.state.espacios();
        const current = this.activeSpace();
        if (!current || !espacios.find(e => e.id === current.id)) {
          this.activeSpace.set(espacios[0] ?? null);
        }
      },
      error: () => {
        this.loadingBackend.set(false);
        this.backendReady.set(false);
        this.snackBar.open(
          'No se pudo conectar con el backend. Verifica que la API esté activa.',
          'Cerrar',
          { duration: 5000, panelClass: ['snack-error'] }
        );
      }
    });
  }

  // ── Merged session computation ────────────────────────────────────────────────

  /**
   * Cada sesión es atómica y trae su duración real desde el backend.
   * `duracionSlots` proviene de `sesion.duracionHoras` (1, 2, 3 horas, ...).
   * Si por error vinieran dos sesiones consecutivas legítimas, se renderizan como
   * cards separados — NO se fusionan (eso era el bug visual viejo).
   */
  private computeMergedMap(spaceId: string | undefined, allSesiones: Sesion[]): Map<string, MergedSesion[]> {
    const map = new Map<string, MergedSesion[]>();

    // Una sesión pertenece a un lab si es presencial ahí (espacioId) o si es su lab de origen
    // (espacioIdHogar) cuando esa semana se dicta virtual. Así la fila virtual aparece SOLO en su
    // laboratorio, no en todos. Fallback a la lógica previa si el backend no envía el lab de origen.
    const visible = allSesiones.filter(s =>
      this.sesionPerteneceAlEspacio(s, spaceId) && this.sesionVisibleEnSemana(s)
    );

    for (const s of visible) {
      const dur = Math.max(1, Math.round(s.duracionHoras ?? this.diffHoras(s.horaInicio, s.horaFin)));
      const merged: MergedSesion = {
        key:           s.id,
        sesiones:      [s],
        dia:           s.dia,
        horaInicio:    s.horaInicio,
        horaFin:       s.horaFin,
        duracionSlots: dur,
        virtual:       s.virtual,
        alternancia:   s.alternancia,
        semana:        s.semana,
        asignaturaId:  s.asignaturaId,
        docenteId:     s.docenteId,
        espacioId:     s.espacioId,
        espacioIdHogar: s.espacioIdHogar,
      };
      const cid = this.cellId(s.dia, s.horaInicio);
      if (!map.has(cid)) map.set(cid, []);
      map.get(cid)!.push(merged);
    }

    return map;
  }

  private diffHoras(horaInicio: string, horaFin: string): number {
    const [hi, mi] = horaInicio.split(':').map(Number);
    const [hf, mf] = horaFin.split(':').map(Number);
    return Math.max(1, (hf * 60 + mf - (hi * 60 + mi)) / 60);
  }

  /**
   * Una sesión se muestra en la matriz de un lab si:
   *   - es presencial en ese lab (espacioId === spaceId), o
   *   - ese lab es su origen (espacioIdHogar === spaceId) cuando la fila es virtual.
   * Sin spaceId seleccionado no filtra por espacio. Fallback: si no llega espacioIdHogar,
   * conserva la regla previa (toda virtual visible) para datos anteriores al campo.
   */
  private sesionPerteneceAlEspacio(s: Sesion, spaceId: string | undefined): boolean {
    if (!spaceId) return true;
    if (s.espacioId === spaceId) return true;
    if (s.espacioIdHogar) return s.espacioIdHogar === spaceId;
    return s.virtual; // fallback datos legacy sin lab de origen
  }

  private sesionVisibleEnSemana(s: Sesion): boolean {
    // Modelo bi-semanal (Incremento 1): cada DTO ya trae su semana explícita.
    if (s.semana) return s.semana === this.activeWeek();
    // Fallback para datos previos al modelo bi-semanal.
    if (s.alternancia === 'SinAlternancia') return true;
    return this.activeWeek() === 'A' ? s.alternancia === 'TipoA' : s.alternancia === 'TipoB';
  }

  /**
   * Devuelve true si la sesión `s` ocupa la franja dada, es decir,
   * si franja ∈ [s.horaInicio, s.horaInicio + duracionHoras).
   * Usar en conflictos de drag & drop en lugar de `s.horaInicio === franja`.
   */
  private sesionOcupaFranja(s: Sesion, franja: string): boolean {
    const startIdx  = this.franjas.indexOf(s.horaInicio);
    if (startIdx < 0) return false;
    const dur       = Math.max(1, Math.round(s.duracionHoras ?? this.diffHoras(s.horaInicio, s.horaFin)));
    const franjaIdx = this.franjas.indexOf(franja);
    return franjaIdx >= startIdx && franjaIdx < startIdx + dur;
  }

  mergedByCell = computed(() =>
    this.computeMergedMap(this.activeSpace()?.id, this.state.sesiones())
  );

  /** Set of cellIds that are "swallowed" by a prior rowspan and must not render a <td>. */
  coveredCells = computed(() => {
    const covered = new Set<string>();
    for (const mergedList of this.mergedByCell().values()) {
      for (const m of mergedList) {
        if (m.duracionSlots <= 1) continue;
        const startIdx = this.franjas.indexOf(m.horaInicio);
        for (let k = 1; k < m.duracionSlots; k++) {
          const idx = startIdx + k;
          if (idx < this.franjas.length) {
            covered.add(this.cellId(m.dia, this.franjas[idx]));
          }
        }
      }
    }
    return covered;
  });

  getMergedCellSesiones(dia: string, franja: string): MergedSesion[] {
    return this.mergedByCell().get(this.cellId(dia, franja)) ?? [];
  }

  isCoveredByMergedPrior(dia: string, franja: string): boolean {
    return this.coveredCells().has(this.cellId(dia, franja));
  }

  getMergedRowspan(dia: string, franja: string): number {
    const merged = this.getMergedCellSesiones(dia, franja);
    if (merged.length === 0) return 1;
    return Math.max(...merged.map(m => m.duracionSlots));
  }

  // ── Helpers de celda ─────────────────────────────────────────────────────────

  cellId(dia: string, franja: string): string {
    return `cell-${dia}-${franja.replace(':', '')}`;
  }

  /** Celdas fuera de horario — computado una sola vez (dias/franjas son estáticos). */
  private readonly outOfHoursCells = computed(() => {
    const set = new Set<string>();
    const sabadoLimit = this.franjas.indexOf('13:00');
    this.franjas.forEach((franja, idx) => {
      if (idx >= sabadoLimit) set.add(this.cellId('sabado', franja));
    });
    return set;
  });

  isOutOfHours(dia: string, franja: string): boolean {
    return this.outOfHoursCells().has(this.cellId(dia, franja));
  }

  /** Lista de IDs válidos para cdkDropListConnectedTo — computado y cacheado. */
  allCellIds = computed(() => {
    const ids: string[] = [];
    for (const dia of this.dias) {
      for (const franja of this.franjas) {
        if (!this.isOutOfHours(dia, franja)) ids.push(this.cellId(dia, franja));
      }
    }
    return ids;
  });

  esTipoA(merged: MergedSesion): boolean {
    return merged.alternancia === 'TipoA';
  }

  getAsignaturaName(merged: MergedSesion): string {
    return this.state.asignaturaById().get(merged.asignaturaId)?.nombre ?? 'Desconocida';
  }

  getDocenteName(merged: MergedSesion): string {
    return this.state.docenteById().get(merged.docenteId)?.nombre ?? '';
  }

  // ── Drag & Drop ──────────────────────────────────────────────────────────────

  drop(event: CdkDragDrop<{ dia: string; franja: string }>) {
    const merged: MergedSesion = event.item.data;

    if (event.previousContainer === event.container) return;
    if (this.esTipoA(merged)) return;

    const targetDia    = event.container.data.dia;
    const targetFranja = event.container.data.franja;

    // Validate docente availability — verificar que TODA la duración cabe en la ventana
    const docente = this.state.docentes().find(d => d.id === merged.docenteId);
    if (docente && docente.disponibilidad) {
      const diaDisp = docente.disponibilidad[targetDia];
      if (diaDisp?.noDisponible) {
        this.snackBar.open(
          `El docente no tiene disponibilidad el ${targetDia}.`,
          'Cerrar', { duration: 4000, panelClass: ['snack-error'] }
        );
        return;
      }
      if (diaDisp?.tipo === 'Franja específica') {
        const toMin  = (t: string) => { const [h, m] = t.split(':').map(Number); return h * 60 + m; };
        const startMin = toMin(targetFranja);
        const endMin   = startMin + merged.duracionSlots * 60;
        const desdeMin = toMin(diaDisp.desde ?? '00:00');
        const hastaMin = toMin(diaDisp.hasta ?? '23:59');
        if (startMin < desdeMin || endMin > hastaMin) {
          this.snackBar.open(
            `El docente solo está disponible de ${diaDisp.desde} a ${diaDisp.hasta} el ${targetDia} y la sesión (${merged.duracionSlots}h) no cabe en esa ventana.`,
            'Cerrar', { duration: 4000, panelClass: ['snack-error'] }
          );
          return;
        }
      }
    }

    // Validate that all slots the merged block would occupy are free and in-bounds.
    // sesionOcupaFranja() verifica el span completo [horaInicio, horaInicio+dur),
    // no solo el inicio — así detectamos colisiones con sesiones de 2-3 h que cubren la franja.
    const spaceId = this.activeSpace()?.id;
    const targetStartIdx = this.franjas.indexOf(targetFranja);
    for (let k = 0; k < merged.duracionSlots; k++) {
      const checkFranja = this.franjas[targetStartIdx + k];
      if (!checkFranja) {
        this.snackBar.open('La sesión no cabe en ese horario (se saldría del límite).', 'Cerrar', { duration: 4000, panelClass: ['snack-error'] });
        return;
      }
      const currentWeek = this.activeWeek();

      // HC-S01: conflicto de espacio — solo presencial, mismo lab, misma semana del ciclo.
      const conflict = this.state.sesiones().find(s =>
        s.id !== merged.key &&
        s.espacioId === spaceId &&
        s.dia === targetDia &&
        this.sesionOcupaFranja(s, checkFranja) &&
        !s.virtual &&
        (s.semana
          ? s.semana === currentWeek
          : (s.alternancia === merged.alternancia || s.alternancia === 'SinAlternancia' || merged.alternancia === 'SinAlternancia'))
      );
      if (conflict) {
        this.snackBar.open(
          'Conflicto: el espacio ya está ocupado en esa franja para el mismo tipo de alternancia.',
          'Cerrar', { duration: 4000, panelClass: ['snack-error'] }
        );
        return;
      }

      // HC-I01: conflicto de docente — su tiempo se consume en AMBAS semanas (presencial y
      // virtual son sincrónicas y comparten franja), así que el solape es independiente de la
      // semana y de la modalidad. Basta que otra sesión del mismo docente ocupe la franja el
      // mismo día. Se excluyen las filas de la propia sesión (comparten `id`).
      const docenteConflict = this.state.sesiones().find(s =>
        s.id !== merged.key &&
        s.docenteId === merged.docenteId &&
        s.dia === targetDia &&
        this.sesionOcupaFranja(s, checkFranja)
      );
      if (docenteConflict) {
        this.snackBar.open(
          'Conflicto: el docente ya tiene otra sesión en esa franja (presencial o virtual sincrónica).',
          'Cerrar', { duration: 4000, panelClass: ['snack-error'] }
        );
        return;
      }
    }

    // Mover el inicio y recalcular el fin desde la duración. Se mueven TODAS las filas de la
    // sesión (semanas A y B comparten id y horario) preservando la modalidad/semana/espacio de
    // cada una — no se colapsan en un solo objeto.
    const newStart = this.franjas[targetStartIdx];
    const endIdx   = targetStartIdx + merged.duracionSlots;
    const newEnd   = endIdx < this.franjas.length
      ? this.franjas[endIdx]
      : this.addHours(newStart, merged.duracionSlots);
    this.state.moverSesion(merged.key, targetDia, newStart, newEnd);

    this.snackBar.open('Sesión movida correctamente.', '', { duration: 2000 });
  }

  private addHours(franja: string, horas: number): string {
    const [h, m] = franja.split(':').map(Number);
    return `${String(h + horas).padStart(2, '0')}:${String(m).padStart(2, '0')}`;
  }

  // ── Generación de horario ────────────────────────────────────────────────────

  generarHorario() {
    if (!this.backendReady()) {
      this.snackBar.open('Conecta el backend antes de generar el horario.', 'Cerrar', { duration: 4000 });
      return;
    }
    if (this.state.asignaturas().length === 0 || this.state.espacios().length === 0 || this.state.docentes().length === 0) {
      this.snackBar.open('Carga asignaturas, docentes y espacios antes de generar el horario.', 'Cerrar', { duration: 4000 });
      return;
    }

    const dialogRef = this.dialog.open(ProgressDialogComponent, { disableClose: true, width: '500px' });

    this.horarioApi
      .generarHorario(
        this.state.asignaturas(),
        this.state.docentes(),
        this.state.espacios(),
        this.state.configuracionAlgoritmo()
      )
      .subscribe({
        next: (respuesta) => {
          dialogRef.close();
          const sesiones = this.horarioApi.mapearSesiones(respuesta.sesiones);
          this.state.setSesiones(sesiones);
          this.state.setExecutionLogs(respuesta.logs || []);
          this.snackBar.open(
            `✅ Horario generado: ${sesiones.length} sesiones (fitness: ${respuesta.puntajeFitness.toFixed(2)}).`,
            'Cerrar',
            { duration: 6000 }
          );
        },
        error: (err: any) => {
          dialogRef.close();
          const mensaje = err.mensajeError || err.message || err.error || 'Error desconocido';
          if (err.logs && Array.isArray(err.logs)) {
             this.state.setExecutionLogs(err.logs);
          }
          this.snackBar.open(
            `❌ ${mensaje}`,
            'Cerrar',
            { duration: 8000, panelClass: ['snack-error'] }
          );
        }
      });
  }

  // ── Crear sesión manual ──────────────────────────────────────────────────────

  abrirCrearSesion() {
    const ref = this.dialog.open(CrearSesionDialogComponent, {
      width: '580px',
      maxHeight: '90vh',
      data: {
        asignaturas: this.state.asignaturas(),
        docentes:    this.state.docentes(),
        espacios:    this.state.espacios(),
        sesiones:    this.state.sesiones(),
        programaById: this.state.programaById()
      }
    });

    ref.afterClosed().subscribe((nuevas: Sesion[] | undefined) => {
      if (!nuevas?.length) return;
      this.state.sesiones.update(prev => [...prev, ...nuevas]);
      this.snackBar.open(
        `✅ Sesión creada correctamente (${nuevas.length} fila${nuevas.length > 1 ? 's' : ''} añadidas al horario).`,
        'Cerrar', { duration: 5000 }
      );
    });
  }

}

// ─── Diálogo: Crear sesión manual ────────────────────────────────────────────

interface DialogData {
  asignaturas: Asignatura[];
  docentes:    Docente[];
  espacios:    Espacio[];
  sesiones:    Sesion[];
  programaById: Map<string, { id: string; nombre: string }>;
}

interface Check { ok: boolean; texto: string; }

@Component({
  selector: 'app-crear-sesion-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule,
            MatProgressSpinnerModule, MatIconModule, MatSnackBarModule],
  template: `
<h2 mat-dialog-title class="dlg-title">
  <mat-icon>add_circle_outline</mat-icon> Nueva sesión manual
</h2>

<mat-dialog-content class="dlg-content">

  <!-- ── Paso 1: Asignatura ── -->
  <section class="form-section">
    <div class="section-label">
      <span class="step-num">1</span> ¿Qué asignatura?
    </div>
    <div class="field-group">
      <label class="field-label">Asignatura <span class="req">*</span></label>
      <select class="field-select"
              [(ngModel)]="asignaturaId"
              (ngModelChange)="onAsignaturaChange($event)">
        <option value="">— Seleccione una asignatura —</option>
        @for (grupo of gruposAsignatura; track grupo.programa) {
          <optgroup [label]="grupo.programa">
            @for (a of grupo.items; track a.id) {
              <option [value]="a.id">{{ a.nombre }}{{ a.codigo ? ' (' + a.codigo + ')' : '' }}</option>
            }
          </optgroup>
        }
      </select>

      @if (asignaturaSeleccionada()) {
        <div class="asig-hint">
          <mat-icon class="hint-icon">person</mat-icon>
          <span>{{ nombreDocente(asignaturaSeleccionada()?.docenteId) }}</span>
          <span class="sep">·</span>
          <mat-icon class="hint-icon">timer</mat-icon>
          <span>{{ asignaturaSeleccionada()?.horasPorSesion }}h por sesión
            <em>(fijo por plan de estudios, no editable)</em>
          </span>
        </div>
      }
    </div>
  </section>

  <!-- ── Paso 2: Cuándo y dónde ── -->
  <section class="form-section">
    <div class="section-label">
      <span class="step-num">2</span> ¿Cuándo y dónde?
    </div>

    <div class="day-picker">
      <span class="field-label">Día <span class="req">*</span></span>
      <div class="day-pills">
        @for (d of dias; track d.valor) {
          <button type="button" class="day-pill"
                  [class.active]="dia === d.valor"
                  (click)="dia = d.valor; recheck()">
            {{ d.etiqueta }}
          </button>
        }
      </div>
    </div>

    <div class="two-col">
      <div class="field-group">
        <label class="field-label">Hora de inicio <span class="req">*</span></label>
        <select class="field-select" [(ngModel)]="horaInicio" (ngModelChange)="recheck()">
          <option value="">— Seleccione —</option>
          @for (h of horasDisponibles; track h) {
            <option [value]="h">{{ h }}</option>
          }
        </select>
      </div>
      <div class="field-group">
        <label class="field-label">Laboratorio / Espacio <span class="req">*</span></label>
        <select class="field-select" [(ngModel)]="espacioId" (ngModelChange)="recheck()"
                [disabled]="espacioFijoBloqueado()">
          <option value="">— Seleccione —</option>
          @for (e of espaciosDisponibles(); track e.id) {
            <option [value]="e.id">{{ e.nombre }}</option>
          }
        </select>
        @if (espacioFijoBloqueado()) {
          <div class="lock-hint">
            <mat-icon class="hint-icon lock">lock</mat-icon>
            Fijo por currículum — no se puede cambiar
          </div>
        }
      </div>
    </div>
  </section>

  <!-- ── Paso 3: Alternancia ── -->
  <section class="form-section">
    <div class="section-label">
      <span class="step-num">3</span> ¿Cómo alterna?
    </div>
    <div class="alt-pills">
      <button type="button" class="alt-pill" [class.active]="alternancia === 'TipoA'"
              (click)="alternancia = 'TipoA'; recheck()">
        <strong>Tipo A</strong><br>
        <small>Presencial S.A · Virtual S.B</small>
      </button>
      <button type="button" class="alt-pill" [class.active]="alternancia === 'TipoB'"
              (click)="alternancia = 'TipoB'; recheck()">
        <strong>Tipo B</strong><br>
        <small>Virtual S.A · Presencial S.B</small>
      </button>
      <button type="button" class="alt-pill" [class.active]="alternancia === 'SinAlternancia'"
              (click)="alternancia = 'SinAlternancia'; recheck()">
        <strong>Sin alternancia</strong><br>
        <small>Presencial todas las semanas</small>
      </button>
    </div>
  </section>

  <!-- ── Panel de verificación ── -->
  @if (asignaturaId && dia && horaInicio && espacioId) {
    <section class="checks-panel">
      <div class="checks-title">Verificación de restricciones</div>
      @for (c of checks(); track c.texto) {
        <div class="check-row" [class.ok]="c.ok" [class.fail]="!c.ok">
          <mat-icon>{{ c.ok ? 'check_circle' : 'cancel' }}</mat-icon>
          <span>{{ c.texto }}</span>
        </div>
      }
      @if (resumen()) {
        <div class="preview-hint">{{ resumen() }}</div>
      }
    </section>
  }

  @if (guardando()) {
    <div class="saving-row"><mat-spinner diameter="20"></mat-spinner><span>Guardando en la base de datos…</span></div>
  }
  @if (errorServidor()) {
    <div class="server-error">⚠ {{ errorServidor() }}</div>
  }

</mat-dialog-content>

<mat-dialog-actions align="end" class="dlg-actions">
  <button mat-stroked-button (click)="cancelar()" [disabled]="guardando()">Cancelar</button>
  <button mat-flat-button color="primary"
          [disabled]="!puedeCrear() || guardando()"
          (click)="crear()">
    <mat-icon>add</mat-icon>
    {{ guardando() ? 'Creando…' : 'Crear sesión' }}
  </button>
</mat-dialog-actions>
  `,
  styles: [`
    .dlg-title { display: flex; align-items: center; gap: 8px; font-size: 18px; }
    .dlg-content { display: flex; flex-direction: column; gap: 20px; padding: 16px 0; min-width: 0; }
    .dlg-actions { padding: 12px 0 0; gap: 10px; }

    .form-section { background: #fafafa; border: 1px solid #f0f0f0; border-radius: 8px; padding: 16px; display: flex; flex-direction: column; gap: 12px; }
    .section-label { display: flex; align-items: center; gap: 8px; font-weight: 600; font-size: 13px; color: #424242; text-transform: uppercase; letter-spacing: 0.05em; }
    .step-num { width: 22px; height: 22px; background: #1976d2; color: white; border-radius: 50%; display: flex; align-items: center; justify-content: center; font-size: 11px; font-weight: 700; flex-shrink: 0; }

    .field-group { display: flex; flex-direction: column; gap: 4px; }
    .field-label { font-size: 13px; color: #616161; font-weight: 500; }
    .req { color: #c62828; }
    .field-select { padding: 9px 10px; border: 1px solid #d0d0d0; border-radius: 6px; font-size: 14px; background: white; width: 100%; cursor: pointer; outline: none; }
    .field-select:focus { border-color: #1976d2; box-shadow: 0 0 0 2px rgba(25,118,210,.2); }
    .field-select:disabled { background: #f5f5f5; cursor: not-allowed; color: #9e9e9e; }

    .asig-hint { display: flex; align-items: center; gap: 6px; font-size: 12px; color: #616161; background: #e3f2fd; padding: 7px 10px; border-radius: 5px; flex-wrap: wrap; }
    .hint-icon { font-size: 14px; width: 14px; height: 14px; }
    .sep { color: #bdbdbd; }
    em { color: #9e9e9e; }

    .day-picker { display: flex; flex-direction: column; gap: 6px; }
    .day-pills { display: flex; gap: 6px; flex-wrap: wrap; }
    .day-pill { padding: 7px 14px; border: 1px solid #e0e0e0; border-radius: 20px; background: white; cursor: pointer; font-size: 13px; font-weight: 500; transition: 0.15s; }
    .day-pill:hover { background: #e3f2fd; border-color: #90caf9; }
    .day-pill.active { background: #1976d2; color: white; border-color: #1976d2; }

    .two-col { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
    .lock-hint { display: flex; align-items: center; gap: 4px; font-size: 11px; color: #9e9e9e; }
    .lock { color: #bdbdbd !important; }

    .alt-pills { display: flex; gap: 8px; flex-wrap: wrap; }
    .alt-pill { flex: 1; min-width: 130px; padding: 10px 12px; border: 1px solid #e0e0e0; border-radius: 8px; background: white; cursor: pointer; text-align: center; line-height: 1.4; font-size: 13px; transition: 0.15s; }
    .alt-pill:hover { background: #f5f5f5; }
    .alt-pill.active { background: #e3f2fd; border-color: #1976d2; color: #1565c0; }

    .checks-panel { border: 1px solid #e0e0e0; border-radius: 8px; overflow: hidden; }
    .checks-title { padding: 10px 14px; background: #f5f5f5; font-size: 12px; font-weight: 600; color: #616161; text-transform: uppercase; letter-spacing: 0.05em; }
    .check-row { display: flex; align-items: center; gap: 8px; padding: 8px 14px; font-size: 13px; border-top: 1px solid #f5f5f5; }
    .check-row.ok mat-icon  { color: #2e7d32; }
    .check-row.fail mat-icon { color: #c62828; }
    .check-row.fail { background: #fff8f8; }
    .preview-hint { padding: 8px 14px; background: #e8f5e9; font-size: 12px; color: #1b5e20; border-top: 1px solid #c8e6c9; }

    .saving-row { display: flex; align-items: center; gap: 10px; color: #1565c0; font-size: 13px; }
    .server-error { background: #ffebee; color: #c62828; padding: 10px 14px; border-radius: 6px; font-size: 13px; }
  `]
})
export class CrearSesionDialogComponent {
  private dialogRef = inject(MatDialogRef<CrearSesionDialogComponent>);
  private data: DialogData = inject(MAT_DIALOG_DATA);
  private persistencia = inject(PersistenciaService);

  // ── Estado del formulario ─────────────────────────────────────────────────
  asignaturaId  = '';
  dia           = '';
  horaInicio    = '';
  espacioId     = '';
  alternancia: 'TipoA' | 'TipoB' | 'SinAlternancia' = 'SinAlternancia';

  guardando     = signal(false);
  errorServidor = signal('');

  readonly dias = [
    { valor: 'lunes',     etiqueta: 'Lun' },
    { valor: 'martes',    etiqueta: 'Mar' },
    { valor: 'miercoles', etiqueta: 'Mié' },
    { valor: 'jueves',    etiqueta: 'Jue' },
    { valor: 'viernes',   etiqueta: 'Vie' },
    { valor: 'sabado',    etiqueta: 'Sáb' },
  ];

  readonly horasDisponibles = [
    '06:00','07:00','08:00','09:00','10:00','11:00','12:00',
    '13:00','14:00','15:00','16:00','17:00','18:00','19:00','20:00'
  ];

  // ── Computed helpers ──────────────────────────────────────────────────────
  asignaturaSeleccionada = signal<Asignatura | undefined>(undefined);
  espacioFijoBloqueado   = signal(false);
  checks                 = signal<Check[]>([]);
  checksOk               = signal(false);

  // Grupos de asignaturas por programa para el <optgroup>
  readonly gruposAsignatura = this.buildGrupos();

  // Solo espacios laboratorio (filtra por tipo si la asignatura lo requiere)
  espaciosDisponibles = computed(() => {
    const a = this.asignaturaSeleccionada();
    if (!a) return this.data.espacios;
    if (a.espacioFijoId) {
      return this.data.espacios.filter(e => e.id === a.espacioFijoId);
    }
    return this.data.espacios.filter(e => e.tipo === 'Laboratorio');
  });

  resumen = signal('');

  puedeCrear = computed(() =>
    !!this.asignaturaId &&
    !!this.dia &&
    !!this.horaInicio &&
    !!this.espacioId &&
    this.checksOk() &&
    !this.guardando()
  );

  // ── Ciclo de vida ─────────────────────────────────────────────────────────
  onAsignaturaChange(id: string) {
    const a = this.data.asignaturas.find(x => x.id === id);
    this.asignaturaSeleccionada.set(a);

    if (a?.alternancia && a.alternancia !== 'SinAlternancia') {
      this.alternancia = a.alternancia as 'TipoA' | 'TipoB';
    } else {
      this.alternancia = 'SinAlternancia';
    }

    if (a?.espacioFijoId) {
      this.espacioId = a.espacioFijoId;
      this.espacioFijoBloqueado.set(true);
    } else {
      this.espacioFijoBloqueado.set(false);
      this.espacioId = '';
    }
    this.recheck();
  }

  recheck() {
    const a   = this.asignaturaSeleccionada();
    const dur = a?.horasPorSesion ?? 2;
    const chks: Check[] = [];
    let ok = true;

    if (!a || !this.dia || !this.horaInicio || !this.espacioId) {
      this.checks.set([]);
      this.checksOk.set(false);
      this.resumen.set('');
      return;
    }

    const startIdx  = this.horasDisponibles.indexOf(this.horaInicio);
    const endIdx    = startIdx + dur;

    const franjasFin = endIdx < this.horasDisponibles.length
      ? this.horasDisponibles[endIdx]
      : this.addH(this.horaInicio, dur);

    // ── HC-I02: docente disponible ese día ──────────────────────────────────
    const docente = this.data.docentes.find(d => d.id === a.docenteId);
    let docenteDisp = true;
    if (docente?.disponibilidad) {
      const diaDisp = docente.disponibilidad[this.dia];
      if (diaDisp?.noDisponible) {
        docenteDisp = false;
      } else if (diaDisp?.tipo === 'Franja específica') {
        const toM = (t: string) => { const [h, m] = t.split(':').map(Number); return h * 60 + m; };
        const sM = toM(this.horaInicio), eM = toM(franjasFin);
        const dM = toM(diaDisp.desde ?? '00:00'), hM = toM(diaDisp.hasta ?? '23:59');
        if (sM < dM || eM > hM) docenteDisp = false;
      }
    }
    if (!docenteDisp) ok = false;
    chks.push({
      ok: docenteDisp,
      texto: docenteDisp
        ? `${this.nombreDocente(a.docenteId)} tiene disponibilidad ese día y hora`
        : `${this.nombreDocente(a.docenteId)} NO tiene disponibilidad en ese horario`
    });

    // ── HC-I01: docente sin conflictos ─────────────────────────────────────
    const conflictoDocente = this.data.sesiones.find(s =>
      s.docenteId === a.docenteId &&
      s.dia === this.dia &&
      this.overlaps(s, startIdx, endIdx)
    );
    if (conflictoDocente) ok = false;
    chks.push({
      ok: !conflictoDocente,
      texto: conflictoDocente
        ? `El docente ya tiene otra sesión: ${this.nomAsig(conflictoDocente.asignaturaId)} (${conflictoDocente.horaInicio}–${conflictoDocente.horaFin})`
        : 'El docente está libre en esa franja horaria'
    });

    // ── HC-S01: espacio libre ──────────────────────────────────────────────
    const presencialSemana = (s: Sesion): boolean => {
      if (s.virtual) return false;
      if (!s.semana) return s.alternancia === 'SinAlternancia';
      // Para la semana presencial de la nueva sesión
      const semPres = this.alternancia === 'TipoA' ? 'A'
                    : this.alternancia === 'TipoB' ? 'B'
                    : null; // SinAlternancia: ambas
      return semPres === null || s.semana === semPres;
    };

    const conflictoEspacio = this.data.sesiones.find(s =>
      s.espacioId === this.espacioId &&
      s.dia === this.dia &&
      presencialSemana(s) &&
      this.overlaps(s, startIdx, endIdx)
    );
    if (conflictoEspacio) ok = false;
    const espNombre = this.data.espacios.find(e => e.id === this.espacioId)?.nombre ?? this.espacioId;
    chks.push({
      ok: !conflictoEspacio,
      texto: conflictoEspacio
        ? `${espNombre} ya está ocupado por ${this.nomAsig(conflictoEspacio.asignaturaId)} (${conflictoEspacio.horaInicio}–${conflictoEspacio.horaFin})`
        : `${espNombre} está libre en esa franja`
    });

    // ── HC-S05: espacio fijo ───────────────────────────────────────────────
    if (a.espacioFijoId && this.espacioId && this.espacioId !== a.espacioFijoId) {
      ok = false;
      const nomFijo = this.data.espacios.find(e => e.id === a.espacioFijoId)?.nombre ?? 'lab asignado';
      chks.push({ ok: false, texto: `Esta asignatura tiene laboratorio fijo: ${nomFijo}` });
    }

    this.checks.set(chks);
    this.checksOk.set(ok);

    if (ok) {
      const filas = this.alternancia === 'SinAlternancia' ? 2 : 2;
      const presDesc = this.alternancia === 'TipoA' ? 'presencial S.A + virtual S.B'
                     : this.alternancia === 'TipoB' ? 'virtual S.A + presencial S.B'
                     : 'presencial en ambas semanas';
      this.resumen.set(`✓ Todo correcto — se agregarán ${filas} filas al horario (${presDesc}).`);
    } else {
      this.resumen.set('');
    }
  }

  // ── Crear ─────────────────────────────────────────────────────────────────
  crear() {
    if (!this.puedeCrear()) return;
    this.guardando.set(true);
    this.errorServidor.set('');

    const a = this.asignaturaSeleccionada()!;
    const payload = {
      asignaturaId:  a.id,
      docenteId:     a.docenteId ?? '',
      espacioId:     this.espacioId || null,
      dia:           this.dia,
      horaInicio:    this.horaInicio,
      duracionHoras: a.horasPorSesion,
      alternancia:   this.alternancia
    };

    this.persistencia.crearSesionManual(payload).subscribe({
      next: (sesiones: Sesion[]) => {
        this.guardando.set(false);
        this.dialogRef.close(sesiones);
      },
      error: (err: any) => {
        this.guardando.set(false);
        const msg = err?.error?.error ?? err?.message ?? 'Error al guardar la sesión.';
        this.errorServidor.set(msg);
      }
    });
  }

  cancelar() { this.dialogRef.close(); }

  // ── Helpers internos ──────────────────────────────────────────────────────
  private overlaps(s: Sesion, newStart: number, newEnd: number): boolean {
    const sStart = this.horasDisponibles.indexOf(s.horaInicio);
    if (sStart < 0) return false;
    const sDur = Math.max(1, Math.round(s.duracionHoras ?? this.diffH(s.horaInicio, s.horaFin)));
    return newStart < sStart + sDur && sStart < newEnd;
  }

  nombreDocente(id?: string): string {
    return this.data.docentes.find(d => d.id === id)?.nombre ?? '—';
  }
  private nomAsig(id: string): string {
    return this.data.asignaturas.find(a => a.id === id)?.nombre ?? 'desconocida';
  }
  private addH(hora: string, h: number): string {
    const [hh, mm] = hora.split(':').map(Number);
    return `${String(hh + h).padStart(2, '0')}:${String(mm).padStart(2, '0')}`;
  }
  private diffH(i: string, f: string): number {
    const [hi, mi] = i.split(':').map(Number);
    const [hf, mf] = f.split(':').map(Number);
    return Math.max(1, (hf * 60 + mf - (hi * 60 + mi)) / 60);
  }

  private buildGrupos(): { programa: string; items: Asignatura[] }[] {
    const map = new Map<string, { programa: string; items: Asignatura[] }>();
    for (const a of this.data.asignaturas) {
      const prog = this.data.programaById.get(a.programaId)?.nombre ?? 'Sin programa';
      if (!map.has(prog)) map.set(prog, { programa: prog, items: [] });
      map.get(prog)!.items.push(a);
    }
    return [...map.values()].sort((a, b) => a.programa.localeCompare(b.programa));
  }
}

// ─── Diálogo de progreso ─────────────────────────────────────────────────────

@Component({
  selector: 'app-progress-dialog',
  standalone: true,
  imports: [MatProgressSpinnerModule, MatDialogModule],
  template: `
    <h2 mat-dialog-title>Optimizando Horario</h2>
    <mat-dialog-content class="prog-dialog">
      <div class="phase-item" [class.active]="phase() === 1" [class.done]="phase() > 1">
        @if (phase() === 1) { <mat-spinner diameter="20"></mat-spinner> }
        @if (phase() > 1)  { <span class="icon-done">✓</span> }
        <span>Fase 1: Pre-procesamiento (coloración de grafos)</span>
      </div>
      <div class="phase-item" [class.active]="phase() === 2" [class.done]="phase() > 2" [class.pending]="phase() < 2">
        @if (phase() === 2) { <mat-spinner diameter="20"></mat-spinner> }
        @if (phase() > 2)  { <span class="icon-done">✓</span> }
        @if (phase() < 2)  { <span class="icon-pending">○</span> }
        <span>Fase 2: Viabilidad (constraint programming)</span>
      </div>
      <div class="phase-item" [class.active]="phase() === 3" [class.done]="phase() > 3" [class.pending]="phase() < 3">
        @if (phase() === 3) { <mat-spinner diameter="20"></mat-spinner> }
        @if (phase() > 3)  { <span class="icon-done">✓</span> }
        @if (phase() < 3)  { <span class="icon-pending">○</span> }
        <span>Fase 3: Optimización (algoritmo genético)</span>
      </div>
    </mat-dialog-content>
  `,
  styles: [`
    .prog-dialog { display: flex; flex-direction: column; gap: 20px; padding: 16px 0; min-width: 340px; }
    .phase-item { display: flex; align-items: center; gap: 12px; color: #757575; }
    .phase-item.active { color: #1976d2; font-weight: 500; }
    .phase-item.done { color: #388e3c; }
    .icon-done, .icon-pending { font-weight: bold; width: 20px; text-align: center; }
  `]
})
export class ProgressDialogComponent implements OnInit, OnDestroy {
  phase = signal(1);
  private timers: ReturnType<typeof setTimeout>[] = [];

  ngOnInit() {
    this.timers.push(setTimeout(() => { if (this.phase() === 1) this.phase.set(2); }, 2000));
    this.timers.push(setTimeout(() => { if (this.phase() === 2) this.phase.set(3); }, 10000));
  }

  ngOnDestroy() {
    this.timers.forEach(t => clearTimeout(t));
  }
}
