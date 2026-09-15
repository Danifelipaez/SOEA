import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { ImportExcelStatsDto } from '../../core/persistencia.service';

/**
 * Resultado de "Importar Excel" (HF-5 · REQUISITOS §1.5): contadores creados/actualizados
 * por entidad (ImportExcelStatsDto) + advertencias colapsables.
 */
@Component({
  selector: 'app-import-resultado-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule],
  template: `
    <div class="pophd">Importar Excel — resultado <button type="button" class="pop-close" (click)="ref.close()" aria-label="Cerrar">✕</button></div>
    <div class="popbd">
      <div class="okb">✓ Importación terminada</div>

      <div class="grid">
        <span>Facultades</span><span><b>{{ data.facultadesCreadas }}</b> creadas</span>
        <span>Programas</span><span><b>{{ data.programasCreados }}</b> creados</span>
        <span>Asignaturas</span><span><b>{{ data.asignaturasCreadas }}</b> creadas · {{ data.asignaturasActualizadas }} actualizadas</span>
        <span>Docentes</span><span><b>{{ data.docentesCreados }}</b> creados · {{ data.docentesActualizados }} actualizados</span>
        <span>Espacios</span><span><b>{{ data.espaciosCreados }}</b> creados · {{ data.espaciosActualizados }} actualizados</span>
        <span>Grupos</span><span><b>{{ data.gruposCreados }}</b> creados</span>
        <ng-container *ngIf="data.gruposSinDocente > 0">
          <span style="color:var(--err-bd)">Grupos sin docente</span>
          <span style="color:var(--err-bd)"><b>{{ data.gruposSinDocente }}</b></span>
        </ng-container>
      </div>

      <div *ngIf="data.advertencias.length" class="soft">
        ⚠ {{ data.advertencias.length }} advertencia(s)<span *ngIf="!verAdv()">: {{ data.advertencias[0] }}…</span>
        <ul *ngIf="verAdv()"><li *ngFor="let w of data.advertencias">{{ w }}</li></ul>
      </div>

      <div class="popfoot">
        <button *ngIf="data.advertencias.length" class="btn btn-secondary" (click)="verAdv.set(!verAdv())">
          {{ verAdv() ? 'Ocultar' : 'Ver' }} advertencias
        </button>
        <button class="btn btn-primary" (click)="ref.close()">Cerrar</button>
      </div>
    </div>
  `,
  styles: [`
    .grid { display: grid; grid-template-columns: 1fr auto; gap: 6px 12px; font-size: 13px; }
    .soft ul { margin: 6px 0 0; padding-left: 18px; }
    .soft li { margin-bottom: 2px; }
  `]
})
export class ImportResultadoDialogComponent {
  data = inject(MAT_DIALOG_DATA) as ImportExcelStatsDto;
  ref = inject(MatDialogRef<ImportResultadoDialogComponent>);
  verAdv = signal(false);
}
