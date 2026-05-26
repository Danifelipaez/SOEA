import { Component, inject, signal, computed, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatIconModule } from '@angular/material/icon';
import {
  CdkDragDrop, DragDropModule, CdkDropList
} from '@angular/cdk/drag-drop';
import { RouterModule } from '@angular/router';
import { StateService } from '../../core/state.service';
import { HorarioApiService } from '../../core/horario-api.service';
import { Espacio, Sesion } from '../../core/models';

/** Representación visual de una sesión atómica multi-slot. */
/** Representación visual de una sesión atómica multi-slot. */
interface MergedSesion {
  key: string;
  sesiones: Sesion[];     // siempre length=1 con el nuevo backend; se conserva la forma para drag-drop
  sesiones: Sesion[];     // siempre length=1 con el nuevo backend; se conserva la forma para drag-drop
  dia: string;
  horaInicio: string;
  horaFin: string;
  duracionSlots: number;
  virtual: boolean;
  alternancia: string;
  asignaturaId: string;
  docenteId: string;
  espacioId?: string;
}

@Component({
  selector: 'app-horario',
  standalone: true,
  imports: [
    CommonModule, MatButtonModule, MatDialogModule, MatSnackBarModule,
    DragDropModule, RouterModule, MatProgressSpinnerModule, MatIconModule
  ],
  template: `
    <div class="horario-container">
      <div class="header-actions">
        <h1 class="page-title text-primary">Horario</h1>
        <button mat-flat-button color="primary" class="primary-button" (click)="generarHorario()">
          <mat-icon>auto_awesome</mat-icon> Generar Horario
        </button>
      </div>

      <ng-container *ngIf="state.espacios().length > 0; else noData">
        <!-- Selector de espacio -->
        <div class="space-selector">
          <button *ngFor="let esp of state.espacios()"
                  class="pill-button"
                  [class.active]="activeSpace()?.id === esp.id"
                  (click)="selectSpace(esp)">
            {{ esp.nombre }}
          </button>
        </div>

        <!-- Matriz de horario -->
        <div class="matrix-scroll">
          <table class="horario-matrix">
            <thead>
              <tr>
                <th class="time-col">Hora</th>
                <th *ngFor="let dia of dias">{{ dia | titlecase }}</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let franja of franjas">
                <td class="time-cell">{{ franja }}</td>
                <ng-container *ngFor="let dia of dias">
                  <td *ngIf="!isCoveredByMergedPrior(dia, franja)"
                      class="matrix-cell"
                      [attr.rowspan]="getMergedRowspan(dia, franja)"
                      [class.out-of-hours]="isOutOfHours(dia, franja)">

                    <div class="cell-drop-zone"
                         cdkDropList
                         [id]="cellId(dia, franja)"
                         [cdkDropListData]="{ dia: dia, franja: franja }"
                         [cdkDropListConnectedTo]="allCellIds()"
                         [cdkDropListDisabled]="isOutOfHours(dia, franja)"
                         (cdkDropListDropped)="drop($event)">

                      <div class="session-card"
                           *ngFor="let merged of getMergedCellSesiones(dia, franja)"
                           cdkDrag
                           [cdkDragData]="merged"
                           [cdkDragDisabled]="esTipoA(merged)"
                           [class.presencial]="!merged.virtual"
                           [class.virtual]="merged.virtual"
                           [class.tipo-a]="esTipoA(merged)">

                        <div class="tipo-a-badge" *ngIf="esTipoA(merged)">Tipo A</div>
                        <div class="card-title">{{ getAsignaturaName(merged) }}</div>
                        <div class="card-sub">{{ getDocenteName(merged) }}</div>
                        <div class="card-duration">{{ merged.horaInicio }} – {{ merged.horaFin }}</div>
                        <div class="card-badges">
                          <span class="badge-virtual" *ngIf="merged.virtual">Virtual</span>
                          <span class="badge-alt" *ngIf="merged.alternancia !== 'SinAlternancia'">{{ merged.alternancia }}</span>
                        </div>

                        <!-- Placeholder durante el arrastre -->
                        <div *cdkDragPlaceholder class="drag-placeholder"></div>
                      </div>

                    </div>
                  </td>
                </ng-container>
              </tr>
            </tbody>
          </table>
        </div>
      </ng-container>

      <ng-template #noData>
        <div class="empty-state">
          <mat-icon>event_busy</mat-icon>
          <p>No hay datos cargados. Ve a <strong>Ingesta de Datos</strong> para comenzar.</p>
          <button mat-stroked-button routerLink="/ingesta">Ir a Ingesta</button>
        </div>
      </ng-template>
    </div>
  `,
  styles: [`
    .horario-container { padding: 16px; background: white; border-radius: 8px; border: 1px solid #e0e0e0; min-height: 500px; }
    .header-actions { display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px; }
    .page-title { margin: 0; font-weight: 500; font-size: 24px; }
    .space-selector { display: flex; gap: 8px; margin-bottom: 24px; overflow-x: auto; padding-bottom: 8px; flex-wrap: wrap; }
    .pill-button { padding: 8px 16px; border-radius: 20px; border: 1px solid #e0e0e0; background: white; cursor: pointer; transition: 0.2s; white-space: nowrap; }
    .pill-button.active { background: #1976d2; color: white; border-color: #1976d2; }
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
    .tipo-a     { border-left-color: #f57c00; background-color: #fff8e1; }

    .card-title    { font-weight: 600; margin-bottom: 2px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .card-sub      { color: #616161; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .card-duration { color: #9e9e9e; font-size: 10px; margin-top: 2px; }
    .card-badges   { display: flex; gap: 4px; margin-top: 3px; flex-wrap: wrap; }
    .badge-virtual { padding: 1px 5px; background: #e0e0e0; border-radius: 10px; font-size: 9px; }
    .badge-alt     { padding: 1px 5px; background: #e3f2fd; color: #1565c0; border-radius: 10px; font-size: 9px; }
    .tipo-a-badge  { position: absolute; top: 2px; right: 2px; font-size: 9px; background: #ff9800; color: white; padding: 1px 4px; border-radius: 2px; }

    .empty-state { text-align: center; padding: 64px 32px; color: #757575; }
    .empty-state mat-icon { font-size: 64px; width: 64px; height: 64px; margin-bottom: 16px; }
    .empty-state p { font-size: 16px; margin-bottom: 16px; }
  `]
})
export class HorarioComponent {
  state     = inject(StateService);
  dialog    = inject(MatDialog);
  snackBar  = inject(MatSnackBar);
  horarioApi = inject(HorarioApiService);

  dias = ['lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado'];
  franjas = [
    '06:00','07:00','08:00','09:00','10:00','11:00','12:00',
    '13:00','14:00','15:00','16:00','17:00','18:00','19:00','20:00'
  ];

  activeSpace = signal<Espacio | null>(null);

  constructor() {
    const espacios = this.state.espacios();
    if (espacios.length > 0) this.activeSpace.set(espacios[0]);
  }

  selectSpace(esp: Espacio) { this.activeSpace.set(esp); }

  // ── Merged session computation ────────────────────────────────────────────────

  /**
   * Cada sesión es atómica y trae su duración real desde el backend.
   * `duracionSlots` proviene de `sesion.duracionHoras` (1, 2, 3 horas, ...).
   * Si por error vinieran dos sesiones consecutivas legítimas, se renderizan como
   * cards separados — NO se fusionan (eso era el bug visual viejo).
   * Cada sesión es atómica y trae su duración real desde el backend.
   * `duracionSlots` proviene de `sesion.duracionHoras` (1, 2, 3 horas, ...).
   * Si por error vinieran dos sesiones consecutivas legítimas, se renderizan como
   * cards separados — NO se fusionan (eso era el bug visual viejo).
   */
  private computeMergedMap(spaceId: string | undefined, allSesiones: Sesion[]): Map<string, MergedSesion[]> {
    const map = new Map<string, MergedSesion[]>();

    const visible = allSesiones.filter(s => s.virtual || s.espacioId === spaceId);

    for (const s of visible) {
<<<<<<< HEAD
      const k = `${s.dia}||${s.asignaturaId}||${s.alternancia}`;
      if (!chains.has(k)) chains.set(k, []);
      chains.get(k)!.push(s);
    }

    for (const chain of chains.values()) {
      chain.sort((a, b) => a.horaInicio.localeCompare(b.horaInicio));

      let i = 0;
      while (i < chain.length) {
        // Extend run while consecutive (end of slot i == start of slot i+1)
        let j = i;
        while (j + 1 < chain.length && chain[j].horaFin === chain[j + 1].horaInicio) {
          j++;
        }
        const run = chain.slice(i, j + 1);
        const first = run[0];
        const last  = run[run.length - 1];
        const startH = parseInt(first.horaInicio.split(':')[0], 10);
        const endH   = parseInt(last.horaFin.split(':')[0], 10);
        const merged: MergedSesion = {
          key:           run.map(s => s.id).join('-'),
          sesiones:      run,
          dia:           first.dia,
          horaInicio:    first.horaInicio,
          horaFin:       last.horaFin,
          duracionSlots: Math.max(1, endH - startH),
          virtual:       first.virtual,
          alternancia:   first.alternancia,
          asignaturaId:  first.asignaturaId,
          docenteId:     first.docenteId,
          espacioId:     first.espacioId,
        };
        const cid = this.cellId(first.dia, first.horaInicio);
        if (!map.has(cid)) map.set(cid, []);
        map.get(cid)!.push(merged);
        i = j + 1;
      }
=======
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
        asignaturaId:  s.asignaturaId,
        docenteId:     s.docenteId,
        espacioId:     s.espacioId,
      };
      const cid = this.cellId(s.dia, s.horaInicio);
      if (!map.has(cid)) map.set(cid, []);
      map.get(cid)!.push(merged);
>>>>>>> 9cb7fe8214ddaa0985ea442d261553b840dd0f28
    }

    return map;
  }

  private diffHoras(horaInicio: string, horaFin: string): number {
    const [hi, mi] = horaInicio.split(':').map(Number);
    const [hf, mf] = horaFin.split(':').map(Number);
    return Math.max(1, (hf * 60 + mf - (hi * 60 + mi)) / 60);
  }

  private diffHoras(horaInicio: string, horaFin: string): number {
    const [hi, mi] = horaInicio.split(':').map(Number);
    const [hf, mf] = horaFin.split(':').map(Number);
    return Math.max(1, (hf * 60 + mf - (hi * 60 + mi)) / 60);
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

  allCellIds(): string[] {
    const ids: string[] = [];
    for (const dia of this.dias) {
      for (const franja of this.franjas) {
        if (!this.isOutOfHours(dia, franja)) ids.push(this.cellId(dia, franja));
      }
    }
    return ids;
  }

  isOutOfHours(dia: string, franja: string): boolean {
    if (dia === 'sabado' && this.franjas.indexOf(franja) >= this.franjas.indexOf('13:00')) return true;
    return false;
  }

  esTipoA(merged: MergedSesion): boolean {
    return merged.alternancia === 'TipoA';
  }

  getAsignaturaName(merged: MergedSesion): string {
    const a = this.state.asignaturas().find(x => x.id === merged.asignaturaId);
    return a?.nombre ?? 'Desconocida';
  }

  getDocenteName(merged: MergedSesion): string {
    const d = this.state.docentes().find(x => x.id === merged.docenteId);
    return d?.nombre ?? '';
  }

  // ── Drag & Drop ──────────────────────────────────────────────────────────────

  drop(event: CdkDragDrop<{ dia: string; franja: string }>) {
    const merged: MergedSesion = event.item.data;

    if (event.previousContainer === event.container) return;
    if (this.esTipoA(merged)) return;

    const targetDia    = event.container.data.dia;
    const targetFranja = event.container.data.franja;

    // Validate docente availability
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
        const horaInt = parseInt(targetFranja.replace(':', ''), 10);
        const desde   = parseInt((diaDisp.desde ?? '00:00').replace(':', ''), 10);
        const hasta   = parseInt((diaDisp.hasta ?? '23:59').replace(':', ''), 10);
        if (horaInt < desde || horaInt >= hasta) {
          this.snackBar.open(
            `El docente solo está disponible de ${diaDisp.desde} a ${diaDisp.hasta} el ${targetDia}.`,
            'Cerrar', { duration: 4000, panelClass: ['snack-error'] }
          );
          return;
        }
      }
    }

    // Validate that all slots the merged block would occupy are free and in-bounds
    const spaceId = this.activeSpace()?.id;
    const targetStartIdx = this.franjas.indexOf(targetFranja);
    for (let k = 0; k < merged.duracionSlots; k++) {
      const checkFranja = this.franjas[targetStartIdx + k];
      if (!checkFranja) {
        this.snackBar.open('La sesión no cabe en ese horario (se saldría del límite).', 'Cerrar', { duration: 4000, panelClass: ['snack-error'] });
        return;
      }
      const conflict = this.state.sesiones().find(s =>
        !merged.sesiones.some(ms => ms.id === s.id) &&
        s.espacioId === spaceId &&
        s.dia === targetDia &&
        s.horaInicio === checkFranja &&
        !s.virtual &&
        (s.alternancia === merged.alternancia || s.alternancia === 'SinAlternancia' || merged.alternancia === 'SinAlternancia')
      );
      if (conflict) {
        this.snackBar.open(
          'Conflicto: el espacio ya está ocupado en esa franja para el mismo tipo de alternancia.',
          'Cerrar', { duration: 4000, panelClass: ['snack-error'] }
        );
        return;
      }
    }

    // La sesión es atómica: mover su inicio y recalcular su fin desde la duración.
    const sesion = merged.sesiones[0];
    const newStart = this.franjas[targetStartIdx];
    const endIdx   = targetStartIdx + merged.duracionSlots;
    const newEnd   = endIdx < this.franjas.length
      ? this.franjas[endIdx]
      : this.addHours(newStart, merged.duracionSlots);
    this.state.updateSesion({ ...sesion, dia: targetDia, horaInicio: newStart, horaFin: newEnd });
    // La sesión es atómica: mover su inicio y recalcular su fin desde la duración.
    const sesion = merged.sesiones[0];
    const newStart = this.franjas[targetStartIdx];
    const endIdx   = targetStartIdx + merged.duracionSlots;
    const newEnd   = endIdx < this.franjas.length
      ? this.franjas[endIdx]
      : this.addHours(newStart, merged.duracionSlots);
    this.state.updateSesion({ ...sesion, dia: targetDia, horaInicio: newStart, horaFin: newEnd });

    this.snackBar.open('Sesión movida correctamente.', '', { duration: 2000 });
  }

  private addHours(franja: string, horas: number): string {
  private addHours(franja: string, horas: number): string {
    const [h, m] = franja.split(':').map(Number);
    return `${String(h + horas).padStart(2, '0')}:${String(m).padStart(2, '0')}`;
    return `${String(h + horas).padStart(2, '0')}:${String(m).padStart(2, '0')}`;
  }

  // ── Generación de horario ────────────────────────────────────────────────────

  generarHorario() {
    if (this.state.asignaturas().length === 0 || this.state.espacios().length === 0 || this.state.docentes().length === 0) {
      this.snackBar.open('Carga asignaturas, docentes y espacios antes de generar el horario.', 'Cerrar', { duration: 4000 });
      return;
    }

    const dialogRef = this.dialog.open(ProgressDialogComponent, { disableClose: true, width: '500px' });

    this.horarioApi
      .generarHorario(
        this.state.asignaturas(),
        this.state.docentes(),
        this.state.espacios()
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
}

// ─── Diálogo de progreso ─────────────────────────────────────────────────────

@Component({
  selector: 'app-progress-dialog',
  standalone: true,
  imports: [CommonModule, MatProgressSpinnerModule, MatDialogModule],
  template: `
    <h2 mat-dialog-title>Optimizando Horario</h2>
    <mat-dialog-content class="prog-dialog">
      <div class="phase-item" [class.active]="phase === 1" [class.done]="phase > 1">
        <mat-spinner diameter="20" *ngIf="phase === 1"></mat-spinner>
        <span class="icon-done" *ngIf="phase > 1">✓</span>
        <span>Fase 1: Pre-procesamiento (coloración de grafos)</span>
      </div>
      <div class="phase-item" [class.active]="phase === 2" [class.done]="phase > 2" [class.pending]="phase < 2">
        <mat-spinner diameter="20" *ngIf="phase === 2"></mat-spinner>
        <span class="icon-done" *ngIf="phase > 2">✓</span>
        <span class="icon-pending" *ngIf="phase < 2">○</span>
        <span>Fase 2: Viabilidad (constraint programming)</span>
      </div>
      <div class="phase-item" [class.active]="phase === 3" [class.done]="phase > 3" [class.pending]="phase < 3">
        <mat-spinner diameter="20" *ngIf="phase === 3"></mat-spinner>
        <span class="icon-done" *ngIf="phase > 3">✓</span>
        <span class="icon-pending" *ngIf="phase < 3">○</span>
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
  phase = 1;
  private timers: ReturnType<typeof setTimeout>[] = [];

  ngOnInit() {
    this.timers.push(setTimeout(() => { if (this.phase === 1) this.phase = 2; }, 2000));
    this.timers.push(setTimeout(() => { if (this.phase === 2) this.phase = 3; }, 10000));
  }

  ngOnDestroy() {
    this.timers.forEach(t => clearTimeout(t));
  }
}
