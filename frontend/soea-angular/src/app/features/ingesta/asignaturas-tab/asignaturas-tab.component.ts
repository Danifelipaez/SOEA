import { Component, inject, computed, signal } from '@angular/core';
import { forkJoin, of, Observable } from 'rxjs';
import { map, catchError } from 'rxjs/operators';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { StateService } from '../../../core/state.service';
import { PersistenciaService } from '../../../core/persistencia.service';
import { CatalogoService } from '../../../core/catalogo.service';
import { mensajeErrorHttp } from '../../../core/http-error.util';
import { GuardadoResultadoDialogComponent } from '../../../shared/guardado-resultado-dialog/guardado-resultado-dialog.component';
import { ConfirmDeleteDialogComponent } from '../../../shared/confirm-delete-dialog/confirm-delete-dialog.component';
import { Asignatura, Facultad, Programa } from '../../../core/models';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ImportExcelStatsDto } from '../../../core/persistencia.service';

@Component({
  selector: 'app-asignaturas-tab',
  standalone: true,
  imports: [
    CommonModule, MatTableModule, MatButtonModule, MatDialogModule,
    MatFormFieldModule, MatInputModule, MatSelectModule, MatIconModule,
    MatTooltipModule, MatChipsModule, MatProgressSpinnerModule
  ],
  template: `
    <div class="tab-content">
      <!-- ── Barra de herramientas ── -->
      <div class="actions-row">
        <mat-form-field appearance="outline" class="filter-input">
          <mat-label>Buscar...</mat-label>
          <input matInput (keyup)="applyFilter($event)" placeholder="Ej. Química 101">
        </mat-form-field>
        <div class="btn-group">
          <button mat-stroked-button (click)="importarExcel()" [disabled]="uploading()"
                  matTooltip="Sube el Excel al servidor — el backend parsea y persiste todo">
            <mat-icon>upload_file</mat-icon>
            {{ uploading() ? 'Subiendo...' : 'Importar Excel' }}
          </button>
          <button mat-stroked-button (click)="cargarDesdeBD()" [disabled]="saving()">Cargar desde BD</button>
          <button mat-stroked-button color="accent" (click)="guardarEnBD()" [disabled]="saving()">
            {{ saving() ? 'Guardando...' : 'Guardar en BD' }}
          </button>
          <button mat-flat-button color="primary" class="primary-button" (click)="openDialog()">
            <mat-icon>add</mat-icon> Agregar asignatura
          </button>
        </div>
      </div>

      <!-- ── Input oculto para selección de archivo ── -->
      <input type="file" #fileInput accept=".xlsx,.xls" (change)="onFileSelected($event)" style="display:none">

      <!-- ── Indicador de datos cargados ── -->
      <div class="stats-row" *ngIf="state.asignaturas().length > 0">
        <mat-chip-set>
          <mat-chip>{{ state.asignaturas().length }} asignaturas</mat-chip>
          <mat-chip>{{ state.facultades().length }} facultades</mat-chip>
          <mat-chip>{{ state.programas().length }} programas</mat-chip>
        </mat-chip-set>
      </div>

      <!-- ── Tabla ── -->
      <table mat-table [dataSource]="filteredAsignaturas()" class="mat-elevation-z0 border-table">

        <ng-container matColumnDef="facultad">
          <th mat-header-cell *matHeaderCellDef> Facultad </th>
          <td mat-cell *matCellDef="let element"> {{ getFacultadName(element.programaId) }} </td>
        </ng-container>

        <ng-container matColumnDef="programa">
          <th mat-header-cell *matHeaderCellDef> Programa </th>
          <td mat-cell *matCellDef="let element"> {{ getProgramaName(element.programaId) }} </td>
        </ng-container>

        <ng-container matColumnDef="codigo">
          <th mat-header-cell *matHeaderCellDef> Código </th>
          <td mat-cell *matCellDef="let element"> {{element.codigo}} </td>
        </ng-container>

        <ng-container matColumnDef="nombre">
          <th mat-header-cell *matHeaderCellDef> Nombre </th>
          <td mat-cell *matCellDef="let element"> {{element.nombre}} </td>
        </ng-container>

        <ng-container matColumnDef="categoria">
          <th mat-header-cell *matHeaderCellDef> Tipo </th>
          <td mat-cell *matCellDef="let element">
            <span class="badge" [ngClass]="categoriaBadgeClass(element.categoria)">{{ element.categoria ?? '—' }}</span>
          </td>
        </ng-container>

        <ng-container matColumnDef="alternancia">
          <th mat-header-cell *matHeaderCellDef> Alternancia </th>
          <td mat-cell *matCellDef="let element">
            <span class="badge" [ngClass]="badgeClass(element.alternancia)">{{ element.alternancia }}</span>
          </td>
        </ng-container>

        <ng-container matColumnDef="horas">
          <th mat-header-cell *matHeaderCellDef> Horas/sesión </th>
          <td mat-cell *matCellDef="let element"> {{element.horasPorSesion}}h </td>
        </ng-container>

        <ng-container matColumnDef="sesiones">
          <th mat-header-cell *matHeaderCellDef> Sesiones/sem </th>
          <td mat-cell *matCellDef="let element"> {{element.sesionesPorSemana}} </td>
        </ng-container>

        <ng-container matColumnDef="docente">
          <th mat-header-cell *matHeaderCellDef> Docente </th>
          <td mat-cell *matCellDef="let element"> {{getDocenteName(element.docenteId)}} </td>
        </ng-container>

        <ng-container matColumnDef="acciones">
          <th mat-header-cell *matHeaderCellDef> Acciones </th>
          <td mat-cell *matCellDef="let element">
            <button mat-icon-button class="text-primary" (click)="openDialog(element)" matTooltip="Editar">
              <mat-icon>edit</mat-icon>
            </button>
            <button mat-icon-button class="text-error" (click)="delete(element)" matTooltip="Eliminar">
              <mat-icon>delete</mat-icon>
            </button>
          </td>
        </ng-container>

        <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
        <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
        <tr class="mat-row" *matNoDataRow>
          <td class="mat-cell" colspan="9">
            <div class="empty-hint">
              <mat-icon>inbox</mat-icon>
              <p>No hay asignaturas cargadas. Usa "Importar Excel" o "Agregar asignatura".</p>
            </div>
          </td>
        </tr>
      </table>
    </div>
  `,
  styles: [`
    .tab-content { padding: 24px 0; }
    .actions-row { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
    .btn-group { display: flex; gap: 8px; align-items: center; }
    .filter-input { width: 300px; }
    .stats-row { margin-bottom: 16px; }
    .border-table { border: 1px solid #e0e0e0; border-bottom: 0; width: 100%; }
    .badge { padding: 2px 8px; border-radius: 12px; font-size: 12px; font-weight: 500; }
    .badge-a { background: #fff3e0; color: #e65100; }
    .badge-b { background: #e3f2fd; color: #1565c0; }
    .badge-sin   { background: #f3e5f5; color: #6a1b9a; }
    .badge-oblig { background: #e8f5e9; color: #1b5e20; }
    .badge-opt   { background: #fff8e1; color: #f57f17; }
    .badge-elec  { background: #fce4ec; color: #880e4f; }
    .empty-hint { display: flex; flex-direction: column; align-items: center; padding: 32px; color: #757575; }
    .empty-hint mat-icon { font-size: 48px; width: 48px; height: 48px; margin-bottom: 8px; }
  `]
})
export class AsignaturasTabComponent {
  state = inject(StateService);
  dialog = inject(MatDialog);
  snackBar = inject(MatSnackBar);
  persistencia = inject(PersistenciaService);
  catalogo = inject(CatalogoService);

  displayedColumns = ['facultad', 'programa', 'codigo', 'nombre', 'categoria', 'alternancia', 'horas', 'sesiones', 'docente', 'acciones'];
  saving = signal(false);
  uploading = signal(false);
  filterStr = signal('');

  filteredAsignaturas = computed(() => {
    const f = this.filterStr().toLowerCase();
    const all = this.state.asignaturas();
    if (!f) return all;
    return all.filter(a =>
      a.nombre.toLowerCase().includes(f) ||
      a.codigo.toLowerCase().includes(f) ||
      this.getProgramaName(a.programaId).toLowerCase().includes(f) ||
      this.getFacultadName(a.programaId).toLowerCase().includes(f)
    );
  });

  applyFilter(event: Event) {
    this.filterStr.set((event.target as HTMLInputElement).value);
  }

  getFacultadName(programaId: string): string {
    const prog = this.state.getProgramaById(programaId);
    if (!prog) return '—';
    const fac = this.state.getFacultadById(prog.facultadId);
    return fac ? fac.nombre : '—';
  }

  getProgramaName(programaId: string): string {
    const prog = this.state.getProgramaById(programaId);
    return prog ? prog.nombre : '—';
  }

  getDocenteName(id?: string): string {
    if (!id) return 'Sin asignar';
    const d = this.state.docentes().find(x => x.id === id);
    return d ? d.nombre : 'Sin asignar';
  }

  badgeClass(alt: string): string {
    if (alt === 'TipoA') return 'badge badge-a';
    if (alt === 'TipoB') return 'badge badge-b';
    return 'badge badge-sin';
  }

  categoriaBadgeClass(cat?: string): string {
    if (cat === 'Obligatoria') return 'badge badge-oblig';
    if (cat === 'Optativa')    return 'badge badge-opt';
    if (cat === 'Electiva')    return 'badge badge-elec';
    return 'badge badge-sin';
  }

  // ── Importar Excel ────────────────────────────────────────────────────────────
  importarExcel() {
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    input?.click();
  }

  onFileSelected(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    (event.target as HTMLInputElement).value = '';
    this.uploading.set(true);
    this.persistencia.importarExcel(file).subscribe({
      next: (stats: ImportExcelStatsDto) => {
        this.uploading.set(false);
        this.cargarDesdeBD();
        const resumen = [
          `${stats.asignaturasCreadas} asig. nuevas`,
          stats.asignaturasActualizadas > 0 ? `${stats.asignaturasActualizadas} actualizadas` : '',
          `${stats.sesionesPersistidas} sesiones`,
          stats.asignaturasSinDocente > 0 ? `⚠ ${stats.asignaturasSinDocente} sin docente` : '',
          stats.advertencias?.length > 0 ? `${stats.advertencias.length} advertencias` : ''
        ].filter(Boolean).join(' · ');
        this.snackBar.open(resumen || 'Importación completa', 'Cerrar', { duration: 8000 });
        if (stats.advertencias?.length > 0) {
          console.group('Import Excel — advertencias');
          stats.advertencias.forEach(w => console.warn(w));
          console.groupEnd();
        }
      },
      error: (err) => {
        this.uploading.set(false);
        const msg = err?.error?.detail ?? err?.error ?? err?.message ?? 'Error desconocido';
        this.snackBar.open(`Error al importar: ${msg}`, 'Cerrar', { duration: 5000 });
      }
    });
  }

  // ── Dialog manual ─────────────────────────────────────────────────────────────
  openDialog(asignatura?: Asignatura) {
    const dialogRef = this.dialog.open(AsignaturaDialogComponent, { width: '560px', data: asignatura });
    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        if (asignatura) {
          this.state.updateAsignatura({ ...asignatura, ...result });
          this.snackBar.open('Asignatura actualizada', 'Cerrar', { duration: 3000 });
        } else {
          this.state.addAsignatura({ id: crypto.randomUUID(), ...result });
          this.snackBar.open('Asignatura agregada', 'Cerrar', { duration: 3000 });
        }
      }
    });
  }

  delete(asignatura: Asignatura) {
    const enBd = this.catalogo.estaEnBd('asignatura', asignatura.id);
    const ref = this.dialog.open(ConfirmDeleteDialogComponent, {
      width: '420px',
      data: {
        title: 'Eliminar asignatura',
        message: enBd
          ? `Se eliminará "${asignatura.nombre}" de la base de datos. Esta acción es irreversible.`
          : `Se eliminará "${asignatura.nombre}" (aún no está guardada en la BD).`
      }
    });
    ref.afterClosed().subscribe(confirmado => {
      if (!confirmado) return;
      if (!enBd) {
        this.state.deleteAsignatura(asignatura.id);
        this.snackBar.open('Asignatura eliminada localmente.', 'Cerrar', { duration: 3000 });
        return;
      }
      this.persistencia.eliminarAsignatura(asignatura.id).subscribe({
        next: () => {
          this.catalogo.quitarDeBd('asignatura', asignatura.id);
          this.state.deleteAsignatura(asignatura.id);
          // Refetch para garantizar consistencia UI↔BD tras el borrado.
          this.catalogo.cargarTodo().subscribe();
          this.snackBar.open('Asignatura eliminada de la BD.', 'Cerrar', { duration: 3000 });
        },
        error: (err) => this.snackBar.open(
          `Error al eliminar de la BD: ${mensajeErrorHttp(err)}`, 'Cerrar', { duration: 5000 })
      });
    });
  }

  guardarEnBD() {
    const asignaturas = this.state.asignaturas();
    if (asignaturas.length === 0) {
      this.snackBar.open('No hay asignaturas para guardar.', 'Cerrar', { duration: 3000 });
      return;
    }
    this.saving.set(true);

    // PUT-first (mismo patrón que Docentes/Espacios): las asignaturas que ya existen
    // en BD se actualizan vía PUT; las creadas localmente van por el import, que
    // también crea sus facultades/programas/docentes nuevos.
    const existentes = asignaturas.filter(a => this.catalogo.estaEnBd('asignatura', a.id));
    const nuevas     = asignaturas.filter(a => !this.catalogo.estaEnBd('asignatura', a.id));

    type Resultado = { ok: boolean; nombre: string; tipo: 'nuevo' | 'actualizado' };

    const puts$: Observable<Resultado[]> = existentes.length === 0 ? of([]) : forkJoin(
      existentes.map(a =>
        this.persistencia.actualizarAsignatura(a).pipe(
          map((): Resultado => ({ ok: true, nombre: a.nombre, tipo: 'actualizado' })),
          catchError(err => of<Resultado>({
            ok: false, nombre: `${a.nombre} — ${mensajeErrorHttp(err)}`, tipo: 'actualizado'
          }))
        )
      )
    );

    const import$: Observable<Resultado[]> = nuevas.length === 0 ? of([]) :
      this.persistencia.importarCurriculum(this.construirPayloadImport(nuevas)).pipe(
        map(() => nuevas.map((a): Resultado => ({ ok: true, nombre: a.nombre, tipo: 'nuevo' }))),
        catchError(err => of(nuevas.map((a): Resultado => ({
          ok: false, nombre: `${a.nombre} — ${mensajeErrorHttp(err)}`, tipo: 'nuevo'
        }))))
      );

    forkJoin([puts$, import$]).subscribe(([resPuts, resImport]) => {
      const resultados   = [...resPuts, ...resImport];
      const nuevos       = resultados.filter(r => r.ok && r.tipo === 'nuevo').map(r => r.nombre);
      const actualizados = resultados.filter(r => r.ok && r.tipo === 'actualizado').map(r => r.nombre);
      const errores      = resultados.filter(r => !r.ok).map(r => r.nombre);

      // Refetch completo: reemplaza los IDs temporales locales por los reales de la BD
      // y deja el estado (y la pertenencia a BD) consistente con lo persistido.
      this.catalogo.cargarTodo().subscribe({
        next: () => this.saving.set(false),
        error: () => {
          this.saving.set(false);
          this.snackBar.open('Guardado procesado, pero falló la recarga desde la BD.', 'Cerrar', { duration: 4000 });
        }
      });

      this.dialog.open(GuardadoResultadoDialogComponent, {
        width: '420px',
        data: { entidad: 'asignaturas', nuevos, actualizados, errores }
      });
    });
  }

  private construirPayloadImport(asignaturas: Asignatura[]) {
    return {
      facultades: this.state.facultades().map(f => ({ id: f.id, nombre: f.nombre })),
      programas: this.state.programas().map(p => ({ id: p.id, nombre: p.nombre, facultadId: p.facultadId })),
      docentes: this.state.docentes().map(d => ({ id: d.id, nombre: d.nombre, cedula: d.cedula, maxHoras: d.maxHoras })),
      asignaturas: asignaturas.map(a => ({
        id: a.id, nombre: a.nombre, codigo: a.codigo,
        horasPorSesion: a.horasPorSesion, sesionesPorSemana: a.sesionesPorSemana,
        sesionesLaboratorioSemestre: a.sesionesLaboratorioSemestre,
        alternancia: a.alternancia, categoria: a.categoria ?? null,
        programaId: a.programaId, grupoNumero: a.grupoNumero,
        docenteId: a.docenteId ?? null
      }))
    };
  }


  cargarDesdeBD() {
    this.saving.set(true);
    this.catalogo.cargarTodo().subscribe({
      next: (resumen) => {
        this.saving.set(false);
        this.snackBar.open(
          `${resumen.asignaturas} asignatura(s) · ${resumen.docentes} docente(s) cargados desde la BD.`,
          'Cerrar', { duration: 4000 }
        );
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Error al cargar desde la BD.', 'Cerrar', { duration: 4000 });
      }
    });
  }
}

// ─── Dialog de creación/edición manual ───────────────────────────────────────

@Component({
  selector: 'app-asignatura-dialog',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, MatDialogModule, MatFormFieldModule,
    MatInputModule, MatSelectModule, MatButtonModule, MatCheckboxModule, MatIconModule
  ],
  template: `
    <h2 mat-dialog-title>{{ data ? 'Editar Asignatura' : 'Nueva Asignatura' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="dialog-form">

        <!-- Facultad & Programa -->
        <div class="form-row">
          <mat-form-field appearance="outline">
            <mat-label>Facultad</mat-label>
            <mat-select formControlName="facultadId" required (selectionChange)="onFacultadChange($event.value)">
              <mat-option *ngFor="let f of facultades" [value]="f.id">{{ f.nombre }}</mat-option>
              <mat-option value="__nueva__">+ Nueva facultad...</mat-option>
            </mat-select>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Programa</mat-label>
            <mat-select formControlName="programaId" required>
              <mat-option *ngFor="let p of programasFiltrados" [value]="p.id">{{ p.nombre }}</mat-option>
              <mat-option value="__nuevo__">+ Nuevo programa...</mat-option>
            </mat-select>
          </mat-form-field>
        </div>

        <!-- Nueva facultad inline -->
        <mat-form-field appearance="outline" *ngIf="form.get('facultadId')?.value === '__nueva__'">
          <mat-label>Nombre de la nueva facultad</mat-label>
          <input matInput formControlName="nuevaFacultad" required>
        </mat-form-field>

        <!-- Nuevo programa inline -->
        <mat-form-field appearance="outline" *ngIf="form.get('programaId')?.value === '__nuevo__'">
          <mat-label>Nombre del nuevo programa</mat-label>
          <input matInput formControlName="nuevoPrograma" required>
        </mat-form-field>

        <!-- Datos de la asignatura -->
        <div class="form-row">
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
            <mat-select formControlName="categoria">
              <mat-option value="Obligatoria">Obligatoria</mat-option>
              <mat-option value="Optativa">Optativa</mat-option>
              <mat-option value="Electiva">Electiva</mat-option>
            </mat-select>
          </mat-form-field>
        </div>

        <div class="form-row">
          <mat-form-field appearance="outline">
            <mat-label>Horas por sesión</mat-label>
            <mat-select formControlName="horasPorSesion" required>
              <mat-option [value]="2">2 horas</mat-option>
              <mat-option [value]="3">3 horas</mat-option>
            </mat-select>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Sesiones por semana</mat-label>
            <input matInput type="number" formControlName="sesionesPorSemana" required min="1">
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Sesiones lab/semestre</mat-label>
            <input matInput type="number" formControlName="sesionesLaboratorioSemestre" required min="0">
            <mat-hint>8=TipoA · &gt;8=TipoB · 0=SinAlternancia</mat-hint>
          </mat-form-field>
        </div>

        <mat-form-field appearance="outline">
          <mat-label>Docente asignado (opcional)</mat-label>
          <mat-select formControlName="docenteId">
            <mat-option value="">Sin asignar</mat-option>
            <mat-option *ngFor="let doc of docentes" [value]="doc.id">{{ doc.nombre }}</mat-option>
          </mat-select>
        </mat-form-field>

        <mat-checkbox formControlName="reqEspacio" (change)="toggleEspacio($event.checked)">
          ¿Requiere espacio fijo?
        </mat-checkbox>
        <mat-form-field appearance="outline" *ngIf="form.get('reqEspacio')?.value">
          <mat-label>Espacio fijo</mat-label>
          <mat-select formControlName="espacioFijoId">
            <mat-option *ngFor="let esp of espacios" [value]="esp.id">{{ esp.nombre }}</mat-option>
          </mat-select>
        </mat-form-field>

      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancelar</button>
      <button mat-flat-button color="primary" class="primary-button" [disabled]="!canSave()" (click)="save()">Guardar</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .dialog-form { display: flex; flex-direction: column; gap: 16px; margin-top: 16px; min-width: 480px; }
    .form-row { display: flex; gap: 16px; }
    .form-row > * { flex: 1; }
  `]
})
export class AsignaturaDialogComponent {
  fb = inject(FormBuilder);
  dialogRef = inject(MatDialogRef<AsignaturaDialogComponent>);
  data = inject(MAT_DIALOG_DATA) as Asignatura | undefined;
  state = inject(StateService);

  facultades = this.state.facultades();
  docentes = this.state.docentes();
  espacios = this.state.espacios();
  programasFiltrados: Programa[] = [];

  private currentFacultadId: string = '';

  form = this.fb.group({
    facultadId:    ['', Validators.required],
    programaId:    ['', Validators.required],
    nuevaFacultad: [''],
    nuevoPrograma: [''],
    codigo:        [this.data?.codigo ?? '', Validators.required],
    nombre:        [this.data?.nombre ?? '', Validators.required],
    categoria:     [this.data?.categoria ?? ''],
    horasPorSesion:[this.data?.horasPorSesion ?? 2, Validators.required],
    sesionesPorSemana: [this.data?.sesionesPorSemana ?? 2, [Validators.required, Validators.min(1)]],
    sesionesLaboratorioSemestre: [this.data?.sesionesLaboratorioSemestre ?? 0, [Validators.required, Validators.min(0)]],
    docenteId:     [this.data?.docenteId ?? ''],
    reqEspacio:    [!!this.data?.espacioFijoId],
    espacioFijoId: [this.data?.espacioFijoId ?? '']
  });

  constructor() {
    if (this.data?.programaId) {
      const prog = this.state.getProgramaById(this.data.programaId);
      if (prog) {
        this.currentFacultadId = prog.facultadId;
        this.form.patchValue({ facultadId: prog.facultadId, programaId: prog.id });
        this.programasFiltrados = this.state.getProgramasByFacultad(prog.facultadId);
      }
    }
  }

  onFacultadChange(facultadId: string) {
    if (facultadId === '__nueva__') {
      this.programasFiltrados = [];
      this.form.patchValue({ programaId: '' });
    } else {
      this.programasFiltrados = this.state.getProgramasByFacultad(facultadId);
      this.form.patchValue({ programaId: '' });
    }
  }

  toggleEspacio(checked: boolean) {
    if (!checked) this.form.patchValue({ espacioFijoId: '' });
  }

  canSave(): boolean {
    const v = this.form.value;
    if (!v.nombre || !v.codigo) return false;
    if (!v.facultadId) return false;
    if (v.facultadId === '__nueva__' && !v.nuevaFacultad) return false;
    if (!v.programaId) return false;
    if (v.programaId === '__nuevo__' && !v.nuevoPrograma) return false;
    return true;
  }

  save() {
    if (!this.canSave()) return;
    const v = this.form.value;

    let facultadId = v.facultadId!;
    if (facultadId === '__nueva__') {
      const fac: Facultad = { id: crypto.randomUUID(), nombre: v.nuevaFacultad! };
      this.state.addFacultad(fac);
      facultadId = fac.id;
    }

    let programaId = v.programaId!;
    if (programaId === '__nuevo__') {
      const prog: Programa = { id: crypto.randomUUID(), nombre: v.nuevoPrograma!, facultadId };
      this.state.addPrograma(prog);
      programaId = prog.id;
    }

    const sesionesLab = Number(v.sesionesLaboratorioSemestre) || 0;
    const alternancia: 'TipoA' | 'TipoB' | 'SinAlternancia' =
      sesionesLab === 8 ? 'TipoA' : sesionesLab > 8 ? 'TipoB' : 'SinAlternancia';

    const result: Partial<Asignatura> = {
      codigo: v.codigo!,
      nombre: v.nombre!,
      categoria: (v.categoria as 'Obligatoria' | 'Optativa' | 'Electiva') || undefined,
      horasPorSesion: Number(v.horasPorSesion) || 2,
      sesionesPorSemana: Number(v.sesionesPorSemana) || 2,
      sesionesLaboratorioSemestre: sesionesLab,
      alternancia,
      programaId,
      docenteId: v.docenteId || undefined,
      espacioFijoId: v.reqEspacio ? (v.espacioFijoId || undefined) : undefined
    };

    this.dialogRef.close(result);
  }
}
