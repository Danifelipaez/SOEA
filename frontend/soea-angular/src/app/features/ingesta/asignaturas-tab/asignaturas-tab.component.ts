import { Component, inject, computed, signal } from '@angular/core';
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
import { StateService } from '../../../core/state.service';
import { Asignatura, Facultad, Programa } from '../../../core/models';
import { MatSnackBar } from '@angular/material/snack-bar';
import * as XLSX from 'xlsx';

// ─── Columnas del Excel esperadas (modo asignaturas separadas) ──────────────────
// Facultad | Programa | Código | Nombre | HorasPorSesion | SesionesPorSemana | SesionesLabSemestre
// ───────────────────────────────────────────────────────────────────────────────

@Component({
  selector: 'app-asignaturas-tab',
  standalone: true,
  imports: [
    CommonModule, MatTableModule, MatButtonModule, MatDialogModule,
    MatFormFieldModule, MatInputModule, MatSelectModule, MatIconModule,
    MatTooltipModule, MatChipsModule
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
          <button mat-stroked-button (click)="importarExcel()" matTooltip="Importar desde Excel (.xlsx)">
            <mat-icon>upload_file</mat-icon> Importar Excel
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
    .badge-sin { background: #f3e5f5; color: #6a1b9a; }
    .empty-hint { display: flex; flex-direction: column; align-items: center; padding: 32px; color: #757575; }
    .empty-hint mat-icon { font-size: 48px; width: 48px; height: 48px; margin-bottom: 8px; }
  `]
})
export class AsignaturasTabComponent {
  state = inject(StateService);
  dialog = inject(MatDialog);
  snackBar = inject(MatSnackBar);

  displayedColumns = ['facultad', 'programa', 'codigo', 'nombre', 'alternancia', 'horas', 'sesiones', 'docente', 'acciones'];
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

  // ── Importar Excel ────────────────────────────────────────────────────────────
  importarExcel() {
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    input?.click();
  }

  onFileSelected(event: Event) {
    // Formato por posicion de columna (igual que LectorExcel.cs):
    // A(0)=FACULTAD  B(1)=PROGRAMA  C(2)=ASIGNATURA  D(3)=CODIGO
    // E(4)=TIPO_ESPACIO  F(5)=ESPACIO  G(6)=DURACION[h]
    // Modo 1 (horario existente): H(7)=DIA  I(8)=HORA  J(9)=DOCENTE
    // Modo 2 (solo asignaturas):  H(7)=DOCENTE
    // Auto-deteccion: si col H de la primera fila de datos es un dia, es Modo 1.
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = (e) => {
      try {
        const data = new Uint8Array(e.target!.result as ArrayBuffer);
        const wb = XLSX.read(data, { type: 'array' });
        const ws = wb.Sheets[wb.SheetNames[0]];
        const rows: any[][] = XLSX.utils.sheet_to_json(ws, { header: 1, defval: '' });

        // Encabezados esperados (fila 0): Facultad | Programa | Código | Nombre | HorasPorSesion | SesionesPorSemana | SesionesLabSemestre
        const headerRow = rows[0]?.map((h: any) => String(h).trim().toLowerCase()) ?? [];
        const col = (name: string) => headerRow.findIndex((h: string) => h.includes(name));

        const iFacultad  = col('facultad');
        const iPrograma  = col('programa');
        const iCodigo    = col('c\u00f3digo') !== -1 ? col('c\u00f3digo') : col('codigo');
        const iNombre    = col('nombre');
        const iHoras     = col('horas');
        const iSesiones  = col('sesiones/sem') !== -1 ? col('sesiones/sem') : col('sesionesp');
        const iLab       = col('lab');

        if (iFacultad < 0 || iPrograma < 0 || iNombre < 0) {
          this.snackBar.open('El Excel no tiene las columnas esperadas (Facultad, Programa, Nombre)', 'Cerrar', { duration: 5000 });
          return;
        }

        // Índices de columnnas por nombre flexible
        const getCol = (r: any[], idx: number) => idx >= 0 ? String(r[idx] ?? '').trim() : '';
        const getNum = (r: any[], idx: number, def = 2) => idx >= 0 ? (Number(r[idx]) || def) : def;

        const nuevasFacultades: Facultad[] = [...this.state.facultades()];
        const nuevosProgamas: Programa[] = [...this.state.programas()];
        const nuevasAsignaturas: Asignatura[] = [...this.state.asignaturas()];

        const getOrCreateFacultad = (nombre: string): string => {
          const existing = nuevasFacultades.find(f => f.nombre.toLowerCase() === nombre.toLowerCase());
          if (existing) return existing.id;
          const id = crypto.randomUUID();
          nuevasFacultades.push({ id, nombre });
          return id;
        };

        const getOrCreatePrograma = (nombre: string, facultadId: string): string => {
          const existing = nuevosProgamas.find(p =>
            p.nombre.toLowerCase() === nombre.toLowerCase() && p.facultadId === facultadId
          );
          if (existing) return existing.id;
          const id = crypto.randomUUID();
          nuevosProgamas.push({ id, nombre, facultadId });
          return id;
        };

        let added = 0;
        let skipped = 0;

        for (let i = 1; i < rows.length; i++) {
          const row = rows[i];
          const nombreFac = getCol(row, iFacultad);
          const nombreProg = getCol(row, iPrograma);
          const codigo = getCol(row, iCodigo) || `IMPORT-${i}`;
          const nombre = getCol(row, iNombre);
          if (!nombre) { skipped++; continue; }

          const horasPorSesion = getNum(row, iHoras, 2);
          const sesionesPorSemana = getNum(row, iSesiones, 2);
          const sesionesLab = getNum(row, iLab, 0);

          const facultadId = getOrCreateFacultad(nombreFac || 'Sin Facultad');
          const programaId = getOrCreatePrograma(nombreProg || 'Sin Programa', facultadId);

          // Una asignatura puede repetirse en distintos programas (business rule)
          // El ID único es (codigo, programaId)
          const existeEnPrograma = nuevasAsignaturas.some(
            a => a.codigo === codigo && a.programaId === programaId
          );

          if (!existeEnPrograma) {
            const alternancia = sesionesLab === 8 ? 'TipoA' : sesionesLab > 8 ? 'TipoB' : 'SinAlternancia';
            nuevasAsignaturas.push({
              id: crypto.randomUUID(),
              codigo,
              nombre,
              alternancia: alternancia as any,
              horasPorSesion,
              sesionesPorSemana,
              sesionesLaboratorioSemestre: sesionesLab,
              programaId
            });
            added++;
          } else {
            skipped++;
          }
        }

        this.state.facultades.set(nuevasFacultades);
        this.state.programas.set(nuevosProgamas);
        this.state.setAsignaturas(nuevasAsignaturas);
        this.snackBar.open(`Importación completada: ${added} asignaturas agregadas, ${skipped} omitidas.`, 'Cerrar', { duration: 5000 });
      } catch (err) {
        console.error(err);
        this.snackBar.open('Error al leer el archivo Excel.', 'Cerrar', { duration: 5000 });
      }
    };
    reader.readAsArrayBuffer(file);
    // Reset para permitir reimportar el mismo archivo
    (event.target as HTMLInputElement).value = '';
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
    this.state.deleteAsignatura(asignatura.id);
    this.snackBar.open('Asignatura eliminada', 'Cerrar', { duration: 3000 });
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

  // Pre-select facultad from existing data
  private currentFacultadId: string = '';

  form = this.fb.group({
    facultadId:    ['', Validators.required],
    programaId:    ['', Validators.required],
    nuevaFacultad: [''],
    nuevoPrograma: [''],
    codigo:        [this.data?.codigo ?? '', Validators.required],
    nombre:        [this.data?.nombre ?? '', Validators.required],
    horasPorSesion:[this.data?.horasPorSesion ?? 2, Validators.required],
    sesionesPorSemana: [this.data?.sesionesPorSemana ?? 2, [Validators.required, Validators.min(1)]],
    sesionesLaboratorioSemestre: [this.data?.sesionesLaboratorioSemestre ?? 0, [Validators.required, Validators.min(0)]],
    docenteId:     [this.data?.docenteId ?? ''],
    reqEspacio:    [!!this.data?.espacioFijoId],
    espacioFijoId: [this.data?.espacioFijoId ?? '']
  });

  constructor() {
    // If editing, restore facultad selection
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

    // Resolve or create facultad
    let facultadId = v.facultadId!;
    if (facultadId === '__nueva__') {
      const fac: Facultad = { id: crypto.randomUUID(), nombre: v.nuevaFacultad! };
      this.state.addFacultad(fac);
      facultadId = fac.id;
    }

    // Resolve or create programa
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
