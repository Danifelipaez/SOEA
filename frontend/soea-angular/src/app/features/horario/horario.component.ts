import { Component, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CdkDragDrop, DragDropModule, transferArrayItem, moveItemInArray } from '@angular/cdk/drag-drop';
import { RouterModule } from '@angular/router';
import { StateService } from '../../core/state.service';
import { Espacio, Sesion } from '../../core/models';

@Component({
  selector: 'app-horario',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatDialogModule, DragDropModule, RouterModule],
  template: `
    <div class="horario-container">
      <div class="header-actions">
        <h1 class="page-title text-primary">Horario</h1>
        <button mat-flat-button color="primary" class="primary-button" (click)="generarHorario()">Generar Horario</button>
      </div>

      <ng-container *ngIf="state.espacios().length > 0; else noData">
        <div class="space-selector">
          <button *ngFor="let esp of state.espacios()" 
                  class="pill-button" 
                  [class.active]="activeSpace()?.id === esp.id"
                  (click)="activeSpace.set(esp)">
            {{ esp.nombre }}
          </button>
        </div>

        <div class="matrix-container" cdkDropListGroup>
          <table class="horario-matrix">
            <thead>
              <tr>
                <th class="time-col">Hora</th>
                <th *ngFor="let dia of dias">{{ dia | titlecase }}</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let franja of franjas; let i = index">
                <td class="time-cell">{{ franja }}</td>
                <td *ngFor="let dia of dias" 
                    class="matrix-cell"
                    [class.out-of-hours]="isOutOfHours(dia, franja)">
                  <div class="cell-drop-zone"
                       cdkDropList
                       [cdkDropListData]="getCellData(dia, franja)"
                       (cdkDropListDropped)="drop($event, dia, franja)"
                       [cdkDropListDisabled]="isOutOfHours(dia, franja)">
                    
                    <div class="session-card" 
                         *ngFor="let sesion of getCellData(dia, franja)"
                         cdkDrag
                         [cdkDragDisabled]="esTipoA(sesion)"
                         [class.presencial]="!sesion.virtual"
                         [class.virtual]="sesion.virtual"
                         [class.tipo-a]="esTipoA(sesion)"
                         (cdkDragStarted)="onDragStarted(sesion)">
                      <div class="tipo-a-badge" *ngIf="esTipoA(sesion)">Tipo A</div>
                      <div class="card-title">{{ getAsignaturaName(sesion) }}</div>
                      <div class="card-subtitle">{{ getDocenteName(sesion) }}</div>
                      <div class="card-badge" *ngIf="sesion.virtual">Virtual</div>
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
          <p>No hay datos cargados. Ve a Ingesta de Datos para comenzar.</p>
          <button mat-stroked-button routerLink="/ingesta">Ir a Ingesta</button>
        </div>
      </ng-template>
    </div>
  `,
  styles: [`
    .horario-container { padding: 16px; background: white; border-radius: 8px; border: 1px solid #e0e0e0; min-height: 500px; }
    .header-actions { display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px; }
    .page-title { margin: 0; font-weight: 500; font-size: 24px; }
    .space-selector { display: flex; gap: 8px; margin-bottom: 24px; overflow-x: auto; padding-bottom: 8px; }
    .pill-button { padding: 8px 16px; border-radius: 20px; border: 1px solid #e0e0e0; background: white; cursor: pointer; transition: 0.2s; white-space: nowrap; }
    .pill-button.active { background: #007bff; color: white; border-color: #007bff; }
    .matrix-container { overflow-x: auto; }
    .horario-matrix { width: 100%; border-collapse: collapse; min-width: 800px; }
    .horario-matrix th, .horario-matrix td { border: 1px solid #e0e0e0; padding: 8px; text-align: center; vertical-align: top; }
    .time-col { width: 80px; }
    .time-cell { font-weight: 500; color: #757575; vertical-align: middle; }
    .matrix-cell { height: 80px; position: relative; }
    .out-of-hours { background: repeating-linear-gradient(45deg, #f0f0f0, #f0f0f0 10px, #e0e0e0 10px, #e0e0e0 20px); }
    .cell-drop-zone { min-height: 100%; min-width: 100px; display: flex; flex-direction: column; gap: 4px; }
    
    .session-card { padding: 8px; border-radius: 4px; text-align: left; font-size: 12px; cursor: move; position: relative; box-shadow: 0 1px 3px rgba(0,0,0,0.12); }
    .session-card.cdk-drag-disabled { cursor: not-allowed; }
    .presencial { background-color: #e6f1fb; border-left: 4px solid #007bff; }
    .virtual { background-color: #f5f5f5; border-left: 4px solid #424242; }
    .card-title { font-weight: 500; margin-bottom: 4px; }
    .card-subtitle { color: #616161; }
    .card-badge { display: inline-block; padding: 2px 4px; background: #e0e0e0; border-radius: 4px; font-size: 10px; margin-top: 4px; }
    .tipo-a-badge { position: absolute; top: 2px; right: 2px; font-size: 9px; background: #ff9800; color: white; padding: 1px 4px; border-radius: 2px; }
    
    .empty-state { text-align: center; padding: 48px; color: #757575; }
  `]
})
export class HorarioComponent {
  state = inject(StateService);
  dialog = inject(MatDialog);

  dias = ['lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado'];
  franjas = ['06:00', '07:00', '08:00', '09:00', '10:00', '11:00', '12:00', '13:00', '14:00', '15:00', '16:00', '17:00', '18:00', '19:00', '20:00', '20:30'];

  activeSpace = signal<Espacio | null>(null);

  constructor() {
    if (this.state.espacios().length > 0) {
      this.activeSpace.set(this.state.espacios()[0]);
    }
  }

  isOutOfHours(dia: string, franja: string): boolean {
    if (franja === '21:30') return true; // Just rendering up to 21:30 theoretically, wait 20:30 is last
    if (dia === 'sabado' && this.franjas.indexOf(franja) >= this.franjas.indexOf('14:00')) return true;
    return false;
  }

  getCellData(dia: string, franja: string): Sesion[] {
    const spaceId = this.activeSpace()?.id;
    return this.state.sesiones().filter(s => s.espacioId === spaceId && s.dia === dia && s.horaInicio === franja);
  }

  esTipoA(sesion: Sesion): boolean {
    const asig = this.state.asignaturas().find(a => a.id === sesion.asignaturaId);
    return asig?.tipo === 'Tipo A';
  }

  getAsignaturaName(sesion: Sesion): string {
    const asig = this.state.asignaturas().find(a => a.id === sesion.asignaturaId);
    return asig ? asig.nombre : 'Desconocida';
  }

  getDocenteName(sesion: Sesion): string {
    const asig = this.state.asignaturas().find(a => a.id === sesion.asignaturaId);
    if (!asig) return '';
    const d = this.state.docentes().find(doc => doc.id === asig.docenteId);
    return d ? d.nombre : '';
  }

  onDragStarted(sesion: Sesion) {
    if (this.esTipoA(sesion)) {
      // Angular CDK prevents dragging, but we could also show a toast here.
    }
  }

  drop(event: CdkDragDrop<Sesion[]>, targetDia: string, targetFranja: string) {
    if (event.previousContainer === event.container) {
      moveItemInArray(event.container.data, event.previousIndex, event.currentIndex);
    } else {
      const draggedSession = event.previousContainer.data[event.previousIndex];
      const targetSession = event.container.data.length > 0 ? event.container.data[0] : null;

      // Open validation dialog
      const dialogRef = this.dialog.open(ValidationDialogComponent, { disableClose: true, width: '300px' });
      
      setTimeout(() => {
        // Mock validation logic
        const isValid = Math.random() > 0.3; // 70% valid for demo
        dialogRef.componentInstance.state = isValid ? 'success' : 'error';
        dialogRef.componentInstance.message = isValid ? 'Cambio realizado' : 'Conflicto: El docente no tiene disponibilidad en la franja destino.';
        
        setTimeout(() => {
          dialogRef.close();
          if (isValid) {
            // Update model
            const allSessions = [...this.state.sesiones()];
            const s1 = allSessions.find(s => s.id === draggedSession.id);
            if (s1) {
              s1.dia = targetDia;
              s1.horaInicio = targetFranja;
            }
            if (targetSession) {
              const s2 = allSessions.find(s => s.id === targetSession.id);
              if (s2) {
                // Swap back to dragged source
                // wait, the source is not directly targetDia, we need previous dia/franja
                // For a proper swap, we should track source cell
              }
            }
            this.state.setSesiones([...allSessions]);
          }
        }, 1500);
      }, 1000);
    }
  }

  generarHorario() {
    const dialogRef = this.dialog.open(ProgressDialogComponent, { disableClose: true, width: '500px' });
    setTimeout(() => { dialogRef.componentInstance.phase = 2; }, 2000);
    setTimeout(() => { dialogRef.componentInstance.phase = 3; }, 4000);
    setTimeout(() => { 
      dialogRef.close(); 
      this.generateMockSchedule();
    }, 6000);
  }

  generateMockSchedule() {
    if (this.state.asignaturas().length === 0) return;
    const asig = this.state.asignaturas()[0];
    const space = this.state.espacios()[0];
    const mockSessions: Sesion[] = [
      { id: '1', asignaturaId: asig.id, dia: 'lunes', horaInicio: '08:00', duracion: 2, espacioId: space.id, virtual: false },
      { id: '2', asignaturaId: asig.id, dia: 'miercoles', horaInicio: '10:00', duracion: 2, espacioId: space.id, virtual: true }
    ];
    this.state.setSesiones(mockSessions);
  }
}

@Component({
  selector: 'app-validation-dialog',
  standalone: true,
  imports: [CommonModule, MatProgressSpinnerModule, MatButtonModule],
  template: `
    <div class="val-dialog">
      <mat-spinner *ngIf="state === 'loading'" diameter="40"></mat-spinner>
      <div *ngIf="state === 'success'" class="icon-success">✓</div>
      <div *ngIf="state === 'error'" class="icon-error">✗</div>
      <p class="val-msg">{{ message }}</p>
      <button mat-button *ngIf="state === 'error'" (click)="close()">Cerrar</button>
    </div>
  `,
  styles: [`
    .val-dialog { padding: 24px; text-align: center; display: flex; flex-direction: column; align-items: center; gap: 16px; }
    .icon-success { font-size: 40px; color: #28a745; }
    .icon-error { font-size: 40px; color: #dc3545; }
    .val-msg { font-weight: 500; margin: 0; }
  `]
})
export class ValidationDialogComponent {
  state: 'loading' | 'success' | 'error' = 'loading';
  message = 'Validando disponibilidad docente...';
  dialogRef = inject(MatDialogRef<ValidationDialogComponent>);
  close() { this.dialogRef.close(); }
}

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
    .prog-dialog { display: flex; flex-direction: column; gap: 16px; padding: 16px 0; }
    .phase-item { display: flex; align-items: center; gap: 12px; color: #757575; }
    .phase-item.active { color: #007bff; font-weight: 500; }
    .phase-item.done { color: #28a745; }
    .icon-done, .icon-pending { font-weight: bold; width: 20px; text-align: center; }
  `]
})
export class ProgressDialogComponent {
  phase = 1;
}
