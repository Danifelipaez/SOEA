import { Component, inject, computed, signal } from '@angular/core';
import { CommonModule, TitleCasePipe } from '@angular/common';
import { ReactiveFormsModule, FormsModule, FormBuilder, Validators, FormGroup } from '@angular/forms';
import { MatDialogModule, MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { StateService } from '../../../core/state.service';
import { PersistenciaService } from '../../../core/persistencia.service';
import { CatalogoService } from '../../../core/catalogo.service';
import { mensajeErrorHttp } from '../../../core/http-error.util';
import { GuardadoResultadoDialogComponent } from '../../../shared/guardado-resultado-dialog/guardado-resultado-dialog.component';
import { ConfirmDeleteDialogComponent } from '../../../shared/confirm-delete-dialog/confirm-delete-dialog.component';
import { Docente } from '../../../core/models';
import { MatSnackBar } from '@angular/material/snack-bar';
import { forkJoin, of } from 'rxjs';
import { map, catchError } from 'rxjs/operators';

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
          <button class="btn btn-secondary" (click)="cargarDesdeBD()" [disabled]="saving()">Cargar BD</button>
          <button class="btn btn-secondary" (click)="guardarEnBD()" [disabled]="saving()">{{ saving() ? 'Guardando…' : 'Guardar en BD' }}</button>
          <button class="btn btn-secondary" (click)="detectarDuplicados()" [disabled]="saving()">Revisar duplicados</button>
          <button class="btn btn-primary" (click)="openDialog()">＋ Nuevo docente</button>
        </div>
      </div>

      <table class="table">
        <thead><tr>
          <th style="width:26%">Docente</th><th>Disponibilidad declarada</th><th>Asignaturas</th><th style="width:110px">Máx. hrs</th><th style="width:60px"></th>
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
                <span class="material-icons ic-edit" (click)="openDialog(d)" title="Editar">edit</span>
                <span class="material-icons ic-del" (click)="delete(d)" title="Eliminar">delete</span>
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
      if (docente) {
        this.state.updateDocente({ ...docente, ...result });
        this.snackBar.open('Docente actualizado', '', { duration: 2500 });
      } else {
        this.state.addDocente({ id: crypto.randomUUID(), ...result });
        this.snackBar.open('Docente agregado', '', { duration: 2500 });
      }
    });
  }

  delete(docente: Docente) {
    const enBd = this.catalogo.estaEnBd('docente', docente.id);
    const ref = this.dialog.open(ConfirmDeleteDialogComponent, {
      width: '320px',
      data: {
        title: 'Eliminar docente',
        message: enBd
          ? `Se eliminará "${docente.nombre}" de la base de datos. Esta acción es irreversible.`
          : `Se eliminará "${docente.nombre}" (aún no está guardado en la BD).`
      }
    });
    ref.afterClosed().subscribe(confirmado => {
      if (!confirmado) return;
      if (!enBd) {
        this.state.deleteDocente(docente.id);
        this.snackBar.open('Docente eliminado localmente.', '', { duration: 2500 });
        return;
      }
      this.persistencia.eliminarDocenteBD(docente.id).subscribe({
        next: () => {
          this.catalogo.quitarDeBd('docente', docente.id);
          this.state.deleteDocente(docente.id);
          this.catalogo.cargarTodo().subscribe();
          this.snackBar.open('Docente eliminado de la BD.', '', { duration: 2500 });
        },
        error: (err) => this.snackBar.open(`Error al eliminar: ${mensajeErrorHttp(err)}`, 'Cerrar', { duration: 5000 })
      });
    });
  }

  guardarEnBD() {
    const docentes = this.state.docentes();
    if (!docentes.length) { this.snackBar.open('No hay docentes para guardar.', '', { duration: 2500 }); return; }
    this.saving.set(true);
    const calls$ = docentes.map(d =>
      this.persistencia.actualizarDocente(d).pipe(
        map(updated => { this.state.updateDocente(updated); this.catalogo.marcarEnBd('docente', updated.id); return { ok: true, nombre: d.nombre, tipo: 'actualizado' as const }; }),
        catchError(err => {
          if (err.status === 404) {
            return this.persistencia.guardarDocente(d).pipe(
              map(created => { this.catalogo.marcarEnBd('docente', created.id); this.state.updateDocente(created); return { ok: true, nombre: d.nombre, tipo: 'nuevo' as const }; }),
              catchError(() => of({ ok: false, nombre: d.nombre, tipo: 'nuevo' as const }))
            );
          }
          return of({ ok: false, nombre: d.nombre, tipo: 'actualizado' as const });
        })
      )
    );
    forkJoin(calls$).subscribe(results => {
      this.saving.set(false);
      this.dialog.open(GuardadoResultadoDialogComponent, {
        width: '340px',
        data: {
          entidad: 'docentes',
          nuevos: results.filter(r => r.ok && r.tipo === 'nuevo').map(r => r.nombre),
          actualizados: results.filter(r => r.ok && r.tipo === 'actualizado').map(r => r.nombre),
          errores: results.filter(r => !r.ok).map(r => r.nombre)
        }
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
      error: () => { this.saving.set(false); this.snackBar.open('Error al detectar duplicados.', 'Cerrar', { duration: 4000 }); }
    });
  }

  cargarDesdeBD() {
    this.saving.set(true);
    this.catalogo.cargarTodo().subscribe({
      next: (resumen) => { this.saving.set(false); this.snackBar.open(`${resumen.docentes} docente(s) cargados.`, '', { duration: 3000 }); },
      error: () => { this.saving.set(false); this.snackBar.open('Error al cargar desde la BD.', 'Cerrar', { duration: 4000 }); }
    });
  }
}

// ─── Popup: Crear/Editar docente + disponibilidad por día (REQUISITOS §1.2) ────
@Component({
  selector: 'app-docente-dialog',
  standalone: true,
  imports: [CommonModule, TitleCasePipe, ReactiveFormsModule, MatDialogModule],
  template: `
    <div class="pophd">{{ data ? 'Editar docente' : 'Nuevo docente' }} <i (click)="ref.close()">✕</i></div>
    <div class="popbd" style="max-height:74vh;overflow:auto">
      <form [formGroup]="form" style="display:flex;gap:8px">
        <div class="dfield" style="flex:1.4"><label>Nombre <span class="rq">*</span></label>
          <input class="input" formControlName="nombre"></div>
        <div class="dfield" style="flex:1"><label>Cédula</label>
          <input class="input" formControlName="cedula"></div>
        <div class="dfield" style="width:96px"><label>Máx. hrs <span class="rq">*</span></label>
          <input class="input" type="number" min="1" formControlName="maxHoras"></div>
      </form>

      <h3 class="sec" style="margin-top:4px">Disponibilidad por día</h3>
      <div class="disp-table">
        <div class="disp-row hd">
          <span class="c-dia">Día</span><span class="c-nd">No disp.</span><span class="c-tipo">Franja</span><span class="c-times">Horario</span>
        </div>
        <div *ngFor="let dia of dias" class="disp-row">
          <span class="c-dia">{{ dia | titlecase }}</span>
          <span class="c-nd">
            <input type="checkbox" [checked]="getDisp(dia,'noDisponible')"
                   (change)="setDisp(dia,'noDisponible',$any($event.target).checked)">
          </span>
          <span class="c-tipo">
            <select *ngIf="!getDisp(dia,'noDisponible')" class="input" style="min-height:30px;padding:4px 8px"
                    [value]="getDisp(dia,'tipo')" (change)="setDisp(dia,'tipo',$any($event.target).value)">
              <option value="todo">Todo el día (06:00–22:00)</option>
              <option value="oficina">Oficina (06:00–18:00)</option>
              <option value="matutino">Matutino (06:00–12:00)</option>
              <option value="vespertino">Vespertino (12:00–18:00)</option>
              <option value="nocturno">Nocturno (18:00–22:00)</option>
              <option value="especifico">Franja específica</option>
            </select>
            <span *ngIf="getDisp(dia,'noDisponible')" class="text-muted">—</span>
          </span>
          <span class="c-times">
            <ng-container *ngIf="!getDisp(dia,'noDisponible') && getDisp(dia,'tipo')==='especifico'">
              <input class="input time" type="time" [value]="getDisp(dia,'desde')" (input)="setDisp(dia,'desde',$any($event.target).value)">
              <span class="text-muted">–</span>
              <input class="input time" type="time" [value]="getDisp(dia,'hasta')" (input)="setDisp(dia,'hasta',$any($event.target).value)">
            </ng-container>
            <span *ngIf="!getDisp(dia,'noDisponible') && getDisp(dia,'tipo')!=='especifico'" class="text-muted">{{ tipoLabel(dia) }}</span>
            <span *ngIf="getDisp(dia,'noDisponible')" class="text-muted">—</span>
          </span>
        </div>
      </div>

      <div class="popfoot">
        <button type="button" class="btn btn-secondary" (click)="ref.close()">Cancelar</button>
        <button type="button" class="btn btn-primary" [disabled]="form.invalid" (click)="save()">Guardar</button>
      </div>
    </div>
  `,
  styles: [`
    .disp-table { border: 1px solid var(--color-divider); }
    .disp-row { display: flex; gap: 8px; align-items: center; padding: 7px 11px; border-top: 1px solid color-mix(in srgb, var(--color-text) 8%, transparent); min-height: 44px; }
    .disp-row.hd { border-top: 0; background: var(--color-neutral-100); font: 600 10px var(--font-heading); letter-spacing: .08em; text-transform: uppercase; color: var(--color-neutral-600); min-height: 32px; }
    .c-dia { width: 82px; font-size: 13px; }
    .c-nd { width: 60px; display: flex; justify-content: center; }
    .c-tipo { width: 200px; }
    .c-times { flex: 1; display: flex; gap: 6px; align-items: center; font-size: 12.5px; }
    .time { width: 96px; min-height: 30px; padding: 3px 6px; }
  `]
})
export class DocenteDialogComponent {
  fb = inject(FormBuilder);
  ref = inject(MatDialogRef<DocenteDialogComponent>);
  data = inject(MAT_DIALOG_DATA);

  dias = ['lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado'];
  form: FormGroup;
  disp: Record<string, any> = {};

  constructor() {
    const generalToTipo: Record<string, string> = {
      'Todo el día': 'todo', 'Todo el día (06:00–22:00)': 'todo',
      'Horario de oficina (06:00–18:00)': 'oficina',
      'Matutino (06:00–12:00)': 'matutino', 'Matutino (06:00–13:00)': 'matutino',
      'Vespertino (12:00–18:00)': 'vespertino', 'Vespertino (13:00–19:00)': 'vespertino',
      'Nocturno (18:00–22:00)': 'nocturno', 'Nocturno (19:00–22:00)': 'nocturno'
    };
    this.dias.forEach(dia => {
      const d = this.data?.disponibilidad?.[dia] ?? {};
      const tipoRaw = d.tipo ?? 'todo';
      let tipo = tipoRaw;
      if (tipoRaw === 'Franja específica') tipo = 'especifico';
      else if (tipoRaw === 'Franja general') tipo = generalToTipo[d.franjaGeneral] ?? 'todo';
      this.disp[dia] = { noDisponible: d.noDisponible ?? false, tipo, desde: d.desde ?? '06:00', hasta: d.hasta ?? '22:00' };
    });
    this.form = this.fb.group({
      nombre: [this.data?.nombre ?? '', Validators.required],
      cedula: [this.data?.cedula ?? ''],
      maxHoras: [this.data?.maxHoras ?? 40, [Validators.required, Validators.min(1)]]
    });
  }

  getDisp(dia: string, field: string): any { return this.disp[dia]?.[field]; }
  setDisp(dia: string, field: string, value: any): void { this.disp[dia] = { ...this.disp[dia], [field]: value }; }

  tipoLabel(dia: string): string {
    switch (this.disp[dia]?.tipo) {
      case 'todo': return '06:00 – 22:00';
      case 'oficina': return '06:00 – 18:00';
      case 'matutino': return '06:00 – 12:00';
      case 'vespertino': return '12:00 – 18:00';
      case 'nocturno': return '18:00 – 22:00';
      default: return '';
    }
  }

  save() {
    if (this.form.invalid) return;
    const disponibilidad: Record<string, any> = {};
    this.dias.forEach(dia => {
      const d = this.disp[dia];
      if (d.noDisponible) { disponibilidad[dia] = { noDisponible: true }; }
      else if (d.tipo === 'especifico') { disponibilidad[dia] = { noDisponible: false, tipo: 'Franja específica', desde: d.desde, hasta: d.hasta }; }
      else {
        const franjaMap: Record<string, string> = {
          todo: 'Todo el día (06:00–22:00)', oficina: 'Horario de oficina (06:00–18:00)',
          matutino: 'Matutino (06:00–12:00)', vespertino: 'Vespertino (12:00–18:00)', nocturno: 'Nocturno (18:00–22:00)'
        };
        disponibilidad[dia] = { noDisponible: false, tipo: 'Franja general', franjaGeneral: franjaMap[d.tipo] ?? 'Todo el día (06:00–22:00)' };
      }
    });
    this.ref.close({ ...this.form.value, disponibilidad });
  }
}

// ─── Popup: Revisar y fusionar duplicados (REQUISITOS §1.2) ────────────────────
@Component({
  selector: 'app-fusion-docentes-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule],
  template: `
    <div class="pophd">Revisar y fusionar <i (click)="ref.close(huboFusion)">✕</i></div>
    <div class="popbd" style="max-height:74vh;overflow:auto">
      <span class="text-muted" style="font-size:12px">Elige el registro principal de cada grupo; los demás se absorben (asignaturas reasignadas, duplicados borrados).</span>

      <div *ngFor="let grupo of data.grupos; let gi = index" class="grupo" [class.done]="done.has(gi)">
        <div class="grupo-head">
          <span class="sec">Grupo {{ gi + 1 }}</span>
          <span *ngIf="done.has(gi)" class="okb" style="padding:2px 8px">✓ Fusionado</span>
        </div>
        <label *ngFor="let d of grupo" class="radio">
          <input type="radio" [name]="'canon-'+gi" [value]="d.id" [(ngModel)]="canonico[gi]" [disabled]="done.has(gi)">
          <span class="dot"></span>
          {{ d.nombre }}
          <span class="text-muted" style="font-size:11px">· {{ d.maxHoras }}h</span>
          <span *ngIf="canonico[gi] === d.id" class="text-muted" style="font-size:11px">(principal)</span>
        </label>
        <div *ngIf="!done.has(gi)" class="popfoot" style="margin-top:6px">
          <button class="btn btn-primary" (click)="fusionar(gi)" [disabled]="busy()">Fusionar</button>
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
        this.snackBar.open(`Fusionados ${r.docentesEliminados} docente(s); ${r.gruposReasignados} grupo(s) reasignado(s).`, '', { duration: 4000 });
      },
      error: (err) => { this.busy.set(false); this.snackBar.open(`Error al fusionar: ${err?.error ?? 'desconocido'}`, 'Cerrar', { duration: 5000, panelClass: ['snack-error'] }); }
    });
  }
}
