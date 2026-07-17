import { Component, inject, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatDialogModule, MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { StateService } from '../../../core/state.service';
import { PersistenciaService } from '../../../core/persistencia.service';
import { CatalogoService } from '../../../core/catalogo.service';
import { mensajeErrorHttp } from '../../../core/http-error.util';
import { GuardadoResultadoDialogComponent } from '../../../shared/guardado-resultado-dialog/guardado-resultado-dialog.component';
import { ConfirmDeleteDialogComponent } from '../../../shared/confirm-delete-dialog/confirm-delete-dialog.component';
import { Espacio } from '../../../core/models';
import { MatSnackBar } from '@angular/material/snack-bar';
import { forkJoin, of } from 'rxjs';
import { map, catchError } from 'rxjs/operators';

type TipoEspacio = 'Salón' | 'Laboratorio' | 'Auditorio';

@Component({
  selector: 'app-espacios-tab',
  standalone: true,
  imports: [CommonModule, MatDialogModule],
  template: `
    <div class="tab-content">
      <div class="toolbar">
        <div class="filters">
          <span class="text-muted seg-lbl">Tipo</span>
          <div class="seg">
            <label class="seg-opt" [class.on]="tipoFiltro() === 'Todos'" (click)="tipoFiltro.set('Todos')">Todos</label>
            <label class="seg-opt" [class.on]="tipoFiltro() === 'Salón'" (click)="tipoFiltro.set('Salón')">Salón</label>
            <label class="seg-opt" [class.on]="tipoFiltro() === 'Laboratorio'" (click)="tipoFiltro.set('Laboratorio')">Laboratorio</label>
            <label class="seg-opt" [class.on]="tipoFiltro() === 'Auditorio'" (click)="tipoFiltro.set('Auditorio')">Auditorio</label>
          </div>
          <input class="input search" placeholder="🔍 Buscar espacio…" (input)="filterStr.set($any($event.target).value)">
          <span class="text-muted count">{{ filtered().length }} espacios</span>
        </div>
        <div class="actions">
          <button class="btn btn-secondary" (click)="cargarDesdeBD()" [disabled]="saving()">Cargar BD</button>
          <button class="btn btn-secondary" (click)="guardarEnBD()" [disabled]="saving()">{{ saving() ? 'Guardando…' : 'Guardar en BD' }}</button>
          <button class="btn btn-primary" (click)="openDialog()">＋ Nuevo espacio</button>
        </div>
      </div>

      <table class="table">
        <thead><tr>
          <th style="width:34%">Espacio</th><th>Tipo</th><th>Capacidad</th><th>Edificio</th><th style="width:70px"></th>
        </tr></thead>
        <tbody>
          @for (e of filtered(); track e.id) {
            <tr>
              <td>{{ e.nombre }}</td>
              <td><span class="tag" [ngClass]="tagClass(e.tipo)">{{ e.tipo }}</span></td>
              <td>{{ e.capacidad }}</td>
              <td>{{ e.edificio || '—' }}</td>
              <td>
                <span class="icell" (click)="openDialog(e)" title="Editar">✎</span>
                <span class="icell del" (click)="delete(e)" title="Eliminar">🗑</span>
              </td>
            </tr>
          }
          @if (filtered().length === 0) {
            <tr><td colspan="5" class="empty">Sin espacios. Usa "＋ Nuevo espacio" o importa un Excel.</td></tr>
          }
        </tbody>
      </table>
    </div>
  `,
  styles: [`
    .tab-content { padding: 20px 0; display: flex; flex-direction: column; gap: 14px; }
    .toolbar { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    .filters { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
    .seg-lbl { font-size: 12px; letter-spacing: .05em; text-transform: uppercase; }
    .search { width: 220px; }
    .count { font-size: 12.5px; }
    .actions { display: flex; gap: 8px; }
    .icell { padding: 0 5px; font-size: 15px; }
    .icell.del:hover { color: var(--err-bd); }
    .empty { text-align: center; color: var(--color-neutral-500); padding: 28px; }
  `]
})
export class EspaciosTabComponent {
  state = inject(StateService);
  dialog = inject(MatDialog);
  snackBar = inject(MatSnackBar);
  persistencia = inject(PersistenciaService);
  catalogo = inject(CatalogoService);

  filterStr = signal('');
  tipoFiltro = signal<'Todos' | TipoEspacio>('Todos');
  saving = signal(false);

  filtered = computed(() => {
    const f = this.filterStr().toLowerCase();
    const tipo = this.tipoFiltro();
    return this.state.espacios().filter(e =>
      (tipo === 'Todos' || e.tipo === tipo) &&
      (!f || e.nombre.toLowerCase().includes(f) || e.tipo.toLowerCase().includes(f) || (e.edificio ?? '').toLowerCase().includes(f))
    );
  });

  tagClass(tipo: string): string {
    if (tipo === 'Laboratorio') return 'tag-accent';
    if (tipo === 'Auditorio') return 'tag-accent-2';
    return 'tag-neutral';
  }

  openDialog(espacio?: Espacio) {
    const dialogRef = this.dialog.open(EspacioDialogComponent, { width: '320px', data: espacio });
    dialogRef.afterClosed().subscribe(result => {
      if (!result) return;
      if (espacio) {
        this.state.updateEspacio({ ...espacio, ...result });
        this.snackBar.open('Espacio actualizado', '', { duration: 2500 });
      } else {
        this.state.addEspacio({ id: crypto.randomUUID(), ...result });
        this.snackBar.open('Espacio agregado', '', { duration: 2500 });
      }
    });
  }

  delete(espacio: Espacio) {
    const enBd = this.catalogo.estaEnBd('espacio', espacio.id);
    const ref = this.dialog.open(ConfirmDeleteDialogComponent, {
      width: '320px',
      data: {
        title: 'Eliminar espacio',
        message: enBd
          ? `Se eliminará "${espacio.nombre}" de la base de datos. Esta acción es irreversible.`
          : `Se eliminará "${espacio.nombre}" (aún no está guardado en la BD).`
      }
    });
    ref.afterClosed().subscribe(confirmado => {
      if (!confirmado) return;
      if (!enBd) {
        this.state.deleteEspacio(espacio.id);
        this.snackBar.open('Espacio eliminado localmente.', '', { duration: 2500 });
        return;
      }
      this.persistencia.eliminarEspacioBD(espacio.id).subscribe({
        next: () => {
          this.catalogo.quitarDeBd('espacio', espacio.id);
          this.state.deleteEspacio(espacio.id);
          this.catalogo.cargarTodo().subscribe();
          this.snackBar.open('Espacio eliminado de la BD.', '', { duration: 2500 });
        },
        error: (err) => this.snackBar.open(`Error al eliminar: ${mensajeErrorHttp(err)}`, 'Cerrar', { duration: 5000 })
      });
    });
  }

  guardarEnBD() {
    const espacios = this.state.espacios();
    if (!espacios.length) { this.snackBar.open('No hay espacios para guardar.', '', { duration: 2500 }); return; }
    this.saving.set(true);
    const calls$ = espacios.map(e =>
      this.persistencia.actualizarEspacio(e).pipe(
        map(() => { this.catalogo.marcarEnBd('espacio', e.id); return { ok: true, nombre: e.nombre, tipo: 'actualizado' as const }; }),
        catchError(err => {
          if (err.status === 404) {
            return this.persistencia.guardarEspacio(e).pipe(
              map(() => { this.catalogo.marcarEnBd('espacio', e.id); return { ok: true, nombre: e.nombre, tipo: 'nuevo' as const }; }),
              catchError(() => of({ ok: false, nombre: e.nombre, tipo: 'nuevo' as const }))
            );
          }
          return of({ ok: false, nombre: e.nombre, tipo: 'actualizado' as const });
        })
      )
    );
    forkJoin(calls$).subscribe(results => {
      this.saving.set(false);
      this.dialog.open(GuardadoResultadoDialogComponent, {
        width: '340px',
        data: {
          entidad: 'espacios',
          nuevos: results.filter(r => r.ok && r.tipo === 'nuevo').map(r => r.nombre),
          actualizados: results.filter(r => r.ok && r.tipo === 'actualizado').map(r => r.nombre),
          errores: results.filter(r => !r.ok).map(r => r.nombre)
        }
      });
    });
  }

  cargarDesdeBD() {
    this.saving.set(true);
    this.catalogo.cargarTodo().subscribe({
      next: (resumen) => { this.saving.set(false); this.snackBar.open(`${resumen.espacios} espacio(s) cargados.`, '', { duration: 3000 }); },
      error: () => { this.saving.set(false); this.snackBar.open('Error al cargar desde la BD.', 'Cerrar', { duration: 4000 }); }
    });
  }
}

// ─── Popup: Crear/Editar espacio (REQUISITOS §1.3) ────────────────────────────
@Component({
  selector: 'app-espacio-dialog',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MatDialogModule],
  template: `
    <div class="pophd">{{ data ? 'Editar espacio' : 'Nuevo espacio' }} <i (click)="ref.close()">✕</i></div>
    <form class="popbd" [formGroup]="form">
      <div class="dfield"><label>Nombre <span class="rq">*</span></label>
        <input class="input" formControlName="nombre"></div>
      <div class="dfield"><label>Tipo <span class="rq">*</span></label>
        <select class="input" formControlName="tipo">
          <option value="Salón">Salón</option>
          <option value="Laboratorio">Laboratorio</option>
          <option value="Auditorio">Auditorio</option>
        </select></div>
      <div class="dfield"><label>Capacidad <span class="rq">*</span></label>
        <input class="input" type="number" min="1" formControlName="capacidad"></div>
      <div style="display:flex;gap:8px">
        <div class="dfield" style="flex:1"><label>Edificio</label>
          <input class="input" formControlName="edificio"></div>
        <div class="dfield" style="width:96px"><label>Piso</label>
          <input class="input" type="number" formControlName="piso"></div>
      </div>
      <div class="popfoot">
        <button type="button" class="btn btn-secondary" (click)="ref.close()">Cancelar</button>
        <button type="button" class="btn btn-primary" [disabled]="form.invalid" (click)="save()">Guardar</button>
      </div>
    </form>
  `
})
export class EspacioDialogComponent {
  fb = inject(FormBuilder);
  ref = inject(MatDialogRef<EspacioDialogComponent>);
  data = inject(MAT_DIALOG_DATA) as Espacio | undefined;

  form = this.fb.group({
    nombre: [this.data?.nombre ?? '', Validators.required],
    tipo: [this.data?.tipo ?? 'Salón', Validators.required],
    capacidad: [this.data?.capacidad ?? null, [Validators.required, Validators.min(1)]],
    edificio: [this.data?.edificio ?? ''],
    piso: [this.data?.piso ?? null]
  });

  save() {
    if (this.form.invalid) return;
    const v = this.form.value;
    this.ref.close({
      nombre: v.nombre, tipo: v.tipo, capacidad: Number(v.capacidad),
      edificio: v.edificio || undefined, piso: v.piso != null && v.piso !== ('' as any) ? Number(v.piso) : undefined
    });
  }
}
