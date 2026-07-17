import { Component, inject, computed, signal } from '@angular/core';
import { forkJoin, of, Observable } from 'rxjs';
import { map, catchError } from 'rxjs/operators';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatDialogModule, MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { StateService } from '../../../core/state.service';
import { PersistenciaService } from '../../../core/persistencia.service';
import { CatalogoService } from '../../../core/catalogo.service';
import { mensajeErrorHttp } from '../../../core/http-error.util';
import { GuardadoResultadoDialogComponent } from '../../../shared/guardado-resultado-dialog/guardado-resultado-dialog.component';
import { ConfirmDeleteDialogComponent } from '../../../shared/confirm-delete-dialog/confirm-delete-dialog.component';
import { ImportResultadoDialogComponent } from '../../../shared/import-resultado-dialog/import-resultado-dialog.component';
import { Asignatura, Facultad, Programa } from '../../../core/models';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ImportExcelStatsDto } from '../../../core/persistencia.service';

@Component({
  selector: 'app-asignaturas-tab',
  standalone: true,
  imports: [CommonModule, MatDialogModule],
  template: `
    <div class="tab-content">
      <div class="toolbar">
        <div class="filters">
          <input class="input search" placeholder="🔍 Buscar asignatura…" (input)="filterStr.set($any($event.target).value)">
          <span class="text-muted count">{{ filtered().length }} asignaturas</span>
        </div>
        <div class="actions">
          <button class="btn btn-secondary" (click)="fileInput.click()" [disabled]="uploading()">
            {{ uploading() ? 'Subiendo…' : '⬆ Importar Excel' }}
          </button>
          <button class="btn btn-secondary" (click)="cargarDesdeBD()" [disabled]="saving()">Cargar BD</button>
          <button class="btn btn-secondary" (click)="guardarEnBD()" [disabled]="saving()">{{ saving() ? 'Guardando…' : 'Guardar en BD' }}</button>
          <button class="btn btn-primary" (click)="openDialog()">＋ Nueva asignatura</button>
        </div>
      </div>
      <input type="file" #fileInput accept=".xlsx,.xls" (change)="onFileSelected($event)" style="display:none">

      <table class="table">
        <thead><tr>
          <th style="width:24%">Asignatura</th><th>Código</th><th>Ses/sem</th><th>Programa</th><th>Docente</th><th style="width:110px">Alternancia</th><th style="width:60px"></th>
        </tr></thead>
        <tbody>
          @for (a of filtered(); track a.id) {
            <tr>
              <td><b>{{ a.nombre }}</b> @if (a.categoria) { <span class="tag tag-neutral" style="font-size:9px">{{ a.categoria }}</span> }</td>
              <td class="text-muted">{{ a.codigo }}</td>
              <td class="text-muted">{{ resumenSesiones(a) }}</td>
              <td>{{ programaNombre(a.programaId) }}</td>
              <td class="text-muted">{{ docenteNombre(a.docenteId) }}</td>
              <td><span class="dpill" [ngClass]="altPill(a.alternancia)"><span class="stat" [style.background]="altColor(a.alternancia)"></span>{{ altLabel(a.alternancia) }}</span></td>
              <td>
                <span class="icell" (click)="openDialog(a)" title="Editar">✎</span>
                <span class="icell del" (click)="delete(a)" title="Eliminar">🗑</span>
              </td>
            </tr>
          }
          @if (filtered().length === 0) {
            <tr><td colspan="7" class="empty">No hay asignaturas. Usa "⬆ Importar Excel" o "＋ Nueva asignatura".</td></tr>
          }
        </tbody>
      </table>
    </div>
  `,
  styles: [`
    .tab-content { padding: 20px 0; display: flex; flex-direction: column; gap: 14px; }
    .toolbar { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    .filters { display: flex; align-items: center; gap: 10px; }
    .search { width: 240px; }
    .count { font-size: 12.5px; }
    .actions { display: flex; gap: 8px; flex-wrap: wrap; }
    .icell { padding: 0 5px; font-size: 15px; }
    .icell.del:hover { color: var(--err-bd); }
    .empty { text-align: center; color: var(--color-neutral-500); padding: 28px; }
  `]
})
export class AsignaturasTabComponent {
  state = inject(StateService);
  dialog = inject(MatDialog);
  snackBar = inject(MatSnackBar);
  persistencia = inject(PersistenciaService);
  catalogo = inject(CatalogoService);

  saving = signal(false);
  uploading = signal(false);
  filterStr = signal('');

  filtered = computed(() => {
    const f = this.filterStr().toLowerCase();
    return this.state.asignaturas().filter(a =>
      !f || a.nombre.toLowerCase().includes(f) || a.codigo.toLowerCase().includes(f) ||
      this.programaNombre(a.programaId).toLowerCase().includes(f)
    );
  });

  programaNombre(programaId: string): string { return this.state.getProgramaById(programaId)?.nombre ?? '—'; }
  docenteNombre(id?: string): string { return id ? (this.state.docentes().find(x => x.id === id)?.nombre ?? 'Sin asignar') : 'Sin asignar'; }

  altColor(alt: string): string { return alt === 'TipoA' ? 'var(--alt-a)' : alt === 'TipoB' ? 'var(--alt-b)' : 'var(--alt-sin)'; }
  altLabel(alt: string): string { return alt === 'SinAlternancia' ? 'SinAlt' : alt; }
  altPill(alt: string): string { return alt === 'TipoA' ? 'a' : alt === 'TipoB' ? 'b' : 'sin'; }

  resumenSesiones(a: Asignatura): string {
    const p: string[] = [];
    if (a.sesionesTeoriaPresencialSemana > 0) p.push(`${a.sesionesTeoriaPresencialSemana}×${a.horasTeoriaPresencial}h pres.`);
    if (a.sesionesTeoriaVirtualSemana > 0) p.push(`${a.sesionesTeoriaVirtualSemana}×${a.horasTeoriaVirtual}h virt.`);
    if (a.sesionesLaboratorioSemana > 0) p.push(`${a.sesionesLaboratorioSemana}×${a.horasLaboratorio}h lab`);
    return p.join(' · ') || '—';
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
        this.dialog.open(ImportResultadoDialogComponent, { width: '340px', data: stats });
      },
      error: (err) => {
        this.uploading.set(false);
        const msg = err?.error?.detail ?? err?.error ?? err?.message ?? 'Error desconocido';
        this.snackBar.open(`Error al importar: ${msg}`, 'Cerrar', { duration: 5000 });
      }
    });
  }

  openDialog(asignatura?: Asignatura) {
    const dialogRef = this.dialog.open(AsignaturaDialogComponent, { width: '540px', maxWidth: '95vw', data: asignatura });
    dialogRef.afterClosed().subscribe(result => {
      if (!result) return;
      if (asignatura) { this.state.updateAsignatura({ ...asignatura, ...result }); this.snackBar.open('Asignatura actualizada', '', { duration: 2500 }); }
      else { this.state.addAsignatura({ id: crypto.randomUUID(), ...result }); this.snackBar.open('Asignatura agregada', '', { duration: 2500 }); }
    });
  }

  delete(asignatura: Asignatura) {
    const enBd = this.catalogo.estaEnBd('asignatura', asignatura.id);
    const ref = this.dialog.open(ConfirmDeleteDialogComponent, {
      width: '320px',
      data: {
        title: 'Eliminar asignatura',
        message: enBd
          ? `Se eliminará "${asignatura.nombre}" de la base de datos. Esta acción es irreversible.`
          : `Se eliminará "${asignatura.nombre}" (aún no está guardada en la BD).`
      }
    });
    ref.afterClosed().subscribe(confirmado => {
      if (!confirmado) return;
      if (!enBd) { this.state.deleteAsignatura(asignatura.id); this.snackBar.open('Asignatura eliminada localmente.', '', { duration: 2500 }); return; }
      this.persistencia.eliminarAsignatura(asignatura.id).subscribe({
        next: () => {
          this.catalogo.quitarDeBd('asignatura', asignatura.id);
          this.state.deleteAsignatura(asignatura.id);
          this.catalogo.cargarTodo().subscribe();
          this.snackBar.open('Asignatura eliminada de la BD.', '', { duration: 2500 });
        },
        error: (err) => this.snackBar.open(`Error al eliminar: ${mensajeErrorHttp(err)}`, 'Cerrar', { duration: 5000 })
      });
    });
  }

  guardarEnBD() {
    const asignaturas = this.state.asignaturas();
    if (!asignaturas.length) { this.snackBar.open('No hay asignaturas para guardar.', '', { duration: 2500 }); return; }
    this.saving.set(true);
    const existentes = asignaturas.filter(a => this.catalogo.estaEnBd('asignatura', a.id));
    const nuevas = asignaturas.filter(a => !this.catalogo.estaEnBd('asignatura', a.id));
    type Resultado = { ok: boolean; nombre: string; tipo: 'nuevo' | 'actualizado' };

    const puts$: Observable<Resultado[]> = existentes.length === 0 ? of([]) : forkJoin(
      existentes.map(a =>
        this.persistencia.actualizarAsignatura(a).pipe(
          map((): Resultado => ({ ok: true, nombre: a.nombre, tipo: 'actualizado' })),
          catchError(err => of<Resultado>({ ok: false, nombre: `${a.nombre} — ${mensajeErrorHttp(err)}`, tipo: 'actualizado' }))
        )
      )
    );
    const import$: Observable<Resultado[]> = nuevas.length === 0 ? of([]) :
      this.persistencia.importarCurriculum(this.construirPayloadImport(nuevas)).pipe(
        map(() => nuevas.map((a): Resultado => ({ ok: true, nombre: a.nombre, tipo: 'nuevo' }))),
        catchError(err => of(nuevas.map((a): Resultado => ({ ok: false, nombre: `${a.nombre} — ${mensajeErrorHttp(err)}`, tipo: 'nuevo' }))))
      );

    forkJoin([puts$, import$]).subscribe(([resPuts, resImport]) => {
      const resultados = [...resPuts, ...resImport];
      this.catalogo.cargarTodo().subscribe({
        next: () => this.saving.set(false),
        error: () => { this.saving.set(false); this.snackBar.open('Guardado procesado, pero falló la recarga.', 'Cerrar', { duration: 4000 }); }
      });
      this.dialog.open(GuardadoResultadoDialogComponent, {
        width: '340px',
        data: {
          entidad: 'asignaturas',
          nuevos: resultados.filter(r => r.ok && r.tipo === 'nuevo').map(r => r.nombre),
          actualizados: resultados.filter(r => r.ok && r.tipo === 'actualizado').map(r => r.nombre),
          errores: resultados.filter(r => !r.ok).map(r => r.nombre)
        }
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
        // ponytail: /import/curriculum solo entiende 1 bloque de sesiones — colapsa al primer track.
        horasPorSesion: a.horasTeoriaPresencial || a.horasLaboratorio || a.horasTeoriaVirtual || 2,
        sesionesPorSemana: a.sesionesTeoriaPresencialSemana || a.sesionesLaboratorioSemana || a.sesionesTeoriaVirtualSemana || 1,
        sesionesLaboratorioSemestre: a.sesionesLaboratorioSemestre,
        alternancia: a.alternancia, categoria: a.categoria ?? null,
        programaId: a.programaId, grupoNumero: a.grupoNumero, docenteId: a.docenteId ?? null
      }))
    };
  }

  cargarDesdeBD() {
    this.saving.set(true);
    this.catalogo.cargarTodo().subscribe({
      next: (resumen) => { this.saving.set(false); this.snackBar.open(`${resumen.asignaturas} asignatura(s) · ${resumen.docentes} docente(s) cargados.`, '', { duration: 3500 }); },
      error: () => { this.saving.set(false); this.snackBar.open('Error al cargar desde la BD.', 'Cerrar', { duration: 4000 }); }
    });
  }
}

// ─── Popup: Crear/Editar asignatura (REQUISITOS §1.1) ─────────────────────────
@Component({
  selector: 'app-asignatura-dialog',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MatDialogModule],
  template: `
    <div class="pophd">{{ data ? 'Editar asignatura' : 'Nueva asignatura' }} <i (click)="ref.close()">✕</i></div>
    <form class="popbd" [formGroup]="form" style="max-height:74vh;overflow:auto">

      <div style="display:flex;gap:8px">
        <div class="dfield" style="flex:1"><label>Facultad <span class="rq">*</span></label>
          <select class="input" formControlName="facultadId" (change)="onFacultadChange($any($event.target).value)">
            <option value="">—</option>
            @for (f of state.facultades(); track f.id) { <option [value]="f.id">{{ f.nombre }}</option> }
            <option value="__nueva__">+ Nueva facultad…</option>
          </select></div>
        <div class="dfield" style="flex:1"><label>Programa <span class="rq">*</span></label>
          <select class="input" formControlName="programaId">
            <option value="">—</option>
            @for (p of programasFiltrados; track p.id) { <option [value]="p.id">{{ p.nombre }}</option> }
            <option value="__nuevo__">+ Nuevo programa…</option>
          </select></div>
      </div>
      @if (form.get('facultadId')?.value === '__nueva__') {
        <div class="dfield"><label>Nombre de la nueva facultad</label><input class="input" formControlName="nuevaFacultad"></div>
      }
      @if (form.get('programaId')?.value === '__nuevo__') {
        <div class="dfield"><label>Nombre del nuevo programa</label><input class="input" formControlName="nuevoPrograma"></div>
      }

      <div style="display:flex;gap:8px">
        <div class="dfield" style="flex:1"><label>Código <span class="rq">*</span></label><input class="input" formControlName="codigo"></div>
        <div class="dfield" style="flex:1.4"><label>Nombre <span class="rq">*</span></label><input class="input" formControlName="nombre"></div>
        <div class="dfield" style="width:120px"><label>Tipo</label>
          <select class="input" formControlName="categoria">
            <option value="">—</option><option value="Obligatoria">Obligatoria</option><option value="Optativa">Optativa</option><option value="Electiva">Electiva</option>
          </select></div>
      </div>

      <h3 class="sec" style="margin-top:2px">Desglose por tipo de sesión</h3>
      <div class="track"><span class="tlabel">Teoría presencial</span>
        <div class="dfield" style="width:96px"><label>Ses/sem</label><input class="input" type="number" min="0" formControlName="sesionesTeoriaPresencialSemana"></div>
        <div class="dfield" style="width:96px"><label>Horas/ses</label><input class="input" type="number" min="1" formControlName="horasTeoriaPresencial"></div>
      </div>
      <div class="track"><span class="tlabel">Teoría virtual</span>
        <div class="dfield" style="width:96px"><label>Ses/sem</label><input class="input" type="number" min="0" formControlName="sesionesTeoriaVirtualSemana"></div>
        <div class="dfield" style="width:96px"><label>Horas/ses</label><input class="input" type="number" min="1" formControlName="horasTeoriaVirtual"></div>
      </div>
      <div class="track"><span class="tlabel">Laboratorio</span>
        <div class="dfield" style="width:96px"><label>Ses/sem</label><input class="input" type="number" min="0" formControlName="sesionesLaboratorioSemana"></div>
        <div class="dfield" style="width:96px"><label>Horas/ses</label><input class="input" type="number" min="1" formControlName="horasLaboratorio"></div>
        <div class="dfield" style="flex:1"><label>Lab/semestre <span class="rq">*</span></label><input class="input" type="number" min="0" formControlName="sesionesLaboratorioSemestre"></div>
      </div>
      <p class="text-muted" style="font-size:11px;margin:0">8=TipoA · &gt;8=TipoB · 0=SinAlternancia (se infiere automáticamente)</p>

      @if (data) {
        <div class="dfield"><label>Docente asignado (opcional)</label>
          <select class="input" formControlName="docenteId">
            <option value="">Sin asignar</option>
            @for (doc of state.docentes(); track doc.id) { <option [value]="doc.id">{{ doc.nombre }}</option> }
          </select></div>
        <div class="dfield"><label>Espacio fijo (opcional)</label>
          <select class="input" formControlName="espacioFijoId">
            <option value="">Sin espacio fijo</option>
            @for (esp of state.espacios(); track esp.id) { <option [value]="esp.id">{{ esp.nombre }}</option> }
          </select></div>
      }

      <div class="popfoot">
        <button type="button" class="btn btn-secondary" (click)="ref.close()">Cancelar</button>
        <button type="button" class="btn btn-primary" [disabled]="!canSave()" (click)="save()">Guardar</button>
      </div>
    </form>
  `,
  styles: [`
    .track { display: flex; align-items: flex-end; gap: 8px; padding: 8px 10px; border: 1px solid var(--color-divider); }
    .tlabel { flex: 1; font: 600 12px var(--font-heading); align-self: center; }
  `]
})
export class AsignaturaDialogComponent {
  fb = inject(FormBuilder);
  ref = inject(MatDialogRef<AsignaturaDialogComponent>);
  data = inject(MAT_DIALOG_DATA) as Asignatura | undefined;
  state = inject(StateService);

  programasFiltrados: Programa[] = [];

  form = this.fb.group({
    facultadId: ['', Validators.required],
    programaId: ['', Validators.required],
    nuevaFacultad: [''],
    nuevoPrograma: [''],
    codigo: [this.data?.codigo ?? '', Validators.required],
    nombre: [this.data?.nombre ?? '', Validators.required],
    categoria: [this.data?.categoria ?? ''],
    sesionesTeoriaPresencialSemana: [this.data?.sesionesTeoriaPresencialSemana ?? 2, [Validators.required, Validators.min(0)]],
    horasTeoriaPresencial: [this.data?.horasTeoriaPresencial ?? 2, [Validators.required, Validators.min(1)]],
    sesionesTeoriaVirtualSemana: [this.data?.sesionesTeoriaVirtualSemana ?? 0, [Validators.required, Validators.min(0)]],
    horasTeoriaVirtual: [this.data?.horasTeoriaVirtual ?? 2, [Validators.required, Validators.min(1)]],
    sesionesLaboratorioSemana: [this.data?.sesionesLaboratorioSemana ?? 0, [Validators.required, Validators.min(0)]],
    horasLaboratorio: [this.data?.horasLaboratorio ?? 2, [Validators.required, Validators.min(1)]],
    sesionesLaboratorioSemestre: [this.data?.sesionesLaboratorioSemestre ?? 0, [Validators.required, Validators.min(0)]],
    docenteId: [this.data?.docenteId ?? ''],
    espacioFijoId: [this.data?.espacioFijoId ?? '']
  });

  constructor() {
    if (this.data?.programaId) {
      const prog = this.state.getProgramaById(this.data.programaId);
      if (prog) {
        this.form.patchValue({ facultadId: prog.facultadId, programaId: prog.id });
        this.programasFiltrados = this.state.getProgramasByFacultad(prog.facultadId);
      }
    }
  }

  onFacultadChange(facultadId: string) {
    this.programasFiltrados = facultadId === '__nueva__' ? [] : this.state.getProgramasByFacultad(facultadId);
    this.form.patchValue({ programaId: '' });
  }

  canSave(): boolean {
    const v = this.form.value;
    if (!v.nombre || !v.codigo || !v.facultadId) return false;
    if (v.facultadId === '__nueva__' && !v.nuevaFacultad) return false;
    if (!v.programaId) return false;
    if (v.programaId === '__nuevo__' && !v.nuevoPrograma) return false;
    const total = (Number(v.sesionesTeoriaPresencialSemana) || 0) + (Number(v.sesionesTeoriaVirtualSemana) || 0) + (Number(v.sesionesLaboratorioSemana) || 0);
    return total > 0;
  }

  save() {
    if (!this.canSave()) return;
    const v = this.form.value;
    let facultadId = v.facultadId!;
    if (facultadId === '__nueva__') { const fac: Facultad = { id: crypto.randomUUID(), nombre: v.nuevaFacultad! }; this.state.addFacultad(fac); facultadId = fac.id; }
    let programaId = v.programaId!;
    if (programaId === '__nuevo__') { const prog: Programa = { id: crypto.randomUUID(), nombre: v.nuevoPrograma!, facultadId }; this.state.addPrograma(prog); programaId = prog.id; }

    const sesionesLab = Number(v.sesionesLaboratorioSemestre) || 0;
    const alternancia: 'TipoA' | 'TipoB' | 'SinAlternancia' = sesionesLab === 8 ? 'TipoA' : sesionesLab > 8 ? 'TipoB' : 'SinAlternancia';

    this.ref.close({
      codigo: v.codigo!, nombre: v.nombre!,
      categoria: (v.categoria as 'Obligatoria' | 'Optativa' | 'Electiva') || undefined,
      sesionesTeoriaPresencialSemana: Number(v.sesionesTeoriaPresencialSemana) || 0,
      horasTeoriaPresencial: Number(v.horasTeoriaPresencial) || 2,
      sesionesTeoriaVirtualSemana: Number(v.sesionesTeoriaVirtualSemana) || 0,
      horasTeoriaVirtual: Number(v.horasTeoriaVirtual) || 2,
      sesionesLaboratorioSemana: Number(v.sesionesLaboratorioSemana) || 0,
      horasLaboratorio: Number(v.horasLaboratorio) || 2,
      sesionesLaboratorioSemestre: sesionesLab,
      alternancia, programaId,
      docenteId: v.docenteId || undefined,
      espacioFijoId: v.espacioFijoId || undefined
    } as Partial<Asignatura>);
  }
}
