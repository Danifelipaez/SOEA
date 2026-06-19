import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';

export interface GuardadoResultado {
  entidad: string;
  nuevos: string[];
  actualizados: string[];
  errores: string[];
}

/**
 * Resultado de "Guardar en BD" de las tabs de Ingesta: lista nuevos, actualizados
 * y errores. Compartido por Asignaturas, Docentes y Espacios.
 */
@Component({
  selector: 'app-guardado-resultado-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule, MatIconModule],
  template: `
    <h2 mat-dialog-title>Resultado: Guardar en BD</h2>
    <mat-dialog-content>
      <div *ngIf="data.nuevos.length > 0" class="section">
        <p class="section-title ok"><mat-icon>check_circle</mat-icon> Nuevos guardados ({{data.nuevos.length}})</p>
        <ul><li *ngFor="let n of data.nuevos">{{n}}</li></ul>
      </div>
      <div *ngIf="data.actualizados.length > 0" class="section">
        <p class="section-title info"><mat-icon>sync</mat-icon> Ya estaban en BD — actualizados ({{data.actualizados.length}})</p>
        <ul><li *ngFor="let n of data.actualizados">{{n}}</li></ul>
      </div>
      <div *ngIf="data.errores.length > 0" class="section">
        <p class="section-title err"><mat-icon>error</mat-icon> Errores ({{data.errores.length}})</p>
        <ul><li *ngFor="let n of data.errores">{{n}}</li></ul>
      </div>
      <p *ngIf="!data.nuevos.length && !data.actualizados.length && !data.errores.length" class="empty">
        No había cambios que guardar.
      </p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-flat-button mat-dialog-close color="primary">Aceptar</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .section { margin-bottom: 14px; }
    .section-title { display: flex; align-items: center; gap: 6px; font-weight: 500; margin: 0 0 4px; }
    .ok   { color: #2e7d32; } .info { color: #1565c0; } .err { color: #c62828; }
    ul { margin: 0 0 0 28px; padding: 0; font-size: 14px; }
    .empty { color: #757575; font-style: italic; }
  `]
})
export class GuardadoResultadoDialogComponent {
  data = inject(MAT_DIALOG_DATA) as GuardadoResultado;
}
