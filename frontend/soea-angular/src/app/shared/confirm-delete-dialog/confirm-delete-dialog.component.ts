import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';

export interface ConfirmDeleteDialogData {
  title?: string;
  message?: string;
  confirmText?: string;
  cancelText?: string;
}

@Component({
  selector: 'app-confirm-delete-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.title || 'Confirmar eliminacion' }}</h2>
    <mat-dialog-content>
      <p>{{ data.message || 'Esta accion eliminara el registro.' }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="false">{{ data.cancelText || 'Cancelar' }}</button>
      <button mat-flat-button color="warn" [mat-dialog-close]="true">
        {{ data.confirmText || 'Eliminar' }}
      </button>
    </mat-dialog-actions>
  `,
})
export class ConfirmDeleteDialogComponent {
  data = inject(MAT_DIALOG_DATA) as ConfirmDeleteDialogData;
}
