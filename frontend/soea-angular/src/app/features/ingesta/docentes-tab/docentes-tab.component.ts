import { Component, inject, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormsModule, FormBuilder, Validators, FormGroup } from '@angular/forms';
import { MatDialogModule, MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { StateService } from '../../../core/state.service';
import { PersistenciaService } from '../../../core/persistencia.service';
import { CatalogoService } from '../../../core/catalogo.service';
import { mensajeErrorHttp } from '../../../core/http-error.util';
import { ConfirmDeleteDialogComponent } from '../../../shared/confirm-delete-dialog/confirm-delete-dialog.component';
import { Docente } from '../../../core/models';
import { nuevoId } from '../../../core/id.util';
import { MatSnackBar } from '@angular/material/snack-bar';
import { DisponibilidadEditorComponent, FranjaOption } from '../../../shared/disponibilidad-editor/disponibilidad-editor.component';

@Component({
  selector: 'app-docentes-tab',
  standalone: true,
  imports: [CommonModule, MatDialogModule],
  template: `
    <div class="tab-content">
      <div class="toolbar">
        <div class="filters">
          <input class="input search" placeholder="🔍 Buscar docente…" (input)="filterStr.set($any($event.target).value)">
          <span class="text-muted count">{{ filtered().length }} docentes</span>
        </div>
        <div class="actions">
          <button class="btn btn-secondary" (click)="detectarDuplicados()" [disabled]="saving()">Revisar duplicados</button>
          <button class="btn btn-primary" (click)="openDialog()">＋ Nuevo docente</button>
        </div>
      </div>

      <table class="table">
        <thead><tr>
          <th style="width:26%">Docente</th><th>Disponibilidad declarada</th><th>Asignaturas</th><th style="width:110px">Máx. horas/sem</th><th style="width:60px"></th>
        </tr></thead>
        <tbody>
          @for (d of filtered(); track d.id) {
            <tr>
              <td>{{ d.nombre }}</td>
              <td>
                @if (dispDeclarada(d)) { <span class="text-muted disp">{{ dispDeclarada(d) }}</span> }
                @else { <span style="color:var(--err-bd);font-size:12.5px">Sin disponibilidad declarada</span> }
              </td>
              <td class="text-muted">{{ asignaturasDe(d.id) || '—' }}</td>
              <td><span class="dpill" [ngClass]="d.maxHoras ? 'ok' : ''">{{ d.maxHoras || '—' }}</span></td>
              <td>
                <button type="button" class="material-icons ic-edit" (click)="openDialog(d)" [attr.aria-label]="'Editar ' + d.nombre">edit</button>
                <button type="button" class="material-icons ic-del" (click)="delete(d)" [attr.aria-label]="'Eliminar ' + d.nombre">delete</button>
              </td>
            </tr>
          }
          @if (filtered().length === 0) {
            <tr><td colspan="5" class="empty">Sin docentes. Usa "＋ Nuevo docente" o importa un Excel.</td></tr>
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
    .disp { font-size: 12.5px; }
    .empty { text-align: center; color: var(--color-neutral-500); padding: 28px; }
  `]
})
export class DocentesTabComponent {
  state = inject(StateService);
  dialog = inject(MatDialog);
  snackBar = inject(MatSnackBar);
  persistencia = inject(PersistenciaService);
  catalogo = inject(CatalogoService);

  filterStr = signal('');
  saving = signal(false);

  filtered = computed(() => {
    const f = this.filterStr().toLowerCase();
    return this.state.docentes().filter(d => !f || d.nombre.toLowerCase().includes(f) || (d.cedula ?? '').includes(f));
  });

  /** Resumen textual de la disponibilidad declarada por día. Vacío = sin declarar. */
  dispDeclarada(d: Docente): string {
    const disp = d.disponibilidad;
    if (!disp || typeof disp !== 'object') return '';
    const dias = ['lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado'];
    const activos = dias.filter(dia => disp[dia] && !disp[dia].noDisponible);
    if (!activos.length) return '';
    const especifica = activos.find(dia => disp[dia].tipo === 'Franja específica');
    if (especifica) return `${activos.length} día(s) · incluye franja específica`;
    return `${activos.length} día(s) declarados`;
  }

  /** Asignaturas que dicta el docente, derivadas de sus grupos (Fase 2: docente vive en el grupo). */
  asignaturasDe(docenteId: string): string {
    const asigById = this.state.asignaturaById();
    const nombres = [...new Set(
      this.state.grupos()
        .filter(g => g.docenteId === docenteId)
        .map(g => asigById.get(g.asignaturaId)?.nombre)
        .filter((n): n is string => !!n)
    )];
    return nombres.slice(0, 2).join(', ') + (nombres.length > 2 ? `, +${nombres.length - 2}` : '');
  }

  openDialog(docente?: Docente) {
    const dialogRef = this.dialog.open(DocenteDialogComponent, { width: '620px', maxWidth: '95vw', data: docente });
    dialogRef.afterClosed().subscribe(result => {
      if (!result) return;
      const entidad: Docente = docente ? { ...docente, ...result } : { id: nuevoId(), ...result };
      this.catalogo.guardar('docente', entidad).subscribe({
        next: () => this.snackBar.open(docente ? 'Docente actualizado' : 'Docente agregado', '', { duration: 2500 }),
        error: (err) => this.snackBar.open(`Error al guardar: ${mensajeErrorHttp(err)}`, 'Cerrar', { duration: 4000, panelClass: ['snack-error'] })
      });
    });
  }

  delete(docente: Docente) {
    const enBd = this.catalogo.estaEnBd('docente', docente.id);
    const ref = this.dialog.open(ConfirmDeleteDialogComponent, {
      width: '320px',
      data: {
        title: 'Eliminar docente',
        message: enBd
          ? `Se eliminará "${docente.nombre}" definitivamente.`
          : `Se descartará "${docente.nombre}", que aún no se había guardado.`
      }
    });
    ref.afterClosed().subscribe(confirmado => {
      if (!confirmado) return;
      if (!enBd) {
        this.state.deleteDocente(docente.id);
        this.snackBar.open('Docente eliminado.', '', { duration: 2500 });
        return;
      }
      // FE10 auditoría: antes borraba a mano en vez de pasar por CatalogoService.eliminar.
      this.catalogo.eliminar('docente', docente.id).subscribe({
        next: () => {
          this.catalogo.cargarTodo().subscribe({ error: () => this.snackBar.open('Se eliminó, pero la lista no se actualizó. Recargue la página.', 'Cerrar', { duration: 6000 }) });
          this.snackBar.open('Docente eliminado.', '', { duration: 2500 });
        },
        error: (err) => this.snackBar.open(`Error al eliminar: ${mensajeErrorHttp(err)}`, 'Cerrar', { duration: 5000, panelClass: ['snack-error'] })
      });
    });
  }

  detectarDuplicados() {
    this.saving.set(true);
    this.persistencia.detectarDuplicadosDocentes().subscribe({
      next: (grupos) => {
        this.saving.set(false);
        if (!grupos.length) { this.snackBar.open('No se detectaron docentes duplicados.', '', { duration: 4000 }); return; }
        const ref = this.dialog.open(FusionDocentesDialogComponent, { width: '380px', maxWidth: '95vw', data: { grupos } });
        ref.afterClosed().subscribe((huboFusion) => { if (huboFusion) this.cargarDesdeBD(); });
      },
      error: () => { this.saving.set(false); this.snackBar.open('No se pudo revisar repetidos. Intente de nuevo.', 'Cerrar', { duration: 4000, panelClass: ['snack-error'] }); }
    });
  }

  cargarDesdeBD() {
    this.saving.set(true);
    this.catalogo.cargarTodo().subscribe({
      next: (resumen) => { this.saving.set(false); this.snackBar.open(`${resumen.docentes} docente(s) cargados.`, '', { duration: 3000 }); },
      error: () => { this.saving.set(false); this.snackBar.open('No se pudo actualizar la lista.', 'Cerrar', { duration: 4000, panelClass: ['snack-error'] }); }
    });
  }
}

// ─── Popup: Crear/Editar docente + disponibilidad por día (REQUISITOS §1.2) ────
@Component({
  selector: 'app-docente-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule, MatDialogModule, DisponibilidadEditorComponent],
  template: `
    <div class="pophd">{{ data ? 'Editar docente' : 'Nuevo docente' }} <button type="button" class="pop-close" (click)="ref.close()" aria-label="Cerrar">✕</button></div>
    <div class="popbd" style="max-height:74vh;overflow:auto">
      <form [formGroup]="form" style="display:flex;gap:8px">
        <div class="dfield" style="flex:1.4"><label>Nombre <span class="rq">*</span></label>
          <input class="input" formControlName="nombre"></div>
        <div class="dfield" style="flex:1"><label>Cédula</label>
          <input class="input" formControlName="cedula"></div>
        <div class="dfield" style="width:96px"><label>Máx. horas por semana <span class="rq">*</span></label>
          <input class="input" type="number" min="1" formControlName="maxHoras"></div>
      </form>

      <h3 class="sec" style="margin-top:4px">Disponibilidad por día</h3>
      <app-disponibilidad-editor [opciones]="opcionesFranja" [legacyMap]="legacyMap"
        [ngModel]="disponibilidad()" (ngModelChange)="disponibilidad.set($event)"></app-disponibilidad-editor>

      <div class="popfoot">
        <button type="button" class="btn btn-secondary" (click)="ref.close()">Cancelar</button>
        <button type="button" class="btn btn-primary" [disabled]="form.invalid" (click)="save()">Guardar</button>
      </div>
    </div>
  `
})
export class DocenteDialogComponent {
  fb = inject(FormBuilder);
  ref = inject(MatDialogRef<DocenteDialogComponent>);
  data = inject(MAT_DIALOG_DATA);

  form: FormGroup;
  disponibilidad = signal<Record<string, any>>(this.data?.disponibilidad ?? {});

  readonly opcionesFranja: FranjaOption[] = [
    { value: 'todo', label: 'Todo el día (06:00–22:00)' },
    { value: 'oficina', label: 'Horario de oficina (06:00–18:00)' },
    { value: 'matutino', label: 'Matutino (06:00–12:00)' },
    { value: 'vespertino', label: 'Vespertino (12:00–18:00)' },
    { value: 'nocturno', label: 'Nocturno (18:00–22:00)' }
  ];
  // Variantes de texto que ya no coinciden con los labels vigentes (horarios históricos de Excel).
  readonly legacyMap: Record<string, string> = {
    'Todo el día': 'todo',
    'Matutino (06:00–13:00)': 'matutino',
    'Vespertino (13:00–19:00)': 'vespertino',
    'Nocturno (19:00–22:00)': 'nocturno'
  };

  constructor() {
    this.form = this.fb.group({
      nombre: [this.data?.nombre ?? '', Validators.required],
      cedula: [this.data?.cedula ?? ''],
      maxHoras: [this.data?.maxHoras ?? 40, [Validators.required, Validators.min(1)]]
    });
  }

  save() {
    if (this.form.invalid) return;
    this.ref.close({ ...this.form.value, disponibilidad: this.disponibilidad() });
  }
}

// ─── Popup: Revisar y fusionar duplicados (REQUISITOS §1.2) ────────────────────
@Component({
  selector: 'app-fusion-docentes-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule],
  template: `
    <div class="pophd">Unificar docentes repetidos <button type="button" class="pop-close" (click)="ref.close(huboFusion)" aria-label="Cerrar">✕</button></div>
    <div class="popbd" style="max-height:74vh;overflow:auto">
      <span class="text-muted" style="font-size:12px">Elija el nombre correcto en cada caso; los demás se eliminarán y sus grupos pasarán al elegido.</span>

      <div *ngFor="let grupo of data.grupos; let gi = index" class="grupo" [class.done]="done.has(gi)">
        <div class="grupo-head">
          <span class="sec">Posible repetido {{ gi + 1 }}</span>
          <span *ngIf="done.has(gi)" class="okb" style="padding:2px 8px">✓ Unificado</span>
        </div>
        <label *ngFor="let d of grupo" class="radio">
          <input type="radio" [name]="'canon-'+gi" [value]="d.id" [(ngModel)]="canonico[gi]" [disabled]="done.has(gi)">
          <span class="dot"></span>
          {{ d.nombre }}
          <span class="text-muted" style="font-size:11px">· {{ d.maxHoras }}h</span>
          <span *ngIf="canonico[gi] === d.id" class="text-muted" style="font-size:11px">(se conserva)</span>
        </label>
        <div *ngIf="!done.has(gi)" class="popfoot" style="margin-top:6px">
          <button class="btn btn-primary" (click)="fusionar(gi)" [disabled]="busy()">Unificar</button>
        </div>
      </div>

      <div class="popfoot">
        <button class="btn btn-secondary" (click)="ref.close(huboFusion)">Cerrar</button>
      </div>
    </div>
  `,
  styles: [`
    .grupo { border: 1px solid var(--color-divider); padding: 11px 13px; display: flex; flex-direction: column; gap: 6px; }
    .grupo.done { opacity: .6; background: var(--color-neutral-100); }
    .grupo-head { display: flex; justify-content: space-between; align-items: center; }
  `]
})
export class FusionDocentesDialogComponent {
  data = inject(MAT_DIALOG_DATA) as { grupos: Docente[][] };
  ref = inject(MatDialogRef<FusionDocentesDialogComponent>);
  persistencia = inject(PersistenciaService);
  snackBar = inject(MatSnackBar);

  canonico: Record<number, string> = {};
  done = new Set<number>();
  busy = signal(false);
  huboFusion = false;

  constructor() { this.data.grupos.forEach((g, i) => { if (g.length) this.canonico[i] = g[0].id; }); }

  fusionar(gi: number) {
    const canonicoId = this.canonico[gi];
    const duplicadosIds = this.data.grupos[gi].filter(d => d.id !== canonicoId).map(d => d.id);
    if (!canonicoId || !duplicadosIds.length) return;
    this.busy.set(true);
    this.persistencia.fusionarDocentes(canonicoId, duplicadosIds).subscribe({
      next: (r) => {
        this.busy.set(false); this.done.add(gi); this.huboFusion = true;
        this.snackBar.open(`Unificados: se eliminaron ${r.docentesEliminados} docente(s) repetido(s) y se reasignaron ${r.gruposReasignados} grupo(s).`, '', { duration: 4000 });
      },
      error: (err) => { this.busy.set(false); this.snackBar.open(`Error al fusionar: ${mensajeErrorHttp(err)}`, 'Cerrar', { duration: 5000, panelClass: ['snack-error'] }); }
    });
  }
}
