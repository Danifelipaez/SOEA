import { Component, inject, computed, signal } from '@angular/core';
import { CommonModule, TitleCasePipe } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators, FormGroup } from '@angular/forms';
import { MatDialogModule, MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
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
  imports: [CommonModule, MatDialogModule],
  template: `
    <div class="tab-content">
      <div class="toolbar">
        <div class="filters">
          <select class="input prog" [value]="progFiltro()" (change)="progFiltro.set($any($event.target).value)">
            <option value="">Programa: todos</option>
            @for (p of state.programas(); track p.id) { <option [value]="p.id">{{ p.nombre }}</option> }
          </select>
          <input class="input search" placeholder="🔍 Buscar grupo…" (input)="filterStr.set($any($event.target).value)">
          <span class="text-muted count">{{ filtered().length }} grupos</span>
        </div>
        <div class="actions">
          <button class="btn btn-secondary" (click)="cargarDesdeBD()" [disabled]="saving()">Cargar BD</button>
          <button class="btn btn-secondary" (click)="guardarEnBD()" [disabled]="saving()">{{ saving() ? 'Guardando…' : 'Guardar en BD' }}</button>
          <button class="btn btn-primary" (click)="openDialog()">＋ Nuevo grupo</button>
        </div>
      </div>

      <table class="table">
        <thead><tr>
          <th style="width:16%">Grupo</th><th>Programa</th><th>Semestre</th><th>Estudiantes</th><th>Disponibilidad</th><th style="width:60px"></th>
        </tr></thead>
        <tbody>
          @for (g of filtered(); track g.id) {
            <tr>
              <td>{{ g.nombre }}</td>
              <td>{{ programaNombre(g.programaId) }}</td>
              <td>{{ g.semestre }}°</td>
              <td>{{ g.estudiantesInscritos }}</td>
              <td>
                @if (g.disponibilidadUiJson) { <span class="text-muted" style="font-size:12.5px">{{ dispResumen(g) }}</span> }
                @else { <span style="color:var(--err-bd);font-size:12.5px">Sin declarar</span> }
              </td>
              <td>
                <span class="icell" (click)="openDialog(g)" title="Editar">✎</span>
                <span class="icell del" (click)="delete(g)" title="Eliminar">🗑</span>
              </td>
            </tr>
          }
          @if (filtered().length === 0) {
            <tr><td colspan="6" class="empty">Sin grupos. Usa "＋ Nuevo grupo".</td></tr>
          }
        </tbody>
      </table>
    </div>
  `,
  styles: [`
    .tab-content { padding: 20px 0; display: flex; flex-direction: column; gap: 14px; }
    .toolbar { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    .filters { display: flex; align-items: center; gap: 10px; }
    .prog { width: 200px; }
    .search { width: 200px; }
    .count { font-size: 12.5px; }
    .actions { display: flex; gap: 8px; }
    .icell { padding: 0 5px; font-size: 15px; }
    .icell.del:hover { color: var(--err-bd); }
    .empty { text-align: center; color: var(--color-neutral-500); padding: 28px; }
  `]
})
export class GrupoTabComponent {
  state = inject(StateService);
  dialog = inject(MatDialog);
  snackBar = inject(MatSnackBar);
  persistencia = inject(PersistenciaService);
  catalogo = inject(CatalogoService);

  filterStr = signal('');
  progFiltro = signal('');
  saving = signal(false);

  filtered = computed(() => {
    const f = this.filterStr().toLowerCase();
    const prog = this.progFiltro();
    return this.state.grupos().filter(g =>
      (!prog || g.programaId === prog) &&
      (!f || g.nombre.toLowerCase().includes(f) || this.programaNombre(g.programaId).toLowerCase().includes(f))
    );
  });

  programaNombre(id: string): string { return this.state.getProgramaById(id)?.nombre ?? '—'; }

  dispResumen(g: Grupo): string {
    try {
      const disp = JSON.parse(g.disponibilidadUiJson!);
      const dias = ['lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado'];
      const n = dias.filter(d => disp[d] && !disp[d].noDisponible).length;
      return n ? `${n} día(s) disponibles` : 'Sin días disponibles';
    } catch { return 'Configurada'; }
  }

  openDialog(grupo?: Grupo) {
    const ref = this.dialog.open(GrupoDialogComponent, { width: '620px', maxWidth: '95vw', data: grupo });
    ref.afterClosed().subscribe(result => {
      if (!result) return;
      if (grupo) { this.state.updateGrupo({ ...grupo, ...result }); this.snackBar.open('Grupo actualizado', '', { duration: 2500 }); }
      else { this.state.addGrupo({ id: crypto.randomUUID(), ...result }); this.snackBar.open('Grupo agregado', '', { duration: 2500 }); }
    });
  }

  delete(grupo: Grupo) {
    const enBd = this.catalogo.estaEnBd('grupo', grupo.id);
    const ref = this.dialog.open(ConfirmDeleteDialogComponent, {
      width: '320px',
      data: {
        title: 'Eliminar grupo',
        message: enBd
          ? `Se eliminará "${grupo.nombre}" de la base de datos. Esta acción es irreversible.`
          : `Se eliminará "${grupo.nombre}" (aún no está guardado en la BD).`
      }
    });
    ref.afterClosed().subscribe(confirmado => {
      if (!confirmado) return;
      if (!enBd) { this.state.deleteGrupo(grupo.id); this.snackBar.open('Grupo eliminado localmente.', '', { duration: 2500 }); return; }
      this.persistencia.eliminarGrupoBD(grupo.id).subscribe({
        next: () => { this.catalogo.quitarDeBd('grupo', grupo.id); this.state.deleteGrupo(grupo.id); this.snackBar.open('Grupo eliminado de la BD.', '', { duration: 2500 }); },
        error: (err) => this.snackBar.open(`Error al eliminar: ${mensajeErrorHttp(err)}`, 'Cerrar', { duration: 5000 })
      });
    });
  }

  guardarEnBD() {
    const grupos = this.state.grupos();
    if (!grupos.length) { this.snackBar.open('No hay grupos para guardar.', '', { duration: 2500 }); return; }
    this.saving.set(true);
    const calls$ = grupos.map(g =>
      this.persistencia.actualizarGrupo(g).pipe(
        map(updated => { this.state.updateGrupo(updated); this.catalogo.marcarEnBd('grupo', updated.id); return { ok: true, nombre: g.nombre, tipo: 'actualizado' as const }; }),
        catchError(err => {
          if (err.status === 404) {
            return this.persistencia.guardarGrupo(g).pipe(
              map(created => { this.catalogo.marcarEnBd('grupo', created.id); this.state.updateGrupo(created); return { ok: true, nombre: g.nombre, tipo: 'nuevo' as const }; }),
              catchError(() => of({ ok: false, nombre: g.nombre, tipo: 'nuevo' as const }))
            );
          }
          return of({ ok: false, nombre: g.nombre, tipo: 'actualizado' as const });
        })
      )
    );
    forkJoin(calls$).subscribe(results => {
      this.saving.set(false);
      this.dialog.open(GuardadoResultadoDialogComponent, {
        width: '340px',
        data: {
          entidad: 'grupos',
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
      next: (resumen) => { this.saving.set(false); this.snackBar.open(`${resumen.grupos} grupo(s) cargados.`, '', { duration: 3000 }); },
      error: () => { this.saving.set(false); this.snackBar.open('Error al cargar desde la BD.', 'Cerrar', { duration: 4000 }); }
    });
  }
}

// ─── Popup: Crear/Editar grupo + disponibilidad (REQUISITOS §1.4) ──────────────
@Component({
  selector: 'app-grupo-dialog',
  standalone: true,
  imports: [CommonModule, TitleCasePipe, ReactiveFormsModule, MatDialogModule],
  template: `
    <div class="pophd">{{ data ? 'Editar grupo' : 'Nuevo grupo' }} <i (click)="ref.close()">✕</i></div>
    <div class="popbd" style="max-height:74vh;overflow:auto">
      <form [formGroup]="form" style="display:flex;flex-direction:column;gap:10px">
        <div class="dfield"><label>Asignatura <span class="rq">*</span></label>
          <select class="input" formControlName="asignaturaId" (change)="onAsignaturaChange($any($event.target).value)">
            <option value="">— Seleccione —</option>
            @for (a of state.asignaturas(); track a.id) { <option [value]="a.id">{{ a.codigo }} – {{ a.nombre }}</option> }
          </select></div>
        <div style="display:flex;gap:8px">
          <div class="dfield" style="flex:1"><label>Programa <span class="rq">*</span></label>
            <select class="input" formControlName="programaId">
              <option value="">—</option>
              @for (p of state.programas(); track p.id) { <option [value]="p.id">{{ p.nombre }}</option> }
            </select></div>
          <div class="dfield" style="flex:1"><label>Facultad</label>
            <select class="input" formControlName="facultadId">
              <option value="">—</option>
              @for (f of state.facultades(); track f.id) { <option [value]="f.id">{{ f.nombre }}</option> }
            </select></div>
        </div>
        <div style="display:flex;gap:8px">
          <div class="dfield" style="flex:1.4"><label>Nombre <span class="rq">*</span></label>
            <input class="input" formControlName="nombre" placeholder="Ej. G1"></div>
          <div class="dfield" style="flex:1"><label>Código</label>
            <input class="input" formControlName="codigo"></div>
        </div>
        <div style="display:flex;gap:8px">
          <div class="dfield" style="flex:1"><label>Semestre <span class="rq">*</span></label>
            <input class="input" type="number" min="1" max="12" formControlName="semestre"></div>
          <div class="dfield" style="flex:1"><label>Estudiantes <span class="rq">*</span></label>
            <input class="input" type="number" min="1" formControlName="estudiantesInscritos"></div>
        </div>
      </form>

      <h3 class="sec" style="margin-top:4px">Disponibilidad del grupo</h3>
      <div class="disp-table">
        <div class="disp-row hd"><span class="c-dia">Día</span><span class="c-nd">No disp.</span><span class="c-tipo">Ventana</span><span class="c-times">Horario</span></div>
        <div *ngFor="let dia of dias" class="disp-row">
          <span class="c-dia">{{ dia | titlecase }}</span>
          <span class="c-nd"><input type="checkbox" [checked]="getDisp(dia,'noDisponible')" (change)="setDisp(dia,'noDisponible',$any($event.target).checked)"></span>
          <span class="c-tipo">
            <select *ngIf="!getDisp(dia,'noDisponible')" class="input" style="min-height:30px;padding:4px 8px"
                    [value]="getDisp(dia,'tipo')" (change)="setDisp(dia,'tipo',$any($event.target).value)">
              <option value="todo">Todo el día (06:00–22:00)</option>
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
export class GrupoDialogComponent {
  fb = inject(FormBuilder);
  ref = inject(MatDialogRef<GrupoDialogComponent>);
  data = inject(MAT_DIALOG_DATA) as Grupo | undefined;
  state = inject(StateService);

  dias = ['lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado'];
  disp: Record<string, any> = {};
  form: FormGroup;

  constructor() {
    const dispExistente = this.data?.disponibilidadUiJson
      ? (() => { try { return JSON.parse(this.data!.disponibilidadUiJson!); } catch { return {}; } })()
      : {};
    this.dias.forEach(dia => {
      const d = dispExistente[dia] ?? {};
      const tipoRaw = d.tipo ?? 'todo';
      let tipo = tipoRaw;
      if (tipoRaw === 'Franja específica') tipo = 'especifico';
      else if (tipoRaw === 'Franja general') tipo = 'todo';
      this.disp[dia] = { noDisponible: d.noDisponible ?? true, tipo, desde: d.desde ?? '06:00', hasta: d.hasta ?? '22:00' };
    });
    this.form = this.fb.group({
      asignaturaId: [this.data?.asignaturaId ?? '', Validators.required],
      programaId: [this.data?.programaId ?? '', Validators.required],
      facultadId: [this.data?.facultadId ?? ''],
      nombre: [this.data?.nombre ?? '', Validators.required],
      codigo: [this.data?.codigo ?? ''],
      semestre: [this.data?.semestre ?? 1, [Validators.required, Validators.min(1), Validators.max(12)]],
      estudiantesInscritos: [this.data?.estudiantesInscritos ?? 30, [Validators.required, Validators.min(1)]]
    });
  }

  onAsignaturaChange(asignaturaId: string) {
    const a = this.state.asignaturas().find(x => x.id === asignaturaId);
    if (a) this.form.patchValue({ programaId: a.programaId });
  }

  getDisp(dia: string, field: string): any { return this.disp[dia]?.[field]; }
  setDisp(dia: string, field: string, value: any): void { this.disp[dia] = { ...this.disp[dia], [field]: value }; }

  tipoLabel(dia: string): string {
    switch (this.disp[dia]?.tipo) {
      case 'todo': return '06:00 – 22:00';
      case 'matutino': return '06:00 – 12:00';
      case 'vespertino': return '12:00 – 18:00';
      case 'nocturno': return '18:00 – 22:00';
      default: return '';
    }
  }

  save() {
    if (this.form.invalid) return;
    const dispObj: Record<string, any> = {};
    this.dias.forEach(dia => {
      const d = this.disp[dia];
      if (d.noDisponible) { dispObj[dia] = { noDisponible: true }; }
      else if (d.tipo === 'especifico') { dispObj[dia] = { noDisponible: false, tipo: 'Franja específica', desde: d.desde, hasta: d.hasta }; }
      else {
        const franjaMap: Record<string, string> = {
          todo: 'Todo el día (06:00–22:00)', matutino: 'Matutino (06:00–12:00)',
          vespertino: 'Vespertino (12:00–18:00)', nocturno: 'Nocturno (18:00–22:00)'
        };
        dispObj[dia] = { noDisponible: false, tipo: 'Franja general', franjaGeneral: franjaMap[d.tipo] ?? 'Todo el día (06:00–22:00)' };
      }
    });
    const v = this.form.value;
    this.ref.close({ ...v, facultadId: v.facultadId || undefined, codigo: v.codigo || undefined, disponibilidadUiJson: JSON.stringify(dispObj) });
  }
}
