import { Component, inject, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormsModule, FormBuilder, Validators, FormGroup } from '@angular/forms';
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
import { CatalogoService } from '../../../core/catalogo.service';
import { mensajeErrorHttp } from '../../../core/http-error.util';
import { GuardadoResultadoDialogComponent } from '../../../shared/guardado-resultado-dialog/guardado-resultado-dialog.component';
import { ConfirmDeleteDialogComponent } from '../../../shared/confirm-delete-dialog/confirm-delete-dialog.component';
import { Grupo } from '../../../core/models';
import { MatSnackBar } from '@angular/material/snack-bar';
import { forkJoin, of } from 'rxjs';
import { map, catchError } from 'rxjs/operators';

@Component({
  selector: 'app-grupo-tab',
  standalone: true,
  imports: [CommonModule, MatTableModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatIconModule],
  template: `
    <div class="tab-content">
      <div class="actions-row">
        <mat-form-field appearance="outline" class="filter-input">
          <mat-label>Buscar...</mat-label>
          <input matInput (keyup)="applyFilter($event)" placeholder="Ej. Grupo A">
        </mat-form-field>
        <div class="btn-group">
          <button mat-stroked-button (click)="cargarDesdeBD()" [disabled]="saving()">Cargar desde BD</button>
          <button mat-stroked-button color="accent" (click)="guardarEnBD()" [disabled]="saving()">
            {{ saving() ? 'Guardando...' : 'Guardar en BD' }}
          </button>
          <button mat-flat-button color="primary" class="primary-button" (click)="openDialog()">Agregar grupo</button>
        </div>
      </div>
      <table mat-table [dataSource]="filteredGrupos()" class="mat-elevation-z0 border-table">
        <ng-container matColumnDef="nombre">
          <th mat-header-cell *matHeaderCellDef> Nombre </th>
          <td mat-cell *matCellDef="let g"> {{g.nombre}} </td>
        </ng-container>
        <ng-container matColumnDef="asignatura">
          <th mat-header-cell *matHeaderCellDef> Asignatura </th>
          <td mat-cell *matCellDef="let g"> {{getAsignaturaNombre(g.asignaturaId)}} </td>
        </ng-container>
        <ng-container matColumnDef="programa">
          <th mat-header-cell *matHeaderCellDef> Programa </th>
          <td mat-cell *matCellDef="let g"> {{getProgramaNombre(g.programaId)}} </td>
        </ng-container>
        <ng-container matColumnDef="semestre">
          <th mat-header-cell *matHeaderCellDef> Semestre </th>
          <td mat-cell *matCellDef="let g"> {{g.semestre}} </td>
        </ng-container>
        <ng-container matColumnDef="estudiantes">
          <th mat-header-cell *matHeaderCellDef> Estudiantes </th>
          <td mat-cell *matCellDef="let g"> {{g.estudiantesInscritos}} </td>
        </ng-container>
        <ng-container matColumnDef="disponibilidad">
          <th mat-header-cell *matHeaderCellDef> Disponibilidad </th>
          <td mat-cell *matCellDef="let g"> {{g.disponibilidadUiJson ? 'Configurada por día' : 'No configurada'}} </td>
        </ng-container>
        <ng-container matColumnDef="acciones">
          <th mat-header-cell *matHeaderCellDef> Acciones </th>
          <td mat-cell *matCellDef="let g">
            <button mat-button class="text-primary" (click)="openDialog(g)">Editar</button>
            <button mat-button class="text-error" (click)="delete(g)">Eliminar</button>
          </td>
        </ng-container>
        <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
        <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
        <tr class="mat-row" *matNoDataRow>
          <td class="mat-cell" colspan="7">No hay grupos. Agrega uno con el botón.</td>
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
export class GrupoTabComponent {
  state = inject(StateService);
  dialog = inject(MatDialog);
  snackBar = inject(MatSnackBar);
  persistencia = inject(PersistenciaService);
  catalogo = inject(CatalogoService);

  displayedColumns = ['nombre', 'asignatura', 'programa', 'semestre', 'estudiantes', 'disponibilidad', 'acciones'];
  filterStr = signal('');
  saving = signal(false);

  filteredGrupos = computed(() => {
    const f = this.filterStr().toLowerCase();
    const all = this.state.grupos();
    if (!f) return all;
    return all.filter(g =>
      g.nombre.toLowerCase().includes(f) ||
      this.getAsignaturaNombre(g.asignaturaId).toLowerCase().includes(f) ||
      this.getProgramaNombre(g.programaId).toLowerCase().includes(f)
    );
  });

  applyFilter(event: Event) {
    this.filterStr.set((event.target as HTMLInputElement).value);
  }

  getAsignaturaNombre(id: string): string {
    const a = this.state.asignaturas().find(x => x.id === id);
    return a ? `${a.codigo} – ${a.nombre}` : '—';
  }

  getProgramaNombre(programaId: string): string {
    const p = this.state.getProgramaById(programaId);
    return p ? p.nombre : '—';
  }

  openDialog(grupo?: Grupo) {
    const ref = this.dialog.open(GrupoDialogComponent, {
      width: '1000px', maxWidth: '95vw', data: grupo
    });
    ref.afterClosed().subscribe(result => {
      if (!result) return;
      if (grupo) {
        this.state.updateGrupo({ ...grupo, ...result });
        this.snackBar.open('Grupo actualizado', 'Cerrar', { duration: 3000 });
      } else {
        this.state.addGrupo({ id: crypto.randomUUID(), ...result });
        this.snackBar.open('Grupo agregado', 'Cerrar', { duration: 3000 });
      }
    });
  }

  delete(grupo: Grupo) {
    const enBd = this.catalogo.estaEnBd('grupo', grupo.id);
    const ref = this.dialog.open(ConfirmDeleteDialogComponent, {
      width: '420px',
      data: {
        title: 'Eliminar grupo',
        message: enBd
          ? `Se eliminará "${grupo.nombre}" de la base de datos. Esta acción es irreversible.`
          : `Se eliminará "${grupo.nombre}" (aún no está guardado en la BD).`
      }
    });
    ref.afterClosed().subscribe(confirmado => {
      if (!confirmado) return;
      if (!enBd) {
        this.state.deleteGrupo(grupo.id);
        this.snackBar.open('Grupo eliminado localmente.', 'Cerrar', { duration: 3000 });
        return;
      }
      this.persistencia.eliminarGrupoBD(grupo.id).subscribe({
        next: () => {
          this.catalogo.quitarDeBd('grupo', grupo.id);
          this.state.deleteGrupo(grupo.id);
          this.snackBar.open('Grupo eliminado de la BD.', 'Cerrar', { duration: 3000 });
        },
        error: (err) => this.snackBar.open(
          `Error al eliminar: ${mensajeErrorHttp(err)}`, 'Cerrar', { duration: 5000 })
      });
    });
  }

  guardarEnBD() {
    const grupos = this.state.grupos();
    if (!grupos.length) {
      this.snackBar.open('No hay grupos para guardar.', 'Cerrar', { duration: 3000 });
      return;
    }
    this.saving.set(true);

    const calls$ = grupos.map(g =>
      this.persistencia.actualizarGrupo(g).pipe(
        map(updated => {
          this.state.updateGrupo(updated);
          this.catalogo.marcarEnBd('grupo', updated.id);
          return { ok: true, nombre: g.nombre, tipo: 'actualizado' as const };
        }),
        catchError(err => {
          if (err.status === 404) {
            return this.persistencia.guardarGrupo(g).pipe(
              map(created => {
                this.catalogo.marcarEnBd('grupo', created.id);
                this.state.updateGrupo(created);
                return { ok: true, nombre: g.nombre, tipo: 'nuevo' as const };
              }),
              catchError(() => of({ ok: false, nombre: g.nombre, tipo: 'nuevo' as const }))
            );
          }
          return of({ ok: false, nombre: g.nombre, tipo: 'actualizado' as const });
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
        data: { entidad: 'grupos', nuevos, actualizados, errores }
      });
    });
  }

  cargarDesdeBD() {
    this.saving.set(true);
    this.catalogo.cargarTodo().subscribe({
      next: (resumen) => {
        this.saving.set(false);
        this.snackBar.open(`${resumen.grupos} grupo(s) cargados desde la BD.`, 'Cerrar', { duration: 4000 });
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Error al cargar desde la BD.', 'Cerrar', { duration: 4000 });
      }
    });
  }
}

// ─── Dialog de creación/edición de grupo ─────────────────────────────────────

@Component({
  selector: 'app-grupo-dialog',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MatDialogModule, MatFormFieldModule,
            MatInputModule, MatSelectModule, MatButtonModule, MatCheckboxModule],
  template: `
    <h2 mat-dialog-title>{{ data ? 'Editar Grupo' : 'Agregar Grupo' }}</h2>
    <mat-dialog-content class="dialog-scroll">
      <form [formGroup]="form" class="dialog-form">

        <!-- Asignatura — requerida: invariante del dominio -->
        <mat-form-field appearance="outline">
          <mat-label>Asignatura *</mat-label>
          <mat-select formControlName="asignaturaId" required
                      (selectionChange)="onAsignaturaChange($event.value)">
            <mat-option *ngFor="let a of asignaturas" [value]="a.id">
              {{ a.codigo }} – {{ a.nombre }}
            </mat-option>
          </mat-select>
          <mat-error>La asignatura es obligatoria</mat-error>
        </mat-form-field>

        <div class="form-row">
          <mat-form-field appearance="outline">
            <mat-label>Nombre del grupo</mat-label>
            <input matInput formControlName="nombre" required placeholder="Ej. G-QO-2026A">
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Programa</mat-label>
            <mat-select formControlName="programaId" required>
              <mat-option *ngFor="let p of programas" [value]="p.id">{{ p.nombre }}</mat-option>
            </mat-select>
          </mat-form-field>
        </div>

        <div class="form-row">
          <mat-form-field appearance="outline">
            <mat-label>Semestre</mat-label>
            <input matInput type="number" formControlName="semestre" min="1" max="10" required>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Estudiantes inscritos</mat-label>
            <input matInput type="number" formControlName="estudiantesInscritos" min="1" required>
            <mat-hint>Define el aforo requerido (HC-CAP)</mat-hint>
          </mat-form-field>
        </div>

        <h3 class="section-label">Disponibilidad del grupo por día</h3>
        <div class="disp-table">
          <div class="disp-header">
            <span class="col-dia">Día</span>
            <span class="col-nd">No disponible</span>
            <span class="col-tipo">Ventana</span>
            <span class="col-times">Horario</span>
          </div>

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
                <option value="matutino">Matutino (06:00–12:00)</option>
                <option value="vespertino">Vespertino (12:00–18:00)</option>
                <option value="nocturno">Nocturno (18:00–22:00)</option>
                <option value="especifico">Franja específica</option>
              </select>
            </span>
            <span class="col-tipo" *ngIf="getDisp(dia, 'noDisponible')" style="color:#aaa">—</span>

            <span class="col-times"
                  *ngIf="!getDisp(dia, 'noDisponible') && getDisp(dia, 'tipo') === 'especifico'">
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
            <span class="col-times summary-text"
                  *ngIf="getDisp(dia, 'noDisponible')" style="color:#aaa">—</span>
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
export class GrupoDialogComponent {
  fb = inject(FormBuilder);
  dialogRef = inject(MatDialogRef<GrupoDialogComponent>);
  data = inject(MAT_DIALOG_DATA) as Grupo | undefined;
  state = inject(StateService);

  asignaturas = this.state.asignaturas();
  programas = this.state.programas();
  dias = ['lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado'];

  // Estado de disponibilidad por día (inicializado desde JSON existente o defaults)
  disp: Record<string, any> = {};

  form: FormGroup;

  constructor() {
    // Parsear disponibilidad existente desde JSON si está editando
    const dispExistente = this.data?.disponibilidadUiJson
      ? (() => { try { return JSON.parse(this.data!.disponibilidadUiJson!); } catch { return {}; } })()
      : {};

    this.dias.forEach(dia => {
      const d = dispExistente[dia] ?? {};
      this.disp[dia] = {
        noDisponible: d.noDisponible ?? false,
        tipo: d.tipo ?? 'todo',
        desde: d.desde ?? '06:00',
        hasta: d.hasta ?? '22:00'
      };
    });

    this.form = this.fb.group({
      asignaturaId:         [this.data?.asignaturaId ?? '', Validators.required],
      nombre:               [this.data?.nombre ?? '', Validators.required],
      programaId:           [this.data?.programaId ?? '', Validators.required],
      semestre:             [this.data?.semestre ?? 1, [Validators.required, Validators.min(1), Validators.max(10)]],
      estudiantesInscritos: [this.data?.estudiantesInscritos ?? 30, [Validators.required, Validators.min(1)]]
    });
  }

  // Al cambiar asignatura, auto-rellenar el programaId desde la asignatura seleccionada
  onAsignaturaChange(asignaturaId: string) {
    const a = this.state.asignaturas().find(x => x.id === asignaturaId);
    if (a) this.form.patchValue({ programaId: a.programaId });
  }

  getDisp(dia: string, field: string): any { return this.disp[dia]?.[field]; }
  setDisp(dia: string, field: string, value: any): void { this.disp[dia] = { ...this.disp[dia], [field]: value }; }

  tipoLabel(dia: string): string {
    switch (this.disp[dia]?.tipo) {
      case 'todo':       return '06:00 – 22:00';
      case 'matutino':   return '06:00 – 12:00';
      case 'vespertino': return '12:00 – 18:00';
      case 'nocturno':   return '18:00 – 22:00';
      default:           return '';
    }
  }

  save() {
    if (this.form.invalid) return;
    const dispObj: Record<string, any> = {};
    this.dias.forEach(dia => {
      const d = this.disp[dia];
      if (d.noDisponible) {
        dispObj[dia] = { noDisponible: true };
      } else if (d.tipo === 'especifico') {
        dispObj[dia] = { noDisponible: false, tipo: 'Franja específica', desde: d.desde, hasta: d.hasta };
      } else {
        const franjaMap: Record<string, string> = {
          todo:       'Todo el día (06:00–22:00)',
          matutino:   'Matutino (06:00–12:00)',
          vespertino: 'Vespertino (12:00–18:00)',
          nocturno:   'Nocturno (18:00–22:00)'
        };
        dispObj[dia] = { noDisponible: false, tipo: 'Franja general',
                         franjaGeneral: franjaMap[d.tipo] ?? 'Todo el día (06:00–22:00)' };
      }
    });

    this.dialogRef.close({
      ...this.form.value,
      disponibilidadUiJson: JSON.stringify(dispObj)
    });
  }
}
