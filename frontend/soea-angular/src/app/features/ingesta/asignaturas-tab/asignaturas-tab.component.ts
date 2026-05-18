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
import { PersistenciaService } from '../../../core/persistencia.service';
import { Asignatura, Facultad, Programa } from '../../../core/models';
import { MatSnackBar } from '@angular/material/snack-bar';
import * as XLSX from 'xlsx';
import { forkJoin } from 'rxjs';

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
  persistencia = inject(PersistenciaService);

  displayedColumns = ['facultad', 'programa', 'codigo', 'nombre', 'alternancia', 'horas', 'sesiones', 'docente', 'acciones'];
  saving = signal(false);
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

        if (rows.length < 2) {
          this.snackBar.open('El archivo Excel no tiene datos.', 'Cerrar', { duration: 4000 });
          return;
        }

        // ── Columnas fijas (igual que LectorExcel.cs) ──────────────────────
        // A(0)=FACULTAD  B(1)=PROGRAMA  C(2)=ASIGNATURA  D(3)=CODIGO
        // E(4)=TIPO_ESPACIO  F(5)=ESPACIO  G(6)=DURACION[h]
        // Modo 1 (horario ya armado): H(7)=DIA  I(8)=HORA  J(9)=DOCENTE
        // Modo 2 (solo asignaturas):  H(7)=DOCENTE
        // ──────────────────────────────────────────────────────────────────
        const PREFIJOS_DIA = ['lun','mar','mie','jue','vie','sab','mon','tue','wed','thu','fri','sat'];
        const esDia = (v: any) => {
          const t = String(v ?? '').trim().toLowerCase();
          return PREFIJOS_DIA.some(p => t.startsWith(p));
        };
        const esModo1 = esDia(rows[1]?.[7]);

        const str = (row: any[], i: number) => String(row[i] ?? '').trim();
        const num = (row: any[], i: number, def = 2) => { const n = Number(row[i]); return n > 0 ? Math.round(n) : def; };

        const nuevasFacultades: Facultad[] = [...this.state.facultades()];
        const nuevosProgamas:   Programa[] = [...this.state.programas()];
        const nuevasAsignaturas: Asignatura[] = [...this.state.asignaturas()];
        const nuevosDocentes: any[] = [...this.state.docentes()];
        const nuevosEspacios: any[] = [...this.state.espacios()];

        const upsert = <T extends {id:string}>(list: T[], match: (x:T)=>boolean, build: ()=>T): string => {
          const found = list.find(match);
          if (found) return found.id;
          const item = build(); list.push(item); return item.id;
        };

        let added = 0, skipped = 0;
        const skippedDetails: { fila: number; razon: string; row: any }[] = [];

        for (let i = 1; i < rows.length; i++) {
          const row = rows[i];
          const nombreFac  = str(row, 0);
          const nombreProg = str(row, 1);
          const nombreAsig = str(row, 2);

          // Saltar filas sin datos esenciales (mismo criterio que el backend)
          if (!nombreFac || !nombreProg || !nombreAsig) { skipped++; skippedDetails.push({ fila: i + 1, razon: 'Facultad/Programa/Asignatura vacío', row }); continue; }

          const codigo      = str(row, 3) || `IMP-${i}`;
          const tipoEspStr  = str(row, 4);
          const nombreEsp   = str(row, 5);
          const duracion    = num(row, 6, 2);
          // En Modo 1 el docente está en col J(9); en Modo 2 en col H(7)
          const nombreDoc   = esModo1 ? str(row, 9) : str(row, 7);

          // 1. Facultad
          const facultadId = upsert(nuevasFacultades,
            f => f.nombre.toLowerCase() === nombreFac.toLowerCase(),
            () => ({ id: crypto.randomUUID(), nombre: nombreFac }));

          // 2. Programa (clave compuesta facultad|programa)
          const programaId = upsert(nuevosProgamas,
            p => p.nombre.toLowerCase() === nombreProg.toLowerCase() && p.facultadId === facultadId,
            () => ({ id: crypto.randomUUID(), nombre: nombreProg, facultadId }));

          // 3. Docente (creacion basica) — must come before asignatura so we can link its ID
          let docenteIdParaAsig: string | undefined;
          if (nombreDoc) {
            docenteIdParaAsig = upsert(nuevosDocentes,
              d => d.nombre.toLowerCase() === nombreDoc.toLowerCase(),
              () => ({ id: crypto.randomUUID(), nombre: nombreDoc, cedula: '', maxHoras: 40, disponibilidad: {} }));
          }

          // 4. Asignatura (deduplicada por nombre+programa, igual que el backend)
          const existeEnPrograma = nuevasAsignaturas.some(
            a => a.nombre.toLowerCase() === nombreAsig.toLowerCase() && a.programaId === programaId
          );
          if (!existeEnPrograma) {
            // Replica DeterminarAlternancia del backend:
            // Solo 'Quimica General' es TipoA; el resto SinAlternancia
            const normalized = nombreAsig.trim().toLowerCase()
              .normalize('NFD').replace(/[\u0300-\u036f]/g, '');
            const alternancia: 'TipoA'|'TipoB'|'SinAlternancia' =
              normalized === 'quimica general' ? 'TipoA' : 'SinAlternancia';
            nuevasAsignaturas.push({
              id: crypto.randomUUID(), codigo, nombre: nombreAsig,
              alternancia, horasPorSesion: duracion,
              sesionesPorSemana: 2, sesionesLaboratorioSemestre: 0, programaId,
              docenteId: docenteIdParaAsig
            });
            added++;
          } else { skipped++; skippedDetails.push({ fila: i + 1, razon: 'Asignatura duplicada en mismo programa', row }); }

          // 5. Espacio (creacion basica)
          if (nombreEsp) {
            upsert(nuevosEspacios,
              e => e.nombre.toLowerCase() === nombreEsp.toLowerCase(),
              () => ({
                id: crypto.randomUUID(), nombre: nombreEsp,
                tipo: tipoEspStr.toLowerCase().includes('laboratorio') ? 'Laboratorio' : 'Sal\u00f3n',
                capacidad: 30
              }));
          }
        }

        this.state.facultades.set(nuevasFacultades);
        this.state.programas.set(nuevosProgamas);
        this.state.setAsignaturas(nuevasAsignaturas);
        this.state.docentes.set(nuevosDocentes);
        this.state.espacios.set(nuevosEspacios);

        const modo = esModo1 ? 'Modo 1 (horario existente)' : 'Modo 2 (asignaturas)';
        this.snackBar.open(
          `${modo}: ${added} asignaturas importadas, ${skipped} omitidas. ` +
          `Docentes: ${nuevosDocentes.length} · Espacios: ${nuevosEspacios.length} — Ver consola para detalles.`,
          'Cerrar', { duration: 8000 }
        );

        if (skippedDetails.length > 0) {
          console.group('Import: filas omitidas');
          console.table(skippedDetails.slice(0, 200));
          console.groupEnd();
        }
      } catch (err) {
        console.error(err);
        this.snackBar.open('Error al leer el archivo Excel. Verifica el formato.', 'Cerrar', { duration: 5000 });
      }
    };
    reader.readAsArrayBuffer(file);
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

  guardarEnBD() {
    const asignaturas = this.state.asignaturas();
    if (asignaturas.length === 0) {
      this.snackBar.open('No hay asignaturas para guardar.', 'Cerrar', { duration: 3000 });
      return;
    }
    this.saving.set(true);
    const calls = asignaturas.map(a => this.persistencia.guardarAsignatura(a));
    forkJoin(calls).subscribe({
      next: () => {
        this.saving.set(false);
        this.snackBar.open(`${asignaturas.length} asignatura(s) guardadas en la BD.`, 'Cerrar', { duration: 4000 });
      },
      error: (err) => {
        this.saving.set(false);
        this.snackBar.open(`Error al guardar: ${err?.error || err?.message || 'desconocido'}`, 'Cerrar', { duration: 5000 });
      }
    });
  }

  cargarDesdeBD() {
    this.saving.set(true);
    this.persistencia.cargarAsignaturas().subscribe({
      next: (list) => {
        this.saving.set(false);
        list.forEach((a: any) => {
          const mapped: Asignatura = {
            id: a.id,
            nombre: a.nombre,
            codigo: a.codigo,
            horasPorSesion: a.horasPorSesion,
            sesionesPorSemana: a.sesionesPorSemana,
            sesionesLaboratorioSemestre: a.sesionesLaboratorioSemestre,
            alternancia: a.alternancia ?? 'SinAlternancia',
            programaId: a.programaId
          };
          const existing = this.state.asignaturas().find(x => x.id === a.id);
          if (existing) this.state.updateAsignatura(mapped);
          else this.state.addAsignatura(mapped);
        });
        this.snackBar.open(`${list.length} asignatura(s) cargadas desde la BD.`, 'Cerrar', { duration: 4000 });
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
