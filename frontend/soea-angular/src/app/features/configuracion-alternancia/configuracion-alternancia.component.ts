import { Component, inject, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { StateService } from '../../core/state.service';
import { PersistenciaService } from '../../core/persistencia.service';
import { CatalogoService } from '../../core/catalogo.service';
import { Asignatura } from '../../core/models';

type TipoAlt = 'TipoA' | 'TipoB' | 'SinAlternancia';

interface AsignaturaFila {
  asignatura: Asignatura;
  guardando: boolean;
  error: string | null;
}

@Component({
  selector: 'app-configuracion-alternancia',
  standalone: true,
  imports: [CommonModule, FormsModule, MatSnackBarModule],
  template: `
    <div class="page-container">
      <div class="page-header">
        <h1 class="page-title">Configuración de Alternancia</h1>
        <p class="page-subtitle">
          Asigna el tipo de alternancia a cada asignatura.
          <strong>TipoA</strong> ocupa el laboratorio en semanas pares (Semana A);
          <strong>TipoB</strong> en semanas impares (Semana B).
        </p>
      </div>

      <!-- Barra de herramientas -->
      <div class="toolbar">
        <input
          class="search-input"
          type="text"
          placeholder="Filtrar por nombre o docente…"
          [ngModel]="filtro()"
          (ngModelChange)="filtro.set($event)"
        />
        <button class="btn-outline" (click)="cargarDesdeApi()" [disabled]="cargando()">
          {{ cargando() ? 'Cargando…' : '↺ Recargar desde BD' }}
        </button>
        <div class="resumen-chips">
          <span class="chip chip-a">TipoA: {{ countTipoA() }}</span>
          <span class="chip chip-b">TipoB: {{ countTipoB() }}</span>
          <span class="chip chip-sa">Sin alt.: {{ countSinAlt() }}</span>
        </div>
      </div>

      <!-- Sin datos -->
      <div *ngIf="!cargando() && filasFiltradas().length === 0" class="empty-state">
        <p>No hay asignaturas cargadas. Ve a <strong>Ingesta de Datos</strong> e importa el Excel primero.</p>
      </div>

      <!-- Tabla agrupada por programa -->
      <div *ngFor="let grupo of gruposFiltrados(); trackBy: trackByPrograma" class="programa-group">
        <div class="programa-header">
          <span class="programa-nombre">{{ grupo.programa }}</span>
          <span class="programa-count">{{ grupo.filas.length }} asignatura(s)</span>
        </div>

        <table class="alt-table">
          <thead>
            <tr>
              <th class="col-nombre">Asignatura</th>
              <th class="col-docente">Docente</th>
              <th class="col-sesiones">Ses/sem · Horas</th>
              <th class="col-tipo">Tipo alternancia</th>
              <th class="col-estado">Estado</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let fila of grupo.filas; trackBy: trackByAsignaturaId" [class.guardando-row]="fila.guardando">
              <td class="col-nombre">
                <span class="nombre-text">{{ fila.asignatura.nombre }}</span>
                <span *ngIf="fila.asignatura.codigo" class="codigo-badge">{{ fila.asignatura.codigo }}</span>
              </td>
              <td class="col-docente">
                {{ nombreDocente(fila.asignatura.docenteId) }}
              </td>
              <td class="col-sesiones">
                {{ fila.asignatura.sesionesPorSemana }}×{{ fila.asignatura.horasPorSesion }}h
              </td>
              <td class="col-tipo">
                <select
                  class="tipo-select"
                  [class.select-tipoa]="fila.asignatura.alternancia === 'TipoA'"
                  [class.select-tipob]="fila.asignatura.alternancia === 'TipoB'"
                  [class.select-sinalt]="fila.asignatura.alternancia === 'SinAlternancia' || !fila.asignatura.alternancia"
                  [ngModel]="fila.asignatura.alternancia || 'SinAlternancia'"
                  (ngModelChange)="cambiarAlternancia(fila, $event)"
                  [disabled]="fila.guardando"
                >
                  <option value="TipoA">TipoA — Semana A (pares)</option>
                  <option value="TipoB">TipoB — Semana B (impares)</option>
                  <option value="SinAlternancia">Sin alternancia</option>
                </select>
              </td>
              <td class="col-estado">
                <span *ngIf="fila.guardando" class="estado-guardando">Guardando…</span>
                <span *ngIf="fila.error" class="estado-error" [title]="fila.error">⚠ Error</span>
                <span *ngIf="!fila.guardando && !fila.error" class="estado-ok">✓</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  `,
  styles: [`
    .page-container { padding: 24px; max-width: 1100px; margin: 0 auto; display: flex; flex-direction: column; gap: 20px; }
    .page-header { display: flex; flex-direction: column; gap: 4px; }
    .page-title { margin: 0; font-size: 24px; font-weight: 500; }
    .page-subtitle { margin: 0; color: #616161; font-size: 14px; }

    .toolbar { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .search-input { padding: 8px 12px; border: 1px solid #e0e0e0; border-radius: 6px; font-size: 14px; min-width: 240px; }
    .btn-outline { padding: 7px 14px; border: 1px solid #007bff; color: #007bff; background: white; border-radius: 6px; cursor: pointer; font-size: 14px; }
    .btn-outline:hover:not(:disabled) { background: #e3f2fd; }
    .btn-outline:disabled { opacity: 0.5; cursor: not-allowed; }
    .resumen-chips { display: flex; gap: 8px; margin-left: auto; }
    .chip { padding: 4px 10px; border-radius: 12px; font-size: 12px; font-weight: 500; }
    .chip-a  { background: #fff8e1; color: #f57f17; border: 1px solid #ffe082; }
    .chip-b  { background: #e8f5e9; color: #2e7d32; border: 1px solid #a5d6a7; }
    .chip-sa { background: #f5f5f5; color: #616161; border: 1px solid #e0e0e0; }

    .empty-state { text-align: center; padding: 48px; background: white; border-radius: 8px; border: 1px solid #e0e0e0; color: #757575; }

    .programa-group { background: white; border-radius: 8px; border: 1px solid #e0e0e0; overflow: hidden; }
    .programa-header { display: flex; align-items: center; justify-content: space-between; padding: 12px 16px; background: #f5f5f5; border-bottom: 1px solid #e0e0e0; }
    .programa-nombre { font-weight: 500; font-size: 14px; }
    .programa-count  { font-size: 12px; color: #757575; }

    .alt-table { width: 100%; border-collapse: collapse; font-size: 13px; }
    .alt-table th { padding: 10px 14px; text-align: left; color: #616161; font-weight: 500; font-size: 12px; border-bottom: 1px solid #f0f0f0; background: #fafafa; }
    .alt-table td { padding: 10px 14px; border-bottom: 1px solid #f5f5f5; vertical-align: middle; }
    .alt-table tr:last-child td { border-bottom: none; }
    .alt-table tr:hover td { background: #fafafa; }
    .guardando-row td { opacity: 0.7; }

    .col-nombre  { width: 35%; }
    .col-docente { width: 20%; color: #616161; }
    .col-sesiones{ width: 10%; color: #757575; }
    .col-tipo    { width: 25%; }
    .col-estado  { width: 10%; text-align: center; }

    .nombre-text { font-weight: 500; }
    .codigo-badge { margin-left: 6px; font-size: 11px; color: #9e9e9e; background: #f5f5f5; padding: 1px 5px; border-radius: 4px; font-family: monospace; }

    .tipo-select { width: 100%; padding: 6px 8px; border-radius: 5px; border: 1px solid; font-size: 13px; cursor: pointer; outline: none; }
    .tipo-select:focus { box-shadow: 0 0 0 2px rgba(0,123,255,.25); }
    .select-tipoa { border-color: #ffe082; background: #fff8e1; color: #f57f17; }
    .select-tipob { border-color: #a5d6a7; background: #e8f5e9; color: #2e7d32; }
    .select-sinalt{ border-color: #e0e0e0; background: #fafafa; color: #616161; }

    .estado-guardando { color: #1565c0; font-size: 12px; }
    .estado-error     { color: #c62828; font-size: 12px; cursor: help; }
    .estado-ok        { color: #2e7d32; font-size: 14px; }
  `]
})
export class ConfiguracionAlternanciaComponent {
  private state       = inject(StateService);
  private persistencia = inject(PersistenciaService);
  private catalogo    = inject(CatalogoService);
  private snackBar    = inject(MatSnackBar);

  cargando  = signal(false);
  filtro    = signal('');

  // ── Filas reactivas (una por asignatura) ──────────────────────────────────
  private filas = computed<AsignaturaFila[]>(() =>
    this.state.asignaturas().map(a => ({ asignatura: a, guardando: false, error: null }))
  );

  // Necesitamos estado mutable por fila → usamos signal separado para guardando/error
  private guardandoSet = signal<Set<string>>(new Set());
  private errorMap     = signal<Map<string, string>>(new Map());

  filasFiltradas = computed<AsignaturaFila[]>(() => {
    const f = this.filtro().toLowerCase().trim();
    return this.filas()
      .map(fila => ({
        ...fila,
        guardando: this.guardandoSet().has(fila.asignatura.id),
        error:     this.errorMap().get(fila.asignatura.id) ?? null
      }))
      .filter(fila => {
        if (!f) return true;
        const nombre  = fila.asignatura.nombre.toLowerCase();
        const docente = this.nombreDocente(fila.asignatura.docenteId).toLowerCase();
        return nombre.includes(f) || docente.includes(f);
      });
  });

  gruposFiltrados = computed(() => {
    const mapa = new Map<string, { programa: string; filas: AsignaturaFila[] }>();
    for (const fila of this.filasFiltradas()) {
      const progId  = fila.asignatura.programaId ?? 'Sin programa';
      const progNom = this.state.programaById().get(progId)?.nombre ?? progId;
      if (!mapa.has(progId)) mapa.set(progId, { programa: progNom, filas: [] });
      mapa.get(progId)!.filas.push(fila);
    }
    return [...mapa.values()].sort((a, b) => a.programa.localeCompare(b.programa));
  });

  countTipoA  = computed(() => this.state.asignaturas().filter(a => a.alternancia === 'TipoA').length);
  countTipoB  = computed(() => this.state.asignaturas().filter(a => a.alternancia === 'TipoB').length);
  countSinAlt = computed(() =>
    this.state.asignaturas().filter(a => !a.alternancia || a.alternancia === 'SinAlternancia').length);

  // ── TrackBy (evita que Angular destruya el DOM al recomputar el signal) ────
  trackByPrograma(_: number, grupo: { programa: string }): string {
    return grupo.programa;
  }
  trackByAsignaturaId(_: number, fila: AsignaturaFila): string {
    return fila.asignatura.id;
  }

  // ── Helpers ────────────────────────────────────────────────────────────────
  nombreDocente(docenteId?: string): string {
    if (!docenteId) return '—';
    return this.state.docenteById().get(docenteId)?.nombre ?? '—';
  }

  // ── Acciones ───────────────────────────────────────────────────────────────
  cambiarAlternancia(fila: AsignaturaFila, nuevo: string): void {
    const tipo = nuevo as TipoAlt;
    const id   = fila.asignatura.id;

    // Actualización optimista local
    this.state.updateAsignatura({ ...fila.asignatura, alternancia: tipo });

    this.guardandoSet.update(s => { const n = new Set(s); n.add(id); return n; });
    this.errorMap.update(m => { const n = new Map(m); n.delete(id); return n; });

    this.persistencia.actualizarAlternancia(id, tipo).subscribe({
      next: () => {
        this.guardandoSet.update(s => { const n = new Set(s); n.delete(id); return n; });
      },
      error: (err) => {
        // Revertir optimismo
        this.state.updateAsignatura({ ...fila.asignatura });
        this.guardandoSet.update(s => { const n = new Set(s); n.delete(id); return n; });
        const msg = err?.error?.message ?? err?.message ?? 'Error al guardar';
        this.errorMap.update(m => { const n = new Map(m); n.set(id, msg); return n; });
        this.snackBar.open(`Error: ${msg}`, 'Cerrar', { duration: 4000 });
      }
    });
  }

  cargarDesdeApi(): void {
    this.cargando.set(true);
    this.catalogo.cargarTodo().subscribe({
      next: (resumen) => {
        this.cargando.set(false);
        this.snackBar.open(`${resumen.asignaturas} asignatura(s) cargadas.`, 'Cerrar', { duration: 3000 });
      },
      error: () => {
        this.cargando.set(false);
        this.snackBar.open('Error al cargar desde la BD.', 'Cerrar', { duration: 4000 });
      }
    });
  }
}
