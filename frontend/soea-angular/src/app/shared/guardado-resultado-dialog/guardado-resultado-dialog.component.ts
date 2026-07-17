import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';

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
  imports: [CommonModule, MatDialogModule],
  template: `
    <div class="pophd">Guardar en BD — resultado <i (click)="ref.close()">✕</i></div>
    <div class="popbd">
      <div *ngIf="data.nuevos.length" class="okb">
        <b>✓ {{ data.nuevos.length }} nuevo(s) guardado(s)</b>
        <div class="lst">{{ data.nuevos.join(' · ') }}</div>
      </div>
      <div *ngIf="data.actualizados.length" class="okb" style="background:var(--color-accent-100);border-color:var(--color-accent);color:var(--color-accent-800)">
        <b>↻ {{ data.actualizados.length }} actualizado(s)</b>
        <div class="lst">{{ data.actualizados.join(' · ') }}</div>
      </div>
      <div *ngIf="data.errores.length" class="errb">
        <b>✕ {{ data.errores.length }} error(es)</b>
        <div class="lst">{{ data.errores.join(' · ') }}</div>
      </div>
      <p *ngIf="!data.nuevos.length && !data.actualizados.length && !data.errores.length" class="text-muted" style="margin:0;font-size:13px">
        No había cambios que guardar.
      </p>
      <div class="popfoot">
        <button class="btn btn-primary" (click)="ref.close()">Aceptar</button>
      </div>
    </div>
  `,
  styles: [`.lst { font-size: 11.5px; opacity: .85; margin-top: 4px; }`]
})
export class GuardadoResultadoDialogComponent {
  data = inject(MAT_DIALOG_DATA) as GuardadoResultado;
  ref = inject(MatDialogRef<GuardadoResultadoDialogComponent>);
}
