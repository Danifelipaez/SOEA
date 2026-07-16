import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { ImportExcelStatsDto } from '../../core/persistencia.service';

/**
 * Resultado de "Importar Excel" — extraído del wireframe de diseño (Fase 3 · 3e):
 * contadores creados/actualizados por entidad (ImportExcelStatsDto) + advertencias.
 * Antes solo se mostraba como texto de snackbar.
 */
@Component({
  selector: 'app-import-resultado-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Importar Excel — resultado</h2>
    <mat-dialog-content>
      <p class="ok">✓ Importación completa</p>

      <div class="grid">
        <span>Facultades</span><span><b>{{ data.facultadesCreadas }}</b> creadas</span>
        <span>Programas</span><span><b>{{ data.programasCreados }}</b> creados</span>
        <span>Asignaturas</span><span><b>{{ data.asignaturasCreadas }}</b> creadas · {{ data.asignaturasActualizadas }} act.</span>
        <span>Docentes</span><span><b>{{ data.docentesCreados }}</b> creados · {{ data.docentesActualizados }} act.</span>
        <span>Espacios</span><span><b>{{ data.espaciosCreados }}</b> creados · {{ data.espaciosActualizados }} act.</span>
        <span>Grupos</span><span><b>{{ data.gruposCreados }}</b> creados</span>
        <span>Sesiones persistidas</span><span><b>{{ data.sesionesPersistidas }}</b></span>
        <span *ngIf="data.asignaturasSinDocente > 0" class="warn-label">Sin docente</span>
        <span *ngIf="data.asignaturasSinDocente > 0" class="warn-label"><b>{{ data.asignaturasSinDocente }}</b></span>
      </div>

      <div *ngIf="data.advertencias.length" class="warn-box">
        <p class="warn-title">⚠ {{ data.advertencias.length }} advertencia(s)</p>
        <ul><li *ngFor="let w of data.advertencias">{{ w }}</li></ul>
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-flat-button mat-dialog-close color="primary">Aceptar</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .ok { color: #2e7d32; font-weight: 500; margin: 0 0 14px; }
    .grid { display: grid; grid-template-columns: 1fr auto; gap: 4px 16px; font-size: 13.5px; }
    .warn-label { color: #b0483e; }
    .warn-box { margin-top: 14px; border: 1px solid #ffe0b2; background: #fff8e1; border-radius: 6px; padding: 8px 12px; }
    .warn-title { margin: 0 0 4px; color: #e65100; font-weight: 500; font-size: 13px; }
    .warn-box ul { margin: 0 0 0 18px; padding: 0; font-size: 12.5px; color: #8a642e; }
  `]
})
export class ImportResultadoDialogComponent {
  data = inject(MAT_DIALOG_DATA) as ImportExcelStatsDto;
}
