import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';

export interface ConfirmDeleteDialogData {
  title?: string;
  message?: string;
  confirmText?: string;
  cancelText?: string;
}

/** Confirmación de borrado con el shell de popup del rediseño (Alta Fidelidad). */
@Component({
  selector: 'app-confirm-delete-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule],
  template: `
    <div class="pophd">{{ data.title || 'Confirmar eliminación' }} <i (click)="ref.close(false)">✕</i></div>
    <div class="popbd">
      <div class="errb">{{ data.message || 'Esta acción eliminará el registro.' }}</div>
      <div class="popfoot">
        <button class="btn btn-secondary" (click)="ref.close(false)">{{ data.cancelText || 'Cancelar' }}</button>
        <button class="btn btn-primary" style="background:var(--err-bd);border-color:var(--err-bd)"
                (click)="ref.close(true)">{{ data.confirmText || 'Eliminar' }}</button>
      </div>
    </div>
  `,
})
export class ConfirmDeleteDialogComponent {
  data = inject(MAT_DIALOG_DATA) as ConfirmDeleteDialogData;
  ref = inject(MatDialogRef<ConfirmDeleteDialogComponent>);
}
