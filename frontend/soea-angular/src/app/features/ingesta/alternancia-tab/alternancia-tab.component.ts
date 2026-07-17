import { Component, inject, computed, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { StateService } from '../../../core/state.service';
import { PersistenciaService } from '../../../core/persistencia.service';
import { CatalogoService } from '../../../core/catalogo.service';
import { ConfirmDeleteDialogComponent } from '../../../shared/confirm-delete-dialog/confirm-delete-dialog.component';
import { Asignatura, TipoAlternanciaConfig } from '../../../core/models';

type Patron = TipoAlternanciaConfig['patronBase'];
type TipoAlt = 'TipoA' | 'TipoB' | 'SinAlternancia';

interface AsignaturaFila {
  asignatura: Asignatura;
  guardando: boolean;
  error: string | null;
}

/**
 * Fusiona lo que antes eran dos pantallas sueltas (tipos-alternancia +
 * configuracion-alternancia) en una sola pestaña del flujo de Catálogo (HF-2).
 */
@Component({
  selector: 'app-alternancia-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatSnackBarModule],
  template: `
    <div class="tab-content">

      <!-- Tipos de alternancia -->
      <section>
        <div class="section-head">
          <h3 class="sec">Tipos de alternancia</h3>
          <button class="btn btn-ghost" (click)="abrirTipo()">＋ Nuevo tipo</button>
        </div>
        @if (cargandoTipos()) {
          <p class="text-muted">Cargando…</p>
        } @else {
          <div class="chips">
            @for (t of tipos(); track t.id) {
              <span class="chip">
                <span class="dot" [style.background]="t.color"></span>
                {{ t.nombre }}
                @if (t.esSistema) { <span class="tag tag-neutral" style="font-size:9px">SISTEMA</span> }
                <span class="icell" (click)="abrirTipo(t)" title="Editar">✎</span>
                @if (!t.esSistema) { <span class="icell del" (click)="eliminarTipo(t)" title="Eliminar">🗑</span> }
              </span>
            }
          </div>
        }
      </section>

      <!-- Asignación por asignatura -->
      <section>
        <h3 class="sec">Asignación por asignatura</h3>
        <p class="text-muted" style="font-size:12.5px;margin:2px 0 10px">Una fila por asignatura — fusiona las dos pantallas de hoy.</p>

        <div class="toolbar">
          <input class="input search" type="text" placeholder="🔍 Filtrar por nombre o docente…"
                 [ngModel]="filtro()" (ngModelChange)="filtro.set($event)">
          <div class="resumen">
            <span class="chip"><span class="dot" style="background:var(--alt-a)"></span>TipoA: {{ countTipoA() }}</span>
            <span class="chip"><span class="dot" style="background:var(--alt-b)"></span>TipoB: {{ countTipoB() }}</span>
            <span class="chip"><span class="dot" style="background:var(--alt-sin)"></span>Sin alt.: {{ countSinAlt() }}</span>
          </div>
        </div>

        @if (filasFiltradas().length === 0) {
          <div class="empty">No hay asignaturas cargadas. Impórtalas desde la pestaña <strong>Asignaturas</strong> primero.</div>
        }

        @for (grupo of gruposFiltrados(); track grupo.programa) {
          <div class="prog-group">
            <div class="prog-head"><span>{{ grupo.programa }}</span><span class="text-muted">{{ grupo.filas.length }} asignatura(s)</span></div>
            <table class="table">
              <thead><tr><th style="width:42%">Asignatura</th><th>Docente</th><th style="width:210px">Tipo de alternancia</th><th style="width:70px">Estado</th></tr></thead>
              <tbody>
                @for (fila of grupo.filas; track fila.asignatura.id) {
                  <tr [class.guardando]="fila.guardando">
                    <td><b>{{ fila.asignatura.nombre }}</b> @if (fila.asignatura.codigo) { <span class="text-muted" style="font-size:11px">· {{ fila.asignatura.codigo }}</span> }</td>
                    <td class="text-muted">{{ nombreDocente(fila.asignatura.docenteId) }}</td>
                    <td>
                      <select class="input" style="min-height:30px;padding:4px 8px"
                              [ngModel]="fila.asignatura.alternancia || 'SinAlternancia'"
                              (ngModelChange)="cambiarAlternancia(fila, $event)" [disabled]="fila.guardando">
                        @for (t of tipos(); track t.id) { <option [value]="tipoValueFor(t)">{{ t.nombre }}</option> }
                      </select>
                    </td>
                    <td style="text-align:center">
                      @if (fila.guardando) { <span style="color:var(--color-accent);font-size:12px">Guardando…</span> }
                      @if (fila.error) { <span style="color:var(--err-bd);font-size:12px" [title]="fila.error">⚠</span> }
                      @if (!fila.guardando && !fila.error) { <span style="color:var(--ok-bd)">✓</span> }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </section>
    </div>
  `,
  styles: [`
    .tab-content { padding: 20px 0; display: flex; flex-direction: column; gap: 24px; }
    .section-head { display: flex; align-items: center; justify-content: space-between; margin-bottom: 11px; }
    .chips { display: flex; gap: 10px; flex-wrap: wrap; }
    .icell { padding: 0 3px; font-size: 13px; }
    .icell.del:hover { color: var(--err-bd); }
    .toolbar { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; }
    .search { min-width: 240px; width: auto; flex: 1; max-width: 340px; }
    .resumen { display: flex; gap: 8px; margin-left: auto; }
    .empty { text-align: center; padding: 28px; border: 1px solid var(--color-divider); color: var(--color-neutral-500); }
    .prog-group { border: 1px solid var(--color-divider); margin-bottom: 14px; }
    .prog-head { display: flex; align-items: center; justify-content: space-between; padding: 9px 12px; background: var(--color-neutral-100); border-bottom: 1px solid var(--color-divider); font: 600 13px var(--font-heading); }
    .prog-group .table th { background: transparent; }
    tr.guardando td { opacity: .7; }
  `]
})
export class AlternanciaTabComponent implements OnInit {
  private state = inject(StateService);
  private persistencia = inject(PersistenciaService);
  private catalogo = inject(CatalogoService);
  private snack = inject(MatSnackBar);
  private dialog = inject(MatDialog);

  cargandoTipos = signal(true);
  tipos = signal<TipoAlternanciaConfig[]>([]);
  filtro = signal('');

  private guardandoSet = signal<Set<string>>(new Set());
  private errorMap = signal<Map<string, string>>(new Map());

  ngOnInit() { this.cargarTipos(); }

  cargarTipos() {
    this.cargandoTipos.set(true);
    this.persistencia.cargarTiposAlternancia().subscribe({
      next: (t) => { this.tipos.set(t); this.cargandoTipos.set(false); },
      error: () => { this.cargandoTipos.set(false); this.snack.open('No se pudo cargar el catálogo de alternancia. ¿Backend activo?', 'Cerrar', { duration: 4000 }); }
    });
  }

  tipoValueFor(t: TipoAlternanciaConfig): TipoAlt {
    if (t.esSistema) return t.nombre as TipoAlt;
    return t.patronBase === 'PresencialEnSemanaA' ? 'TipoA' : t.patronBase === 'PresencialEnSemanaB' ? 'TipoB' : 'SinAlternancia';
  }

  private filas = computed<AsignaturaFila[]>(() => this.state.asignaturas().map(a => ({ asignatura: a, guardando: false, error: null })));

  filasFiltradas = computed<AsignaturaFila[]>(() => {
    const f = this.filtro().toLowerCase().trim();
    return this.filas()
      .map(fila => ({ ...fila, guardando: this.guardandoSet().has(fila.asignatura.id), error: this.errorMap().get(fila.asignatura.id) ?? null }))
      .filter(fila => !f || fila.asignatura.nombre.toLowerCase().includes(f) || this.nombreDocente(fila.asignatura.docenteId).toLowerCase().includes(f));
  });

  gruposFiltrados = computed(() => {
    const mapa = new Map<string, { programa: string; filas: AsignaturaFila[] }>();
    for (const fila of this.filasFiltradas()) {
      const progId = fila.asignatura.programaId ?? 'Sin programa';
      const progNom = this.state.programaById().get(progId)?.nombre ?? progId;
      if (!mapa.has(progId)) mapa.set(progId, { programa: progNom, filas: [] });
      mapa.get(progId)!.filas.push(fila);
    }
    return [...mapa.values()].sort((a, b) => a.programa.localeCompare(b.programa));
  });

  countTipoA = computed(() => this.state.asignaturas().filter(a => a.alternancia === 'TipoA').length);
  countTipoB = computed(() => this.state.asignaturas().filter(a => a.alternancia === 'TipoB').length);
  countSinAlt = computed(() => this.state.asignaturas().filter(a => !a.alternancia || a.alternancia === 'SinAlternancia').length);

  nombreDocente(docenteId?: string): string {
    if (!docenteId) return '—';
    return this.state.docenteById().get(docenteId)?.nombre ?? '—';
  }

  cambiarAlternancia(fila: AsignaturaFila, nuevo: string): void {
    const tipo = nuevo as TipoAlt;
    const id = fila.asignatura.id;
    this.state.updateAsignatura({ ...fila.asignatura, alternancia: tipo });
    this.guardandoSet.update(s => { const n = new Set(s); n.add(id); return n; });
    this.errorMap.update(m => { const n = new Map(m); n.delete(id); return n; });
    this.persistencia.actualizarAlternancia(id, tipo).subscribe({
      next: () => this.guardandoSet.update(s => { const n = new Set(s); n.delete(id); return n; }),
      error: (err) => {
        this.guardandoSet.update(s => { const n = new Set(s); n.delete(id); return n; });
        const msg = err?.error?.message ?? err?.message ?? 'Error al guardar';
        this.errorMap.update(m => { const n = new Map(m); n.set(id, msg); return n; });
        this.snack.open(`Error: ${msg}`, 'Cerrar', { duration: 4000 });
      }
    });
  }

  abrirTipo(tipo?: TipoAlternanciaConfig) {
    const ref = this.dialog.open(TipoAlternanciaDialogComponent, { width: '360px', maxWidth: '95vw', data: tipo ? { ...tipo } : null });
    ref.afterClosed().subscribe((dto?: Partial<TipoAlternanciaConfig>) => {
      if (!dto) return;
      const obs = tipo
        ? this.persistencia.actualizarTipoAlternancia({ ...tipo, ...dto } as TipoAlternanciaConfig)
        : this.persistencia.crearTipoAlternancia(dto);
      obs.subscribe({
        next: () => { this.snack.open(tipo ? 'Tipo actualizado.' : 'Tipo creado.', '', { duration: 3000 }); this.cargarTipos(); },
        error: (e) => this.snack.open(`Error: ${e?.error ?? 'desconocido'}`, 'Cerrar', { duration: 5000, panelClass: ['snack-error'] })
      });
    });
  }

  eliminarTipo(tipo: TipoAlternanciaConfig) {
    if (tipo.esSistema) return;
    const ref = this.dialog.open(ConfirmDeleteDialogComponent, {
      width: '320px', data: { title: 'Eliminar tipo de alternancia', message: `Se eliminará el tipo "${tipo.nombre}".` }
    });
    ref.afterClosed().subscribe(ok => {
      if (!ok) return;
      this.persistencia.eliminarTipoAlternancia(tipo.id).subscribe({
        next: () => { this.snack.open('Tipo eliminado.', '', { duration: 3000 }); this.cargarTipos(); },
        error: (e) => this.snack.open(`Error: ${e?.error ?? 'desconocido'}`, 'Cerrar', { duration: 5000, panelClass: ['snack-error'] })
      });
    });
  }
}

// ─── Popup: Crear/Editar tipo de alternancia (REQUISITOS §5) ───────────────────
@Component({
  selector: 'app-tipo-alternancia-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule],
  template: `
    <div class="pophd">{{ data ? 'Editar tipo' : 'Nuevo tipo de alternancia' }}
      <span *ngIf="esSistema" class="tag tag-neutral" style="font-size:9px">SISTEMA</span>
      <i (click)="ref.close()">✕</i>
    </div>
    <div class="popbd">
      <div class="dfield"><label>Nombre <span class="rq">*</span></label>
        <input class="input" [(ngModel)]="nombre" placeholder="Ej. Laboratorio intensivo" maxlength="100"></div>
      <div class="dfield"><label>Patrón base <span class="rq">*</span></label>
        <select class="input" [(ngModel)]="patronBase" [disabled]="esSistema">
          <option value="PresencialEnSemanaA">Presencial en semanas A (pares)</option>
          <option value="PresencialEnSemanaB">Presencial en semanas B (impares)</option>
          <option value="SinAlternancia">Sin alternancia (presencial siempre)</option>
        </select>
        <small *ngIf="esSistema" class="text-muted" style="font-size:11px">Los tipos de sistema no cambian su patrón base.</small></div>
      <div style="display:flex;gap:8px;align-items:flex-end">
        <div class="dfield" style="flex:1"><label>Semanas presenciales <span class="rq">*</span></label>
          <input class="input" type="number" [(ngModel)]="semanasPresenciales" min="0" max="52"></div>
        <div class="dfield"><label>Color</label>
          <input type="color" [(ngModel)]="color" style="width:56px;height:34px;padding:2px;border:1px solid var(--color-divider);background:var(--color-surface)"></div>
      </div>
      <label class="radio" style="cursor:pointer"><input type="checkbox" [(ngModel)]="activo" style="position:static;width:auto;height:auto;opacity:1"> Activo</label>
      <div class="popfoot">
        <button class="btn btn-secondary" (click)="ref.close()">Cancelar</button>
        <button class="btn btn-primary" [disabled]="!nombre.trim()" (click)="guardar()">Guardar</button>
      </div>
    </div>
  `
})
export class TipoAlternanciaDialogComponent {
  ref = inject(MatDialogRef<TipoAlternanciaDialogComponent>);
  data = inject(MAT_DIALOG_DATA) as TipoAlternanciaConfig | null;

  nombre = this.data?.nombre ?? '';
  patronBase: Patron = this.data?.patronBase ?? 'PresencialEnSemanaA';
  semanasPresenciales = this.data?.semanasPresenciales ?? 8;
  color = this.data?.color ?? '#6f8f6a';
  activo = this.data?.activo ?? true;
  esSistema = this.data?.esSistema ?? false;

  guardar() {
    if (!this.nombre.trim()) return;
    this.ref.close({
      nombre: this.nombre.trim(), patronBase: this.patronBase,
      semanasPresenciales: Number(this.semanasPresenciales) || 0, color: this.color, activo: this.activo
    });
  }
}
