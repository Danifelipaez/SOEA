import { Component, inject, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators, FormGroup, FormArray } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { StateService } from '../../../core/state.service';
import { Docente } from '../../../core/models';
import { MatSnackBar } from '@angular/material/snack-bar';

@Component({
  selector: 'app-docentes-tab',
  standalone: true,
  imports: [CommonModule, MatTableModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule],
  template: `
    <div class="tab-content">
      <div class="actions-row">
        <mat-form-field appearance="outline" class="filter-input">
          <mat-label>Buscar...</mat-label>
          <input matInput (keyup)="applyFilter($event)" placeholder="Ej. Juan Perez" #input>
        </mat-form-field>
        <button mat-flat-button color="primary" class="primary-button" (click)="openDialog()">Agregar docente</button>
      </div>
      <table mat-table [dataSource]="filteredDocentes()" class="mat-elevation-z0 border-table">
        <ng-container matColumnDef="nombre">
          <th mat-header-cell *matHeaderCellDef> Nombre completo </th>
          <td mat-cell *matCellDef="let element"> {{element.nombre}} </td>
        </ng-container>
        <ng-container matColumnDef="cedula">
          <th mat-header-cell *matHeaderCellDef> Cédula </th>
          <td mat-cell *matCellDef="let element"> {{element.cedula}} </td>
        </ng-container>
        <ng-container matColumnDef="maxHoras">
          <th mat-header-cell *matHeaderCellDef> Máx. hrs semanales </th>
          <td mat-cell *matCellDef="let element"> {{element.maxHoras}} </td>
        </ng-container>
        <ng-container matColumnDef="disponibilidad">
          <th mat-header-cell *matHeaderCellDef> Disponibilidad </th>
          <td mat-cell *matCellDef="let element"> {{formatDisp(element.disponibilidad)}} </td>
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
          <td class="mat-cell" colspan="5">No hay datos que coincidan con el filtro.</td>
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
export class DocentesTabComponent {
  state = inject(StateService);
  dialog = inject(MatDialog);
  snackBar = inject(MatSnackBar);
  
  displayedColumns = ['nombre', 'cedula', 'maxHoras', 'disponibilidad', 'acciones'];
  filterStr = signal('');
  
  filteredDocentes = computed(() => {
    const f = this.filterStr().toLowerCase();
    const all = this.state.docentes();
    if (!f) return all;
    return all.filter(d => d.nombre.toLowerCase().includes(f) || d.cedula.includes(f));
  });

  applyFilter(event: Event) {
    const filterValue = (event.target as HTMLInputElement).value;
    this.filterStr.set(filterValue);
  }

  formatDisp(disp: any) {
    if (!disp) return 'No configurada';
    return 'Configurada por día';
  }

  openDialog(docente?: Docente) {
    const dialogRef = this.dialog.open(DocenteDialogComponent, { width: '800px', data: docente });
    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        if (docente) {
          this.state.updateDocente({ ...docente, ...result });
          this.snackBar.open('Docente actualizado', 'Cerrar', { duration: 3000 });
        } else {
          this.state.addDocente({ id: Math.random().toString(), ...result });
          this.snackBar.open('Docente agregado', 'Cerrar', { duration: 3000 });
        }
      }
    });
  }

  delete(docente: Docente) {
    this.state.deleteDocente(docente.id);
    this.snackBar.open('Docente eliminado', 'Cerrar', { duration: 3000 });
  }
}

@Component({
  selector: 'app-docente-dialog',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule, MatCheckboxModule],
  template: `
    <h2 mat-dialog-title>{{ data ? 'Editar Docente' : 'Agregar Docente' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="dialog-form">
        <div class="form-row">
          <mat-form-field appearance="outline">
            <mat-label>Nombre completo</mat-label>
            <input matInput formControlName="nombre" required>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Cédula</mat-label>
            <input matInput formControlName="cedula" required>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Máximo horas semanales</mat-label>
            <input matInput type="number" formControlName="maxHoras" required>
          </mat-form-field>
        </div>
        
        <h3>Disponibilidad por día</h3>
        <div formGroupName="disponibilidad" class="disp-grid">
          <div *ngFor="let dia of dias" [formGroupName]="dia" class="disp-row">
            <div class="dia-label">{{dia | titlecase}}</div>
            <mat-checkbox formControlName="noDisponible">No disponible</mat-checkbox>
            <ng-container *ngIf="!form.get('disponibilidad.' + dia + '.noDisponible')?.value">
              <mat-form-field appearance="outline">
                <mat-label>Tipo</mat-label>
                <mat-select formControlName="tipo">
                  <mat-option value="Franja general">Franja general</mat-option>
                  <mat-option value="Franja específica">Franja específica</mat-option>
                </mat-select>
              </mat-form-field>
              
              <mat-form-field appearance="outline" *ngIf="form.get('disponibilidad.' + dia + '.tipo')?.value === 'Franja general'">
                <mat-label>Franja</mat-label>
                <mat-select formControlName="franjaGeneral">
                  <mat-option value="Matutino (6:00–12:00)">Matutino (6:00–12:00)</mat-option>
                  <mat-option value="Vespertino (12:00–18:00)">Vespertino (12:00–18:00)</mat-option>
                  <mat-option value="Nocturno (18:00–21:30)">Nocturno (18:00–21:30)</mat-option>
                </mat-select>
              </mat-form-field>

              <mat-form-field appearance="outline" *ngIf="form.get('disponibilidad.' + dia + '.tipo')?.value === 'Franja específica'">
                <mat-label>Desde (HH:mm)</mat-label>
                <input matInput formControlName="desde" type="time">
              </mat-form-field>
              
              <mat-form-field appearance="outline" *ngIf="form.get('disponibilidad.' + dia + '.tipo')?.value === 'Franja específica'">
                <mat-label>Hasta (HH:mm)</mat-label>
                <input matInput formControlName="hasta" type="time">
              </mat-form-field>
            </ng-container>
          </div>
        </div>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancelar</button>
      <button mat-flat-button color="primary" class="primary-button" [disabled]="form.invalid" (click)="save()">Guardar</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .dialog-form { display: flex; flex-direction: column; gap: 16px; margin-top: 16px; }
    .form-row { display: flex; gap: 16px; }
    .form-row > * { flex: 1; }
    .disp-grid { display: flex; flex-direction: column; gap: 8px; }
    .disp-row { display: flex; gap: 16px; align-items: center; border-bottom: 1px solid #f0f0f0; padding-bottom: 8px; min-height: 72px; }
    .dia-label { width: 80px; font-weight: 500; }
  `]
})
export class DocenteDialogComponent {
  fb = inject(FormBuilder);
  dialogRef = inject(MatDialogRef<DocenteDialogComponent>);
  data = inject(MAT_DIALOG_DATA);
  dias = ['lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado'];

  form: FormGroup;

  constructor() {
    const dispGroup: any = {};
    this.dias.forEach(dia => {
      const dData = this.data?.disponibilidad?.[dia] || {};
      dispGroup[dia] = this.fb.group({
        noDisponible: [dData.noDisponible || false],
        tipo: [dData.tipo || 'Franja general'],
        franjaGeneral: [dData.franjaGeneral || 'Matutino (6:00–12:00)'],
        desde: [dData.desde || '06:00'],
        hasta: [dData.hasta || '12:00']
      });
    });

    this.form = this.fb.group({
      nombre: [this.data?.nombre || '', Validators.required],
      cedula: [this.data?.cedula || '', Validators.required],
      maxHoras: [this.data?.maxHoras || '', [Validators.required, Validators.min(1)]],
      disponibilidad: this.fb.group(dispGroup)
    });
  }

  save() {
    if (this.form.valid) {
      this.dialogRef.close(this.form.value);
    }
  }
}
