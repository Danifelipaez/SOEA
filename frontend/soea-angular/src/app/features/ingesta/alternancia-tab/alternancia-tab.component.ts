import { Component, inject, computed, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialogModule, MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { StateService } from '../../../core/state.service';
import { PersistenciaService } from '../../../core/persistencia.service';
import { CatalogoService } from '../../../core/catalogo.service';
import { Asignatura, TipoAlternanciaConfig } from '../../../core/models';

type Patron = TipoAlternanciaConfig['patronBase'];
type TipoAlt = 'TipoA' | 'TipoB' | 'SinAlternancia';

const PATRON_LABEL: Record<Patron, string> = {
  PresencialEnSemanaA: 'Presencial en semanas A (pares) · virtual en B',
  PresencialEnSemanaB: 'Presencial en semanas B (impares) · virtual en A',
  SinAlternancia:      'Sin alternancia (presencial siempre)'
};

interface AsignaturaFila {
  asignatura: Asignatura;
  guardando: boolean;
  error: string | null;
}

/**
 * Fusiona lo que antes eran dos pantallas sueltas (tipos-alternancia +
 * configuracion-alternancia) en una sola pestaña del flujo de Catálogo —
 * decisión validada en docs/MAPEO_FLUJOS_FRONTEND.md.
 */
@Component({
  selector: 'app-alternancia-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, MatIconModule, MatDialogModule, MatSnackBarModule],
  template: `
    <div class="page-container">

      <!-- Tipos de alternancia -->
      <section>
        <div class="section-head">
          <h3 class="sec-title">Tipos de alternancia</h3>
          <button mat-stroked-button (click)="abrirTipo()">
            <mat-icon>add</mat-icon> Nuevo tipo
          </button>
        </div>
        @if (cargandoTipos()) {
          <p class="muted">Cargando…</p>
        } @else {
          <div class="chips">
            @for (t of tipos(); track t.id) {
              <span class="chip">
                <span class="dot" [style.background]="t.color"></span>
                {{ t.nombre }}
                @if (t.esSistema) { <span class="tag-sys">sistema</span> }
                <button mat-icon-button class="chip-edit" (click)="abrirTipo(t)" title="Editar">
                  <mat-icon>edit</mat-icon>
                </button>
                @if (!t.esSistema) {
                  <button mat-icon-button class="chip-edit" (click)="eliminarTipo(t)" title="Eliminar">
                    <mat-icon>delete_outline</mat-icon>
                  </button>
                }
              </span>
            }
          </div>
        }
      </section>

      <!-- Asignación por asignatura (unifica lo que antes eran 2 pantallas) -->
      <section>
        <h3 class="sec-title">Asignación por asignatura</h3>
        <p class="hint">Una fila por asignatura.</p>

        <div class="toolbar">
          <input class="search-input" type="text" placeholder="Filtrar por nombre o docente…"
                 [ngModel]="filtro()" (ngModelChange)="filtro.set($event)" />
          <div class="resumen-chips">
            <span class="mini-chip mini-a">TipoA: {{ countTipoA() }}</span>
            <span class="mini-chip mini-b">TipoB: {{ countTipoB() }}</span>
            <span class="mini-chip mini-sa">Sin alt.: {{ countSinAlt() }}</span>
          </div>
        </div>

        @if (filasFiltradas().length === 0) {
          <div class="empty-state">
            <p>No hay asignaturas cargadas. Impórtalas desde la pestaña <strong>Asignaturas</strong> primero.</p>
          </div>
        }

        @for (grupo of gruposFiltrados(); track grupo.programa) {
          <div class="programa-group">
            <div class="programa-header">
              <span class="programa-nombre">{{ grupo.programa }}</span>
              <span class="programa-count">{{ grupo.filas.length }} asignatura(s)</span>
            </div>
            <table class="alt-table">
              <thead>
                <tr>
                  <th class="col-nombre">Asignatura</th>
                  <th class="col-docente">Docente</th>
                  <th class="col-tipo">Tipo de alternancia</th>
                  <th class="col-estado">Estado</th>
                </tr>
              </thead>
              <tbody>
                @for (fila of grupo.filas; track fila.asignatura.id) {
                  <tr [class.guardando-row]="fila.guardando">
                    <td class="col-nombre">
                      <span class="nombre-text">{{ fila.asignatura.nombre }}</span>
                      @if (fila.asignatura.codigo) { <span class="codigo-badge">{{ fila.asignatura.codigo }}</span> }
                    </td>
                    <td class="col-docente">{{ nombreDocente(fila.asignatura.docenteId) }}</td>
                    <td class="col-tipo">
                      <select class="tipo-select"
                              [ngModel]="fila.asignatura.alternancia || 'SinAlternancia'"
                              (ngModelChange)="cambiarAlternancia(fila, $event)"
                              [disabled]="fila.guardando">
                        @for (t of tipos(); track t.id) {
                          <option [value]="tipoValueFor(t)">{{ t.nombre }}</option>
                        }
                      </select>
                    </td>
                    <td class="col-estado">
                      @if (fila.guardando) { <span class="estado-guardando">Guardando…</span> }
                      @if (fila.error) { <span class="estado-error" [title]="fila.error">⚠ Error</span> }
                      @if (!fila.guardando && !fila.error) { <span class="estado-ok">✓</span> }
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
    .page-container { padding: 8px 0 16px; display: flex; flex-direction: column; gap: 28px; }
    .section-head { display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px; }
    .sec-title { margin: 0 0 4px; font-size: 15px; font-weight: 600; text-transform: uppercase; letter-spacing: .04em; color: #424242; }
    .hint { margin: 0 0 10px; color: #757575; font-size: 12.5px; }
    .muted { color: #9e9e9e; font-size: 13px; }

    .chips { display: flex; gap: 10px; flex-wrap: wrap; }
    .chip { display: inline-flex; align-items: center; gap: 7px; padding: 5px 6px 5px 12px; border: 1px solid #e0e0e0; border-radius: 20px; font-size: 13px; background: #fff; }
    .dot { width: 11px; height: 11px; border-radius: 50%; flex: none; }
    .tag-sys { background: #ede7f6; color: #512da8; border-radius: 10px; padding: 1px 8px; font-size: 10px; }
    .chip-edit { width: 26px !important; height: 26px !important; line-height: 26px !important; }
    .chip-edit mat-icon { font-size: 15px; width: 15px; height: 15px; }

    .toolbar { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; }
    .search-input { padding: 8px 12px; border: 1px solid #e0e0e0; border-radius: 6px; font-size: 14px; min-width: 240px; }
    .resumen-chips { display: flex; gap: 8px; margin-left: auto; }
    .mini-chip { padding: 4px 10px; border-radius: 12px; font-size: 12px; font-weight: 500; }
    .mini-a  { background: #e3ecf3; color: #35526b; }
    .mini-b  { background: #f0e7dc; color: #6d5530; }
    .mini-sa { background: #f5f5f5; color: #616161; }

    .empty-state { text-align: center; padding: 32px; background: #fafafa; border-radius: 8px; border: 1px solid #e0e0e0; color: #757575; }

    .programa-group { background: #fff; border-radius: 8px; border: 1px solid #e0e0e0; overflow: hidden; margin-bottom: 14px; }
    .programa-header { display: flex; align-items: center; justify-content: space-between; padding: 10px 14px; background: #f5f5f5; border-bottom: 1px solid #e0e0e0; }
    .programa-nombre { font-weight: 500; font-size: 13px; }
    .programa-count { font-size: 12px; color: #757575; }

    .alt-table { width: 100%; border-collapse: collapse; font-size: 13px; }
    .alt-table th { padding: 9px 14px; text-align: left; color: #616161; font-weight: 500; font-size: 12px; border-bottom: 1px solid #f0f0f0; background: #fafafa; }
    .alt-table td { padding: 9px 14px; border-bottom: 1px solid #f5f5f5; vertical-align: middle; }
    .alt-table tr:last-child td { border-bottom: none; }
    .guardando-row td { opacity: .7; }
    .col-nombre { width: 40%; } .col-docente { width: 25%; color: #616161; } .col-tipo { width: 25%; } .col-estado { width: 10%; text-align: center; }
    .nombre-text { font-weight: 500; }
    .codigo-badge { margin-left: 6px; font-size: 11px; color: #9e9e9e; background: #f5f5f5; padding: 1px 5px; border-radius: 4px; font-family: monospace; }
    .tipo-select { width: 100%; padding: 6px 8px; border-radius: 5px; border: 1px solid #d0d0d0; font-size: 13px; cursor: pointer; outline: none; background: #fff; }
    .estado-guardando { color: #1565c0; font-size: 12px; }
    .estado-error { color: #c62828; font-size: 12px; cursor: help; }
    .estado-ok { color: #2e7d32; font-size: 14px; }
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
      error: () => {
        this.cargandoTipos.set(false);
        this.snack.open('No se pudo cargar el catálogo de alternancia. ¿Backend activo?', 'Cerrar', { duration: 4000 });
      }
    });
  }

  /** Los tipos personalizados no tienen equivalente en Asignatura.alternancia (solo TipoA/TipoB/SinAlternancia) — se mapean a su patrón base. */
  tipoValueFor(t: TipoAlternanciaConfig): TipoAlt {
    if (t.esSistema) return t.nombre as TipoAlt;
    return t.patronBase === 'PresencialEnSemanaA' ? 'TipoA'
      : t.patronBase === 'PresencialEnSemanaB' ? 'TipoB'
      : 'SinAlternancia';
  }

  private filas = computed<AsignaturaFila[]>(() =>
    this.state.asignaturas().map(a => ({ asignatura: a, guardando: false, error: null }))
  );

  filasFiltradas = computed<AsignaturaFila[]>(() => {
    const f = this.filtro().toLowerCase().trim();
    return this.filas()
      .map(fila => ({
        ...fila,
        guardando: this.guardandoSet().has(fila.asignatura.id),
        error: this.errorMap().get(fila.asignatura.id) ?? null
      }))
      .filter(fila => {
        if (!f) return true;
        const nombre = fila.asignatura.nombre.toLowerCase();
        const docente = this.nombreDocente(fila.asignatura.docenteId).toLowerCase();
        return nombre.includes(f) || docente.includes(f);
      });
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
  countSinAlt = computed(() =>
    this.state.asignaturas().filter(a => !a.alternancia || a.alternancia === 'SinAlternancia').length);

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
        this.state.updateAsignatura({ ...fila.asignatura });
        this.guardandoSet.update(s => { const n = new Set(s); n.delete(id); return n; });
        const msg = err?.error?.message ?? err?.message ?? 'Error al guardar';
        this.errorMap.update(m => { const n = new Map(m); n.set(id, msg); return n; });
        this.snack.open(`Error: ${msg}`, 'Cerrar', { duration: 4000 });
      }
    });
  }

  abrirTipo(tipo?: TipoAlternanciaConfig) {
    const ref = this.dialog.open(TipoAlternanciaDialogComponent, {
      width: '520px', maxWidth: '95vw', data: tipo ? { ...tipo } : null
    });
    ref.afterClosed().subscribe((dto?: Partial<TipoAlternanciaConfig>) => {
      if (!dto) return;
      const obs = tipo
        ? this.persistencia.actualizarTipoAlternancia({ ...tipo, ...dto } as TipoAlternanciaConfig)
        : this.persistencia.crearTipoAlternancia(dto);
      obs.subscribe({
        next: () => { this.snack.open(tipo ? 'Tipo actualizado.' : 'Tipo creado.', 'Cerrar', { duration: 3000 }); this.cargarTipos(); },
        error: (e) => this.snack.open(`Error: ${e?.error ?? 'desconocido'}`, 'Cerrar', { duration: 5000, panelClass: ['snack-error'] })
      });
    });
  }

  eliminarTipo(tipo: TipoAlternanciaConfig) {
    if (tipo.esSistema) return;
    if (!window.confirm(`¿Eliminar el tipo "${tipo.nombre}"?`)) return;
    this.persistencia.eliminarTipoAlternancia(tipo.id).subscribe({
      next: () => { this.snack.open('Tipo eliminado.', 'Cerrar', { duration: 3000 }); this.cargarTipos(); },
      error: (e) => this.snack.open(`Error: ${e?.error ?? 'desconocido'}`, 'Cerrar', { duration: 5000, panelClass: ['snack-error'] })
    });
  }
}

// ─── Dialog crear/editar tipo (movido tal cual desde tipos-alternancia.component.ts) ──

@Component({
  selector: 'app-tipo-alternancia-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data ? 'Editar tipo' : 'Nuevo tipo de alternancia' }}</h2>
    <mat-dialog-content class="form">
      <label class="field">
        <span>Nombre</span>
        <input [(ngModel)]="nombre" placeholder="Ej. Laboratorio intensivo" maxlength="100">
      </label>
      <label class="field">
        <span>Patrón base</span>
        <select [(ngModel)]="patronBase" [disabled]="esSistema">
          <option value="PresencialEnSemanaA">Presencial en semanas A (pares)</option>
          <option value="PresencialEnSemanaB">Presencial en semanas B (impares)</option>
          <option value="SinAlternancia">Sin alternancia (presencial siempre)</option>
        </select>
        @if (esSistema) { <small class="hint">Los tipos de sistema no cambian su patrón base.</small> }
      </label>
      <label class="field">
        <span>Semanas presenciales <small class="hint">(informativo)</small></span>
        <input type="number" [(ngModel)]="semanasPresenciales" min="0" max="52">
      </label>
      <label class="field">
        <span>Color</span>
        <input type="color" [(ngModel)]="color" class="color-input">
      </label>
      @if (data) {
        <label class="check">
          <input type="checkbox" [(ngModel)]="activo"> Activo
        </label>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancelar</button>
      <button mat-flat-button color="primary" class="primary-button"
              [disabled]="!nombre.trim()" (click)="guardar()">Guardar</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .form { display: flex; flex-direction: column; gap: 14px; min-width: 380px; padding-top: 8px; }
    .field { display: flex; flex-direction: column; gap: 4px; font-size: 13px; color: #424242; }
    .field input, .field select { padding: 8px 10px; border: 1px solid #bdbdbd; border-radius: 4px; font-size: 14px; outline: none; }
    .field input:focus, .field select:focus { border-color: #1976d2; }
    .color-input { width: 60px; height: 36px; padding: 2px; }
    .hint { color: #9e9e9e; font-weight: 400; }
    .check { display: flex; align-items: center; gap: 8px; font-size: 14px; }
  `]
})
export class TipoAlternanciaDialogComponent {
  private ref = inject(MatDialogRef<TipoAlternanciaDialogComponent>);
  data = inject(MAT_DIALOG_DATA) as TipoAlternanciaConfig | null;

  nombre = this.data?.nombre ?? '';
  patronBase: Patron = this.data?.patronBase ?? 'PresencialEnSemanaA';
  semanasPresenciales = this.data?.semanasPresenciales ?? 8;
  color = this.data?.color ?? '#3f51b5';
  activo = this.data?.activo ?? true;
  esSistema = this.data?.esSistema ?? false;

  guardar() {
    if (!this.nombre.trim()) return;
    this.ref.close({
      nombre: this.nombre.trim(),
      patronBase: this.patronBase,
      semanasPresenciales: Number(this.semanasPresenciales) || 0,
      color: this.color,
      activo: this.activo
    });
  }
}
