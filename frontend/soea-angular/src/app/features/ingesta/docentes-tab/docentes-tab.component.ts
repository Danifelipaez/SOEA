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
import { MatIconModule } from '@angular/material/icon';
import { StateService } from '../../../core/state.service';
import { PersistenciaService } from '../../../core/persistencia.service';
import { Docente } from '../../../core/models';
import { MatSnackBar } from '@angular/material/snack-bar';
import { forkJoin, of } from 'rxjs';
import { map, catchError } from 'rxjs/operators';

@Component({
  selector: 'app-docentes-tab',
  standalone: true,
  imports: [CommonModule, MatTableModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatIconModule],
  template: `
    <div class="tab-content">
      <div class="actions-row">
        <mat-form-field appearance="outline" class="filter-input">
          <mat-label>Buscar...</mat-label>
          <input matInput (keyup)="applyFilter($event)" placeholder="Ej. Juan Perez" #input>
        </mat-form-field>
        <div class="btn-group">
          <button mat-stroked-button (click)="cargarDesdeBD()" [disabled]="saving()">Cargar desde BD</button>
          <button mat-stroked-button color="accent" (click)="guardarEnBD()" [disabled]="saving()">
            {{ saving() ? 'Guardando...' : 'Guardar en BD' }}
          </button>
          <button mat-flat-button color="primary" class="primary-button" (click)="openDialog()">Agregar docente</button>
        </div>
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
    .btn-group { display: flex; gap: 8px; align-items: center; }
    .border-table { border: 1px solid #e0e0e0; border-bottom: 0; }
  `]
})
export class DocentesTabComponent {
  state = inject(StateService);
  dialog = inject(MatDialog);
  snackBar = inject(MatSnackBar);
  persistencia = inject(PersistenciaService);

  displayedColumns = ['nombre', 'cedula', 'maxHoras', 'disponibilidad', 'acciones'];
  filterStr = signal('');
  saving = signal(false);
  private bdIds = new Set<string>();

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
    const dialogRef = this.dialog.open(DocenteDialogComponent, {
      width: '1000px',
      maxWidth: '95vw',
      data: docente
    });
    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        if (docente) {
          this.state.updateDocente({ ...docente, ...result });
          this.snackBar.open('Docente actualizado', 'Cerrar', { duration: 3000 });
        } else {
          this.state.addDocente({ id: crypto.randomUUID(), ...result });
          this.snackBar.open('Docente agregado', 'Cerrar', { duration: 3000 });
        }
      }
    });
  }

  delete(docente: Docente) {
    if (this.bdIds.has(docente.id)) {
      this.persistencia.eliminarDocenteBD(docente.id).subscribe({
        next: () => {
          this.bdIds.delete(docente.id);
          this.state.deleteDocente(docente.id);
          this.snackBar.open('Docente eliminado de la BD.', 'Cerrar', { duration: 3000 });
        },
        error: () => this.snackBar.open('Error al eliminar de la BD.', 'Cerrar', { duration: 4000 })
      });
    } else {
      this.state.deleteDocente(docente.id);
      this.snackBar.open('Docente eliminado localmente.', 'Cerrar', { duration: 3000 });
    }
  }

  guardarEnBD() {
    const docentes = this.state.docentes();
    if (!docentes.length) {
      this.snackBar.open('No hay docentes para guardar.', 'Cerrar', { duration: 3000 });
      return;
    }
    this.saving.set(true);

    // PUT-first: intenta actualizar; si el backend devuelve 404 (no existe aún), crea con POST.
    const calls$ = docentes.map(d =>
      this.persistencia.actualizarDocente(d).pipe(
        map(updated => {
          this.state.updateDocente(updated);
          this.bdIds.add(updated.id);
          return { ok: true, nombre: d.nombre, tipo: 'actualizado' as const };
        }),
        catchError(err => {
          if (err.status === 404) {
            return this.persistencia.guardarDocente(d).pipe(
              map(created => {
                this.bdIds.add(created.id);
                this.state.updateDocente(created);
                return { ok: true, nombre: d.nombre, tipo: 'nuevo' as const };
              }),
              catchError(() => of({ ok: false, nombre: d.nombre, tipo: 'nuevo' as const }))
            );
          }
          return of({ ok: false, nombre: d.nombre, tipo: 'actualizado' as const });
        })
      )
    );

    forkJoin(calls$).subscribe(results => {
      this.saving.set(false);
      const nuevos       = results.filter(r => r.ok && r.tipo === 'nuevo').map(r => r.nombre);
      const actualizados = results.filter(r => r.ok && r.tipo === 'actualizado').map(r => r.nombre);
      const errores      = results.filter(r => !r.ok).map(r => r.nombre);
      this.dialog.open(GuardadoResultadoDialogComponent, {
        width: '420px',
        data: { entidad: 'docentes', nuevos, actualizados, errores }
      });
    });
  }

  cargarDesdeBD() {
    this.saving.set(true);
    this.persistencia.cargarDocentes().subscribe({
      next: (docentes) => {
        this.saving.set(false);
        this.bdIds = new Set(docentes.map(d => d.id));
        docentes.forEach(d => {
          const existing = this.state.docentes().find(x => x.id === d.id);
          if (existing) this.state.updateDocente(d);
          else this.state.addDocente(d);
        });
        this.snackBar.open(`${docentes.length} docente(s) cargados desde la BD.`, 'Cerrar', { duration: 4000 });
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Error al cargar desde la BD.', 'Cerrar', { duration: 4000 });
      }
    });
  }
}

@Component({
  selector: 'app-docente-dialog',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MatDialogModule, MatFormFieldModule,
            MatInputModule, MatSelectModule, MatButtonModule, MatCheckboxModule],
  template: `
    <h2 mat-dialog-title>{{ data ? 'Editar Docente' : 'Agregar Docente' }}</h2>
    <mat-dialog-content class="dialog-scroll">
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
            <mat-label>Máx. horas semanales</mat-label>
            <input matInput type="number" formControlName="maxHoras" min="1" required>
          </mat-form-field>
        </div>

        <h3 class="section-label">Disponibilidad por día</h3>
        <div class="disp-table">
          <!-- Header -->
          <div class="disp-header">
            <span class="col-dia">Día</span>
            <span class="col-nd">No disponible</span>
            <span class="col-tipo">Tipo</span>
            <span class="col-times">Horario</span>
          </div>

          <!-- One row per day — NO nested formGroupName to avoid *ngIf binding issues -->
          <div *ngFor="let dia of dias" class="disp-row">
            <span class="col-dia">{{ dia | titlecase }}</span>

            <span class="col-nd">
              <input type="checkbox"
                     [checked]="getDisp(dia, 'noDisponible')"
                     (change)="setDisp(dia, 'noDisponible', $any($event.target).checked)">
            </span>

            <span class="col-tipo" *ngIf="!getDisp(dia, 'noDisponible')">
              <select class="native-select"
                      [value]="getDisp(dia, 'tipo')"
                      (change)="setDisp(dia, 'tipo', $any($event.target).value)">
                <option value="todo">Todo el día (06:00–22:00)</option>
                <option value="oficina">Horario de oficina (06:00–18:00)</option>
                <option value="matutino">Matutino (06:00–12:00)</option>
                <option value="vespertino">Vespertino (12:00–18:00)</option>
                <option value="nocturno">Nocturno (18:00–22:00)</option>
                <option value="especifico">Franja específica</option>
              </select>
            </span>
            <span class="col-tipo" *ngIf="getDisp(dia, 'noDisponible')" style="color:#aaa">—</span>

            <span class="col-times" *ngIf="!getDisp(dia, 'noDisponible') && getDisp(dia, 'tipo') === 'especifico'">
              <label class="time-label">De
                <input class="time-input" type="time" min="06:00" max="22:00"
                       [value]="getDisp(dia, 'desde')"
                    (input)="setDisp(dia, 'desde', $any($event.target).value)">
              </label>
              <label class="time-label">A
                <input class="time-input" type="time" min="06:00" max="22:00"
                       [value]="getDisp(dia, 'hasta')"
                    (input)="setDisp(dia, 'hasta', $any($event.target).value)">
              </label>
            </span>
            <span class="col-times summary-text"
                  *ngIf="!getDisp(dia, 'noDisponible') && getDisp(dia, 'tipo') !== 'especifico'">
              {{ tipoLabel(dia) }}
            </span>
            <span class="col-times summary-text" *ngIf="getDisp(dia, 'noDisponible')" style="color:#aaa">—</span>
          </div>
        </div>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancelar</button>
      <button mat-flat-button color="primary" class="primary-button"
              [disabled]="form.invalid" (click)="save()">Guardar</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .dialog-scroll { max-height: 70vh; overflow-y: auto; }
    .dialog-form  { display: flex; flex-direction: column; gap: 16px; margin-top: 16px; min-width: 860px; }
    .form-row     { display: flex; gap: 16px; }
    .form-row > * { flex: 1; }
    .section-label { margin: 8px 0 4px; font-weight: 500; font-size: 15px; color: #424242; }

    .disp-table  { border: 1px solid #e0e0e0; border-radius: 6px; overflow: hidden; }
    .disp-header { display: flex; gap: 8px; align-items: center; padding: 8px 12px;
                   background: #f5f5f5; font-size: 12px; font-weight: 600; color: #616161; }
    .disp-row    { display: flex; gap: 8px; align-items: center; padding: 10px 12px;
                   border-top: 1px solid #f0f0f0; min-height: 52px; }
    .disp-row:hover { background: #fafafa; }

    .col-dia   { width: 90px; font-weight: 500; font-size: 14px; }
    .col-nd    { width: 100px; display: flex; justify-content: center; }
    .col-tipo  { width: 190px; }
    .col-times { flex: 1; display: flex; gap: 16px; align-items: center; }

    .native-select {
      width: 100%; padding: 6px 8px; border: 1px solid #bdbdbd; border-radius: 4px;
      font-size: 14px; background: white; cursor: pointer; outline: none;
    }
    .native-select:focus { border-color: #1976d2; }

    .time-label { display: flex; align-items: center; gap: 6px; font-size: 13px; color: #424242; }
    .time-input {
      padding: 6px 8px; border: 1px solid #bdbdbd; border-radius: 4px;
      font-size: 14px; width: 110px; outline: none;
    }
    .time-input:focus { border-color: #1976d2; }

    .summary-text { font-size: 13px; color: #616161; }
  `]
})
export class DocenteDialogComponent {
  fb = inject(FormBuilder);
  dialogRef = inject(MatDialogRef<DocenteDialogComponent>);
  data = inject(MAT_DIALOG_DATA);

  dias = ['lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado'];

  form: FormGroup;

  // Flat availability state keyed by día — avoids nested formGroup binding bugs.
  disp: Record<string, any> = {};

  constructor() {
    const generalToTipo: Record<string, string> = {
      'Todo el día': 'todo',
      'Todo el día (06:00–22:00)': 'todo',
      'Horario de oficina (06:00–18:00)': 'oficina',
      'Matutino (06:00–12:00)': 'matutino',
      'Matutino (6:00–12:00)': 'matutino',
      'Matutino (06:00–13:00)': 'matutino',
      'Vespertino (12:00–18:00)': 'vespertino',
      'Vespertino (13:00–19:00)': 'vespertino',
      'Nocturno (18:00–22:00)': 'nocturno',
      'Nocturno (19:00–22:00)': 'nocturno',
      'Nocturno (18:00–21:30)': 'nocturno'
    };

    this.dias.forEach(dia => {
      const d = this.data?.disponibilidad?.[dia] ?? {};
      const tipoRaw = d.tipo ?? 'todo';
      let tipo = tipoRaw;
      if (tipoRaw === 'Franja específica') {
        tipo = 'especifico';
      } else if (tipoRaw === 'Franja general') {
        tipo = generalToTipo[d.franjaGeneral] ?? 'todo';
      }

      this.disp[dia] = {
        noDisponible: d.noDisponible ?? false,
        tipo,
        desde: d.desde ?? '06:00',
        hasta: d.hasta ?? '22:00'
      };
    });

    this.form = this.fb.group({
      nombre:   [this.data?.nombre   ?? '', Validators.required],
      cedula:   [this.data?.cedula   ?? '', Validators.required],
      maxHoras: [this.data?.maxHoras ?? 40, [Validators.required, Validators.min(1)]]
    });
  }

  getDisp(dia: string, field: string): any {
    return this.disp[dia]?.[field];
  }

  setDisp(dia: string, field: string, value: any): void {
    this.disp[dia] = { ...this.disp[dia], [field]: value };
  }

  tipoLabel(dia: string): string {
    const tipo = this.disp[dia]?.tipo;
    switch (tipo) {
      case 'todo':       return '06:00 – 22:00';
      case 'oficina':    return '06:00 – 18:00';
      case 'matutino':   return '06:00 – 12:00';
      case 'vespertino': return '12:00 – 18:00';
      case 'nocturno':   return '18:00 – 22:00';
      default:           return '';
    }
  }

  save() {
    if (this.form.invalid) return;
    // Normalise disp to the same shape the backend already understands.
    const disponibilidad: Record<string, any> = {};
    this.dias.forEach(dia => {
      const d = this.disp[dia];
      if (d.noDisponible) {
        disponibilidad[dia] = { noDisponible: true };
      } else if (d.tipo === 'especifico') {
        disponibilidad[dia] = { noDisponible: false, tipo: 'Franja específica',
                                desde: d.desde, hasta: d.hasta };
      } else {
        const franjaMap: Record<string, string> = {
          todo:       'Todo el día (06:00–22:00)',
          oficina:    'Horario de oficina (06:00–18:00)',
          matutino:   'Matutino (06:00–12:00)',
          vespertino: 'Vespertino (12:00–18:00)',
          nocturno:   'Nocturno (18:00–22:00)'
        };
        disponibilidad[dia] = { noDisponible: false, tipo: 'Franja general',
                                franjaGeneral: franjaMap[d.tipo] ?? 'Todo el día (06:00–22:00)' };
      }
    });
    this.dialogRef.close({ ...this.form.value, disponibilidad });
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
