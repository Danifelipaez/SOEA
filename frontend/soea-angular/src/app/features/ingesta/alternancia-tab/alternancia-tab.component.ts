import { Component, inject, computed, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { StateService } from '../../../core/state.service';
import { PersistenciaService } from '../../../core/persistencia.service';
import { Asignatura, CriterioCesionAlternancia, TipoEspacioAlternancia } from '../../../core/models';
import { diagnosticarAlternancia } from './diagnostico-alternancia';
import { mensajeErrorHttp } from '../../../core/http-error.util';
import { porTexto } from '../../../core/orden';

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
        <h3 class="sec">Puede alternar</h3>
        <p class="text-muted" style="font-size:12.5px;margin:2px 0 10px">
          Marque las asignaturas que pueden alternar (una semana presencial y otra virtual) si no alcanzan las aulas.
        </p>

        <div class="toolbar">
          <input class="input search" type="text" placeholder="🔍 Filtrar por nombre o docente…"
                 [ngModel]="filtro()" (ngModelChange)="filtro.set($event)">
          <div class="resumen">
            <span class="chip"><span class="dot" style="background:var(--color-accent)"></span>{{ countCandidatas() }} pueden alternar de {{ totalAsignaturas() }} asignaturas</span>
          </div>
        </div>

        @if (filasFiltradas().length === 0) {
          <div class="empty">No hay asignaturas cargadas. Impórtelas desde la pestaña <strong>Asignaturas</strong> primero.</div>
        }

        @for (grupo of gruposFiltrados(); track grupo.id) {
          <div class="prog-group">
            <div class="prog-head"><span>{{ grupo.programa }}</span><span class="text-muted">{{ grupo.filas.length }} asignatura(s)</span></div>
            <table class="table">
              <thead><tr><th style="width:44%">Asignatura</th><th>Docentes (grupos)</th><th style="width:150px">Puede alternar</th><th style="width:70px">Estado</th></tr></thead>
              <tbody>
                @for (fila of grupo.filas; track fila.asignatura.id) {
                  <tr [class.guardando]="fila.guardando">
                    <td><b>{{ fila.asignatura.nombre }}</b> @if (fila.asignatura.codigo) { <span class="text-muted" style="font-size:11px">· {{ fila.asignatura.codigo }}</span> }</td>
                    <td class="text-muted">{{ docentesDeAsignatura(fila.asignatura.id) }}</td>
                    <td style="text-align:center">
                      <input type="checkbox" [ngModel]="fila.asignatura.esCandidataAlternancia ?? false"
                             (ngModelChange)="cambiarCandidatura(fila, $event)" [disabled]="fila.guardando"
                             title="Si faltan aulas, esta asignatura puede alternar semanas">
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

      <!-- Límite por tipo de espacio -->
      <section>
        <h3 class="sec">Qué clases pueden alternar</h3>
        <p class="text-muted" style="font-size:12.5px;margin:2px 0 10px">
          Además de las asignaturas marcadas y la prioridad, elija en qué espacios se permite alternar.
        </p>
        <div class="tipos" role="radiogroup" aria-label="Qué clases pueden alternar">
          @for (o of opcionesTipo(); track o.valor) {
            <label class="tipo" [class.on]="state.tipoEspacioAlternancia() === o.valor" [class.imposible]="!o.diag.hayPareja">
              <input type="radio" name="tipoEspacioAlternancia" [checked]="state.tipoEspacioAlternancia() === o.valor"
                     (change)="state.setTipoEspacioAlternancia(o.valor)">
              <span><b>{{ o.etiqueta }}</b>
                <span class="text-muted">{{ o.diag.hayPareja ? o.diag.sesiones + ' clase(s) de ' + o.diag.asignaturas + ' asignatura(s)' : 'ninguna pareja posible' }}</span></span>
            </label>
          }
        </div>
        @if (diagnostico(); as d) {
          @if (!d.hayPareja) {
            <div class="errb" role="alert" style="margin-top:10px"><b>⚠ Con esta combinación ningún grupo podrá alternar.</b> {{ d.motivo }}
              Si faltan aulas, el horario no se podrá generar.</div>
          } @else {
            <div class="okb" style="margin-top:10px">✓ Si faltan aulas, podrán alternar hasta {{ d.sesiones }} clase(s) de {{ d.asignaturas }} asignatura(s).
              Que la pareja funcione depende además de que compartan horario disponible y un aula con cupo.</div>
          }
        }
      </section>

      <!-- Orden de cesión a alternancia -->
      <section>
        <h3 class="sec">Prioridad para alternar</h3>
        <p class="text-muted" style="font-size:12.5px;margin:2px 0 10px">
          Si faltan aulas, el sistema pondrá a alternar primero las asignaturas del primer renglón, luego
          las del segundo. Desmarque un renglón para no usarlo nunca.
        </p>
        @if (cargandoCriterios()) {
          <p class="text-muted">Cargando…</p>
        } @else {
          <table class="table" style="max-width:480px">
            <thead><tr><th style="width:60px">Orden</th><th>Tipo de asignatura</th><th style="width:90px">Activo</th><th style="width:70px"></th></tr></thead>
            <tbody>
              @for (c of criterios(); track c.id; let i = $index) {
                <tr>
                  <td>{{ c.orden }}</td>
                  <td>{{ etiquetaCriterio(c.criterio) }}
                    @if (c.criterio === 'MultiplesSesiones') {
                      <span class="material-icons" style="font-size:15px;vertical-align:-3px;color:var(--color-neutral-500)"
                            title="Por sí sola no hace que una asignatura alterne; solo desempata entre las que ya pueden alternar.">info</span>
                    }
                  </td>
                  <td style="text-align:center">
                    <input type="checkbox" [ngModel]="c.activo" (ngModelChange)="toggleCriterioActivo(c, $event)">
                  </td>
                  <td style="text-align:right;white-space:nowrap">
                    <button type="button" class="material-icons ic-edit" [disabled]="i === 0"
                          (click)="moverCriterio(c, -1)" aria-label="Subir prioridad">arrow_upward</button>
                    <button type="button" class="material-icons ic-edit" [disabled]="i === criterios().length - 1"
                          (click)="moverCriterio(c, 1)" aria-label="Bajar prioridad">arrow_downward</button>
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
    .ic-edit:disabled { opacity: .3; pointer-events: none; }
    .tipos { display: flex; gap: 10px; flex-wrap: wrap; }
    .tipo { display: flex; gap: 8px; align-items: flex-start; padding: 9px 12px; border: 1px solid var(--color-divider); cursor: pointer; min-width: 220px; }
    .tipo span { display: flex; flex-direction: column; font-size: 13px; }
    .tipo .text-muted { font-size: 11.5px; }
    .tipo.on { border-color: var(--color-accent); background: var(--color-accent-100); }
    .tipo.imposible .text-muted { color: var(--err-bd); }
  `]
})
export class AlternanciaTabComponent implements OnInit {
  state = inject(StateService);
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
      error: () => { this.cargandoCriterios.set(false); this.snack.open('No se pudo cargar la prioridad para alternar.', 'Cerrar', { duration: 4000, panelClass: ['snack-error'] }); }
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
      error: (e) => this.snack.open(`Error: ${mensajeErrorHttp(e)}`, 'Cerrar', { duration: 4000, panelClass: ['snack-error'] })
    });
  }

  moverCriterio(c: CriterioCesionAlternancia, direccion: -1 | 1): void {
    const lista = this.criterios();
    const i = lista.findIndex(x => x.id === c.id);
    const j = i + direccion;
    if (i < 0 || j < 0 || j >= lista.length) return;
    this.persistencia.actualizarCriterioCesion(c.id, { orden: lista[j].orden }).subscribe({
      next: (nueva) => this.criterios.set(this.ordenarCriterios(nueva)),
      error: (e) => this.snack.open(`Error: ${mensajeErrorHttp(e)}`, 'Cerrar', { duration: 4000, panelClass: ['snack-error'] })
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
        // FE8 auditoría: la escritura optimista de arriba nunca se revertía si el PATCH fallaba —
        // la casilla quedaba marcada y countCandidatas() la seguía contando aunque el backend
        // jamás guardó el cambio. Mismo criterio de reversión que CatalogoService.guardar().
        this.state.updateAsignatura(fila.asignatura);
        const msg = mensajeErrorHttp(err);
        this.errorMap.update(m => { const n = new Map(m); n.set(id, msg); return n; });
        this.snack.open(`Error: ${msg}`, 'Cerrar', { duration: 4000, panelClass: ['snack-error'] });
      }
    });
  }

  /** Las tres opciones con su diagnóstico: se ve de antemano cuál deja la alternancia sin parejas. */
  opcionesTipo = computed(() => ([['Todos', 'Todas las clases'], ['Laboratorio', 'Solo laboratorios'], ['Salon', 'Solo salones']] as [TipoEspacioAlternancia, string][])
    .map(([valor, etiqueta]) => ({
      valor, etiqueta,
      diag: diagnosticarAlternancia(this.state.asignaturas(), this.state.grupos(), this.state.espacios(), this.criterios(), valor),
    })));
  /** Sin criterios cargados todavía no hay nada que diagnosticar. */
  diagnostico = computed(() => this.cargandoCriterios() ? null
    : this.opcionesTipo().find(o => o.valor === this.state.tipoEspacioAlternancia())?.diag ?? null);

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
    // L-14 auditoría 2026-09-28: se agrupa por id de programa; el `track` del template usaba el NOMBRE, y hay
    // programas homónimos (dos "INGENIERIA PESQUERA") → NG0955 (clave duplicada) en /catalogo.
    const mapa = new Map<string, { id: string; programa: string; filas: AsignaturaFila[] }>();
    for (const fila of this.filasFiltradas()) {
      const progId = fila.asignatura.programaId ?? 'Sin programa';
      const progNom = this.state.programaById().get(progId)?.nombre ?? 'Programa no encontrado';
      if (!mapa.has(progId)) mapa.set(progId, { id: progId, programa: progNom, filas: [] });
      mapa.get(progId)!.filas.push(fila);
    }
    return [...mapa.values()].sort((a, b) => porTexto(a.programa, b.programa));
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
