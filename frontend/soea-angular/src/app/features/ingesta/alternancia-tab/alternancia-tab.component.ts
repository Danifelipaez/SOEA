import { Component, inject, computed, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { StateService } from '../../../core/state.service';
import { PersistenciaService } from '../../../core/persistencia.service';
import { Asignatura, CriterioCesionAlternancia } from '../../../core/models';

interface AsignaturaFila {
  asignatura: Asignatura;
  guardando: boolean;
  error: string | null;
}

@Component({
  selector: 'app-alternancia-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, MatSnackBarModule],
  template: `
    <div class="tab-content">

      <!-- Candidatas a alternancia -->
      <section>
        <h3 class="sec">Candidatas a alternancia</h3>
        <p class="text-muted" style="font-size:12.5px;margin:2px 0 10px">
          Marca qué asignaturas puede ceder el algoritmo a alternancia cuando el espacio físico no alcanza.
        </p>

        <div class="toolbar">
          <input class="input search" type="text" placeholder="🔍 Filtrar por nombre o docente…"
                 [ngModel]="filtro()" (ngModelChange)="filtro.set($event)">
          <div class="resumen">
            <span class="chip"><span class="dot" style="background:var(--color-accent)"></span>{{ countCandidatas() }} candidatas de {{ totalAsignaturas() }} asignaturas</span>
          </div>
        </div>

        @if (filasFiltradas().length === 0) {
          <div class="empty">No hay asignaturas cargadas. Impórtalas desde la pestaña <strong>Asignaturas</strong> primero.</div>
        }

        @for (grupo of gruposFiltrados(); track grupo.programa) {
          <div class="prog-group">
            <div class="prog-head"><span>{{ grupo.programa }}</span><span class="text-muted">{{ grupo.filas.length }} asignatura(s)</span></div>
            <table class="table">
              <thead><tr><th style="width:44%">Asignatura</th><th>Docentes (grupos)</th><th style="width:150px">Candidata a alternancia</th><th style="width:70px">Estado</th></tr></thead>
              <tbody>
                @for (fila of grupo.filas; track fila.asignatura.id) {
                  <tr [class.guardando]="fila.guardando">
                    <td><b>{{ fila.asignatura.nombre }}</b> @if (fila.asignatura.codigo) { <span class="text-muted" style="font-size:11px">· {{ fila.asignatura.codigo }}</span> }</td>
                    <td class="text-muted">{{ docentesDeAsignatura(fila.asignatura.id) }}</td>
                    <td style="text-align:center">
                      <input type="checkbox" [ngModel]="fila.asignatura.esCandidataAlternancia ?? false"
                             (ngModelChange)="cambiarCandidatura(fila, $event)" [disabled]="fila.guardando"
                             title="Candidata a ceder a alternancia si el algoritmo agota el espacio físico">
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

      <!-- Orden de cesión a alternancia -->
      <section>
        <h3 class="sec">Orden de cesión a alternancia</h3>
        <p class="text-muted" style="font-size:12.5px;margin:2px 0 10px">
          Cuando el algoritmo agota el espacio físico disponible, cede sesiones a alternancia siguiendo
          este orden. Desactiva un criterio para excluirlo por completo de la cesión.
        </p>
        @if (cargandoCriterios()) {
          <p class="text-muted">Cargando…</p>
        } @else {
          <table class="table" style="max-width:480px">
            <thead><tr><th style="width:60px">Orden</th><th>Criterio</th><th style="width:90px">Activo</th><th style="width:70px"></th></tr></thead>
            <tbody>
              @for (c of criterios(); track c.id; let i = $index) {
                <tr>
                  <td>{{ c.orden }}</td>
                  <td>{{ etiquetaCriterio(c.criterio) }}
                    @if (c.criterio === 'MultiplesSesiones') {
                      <span class="material-icons" style="font-size:15px;vertical-align:-3px;color:var(--color-neutral-500)"
                            title="No vuelve candidata a una asignatura por sí sola: solo decide qué candidata cede primero cuando varias ya califican por otro criterio.">info</span>
                    }
                  </td>
                  <td style="text-align:center">
                    <input type="checkbox" [ngModel]="c.activo" (ngModelChange)="toggleCriterioActivo(c, $event)">
                  </td>
                  <td style="text-align:right;white-space:nowrap">
                    <span class="material-icons ic-edit" [class.disabled]="i === 0"
                          (click)="moverCriterio(c, -1)" title="Subir prioridad">arrow_upward</span>
                    <span class="material-icons ic-edit" [class.disabled]="i === criterios().length - 1"
                          (click)="moverCriterio(c, 1)" title="Bajar prioridad">arrow_downward</span>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      </section>
    </div>
  `,
  styles: [`
    .tab-content { padding: 20px 0; display: flex; flex-direction: column; gap: 24px; }
    .toolbar { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; }
    .search { min-width: 240px; width: auto; flex: 1; max-width: 340px; }
    .resumen { display: flex; gap: 8px; margin-left: auto; }
    .empty { text-align: center; padding: 28px; border: 1px solid var(--color-divider); color: var(--color-neutral-500); }
    .prog-group { border: 1px solid var(--color-divider); margin-bottom: 14px; }
    .prog-head { display: flex; align-items: center; justify-content: space-between; padding: 9px 12px; background: var(--color-neutral-100); border-bottom: 1px solid var(--color-divider); font: 600 13px var(--font-heading); }
    .prog-group .table th { background: transparent; }
    tr.guardando td { opacity: .7; }
    .ic-edit.disabled { opacity: .3; pointer-events: none; }
  `]
})
export class AlternanciaTabComponent implements OnInit {
  private state = inject(StateService);
  private persistencia = inject(PersistenciaService);
  private snack = inject(MatSnackBar);

  filtro = signal('');

  cargandoCriterios = signal(true);
  criterios = signal<CriterioCesionAlternancia[]>([]);

  private guardandoSet = signal<Set<string>>(new Set());
  private errorMap = signal<Map<string, string>>(new Map());

  ngOnInit() { this.cargarCriterios(); }

  cargarCriterios() {
    this.cargandoCriterios.set(true);
    this.persistencia.cargarCriteriosCesion().subscribe({
      next: (c) => { this.criterios.set(this.ordenarCriterios(c)); this.cargandoCriterios.set(false); },
      error: () => { this.cargandoCriterios.set(false); this.snack.open('No se pudo cargar la lista de criterios de cesión.', 'Cerrar', { duration: 4000 }); }
    });
  }

  private ordenarCriterios(c: CriterioCesionAlternancia[]): CriterioCesionAlternancia[] {
    return [...c].sort((a, b) => a.orden - b.orden);
  }

  etiquetaCriterio(c: CriterioCesionAlternancia['criterio']): string {
    switch (c) {
      case 'Electiva': return 'Asignaturas electivas';
      case 'Optativa': return 'Asignaturas optativas';
      case 'MultiplesSesiones': return '2 o más sesiones semanales';
      case 'Elegible': return 'Marcadas como candidatas';
    }
  }

  toggleCriterioActivo(c: CriterioCesionAlternancia, activo: boolean): void {
    this.persistencia.actualizarCriterioCesion(c.id, { activo }).subscribe({
      next: (lista) => this.criterios.set(this.ordenarCriterios(lista)),
      error: (e) => this.snack.open(`Error: ${e?.error ?? 'desconocido'}`, 'Cerrar', { duration: 4000, panelClass: ['snack-error'] })
    });
  }

  moverCriterio(c: CriterioCesionAlternancia, direccion: -1 | 1): void {
    const lista = this.criterios();
    const i = lista.findIndex(x => x.id === c.id);
    const j = i + direccion;
    if (i < 0 || j < 0 || j >= lista.length) return;
    this.persistencia.actualizarCriterioCesion(c.id, { orden: lista[j].orden }).subscribe({
      next: (nueva) => this.criterios.set(this.ordenarCriterios(nueva)),
      error: (e) => this.snack.open(`Error: ${e?.error ?? 'desconocido'}`, 'Cerrar', { duration: 4000, panelClass: ['snack-error'] })
    });
  }

  cambiarCandidatura(fila: AsignaturaFila, candidata: boolean): void {
    const id = fila.asignatura.id;
    this.state.updateAsignatura({ ...fila.asignatura, esCandidataAlternancia: candidata });
    this.guardandoSet.update(s => { const n = new Set(s); n.add(id); return n; });
    this.errorMap.update(m => { const n = new Map(m); n.delete(id); return n; });
    this.persistencia.actualizarElegibilidadAlternancia(id, candidata).subscribe({
      next: () => this.guardandoSet.update(s => { const n = new Set(s); n.delete(id); return n; }),
      error: (err) => {
        this.guardandoSet.update(s => { const n = new Set(s); n.delete(id); return n; });
        const msg = err?.error?.message ?? err?.message ?? 'Error al guardar';
        this.errorMap.update(m => { const n = new Map(m); n.set(id, msg); return n; });
        this.snack.open(`Error: ${msg}`, 'Cerrar', { duration: 4000 });
      }
    });
  }

  private filas = computed<AsignaturaFila[]>(() => this.state.asignaturas().map(a => ({ asignatura: a, guardando: false, error: null })));

  totalAsignaturas = computed(() => this.state.asignaturas().length);
  countCandidatas = computed(() => this.state.asignaturas().filter(a => a.esCandidataAlternancia).length);

  filasFiltradas = computed<AsignaturaFila[]>(() => {
    const f = this.filtro().toLowerCase().trim();
    return this.filas()
      .map(fila => ({ ...fila, guardando: this.guardandoSet().has(fila.asignatura.id), error: this.errorMap().get(fila.asignatura.id) ?? null }))
      .filter(fila => !f || fila.asignatura.nombre.toLowerCase().includes(f) || this.docentesDeAsignatura(fila.asignatura.id).toLowerCase().includes(f));
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

  /** Docentes de la asignatura, derivados de sus grupos (Fase 2: el docente vive en el grupo). */
  docentesDeAsignatura(asignaturaId: string): string {
    const docById = this.state.docenteById();
    const nombres = [...new Set(
      this.state.grupos()
        .filter(g => g.asignaturaId === asignaturaId && g.docenteId)
        .map(g => docById.get(g.docenteId!)?.nombre)
        .filter((n): n is string => !!n)
    )];
    if (!nombres.length) return '—';
    return nombres.slice(0, 2).join(', ') + (nombres.length > 2 ? `, +${nombres.length - 2}` : '');
  }
}
