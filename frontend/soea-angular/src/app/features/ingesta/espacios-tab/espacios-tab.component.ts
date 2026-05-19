import { Component, inject, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { StateService } from '../../../core/state.service';
import { PersistenciaService } from '../../../core/persistencia.service';
import { Espacio } from '../../../core/models';
import { MatSnackBar } from '@angular/material/snack-bar';
import { forkJoin, of } from 'rxjs';
import { map, catchError } from 'rxjs/operators';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-espacios-tab',
  standalone: true,
  imports: [CommonModule, MatTableModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule],
  template: `
    <div class="tab-content">
      <div class="actions-row">
        <mat-form-field appearance="outline" class="filter-input">
          <mat-label>Buscar...</mat-label>
          <input matInput (keyup)="applyFilter($event)" placeholder="Ej. Laboratorio A" #input>
        </mat-form-field>
        <div class="btn-group">
          <button mat-stroked-button (click)="cargarDesdeBD()" [disabled]="saving()">Cargar desde BD</button>
          <button mat-stroked-button color="accent" (click)="guardarEnBD()" [disabled]="saving()">
            {{ saving() ? 'Guardando...' : 'Guardar en BD' }}
          </button>
          <button mat-flat-button color="primary" class="primary-button" (click)="openDialog()">Agregar espacio</button>
        </div>
      </div>
      <table mat-table [dataSource]="filteredEspacios()" class="mat-elevation-z0 border-table">
        <ng-container matColumnDef="nombre">
          <th mat-header-cell *matHeaderCellDef> Nombre </th>
          <td mat-cell *matCellDef="let element"> {{element.nombre}} </td>
        </ng-container>
        <ng-container matColumnDef="capacidad">
          <th mat-header-cell *matHeaderCellDef> Capacidad (aforo) </th>
          <td mat-cell *matCellDef="let element"> {{element.capacidad}} </td>
        </ng-container>
        <ng-container matColumnDef="tipo">
          <th mat-header-cell *matHeaderCellDef> Tipo </th>
          <td mat-cell *matCellDef="let element"> {{element.tipo}} </td>
        </ng-container>
        <ng-container matColumnDef="acciones">
          <th mat-header-cell *matHeaderCellDef> Acciones </th>
          <td mat-cell *matCellDef="let element">
            <button mat-button class="text-primary" (click)="openDialog(element)">Editar</button>
            <button mat-button class="text-error" (click)="delete(element)">Eliminar</button>
          </td>
        </ng-container>
        <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
        <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
        <tr class="mat-row" *matNoDataRow>
          <td class="mat-cell" colspan="4">No hay datos que coincidan con el filtro.</td>
        </tr>
      </table>
    </div>
  `,
  styles: [`
    .tab-content { padding: 24px 0; }
    .actions-row { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
    .filter-input { width: 300px; }
    .btn-group { display: flex; gap: 8px; align-items: center; }
    .border-table { border: 1px solid #e0e0e0; border-bottom: 0; }
  `]
})
export class EspaciosTabComponent {
  state = inject(StateService);
  dialog = inject(MatDialog);
  snackBar = inject(MatSnackBar);
  persistencia = inject(PersistenciaService);

  displayedColumns = ['nombre', 'capacidad', 'tipo', 'acciones'];
  filterStr = signal('');
  saving = signal(false);
  private bdIds = new Set<string>();

  filteredEspacios = computed(() => {
    const f = this.filterStr().toLowerCase();
    const all = this.state.espacios();
    if (!f) return all;
    return all.filter(e => e.nombre.toLowerCase().includes(f) || e.tipo.toLowerCase().includes(f));
  });

  applyFilter(event: Event) {
    const filterValue = (event.target as HTMLInputElement).value;
    this.filterStr.set(filterValue);
  }

  openDialog(espacio?: Espacio) {
    const dialogRef = this.dialog.open(EspacioDialogComponent, { width: '400px', data: espacio });
    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        if (espacio) {
          this.state.updateEspacio({ ...espacio, ...result });
          this.snackBar.open('Espacio actualizado', 'Cerrar', { duration: 3000 });
        } else {
          this.state.addEspacio({ id: crypto.randomUUID(), ...result });
          this.snackBar.open('Espacio agregado', 'Cerrar', { duration: 3000 });
        }
      }
    });
  }

  delete(espacio: Espacio) {
    if (this.bdIds.has(espacio.id)) {
      this.persistencia.eliminarEspacioBD(espacio.id).subscribe({
        next: () => {
          this.bdIds.delete(espacio.id);
          this.state.deleteEspacio(espacio.id);
          this.snackBar.open('Espacio eliminado de la BD.', 'Cerrar', { duration: 3000 });
        },
        error: () => this.snackBar.open('Error al eliminar de la BD.', 'Cerrar', { duration: 4000 })
      });
    } else {
      this.state.deleteEspacio(espacio.id);
      this.snackBar.open('Espacio eliminado localmente.', 'Cerrar', { duration: 3000 });
    }
  }

  guardarEnBD() {
    const espacios = this.state.espacios();
    if (espacios.length === 0) {
      this.snackBar.open('No hay espacios para guardar.', 'Cerrar', { duration: 3000 });
      return;
    }

    const paraActualizar = espacios.filter(e => this.bdIds.has(e.id));
    const paraCrear      = espacios.filter(e => !this.bdIds.has(e.id));

    this.saving.set(true);

    const updateCalls$ = paraActualizar.map(e =>
      this.persistencia.actualizarEspacio(e).pipe(
        map(() => ({ ok: true, nombre: e.nombre, tipo: 'actualizado' as const })),
        catchError(() => of({ ok: false, nombre: e.nombre, tipo: 'actualizado' as const }))
      )
    );
    const createCalls$ = paraCrear.map(e =>
      this.persistencia.guardarEspacio(e).pipe(
        map(() => { this.bdIds.add(e.id); return { ok: true, nombre: e.nombre, tipo: 'nuevo' as const }; }),
        catchError(() => of({ ok: false, nombre: e.nombre, tipo: 'nuevo' as const }))
      )
    );

    const all$ = [...updateCalls$, ...createCalls$];
    if (all$.length === 0) { this.saving.set(false); return; }

    forkJoin(all$).subscribe(results => {
      this.saving.set(false);
      const nuevos       = results.filter(r => r.ok && r.tipo === 'nuevo').map(r => r.nombre);
      const actualizados = results.filter(r => r.ok && r.tipo === 'actualizado').map(r => r.nombre);
      const errores      = results.filter(r => !r.ok).map(r => r.nombre);
      this.dialog.open(GuardadoResultadoDialogComponent, {
        width: '420px',
        data: { entidad: 'espacios', nuevos, actualizados, errores }
      });
    });
  }

  cargarDesdeBD() {
    this.saving.set(true);
    this.persistencia.cargarEspacios().subscribe({
      next: (espacios) => {
        this.saving.set(false);
        this.bdIds = new Set(espacios.map(e => e.id));
        espacios.forEach(e => {
          const existing = this.state.espacios().find(x => x.id === e.id);
          if (existing) this.state.updateEspacio(e);
          else this.state.addEspacio(e);
        });
        this.snackBar.open(`${espacios.length} espacio(s) cargados desde la BD.`, 'Cerrar', { duration: 4000 });
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Error al cargar desde la BD.', 'Cerrar', { duration: 4000 });
      }
    });
  }
}

@Component({
  selector: 'app-espacio-dialog',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data ? 'Editar Espacio' : 'Agregar Espacio' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="dialog-form">
        <mat-form-field appearance="outline">
          <mat-label>Nombre</mat-label>
          <input matInput formControlName="nombre" required>
          <mat-error *ngIf="form.get('nombre')?.hasError('required')">Requerido</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Capacidad</mat-label>
          <input matInput type="number" formControlName="capacidad" required>
          <mat-error *ngIf="form.get('capacidad')?.hasError('required')">Requerido</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Tipo</mat-label>
          <mat-select formControlName="tipo" required>
            <mat-option value="Laboratorio">Laboratorio</mat-option>
            <mat-option value="Salón">Salón</mat-option>
            <mat-option value="Auditorio">Auditorio</mat-option>
          </mat-select>
          <mat-error *ngIf="form.get('tipo')?.hasError('required')">Requerido</mat-error>
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancelar</button>
      <button mat-flat-button color="primary" class="primary-button" [disabled]="form.invalid" (click)="save()">Guardar</button>
    </mat-dialog-actions>
  `,
  styles: [` .dialog-form { display: flex; flex-direction: column; gap: 16px; margin-top: 16px; } `]
})
export class EspacioDialogComponent {
  fb = inject(FormBuilder);
  dialogRef = inject(MatDialogRef<EspacioDialogComponent>);
  data = inject(MAT_DIALOG_DATA);

  form = this.fb.group({
    nombre: [this.data?.nombre || '', Validators.required],
    capacidad: [this.data?.capacidad || '', [Validators.required, Validators.min(1)]],
    tipo: [this.data?.tipo || '', Validators.required]
  });

  save() {
    if (this.form.valid) {
      this.dialogRef.close(this.form.value);
    }
  }
}

// ─── Dialog de resultado del guardado ────────────────────────────────────────

interface GuardadoResultado {
  entidad: string;
  nuevos: string[];
  actualizados: string[];
  errores: string[];
}

@Component({
  selector: 'app-guardado-resultado-espacios-dialog',
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
