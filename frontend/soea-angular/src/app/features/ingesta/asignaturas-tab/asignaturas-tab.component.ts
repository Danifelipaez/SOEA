import { Component, inject, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators, FormGroup } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { StateService } from '../../../core/state.service';
import { Asignatura } from '../../../core/models';
import { MatSnackBar } from '@angular/material/snack-bar';

@Component({
  selector: 'app-asignaturas-tab',
  standalone: true,
  imports: [CommonModule, MatTableModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule],
  template: `
    <div class="tab-content">
      <div class="actions-row">
        <mat-form-field appearance="outline" class="filter-input">
          <mat-label>Buscar...</mat-label>
          <input matInput (keyup)="applyFilter($event)" placeholder="Ej. Química 101" #input>
        </mat-form-field>
        <button mat-flat-button color="primary" class="primary-button" (click)="openDialog()">Agregar asignatura</button>
      </div>
      <table mat-table [dataSource]="filteredAsignaturas()" class="mat-elevation-z0 border-table">
        <ng-container matColumnDef="codigo">
          <th mat-header-cell *matHeaderCellDef> Código </th>
          <td mat-cell *matCellDef="let element"> {{element.codigo}} </td>
        </ng-container>
        <ng-container matColumnDef="nombre">
          <th mat-header-cell *matHeaderCellDef> Nombre </th>
          <td mat-cell *matCellDef="let element"> {{element.nombre}} </td>
        </ng-container>
        <ng-container matColumnDef="tipo">
          <th mat-header-cell *matHeaderCellDef> Tipo </th>
          <td mat-cell *matCellDef="let element"> {{element.tipo}} </td>
        </ng-container>
        <ng-container matColumnDef="prioridad">
          <th mat-header-cell *matHeaderCellDef> Prioridad </th>
          <td mat-cell *matCellDef="let element"> {{element.prioridad}} </td>
        </ng-container>
        <ng-container matColumnDef="duracion">
          <th mat-header-cell *matHeaderCellDef> Duración </th>
          <td mat-cell *matCellDef="let element"> {{element.duracion}}h </td>
        </ng-container>
        <ng-container matColumnDef="docente">
          <th mat-header-cell *matHeaderCellDef> Docente </th>
          <td mat-cell *matCellDef="let element"> {{getDocenteName(element.docenteId)}} </td>
        </ng-container>
        <ng-container matColumnDef="espacio">
          <th mat-header-cell *matHeaderCellDef> Espacio </th>
          <td mat-cell *matCellDef="let element"> {{getEspacioName(element.espacioFijoId)}} </td>
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
          <td class="mat-cell" colspan="8">No hay datos que coincidan con el filtro.</td>
        </tr>
      </table>
    </div>
  `,
  styles: [`
    .tab-content { padding: 24px 0; }
    .actions-row { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
    .filter-input { width: 300px; }
    .border-table { border: 1px solid #e0e0e0; border-bottom: 0; }
  `]
})
export class AsignaturasTabComponent {
  state = inject(StateService);
  dialog = inject(MatDialog);
  snackBar = inject(MatSnackBar);
  
  displayedColumns = ['codigo', 'nombre', 'tipo', 'prioridad', 'duracion', 'docente', 'espacio', 'acciones'];
  filterStr = signal('');
  
  filteredAsignaturas = computed(() => {
    const f = this.filterStr().toLowerCase();
    const all = this.state.asignaturas();
    if (!f) return all;
    return all.filter(a => a.nombre.toLowerCase().includes(f) || a.codigo.toLowerCase().includes(f));
  });

  applyFilter(event: Event) {
    const filterValue = (event.target as HTMLInputElement).value;
    this.filterStr.set(filterValue);
  }

  getDocenteName(id: string) {
    const d = this.state.docentes().find(x => x.id === id);
    return d ? d.nombre : 'No asignado';
  }

  getEspacioName(id?: string) {
    if (!id) return 'Cualquiera';
    const e = this.state.espacios().find(x => x.id === id);
    return e ? e.nombre : 'Cualquiera';
  }

  openDialog(asignatura?: Asignatura) {
    const dialogRef = this.dialog.open(AsignaturaDialogComponent, { width: '500px', data: asignatura });
    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        if (asignatura) {
          this.state.updateAsignatura({ ...asignatura, ...result });
          this.snackBar.open('Asignatura actualizada', 'Cerrar', { duration: 3000 });
        } else {
          this.state.addAsignatura({ id: Math.random().toString(), ...result });
          this.snackBar.open('Asignatura agregada', 'Cerrar', { duration: 3000 });
        }
      }
    });
  }

  delete(asignatura: Asignatura) {
    this.state.deleteAsignatura(asignatura.id);
    this.snackBar.open('Asignatura eliminada', 'Cerrar', { duration: 3000 });
  }
}

@Component({
  selector: 'app-asignatura-dialog',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule, MatCheckboxModule],
  template: `
    <h2 mat-dialog-title>{{ data ? 'Editar Asignatura' : 'Agregar Asignatura' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="dialog-form">
        <mat-form-field appearance="outline">
          <mat-label>Código</mat-label>
          <input matInput formControlName="codigo" required>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Nombre</mat-label>
          <input matInput formControlName="nombre" required>
        </mat-form-field>
        
        <mat-form-field appearance="outline">
          <mat-label>Tipo</mat-label>
          <mat-select formControlName="tipo" required>
            <mat-option value="Tipo A">Tipo A (8 presenciales + 8 virtuales)</mat-option>
            <mat-option value="Tipo B">Tipo B (flexible, máx. 12 presenciales)</mat-option>
          </mat-select>
        </mat-form-field>
        
        <mat-form-field appearance="outline">
          <mat-label>Prioridad</mat-label>
          <mat-select formControlName="prioridad" required>
            <mat-option [value]="1">1</mat-option>
            <mat-option [value]="2">2</mat-option>
            <mat-option [value]="3">3</mat-option>
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Duración de sesión</mat-label>
          <mat-select formControlName="duracion" required>
            <mat-option [value]="2">2 horas</mat-option>
            <mat-option [value]="3">3 horas</mat-option>
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Docente asignado</mat-label>
          <mat-select formControlName="docenteId" required>
            <mat-option *ngFor="let doc of docentes" [value]="doc.id">{{doc.nombre}}</mat-option>
          </mat-select>
        </mat-form-field>

        <mat-checkbox formControlName="reqEspacio" (change)="toggleEspacio($event.checked)">¿Requiere espacio fijo?</mat-checkbox>
        <mat-form-field appearance="outline" *ngIf="form.get('reqEspacio')?.value">
          <mat-label>Espacio</mat-label>
          <mat-select formControlName="espacioFijoId">
            <mat-option *ngFor="let esp of espacios" [value]="esp.id">{{esp.nombre}}</mat-option>
          </mat-select>
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancelar</button>
      <button mat-flat-button color="primary" class="primary-button" [disabled]="form.invalid" (click)="save()">Guardar</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .dialog-form { display: flex; flex-direction: column; gap: 16px; margin-top: 16px; }
  `]
})
export class AsignaturaDialogComponent {
  fb = inject(FormBuilder);
  dialogRef = inject(MatDialogRef<AsignaturaDialogComponent>);
  data = inject(MAT_DIALOG_DATA);
  state = inject(StateService);

  docentes = this.state.docentes();
  espacios = this.state.espacios();

  form = this.fb.group({
    codigo: [this.data?.codigo || '', Validators.required],
    nombre: [this.data?.nombre || '', Validators.required],
    tipo: [this.data?.tipo || '', Validators.required],
    prioridad: [this.data?.prioridad || '', Validators.required],
    duracion: [this.data?.duracion || '', Validators.required],
    docenteId: [this.data?.docenteId || '', Validators.required],
    reqEspacio: [!!this.data?.espacioFijoId],
    espacioFijoId: [this.data?.espacioFijoId || '']
  });

  toggleEspacio(checked: boolean) {
    if (!checked) {
      this.form.get('espacioFijoId')?.setValue('');
    }
  }

  save() {
    if (this.form.valid) {
      const val = this.form.value;
      delete val.reqEspacio;
      this.dialogRef.close(val);
    }
  }
}
