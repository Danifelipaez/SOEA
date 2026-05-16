import { Component, inject, signal, computed } from '@angular/core';
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

/**
 * Celda única en la matriz (día × franja).
 * CDK DragDrop necesita que todos los drop-lists se conozcan entre sí:
 * usamos [cdkDropListConnectedTo] con los IDs del resto de celdas.
 */
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
                <td *ngFor="let dia of dias"
                    class="matrix-cell"
                    [class.out-of-hours]="isOutOfHours(dia, franja)">

                  <div class="cell-drop-zone"
                       cdkDropList
                       [id]="cellId(dia, franja)"
                       [cdkDropListData]="{ dia: dia, franja: franja }"
                       [cdkDropListConnectedTo]="allCellIds()"
                       [cdkDropListDisabled]="isOutOfHours(dia, franja)"
                       (cdkDropListDropped)="drop($event)">

                    <div class="session-card"
                         *ngFor="let sesion of getCellSesiones(dia, franja)"
                         cdkDrag
                         [cdkDragData]="sesion"
                         [cdkDragDisabled]="esTipoA(sesion)"
                         [class.presencial]="!sesion.virtual"
                         [class.virtual]="sesion.virtual"
                         [class.tipo-a]="esTipoA(sesion)">

                      <div class="tipo-a-badge" *ngIf="esTipoA(sesion)">Tipo A</div>
                      <div class="card-title">{{ getAsignaturaName(sesion) }}</div>
                      <div class="card-sub">{{ getDocenteName(sesion) }}</div>
                      <div class="card-badges">
                        <span class="badge-virtual" *ngIf="sesion.virtual">Virtual</span>
                        <span class="badge-alt" *ngIf="sesion.alternancia !== 'SinAlternancia'">{{ sesion.alternancia }}</span>
                      </div>

                      <!-- Placeholder durante el arrastre -->
                      <div *cdkDragPlaceholder class="drag-placeholder"></div>
                    </div>

                  </div>
                </td>
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
      user-select: none;
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

    .card-title { font-weight: 600; margin-bottom: 2px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .card-sub   { color: #616161; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .card-badges { display: flex; gap: 4px; margin-top: 3px; flex-wrap: wrap; }
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
    '13:00','14:00','15:00','16:00','17:00','18:00','19:00','20:00','20:30'
  ];

  activeSpace = signal<Espacio | null>(null);

  constructor() {
    const espacios = this.state.espacios();
    if (espacios.length > 0) this.activeSpace.set(espacios[0]);
  }

  selectSpace(esp: Espacio) { this.activeSpace.set(esp); }

  // ── Helpers de celda ─────────────────────────────────────────────────────────

  cellId(dia: string, franja: string): string {
    return `cell-${dia}-${franja.replace(':', '')}`;
  }

  /** Lista de todos los IDs de celdas habilitadas (para conectar drop-lists entre sí). */
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
    if (dia === 'sabado' && this.franjas.indexOf(franja) >= this.franjas.indexOf('14:00')) return true;
    return false;
  }

  getCellSesiones(dia: string, franja: string): Sesion[] {
    const spaceId = this.activeSpace()?.id;
    return this.state.sesiones().filter(s =>
      s.espacioId === spaceId && s.dia === dia && s.horaInicio === franja
    );
  }

  esTipoA(sesion: Sesion): boolean {
    return sesion.alternancia === 'TipoA';
  }

  getAsignaturaName(sesion: Sesion): string {
    const a = this.state.asignaturas().find(x => x.id === sesion.asignaturaId);
    return a?.nombre ?? 'Desconocida';
  }

  getDocenteName(sesion: Sesion): string {
    const d = this.state.docentes().find(x => x.id === sesion.docenteId);
    return d?.nombre ?? '';
  }

  // ── Drag & Drop ──────────────────────────────────────────────────────────────

  /**
   * El evento trae:
   *  - previousContainer.data  → { dia, franja } origen
   *  - container.data           → { dia, franja } destino
   *  - item.data                → la Sesion arrastrada
   *
   * Aplicamos la actualización directamente al signal de sesiones para
   * que Angular detecte el cambio y la vista se actualice.
   */
  drop(event: CdkDragDrop<{ dia: string; franja: string }>) {
    const sesion: Sesion = event.item.data;

    // Si se soltó en la misma celda, no hacer nada
    if (event.previousContainer === event.container) return;

    // Tipo A: el arrastre ya está bloqueado por cdkDragDisabled, pero por seguridad:
    if (this.esTipoA(sesion)) return;

    const targetDia    = event.container.data.dia;
    const targetFranja = event.container.data.franja;

    // Validar disponibilidad del docente
    const docente = this.state.docentes().find(d => d.id === sesion.docenteId);
    if (docente && docente.disponibilidad) {
      const diaDisp = docente.disponibilidad[targetDia];
      if (diaDisp?.noDisponible) {
        this.snackBar.open(
          `El docente no tiene disponibilidad el ${targetDia}.`,
          'Cerrar',
          { duration: 4000, panelClass: ['snack-error'] }
        );
        return;
      }
      // Validar franja horaria si el docente tiene franja específica
      if (diaDisp?.tipo === 'Franja específica') {
        const horaInt = parseInt(targetFranja.replace(':', ''), 10);
        const desde   = parseInt((diaDisp.desde ?? '00:00').replace(':', ''), 10);
        const hasta   = parseInt((diaDisp.hasta ?? '23:59').replace(':', ''), 10);
        if (horaInt < desde || horaInt >= hasta) {
          this.snackBar.open(
            `El docente solo está disponible de ${diaDisp.desde} a ${diaDisp.hasta} el ${targetDia}.`,
            'Cerrar',
            { duration: 4000, panelClass: ['snack-error'] }
          );
          return;
        }
      }
    }

    // Detectar colisión de espacio: ¿hay otra sesión no-virtual del mismo tipo de alternancia en la celda destino?
    const spaceId = this.activeSpace()?.id;
    const conflict = this.state.sesiones().find(s =>
      s.id !== sesion.id &&
      s.espacioId === spaceId &&
      s.dia === targetDia &&
      s.horaInicio === targetFranja &&
      !s.virtual &&
      (s.alternancia === sesion.alternancia || s.alternancia === 'SinAlternancia' || sesion.alternancia === 'SinAlternancia')
    );
    if (conflict) {
      this.snackBar.open(
        'Conflicto: el espacio ya está ocupado en esa franja para el mismo tipo de alternancia.',
        'Cerrar',
        { duration: 4000, panelClass: ['snack-error'] }
      );
      return;
    }

    // Aplicar movimiento
    const updated: Sesion = { ...sesion, dia: targetDia, horaInicio: targetFranja };
    this.state.updateSesion(updated);
    this.snackBar.open('Sesión movida correctamente.', '', { duration: 2000 });
  }

  // ── Generación de horario ────────────────────────────────────────────────────

  generarHorario() {
    if (this.state.asignaturas().length === 0 || this.state.espacios().length === 0) {
      this.snackBar.open('Carga asignaturas y espacios antes de generar el horario.', 'Cerrar', { duration: 4000 });
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
export class ProgressDialogComponent {
  phase = 1;
}
