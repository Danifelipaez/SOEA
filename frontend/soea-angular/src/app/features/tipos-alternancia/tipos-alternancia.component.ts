import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialogModule, MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { RouterModule } from '@angular/router';
import { PersistenciaService } from '../../core/persistencia.service';
import { TipoAlternanciaConfig } from '../../core/models';

type Patron = TipoAlternanciaConfig['patronBase'];

const PATRON_LABEL: Record<Patron, string> = {
  PresencialEnSemanaA: 'Presencial en semanas A (pares) · virtual en B',
  PresencialEnSemanaB: 'Presencial en semanas B (impares) · virtual en A',
  SinAlternancia:      'Sin alternancia (presencial siempre)'
};

@Component({
  selector: 'app-tipos-alternancia',
  standalone: true,
  imports: [CommonModule, MatTableModule, MatButtonModule, MatIconModule, MatDialogModule,
            MatSnackBarModule, RouterModule],
  template: `
    <div class="page">
      <div class="head">
        <div>
          <h1 class="title">Tipos de alternancia</h1>
          <p class="subtitle">
            Define y edita las lógicas de alternancia (presencial/virtual por semana). Puedes ajustar
            Tipo A y Tipo B o crear tipos nuevos sobre la base de 2 semanas (A/B) para probar
            distintas estrategias de uso del espacio.
          </p>
        </div>
        <div class="head-actions">
          <button mat-stroked-button routerLink="/dashboard-developer">
            <mat-icon>arrow_back</mat-icon> Developer
          </button>
          <button mat-flat-button color="primary" class="primary-button" (click)="abrir()">
            <mat-icon>add</mat-icon> Agregar tipo
          </button>
        </div>
      </div>

      @if (cargando()) {
        <p class="muted">Cargando…</p>
      } @else {
        <table mat-table [dataSource]="tipos()" class="mat-elevation-z0 border-table">
          <ng-container matColumnDef="nombre">
            <th mat-header-cell *matHeaderCellDef> Tipo </th>
            <td mat-cell *matCellDef="let t">
              <span class="dot" [style.background]="t.color"></span>
              <strong>{{ t.nombre }}</strong>
              @if (t.esSistema) { <span class="tag-sys">sistema</span> }
              @if (!t.activo)  { <span class="tag-off">inactivo</span> }
            </td>
          </ng-container>
          <ng-container matColumnDef="patron">
            <th mat-header-cell *matHeaderCellDef> Patrón base </th>
            <td mat-cell *matCellDef="let t"> {{ patronLabel(t.patronBase) }} </td>
          </ng-container>
          <ng-container matColumnDef="semanas">
            <th mat-header-cell *matHeaderCellDef> Sem. presenciales </th>
            <td mat-cell *matCellDef="let t"> {{ t.semanasPresenciales }} <span class="muted">(informativo)</span> </td>
          </ng-container>
          <ng-container matColumnDef="acciones">
            <th mat-header-cell *matHeaderCellDef> Acciones </th>
            <td mat-cell *matCellDef="let t">
              <button mat-button class="text-primary" (click)="abrir(t)">Editar</button>
              <button mat-button class="text-error" [disabled]="t.esSistema" (click)="eliminar(t)">Eliminar</button>
            </td>
          </ng-container>
          <tr mat-header-row *matHeaderRowDef="columnas"></tr>
          <tr mat-row *matRowDef="let row; columns: columnas;"></tr>
        </table>
        <p class="note">
          El motor opera sobre la abstracción de 2 semanas (A/B); el número de semanas presenciales
          es informativo. Los tipos de sistema conservan su patrón base.
        </p>
      }
    </div>
  `,
  styles: [`
    .page { padding: 16px; background: white; border-radius: 8px; border: 1px solid #e0e0e0; }
    .head { display: flex; justify-content: space-between; align-items: flex-start; gap: 16px; margin-bottom: 20px; }
    .title { margin: 0; font-size: 22px; font-weight: 500; }
    .subtitle { margin: 6px 0 0; color: #616161; font-size: 13px; max-width: 640px; line-height: 1.5; }
    .head-actions { display: flex; gap: 8px; flex-shrink: 0; }
    .border-table { width: 100%; border: 1px solid #e0e0e0; border-bottom: 0; }
    .dot { display: inline-block; width: 12px; height: 12px; border-radius: 50%; margin-right: 8px; vertical-align: middle; }
    .tag-sys { margin-left: 8px; background: #ede7f6; color: #512da8; border-radius: 10px; padding: 1px 8px; font-size: 11px; }
    .tag-off { margin-left: 8px; background: #ffebee; color: #c62828; border-radius: 10px; padding: 1px 8px; font-size: 11px; }
    .muted { color: #9e9e9e; font-size: 12px; }
    .note  { color: #757575; font-size: 12px; margin-top: 12px; font-style: italic; }
  `]
})
export class TiposAlternanciaComponent implements OnInit {
  private persistencia = inject(PersistenciaService);
  private dialog = inject(MatDialog);
  private snack = inject(MatSnackBar);

  columnas = ['nombre', 'patron', 'semanas', 'acciones'];
  tipos = signal<TipoAlternanciaConfig[]>([]);
  cargando = signal(true);

  ngOnInit() { this.cargar(); }

  cargar() {
    this.cargando.set(true);
    this.persistencia.cargarTiposAlternancia().subscribe({
      next: (t) => { this.tipos.set(t); this.cargando.set(false); },
      error: () => {
        this.cargando.set(false);
        this.snack.open('No se pudo cargar el catálogo. ¿Backend activo?', 'Cerrar', { duration: 4000 });
      }
    });
  }

  patronLabel(p: Patron) { return PATRON_LABEL[p]; }

  abrir(tipo?: TipoAlternanciaConfig) {
    const ref = this.dialog.open(TipoAlternanciaDialogComponent, {
      width: '520px', maxWidth: '95vw', data: tipo ? { ...tipo } : null
    });
    ref.afterClosed().subscribe((dto?: Partial<TipoAlternanciaConfig>) => {
      if (!dto) return;
      const obs = tipo
        ? this.persistencia.actualizarTipoAlternancia({ ...tipo, ...dto } as TipoAlternanciaConfig)
        : this.persistencia.crearTipoAlternancia(dto);
      obs.subscribe({
        next: () => { this.snack.open(tipo ? 'Tipo actualizado.' : 'Tipo creado.', 'Cerrar', { duration: 3000 }); this.cargar(); },
        error: (e) => this.snack.open(`Error: ${e?.error ?? 'desconocido'}`, 'Cerrar', { duration: 5000, panelClass: ['snack-error'] })
      });
    });
  }

  eliminar(tipo: TipoAlternanciaConfig) {
    if (tipo.esSistema) return;
    this.persistencia.eliminarTipoAlternancia(tipo.id).subscribe({
      next: () => { this.snack.open('Tipo eliminado.', 'Cerrar', { duration: 3000 }); this.cargar(); },
      error: (e) => this.snack.open(`Error: ${e?.error ?? 'desconocido'}`, 'Cerrar', { duration: 5000, panelClass: ['snack-error'] })
    });
  }
}

// ─── Dialog crear/editar tipo ────────────────────────────────────────────────

@Component({
  selector: 'app-tipo-alternancia-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data ? 'Editar tipo' : 'Nuevo tipo de alternancia' }}</h2>
    <mat-dialog-content class="form">
      <label class="field">
        <span>Nombre</span>
        <input [(ngModel)]="nombre" placeholder="Ej. Química Orgánica" maxlength="100">
      </label>

      <label class="field">
        <span>Patrón base</span>
        <select [(ngModel)]="patronBase" [disabled]="esSistema">
          <option value="PresencialEnSemanaA">Presencial en semanas A (pares)</option>
          <option value="PresencialEnSemanaB">Presencial en semanas B (impares)</option>
          <option value="SinAlternancia">Sin alternancia (presencial siempre)</option>
        </select>
        @if (esSistema) { <small class="hint">Los tipos de sistema no cambian su patrón base.</small> }
      </label>

      <label class="field">
        <span>Semanas presenciales <small class="hint">(informativo)</small></span>
        <input type="number" [(ngModel)]="semanasPresenciales" min="0" max="52">
      </label>

      <label class="field">
        <span>Color</span>
        <input type="color" [(ngModel)]="color" class="color-input">
      </label>

      @if (data) {
        <label class="check">
          <input type="checkbox" [(ngModel)]="activo"> Activo
        </label>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancelar</button>
      <button mat-flat-button color="primary" class="primary-button"
              [disabled]="!nombre.trim()" (click)="guardar()">Guardar</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .form { display: flex; flex-direction: column; gap: 14px; min-width: 380px; padding-top: 8px; }
    .field { display: flex; flex-direction: column; gap: 4px; font-size: 13px; color: #424242; }
    .field input, .field select {
      padding: 8px 10px; border: 1px solid #bdbdbd; border-radius: 4px; font-size: 14px; outline: none;
    }
    .field input:focus, .field select:focus { border-color: #1976d2; }
    .color-input { width: 60px; height: 36px; padding: 2px; }
    .hint { color: #9e9e9e; font-weight: 400; }
    .check { display: flex; align-items: center; gap: 8px; font-size: 14px; }
  `]
})
export class TipoAlternanciaDialogComponent {
  private ref = inject(MatDialogRef<TipoAlternanciaDialogComponent>);
  data = inject(MAT_DIALOG_DATA) as TipoAlternanciaConfig | null;

  nombre = this.data?.nombre ?? '';
  patronBase: Patron = this.data?.patronBase ?? 'PresencialEnSemanaA';
  semanasPresenciales = this.data?.semanasPresenciales ?? 8;
  color = this.data?.color ?? '#3f51b5';
  activo = this.data?.activo ?? true;
  esSistema = this.data?.esSistema ?? false;

  guardar() {
    if (!this.nombre.trim()) return;
    this.ref.close({
      nombre: this.nombre.trim(),
      patronBase: this.patronBase,
      semanasPresenciales: Number(this.semanasPresenciales) || 0,
      color: this.color,
      activo: this.activo
    });
  }
}
