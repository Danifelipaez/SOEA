import { Component, inject, signal, computed, OnInit, OnDestroy } from '@angular/core';
import { CommonModule, DatePipe, TitleCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatMenuModule } from '@angular/material/menu';
import { RouterModule } from '@angular/router';
import { StateService } from '../../core/state.service';
import { HorarioApiService } from '../../core/horario-api.service';
import { PersistenciaService } from '../../core/persistencia.service';
import { CatalogoService } from '../../core/catalogo.service';
import { Asignatura, ConfiguracionAlgoritmo, Docente, Espacio, Grupo, Sesion, TipoSesionUi, tipoFlujoDesde, esVirtualDesde } from '../../core/models';
import { SearchableSelectComponent, SearchableOption } from '../../shared/searchable-select/searchable-select.component';

/** Representación visual de una sesión atómica multi-slot. */
interface MergedSesion {
  key: string;
  sesiones: Sesion[];
  dia: string;
  horaInicio: string;
  horaFin: string;
  duracionSlots: number;
  virtual: boolean;
  alternancia: string;
  semana?: 'A' | 'B';
  asignaturaId: string;
  docenteId?: string;
  espacioId?: string;
  espacioIdHogar?: string;
  tipoFlujo?: 'Laboratorio' | 'AulaVirtual';
}

@Component({
  selector: 'app-horario',
  standalone: true,
  imports: [CommonModule, DatePipe, TitleCasePipe, FormsModule, MatDialogModule, MatSnackBarModule, RouterModule, MatMenuModule],
  template: `
    <div class="hz-head">
      <div class="hz-head-l"><span class="soea-tag">Pasos 2–3</span><h1 class="hz-title">Horario · grid semanal</h1></div>
      <div class="hz-head-r">
        <span class="text-muted adv-menu" [matMenuTriggerFor]="avanzadoMenu" title="Opciones avanzadas">
          <span class="tag tag-outline" style="font-size:9px">Avanzado</span> ⭳ Exportar · ⭱ Importar · ⋯
        </span>
        <mat-menu #avanzadoMenu="matMenu">
          <button mat-menu-item (click)="exportarHorario()" [disabled]="state.sesiones().length === 0">⭳ Exportar como JSON</button>
          <button mat-menu-item (click)="importarInput.click()">⭱ Importar desde JSON</button>
        </mat-menu>
        <input #importarInput type="file" accept=".json" style="display:none" (change)="importarHorario($event)">
      </div>
    </div>

    <div class="blueprint elev-md frame">
      <i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>

      <!-- toolbar -->
      <div class="toolbar">
        <span class="tb-lbl">Semana</span>
        <div class="seg">
          <label class="seg-opt" [class.on]="activeWeek() === 'A'" (click)="selectWeek('A')">A · presencial</label>
          <label class="seg-opt" [class.on]="activeWeek() === 'B'" (click)="selectWeek('B')">B</label>
        </div>
        <div class="legend">
          <span><span class="lg-box pres"></span> Presencial</span>
          <span><span class="lg-box virt"></span> ⌁ Virtual</span>
        </div>
        <div class="tb-right">
          <button class="btn btn-secondary" (click)="abrirSesionFija()">＋ Sesión fija (base)</button>
          @if (state.sesiones().length > 0) {
            <button class="btn btn-secondary" (click)="abrirCrearSesion()" [disabled]="loadingBackend()">＋ Sesión manual</button>
            <button class="btn btn-secondary" (click)="guardarComoBase()">Guardar como base</button>
          }
          <button class="btn btn-primary" (click)="generarHorario()" [disabled]="loadingBackend()">▶ Generar horario</button>
        </div>
      </div>

      <!-- parámetros avanzados -->
      <div class="adv">
        <button class="adv-toggle" (click)="avanzadoAbierto.set(!avanzadoAbierto())">
          {{ avanzadoAbierto() ? '▾' : '▸' }} Parámetros avanzados <span class="text-muted">(opcional — todo tiene default)</span>
        </button>
        @if (avanzadoAbierto()) {
          <div class="adv-fields">
            <label class="advf"><span>Tamaño población</span><input class="input" type="number" [ngModel]="gaConfig().pobSize" (ngModelChange)="patchGaConfig({ pobSize: $event })"></label>
            <label class="advf"><span>Máx. generaciones</span><input class="input" type="number" [ngModel]="gaConfig().maxGen" (ngModelChange)="patchGaConfig({ maxGen: $event })"></label>
            <label class="advf"><span>Mutación (0–1)</span><input class="input" type="number" step="0.01" [ngModel]="gaConfig().mutRate" (ngModelChange)="patchGaConfig({ mutRate: $event })"></label>
            <label class="advf"><span>Cruce (0–1)</span><input class="input" type="number" step="0.01" [ngModel]="gaConfig().crossRate" (ngModelChange)="patchGaConfig({ crossRate: $event })"></label>
          </div>
        }
      </div>

      <!-- Estado C (HF-3): sin solución + logs -->
      @if (state.sesiones().length === 0 && state.executionLogs().length > 0) {
        <div class="fail">
          <div class="errb"><b>✕ No se encontró un horario factible</b> con el catálogo actual.</div>
          <div class="text-muted logs-lbl">Logs de ejecución (solo al fallar):</div>
          <pre class="logs">{{ state.executionLogs().join('\n') }}</pre>
        </div>
      }

      @if (state.espacios().length > 0) {
        <div class="grid-area">
          <div class="grid-main">
            <!-- selector de espacio -->
            <div class="space-sel">
              @for (esp of state.espacios(); track esp.id) {
                <button class="chip space-chip" [class.on]="activeSpace()?.id === esp.id" (click)="selectSpace(esp)">{{ esp.nombre }}</button>
              }
            </div>

            @if (!backendReady()) {
              <div class="soft backend-alert">Sin conexión al backend. <button class="btn btn-secondary" style="font-size:12px;padding:3px 10px" (click)="syncFromBackend()" [disabled]="loadingBackend()">Reintentar</button></div>
            }

            <div class="matrix-scroll">
              <table class="matrix">
                <thead><tr><th class="time-col"></th>@for (dia of dias; track dia) { <th class="dhdr">{{ diaCorto(dia) }}</th> }</tr></thead>
                <tbody>
                  @for (franja of franjas; track franja) {
                    <tr>
                      <td class="hlbl">{{ franja.slice(0,2) }}</td>
                      @for (dia of dias; track dia) {
                        @if (!isCoveredByMergedPrior(dia, franja)) {
                          <td class="mcell" [attr.rowspan]="getMergedRowspan(dia, franja)" [class.free]="getMergedCellSesiones(dia, franja).length === 0 && !isOutOfHours(dia, franja)">
                            @for (merged of getMergedCellSesiones(dia, franja); track merged.key) {
                              <div class="gcell" [class.gvirt]="merged.virtual" [style.border-left]="'5px solid ' + altColor(merged)" (click)="abrirEditarSesion(merged)" title="Clic para editar">
                                <div class="s">
                                  @if (merged.virtual) { ⌁ }{{ getAsignaturaName(merged) }}
                                  @if (getContextLabel(merged); as c) { <span class="ctx">· {{ c }}</span> }
                                </div>
                                @if (merged.docenteId) { <div class="m">{{ getDocenteName(merged) }}{{ grupoSuffix(merged) }}</div> }
                                @else { <div class="nodoc">sin docente{{ grupoSuffix(merged) }}</div> }
                                <div class="t">{{ celdaEspacio(merged) }} · {{ merged.horaInicio }}–{{ merged.horaFin }}</div>
                              </div>
                            }
                          </td>
                        }
                      }
                    </tr>
                  }
                </tbody>
              </table>
              <div class="grid-foot text-muted">⋯ jornada 06:00 – 22:00 (Sáb: 06:00 – 13:00) ⋯</div>
            </div>
          </div>

          <!-- panel lateral: horarios base -->
          @if (state.horariosBases().length > 0) {
            <div class="blueprint side">
              <i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>
              <div class="pophd" style="font-size:13px">Horarios base</div>
              <div class="side-bd">
                @for (base of state.horariosBases(); track base.id) {
                  <div class="base-item" [class.on]="state.baseSeleccionadaId() === base.id">
                    <label class="radio" style="flex:1">
                      <input type="radio" name="base" [checked]="state.baseSeleccionadaId() === base.id" (click)="toggleBase(base.id)">
                      <span class="dot"></span>
                      <span class="base-info"><span class="base-name">{{ base.nombre }}</span><span class="text-muted base-meta">{{ base.sesiones.length }} ses · {{ base.creadoEn | date:'dd/MM HH:mm' }}</span></span>
                    </label>
                    <span class="material-icons ic-del" (click)="eliminarBase(base.id)" title="Eliminar">delete</span>
                  </div>
                }
                @if (state.baseSeleccionada(); as base) {
                  <div class="okb" style="font-size:11.5px">✓ Usando "{{ base.nombre }}" — {{ base.sesiones.length }} sesiones fijas la próxima generación</div>
                }
              </div>
            </div>
          }
        </div>
      } @else {
        <div class="empty-state">
          <p>No hay datos cargados. Ve a <a routerLink="/catalogo"><strong>Catálogo</strong></a> para comenzar.</p>
        </div>
      }
    </div>
  `,
  styles: [`
    .hz-head { display: flex; align-items: flex-end; justify-content: space-between; gap: 12px; margin-bottom: 16px; flex-wrap: wrap; }
    .hz-head-l { display: flex; align-items: baseline; gap: 12px; }
    .soea-tag { font: 600 11px var(--font-heading); letter-spacing: .1em; text-transform: uppercase; background: var(--color-accent); color: #fff; padding: 4px 10px; }
    .hz-title { margin: 0; font-size: 26px; }
    .adv-menu { display: inline-flex; align-items: center; gap: 8px; font-size: 12px; border: 1px solid var(--color-divider); padding: 5px 10px; cursor: pointer; }

    .frame { background: var(--color-bg); }
    .toolbar { display: flex; align-items: center; gap: 16px; padding: 12px 22px; border-bottom: 1px solid var(--color-divider); flex-wrap: wrap; }
    .tb-lbl { font: 600 12px var(--font-heading); letter-spacing: .08em; text-transform: uppercase; color: var(--color-neutral-700); }
    .legend { display: flex; gap: 18px; align-items: center; font-size: 12px; color: var(--color-neutral-700); }
    .legend span { display: flex; gap: 7px; align-items: center; }
    .lg-box { width: 24px; height: 14px; border: 1px solid var(--color-neutral-700); border-left: 4px solid var(--color-accent); }
    .lg-box.virt { border-style: dashed; background: repeating-linear-gradient(-45deg, transparent 0 3px, color-mix(in srgb, var(--color-accent) 18%, transparent) 3px 5px); }
    .tb-right { margin-left: auto; display: flex; gap: 10px; flex-wrap: wrap; }

    .adv { border-bottom: 1px solid var(--color-divider); }
    .adv-toggle { width: 100%; text-align: left; padding: 9px 22px; background: var(--color-neutral-100); border: 0; cursor: pointer; font: 600 13px var(--font-heading); color: var(--color-neutral-800); }
    .adv-fields { display: flex; gap: 16px; flex-wrap: wrap; padding: 12px 22px; }
    .advf { display: flex; flex-direction: column; gap: 4px; font-size: 12px; color: var(--color-neutral-700); width: 150px; }
    .advf .input { min-height: 30px; }

    .fail { padding: 16px 22px; display: flex; flex-direction: column; gap: 6px; }
    .logs-lbl { font-size: 11.5px; }
    .logs { border: 1px solid var(--color-neutral-900); background: var(--color-neutral-900); color: #c9c9cc; font: 11px/1.6 ui-monospace, Menlo, monospace; padding: 9px 11px; overflow: auto; max-height: 200px; margin: 0; white-space: pre-wrap; }

    .grid-area { padding: 18px 22px; display: flex; gap: 18px; align-items: flex-start; }
    .grid-main { flex: 1; min-width: 0; }
    .space-sel { display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 14px; }
    .space-chip { cursor: pointer; }
    .space-chip.on { background: var(--color-accent); color: #fff; border-color: var(--color-accent); }
    .backend-alert { margin-bottom: 12px; display: flex; align-items: center; gap: 10px; }

    .matrix-scroll { overflow-x: auto; }
    .matrix { width: 100%; border-collapse: separate; border-spacing: 4px; min-width: 760px; table-layout: fixed; }
    .time-col { width: 40px; }
    .dhdr { text-align: center; font: 600 13px var(--font-heading); letter-spacing: .04em; color: var(--color-neutral-800); padding-bottom: 4px; }
    .hlbl { color: var(--color-neutral-500); text-align: right; padding-right: 6px; font-size: 11px; vertical-align: top; width: 40px; }
    .mcell { height: 36px; vertical-align: top; padding: 0; }
    .mcell.free { border: 1px dashed var(--color-neutral-300); background: var(--color-neutral-100); }
    .gcell { border: 1px solid var(--color-neutral-800); background: var(--color-bg); padding: 5px 8px; overflow: hidden; font-size: 11px; line-height: 1.24; height: 100%; cursor: pointer; }
    .gcell:hover { box-shadow: var(--shadow-sm); }
    .gvirt { border-style: dashed; background: repeating-linear-gradient(-45deg, var(--color-bg) 0 5px, color-mix(in srgb, var(--color-accent) 12%, transparent) 5px 8px); }
    .gcell .s { font: 600 13px/1.15 var(--font-heading); letter-spacing: .01em; }
    .gcell .s .ctx { font-weight: 400; font-size: 10px; color: var(--color-neutral-600); }
    .gcell .m { color: var(--color-neutral-700); }
    .gcell .nodoc { color: var(--err-bd); font-weight: 500; }
    .gcell .t { color: var(--color-neutral-500); font-size: 10px; }
    .grid-foot { text-align: center; font-size: 11px; margin-top: 8px; }

    .side { width: 250px; flex: none; background: var(--color-surface); }
    .side-bd { padding: 11px 13px; display: flex; flex-direction: column; gap: 8px; }
    .base-item { display: flex; align-items: center; gap: 6px; padding: 5px 7px; border: 1px solid var(--color-divider); }
    .base-item.on { border-color: var(--color-accent); background: var(--color-accent-100); }
    .base-info { display: flex; flex-direction: column; }
    .base-name { font-size: 13px; } .base-meta { font-size: 11px; }

    .empty-state { text-align: center; padding: 56px 32px; color: var(--color-neutral-600); }
  `]
})
export class HorarioComponent implements OnInit {
  state = inject(StateService);
  dialog = inject(MatDialog);
  snackBar = inject(MatSnackBar);
  horarioApi = inject(HorarioApiService);
  persistencia = inject(PersistenciaService);
  catalogo = inject(CatalogoService);

  dias = ['lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado'];
  franjas = ['06:00','07:00','08:00','09:00','10:00','11:00','12:00','13:00','14:00','15:00','16:00','17:00','18:00','19:00','20:00','21:00'];

  activeSpace = signal<Espacio | null>(null);
  activeWeek = signal<'A' | 'B'>('A');
  loadingBackend = signal(false);
  backendReady = signal(false);
  avanzadoAbierto = signal(false);

  gaConfig = this.state.configuracionAlgoritmo;
  patchGaConfig(patch: Partial<ConfiguracionAlgoritmo>) { this.state.setConfiguracionAlgoritmo({ ...this.gaConfig(), ...patch }); }

  ngOnInit() { this.syncFromBackend(); }
  constructor() { const espacios = this.state.espacios(); if (espacios.length > 0) this.activeSpace.set(espacios[0]); }

  selectSpace(esp: Espacio) { this.activeSpace.set(esp); }
  selectWeek(week: 'A' | 'B') { this.activeWeek.set(week); }
  diaCorto(dia: string): string { return { lunes: 'Lun', martes: 'Mar', miercoles: 'Mié', jueves: 'Jue', viernes: 'Vie', sabado: 'Sáb' }[dia] ?? dia; }
  altColor(m: MergedSesion): string { return m.alternancia === 'TipoA' ? '#5980a6' : m.alternancia === 'TipoB' ? '#a8825a' : '#8a8f94'; }

  syncFromBackend() {
    this.loadingBackend.set(true);
    this.catalogo.cargarTodo().subscribe({
      next: () => {
        this.loadingBackend.set(false);
        this.backendReady.set(true);
        const espacios = this.state.espacios();
        const current = this.activeSpace();
        if (!current || !espacios.find(e => e.id === current.id)) this.activeSpace.set(espacios[0] ?? null);
      },
      error: () => {
        this.loadingBackend.set(false);
        this.backendReady.set(false);
        this.snackBar.open('No se pudo conectar con el backend. Verifica que la API esté activa.', 'Cerrar', { duration: 5000, panelClass: ['snack-error'] });
      }
    });
  }

  private computeMergedMap(spaceId: string | undefined, allSesiones: Sesion[]): Map<string, MergedSesion[]> {
    const map = new Map<string, MergedSesion[]>();
    const visible = allSesiones.filter(s => this.sesionPerteneceAlEspacio(s, spaceId) && this.sesionVisibleEnSemana(s));
    for (const s of visible) {
      const dur = Math.max(1, Math.round(s.duracionHoras ?? this.diffHoras(s.horaInicio, s.horaFin)));
      const merged: MergedSesion = {
        key: s.id, sesiones: [s], dia: s.dia, horaInicio: s.horaInicio, horaFin: s.horaFin, duracionSlots: dur,
        virtual: s.virtual, alternancia: s.alternancia, semana: s.semana, asignaturaId: s.asignaturaId,
        docenteId: s.docenteId, espacioId: s.espacioId, espacioIdHogar: s.espacioIdHogar, tipoFlujo: s.tipoFlujo
      };
      const cid = this.cellId(s.dia, s.horaInicio);
      if (!map.has(cid)) map.set(cid, []);
      map.get(cid)!.push(merged);
    }
    return map;
  }

  private diffHoras(horaInicio: string, horaFin: string): number {
    const [hi, mi] = horaInicio.split(':').map(Number);
    const [hf, mf] = horaFin.split(':').map(Number);
    return Math.max(1, (hf * 60 + mf - (hi * 60 + mi)) / 60);
  }

  private sesionPerteneceAlEspacio(s: Sesion, spaceId: string | undefined): boolean {
    if (!spaceId) return true;
    if (s.espacioId === spaceId) return true;
    if (s.espacioIdHogar) return s.espacioIdHogar === spaceId;
    return s.virtual;
  }

  private sesionVisibleEnSemana(s: Sesion): boolean {
    if (s.semana) return s.semana === this.activeWeek();
    if (s.alternancia === 'SinAlternancia') return true;
    return this.activeWeek() === 'A' ? s.alternancia === 'TipoA' : s.alternancia === 'TipoB';
  }

  mergedByCell = computed(() => this.computeMergedMap(this.activeSpace()?.id, this.state.sesiones()));

  coveredCells = computed(() => {
    const covered = new Set<string>();
    for (const mergedList of this.mergedByCell().values()) {
      for (const m of mergedList) {
        if (m.duracionSlots <= 1) continue;
        const startIdx = this.franjas.indexOf(m.horaInicio);
        for (let k = 1; k < m.duracionSlots; k++) {
          const idx = startIdx + k;
          if (idx < this.franjas.length) covered.add(this.cellId(m.dia, this.franjas[idx]));
        }
      }
    }
    return covered;
  });

  getMergedCellSesiones(dia: string, franja: string): MergedSesion[] { return this.mergedByCell().get(this.cellId(dia, franja)) ?? []; }
  isCoveredByMergedPrior(dia: string, franja: string): boolean { return this.coveredCells().has(this.cellId(dia, franja)); }
  getMergedRowspan(dia: string, franja: string): number {
    const merged = this.getMergedCellSesiones(dia, franja);
    return merged.length === 0 ? 1 : Math.max(...merged.map(m => m.duracionSlots));
  }

  cellId(dia: string, franja: string): string { return `cell-${dia}-${franja.replace(':', '')}`; }

  private readonly outOfHoursCells = computed(() => {
    const set = new Set<string>();
    const sabadoLimit = this.franjas.indexOf('13:00');
    this.franjas.forEach((franja, idx) => { if (idx >= sabadoLimit) set.add(this.cellId('sabado', franja)); });
    return set;
  });
  isOutOfHours(dia: string, franja: string): boolean { return this.outOfHoursCells().has(this.cellId(dia, franja)); }

  getAsignaturaName(merged: MergedSesion): string { return this.state.asignaturaById().get(merged.asignaturaId)?.nombre ?? 'Desconocida'; }
  getDocenteName(merged: MergedSesion): string { return merged.docenteId ? (this.state.docenteById().get(merged.docenteId)?.nombre ?? '') : ''; }
  celdaEspacio(merged: MergedSesion): string {
    if (merged.virtual) {
      const hogar = merged.espacioIdHogar ? this.state.espacioById().get(merged.espacioIdHogar)?.nombre : null;
      return hogar ? `hogar ${hogar}` : 'virtual';
    }
    return merged.espacioId ? (this.state.espacioById().get(merged.espacioId)?.nombre ?? '') : '—';
  }

  getContextLabel(merged: MergedSesion): string {
    const asig = this.state.asignaturaById().get(merged.asignaturaId);
    if (!asig) return '';
    const prog = this.state.programaById().get(asig.programaId);
    return prog?.nombre ?? '';
  }
  grupoSuffix(merged: MergedSesion): string {
    const asig = this.state.asignaturaById().get(merged.asignaturaId);
    return asig?.grupoNumero ? ` · G${asig.grupoNumero}` : '';
  }

  abrirEditarSesion(merged: MergedSesion) {
    const ref = this.dialog.open(EditarSesionDialogComponent, {
      width: '300px', maxHeight: '92vh',
      data: {
        merged, sesion: merged.sesiones[0], asignaturas: this.state.asignaturas(), docentes: this.state.docentes(),
        espacios: this.state.espacios(), sesiones: this.state.sesiones(), programaById: this.state.programaById(), facultadById: this.state.facultadById()
      } satisfies EditarSesionDialogData
    });
    ref.afterClosed().subscribe((result: EditarSesionResult | undefined) => {
      if (!result) return;
      this.state.updateSesion(result.sesion);
      if (result.advertencias?.length) this.snackBar.open(`Sesión actualizada con avisos: ${result.advertencias.join(' · ')}`, 'Cerrar', { duration: 8000 });
      else this.snackBar.open('Sesión actualizada.', '', { duration: 2500 });
    });
  }

  exportarHorario() {
    const sesiones = this.state.sesiones();
    const blob = new Blob([JSON.stringify({ version: 1, exportadoEn: new Date().toISOString(), sesiones }, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = `horario-soea-${new Date().toISOString().slice(0, 10)}.json`; a.click();
    URL.revokeObjectURL(url);
    this.snackBar.open(`Horario exportado (${sesiones.length} sesiones).`, '', { duration: 3000 });
  }

  importarHorario(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = (e) => {
      try {
        const json = JSON.parse(e.target!.result as string);
        const sesiones = json.sesiones ?? json;
        if (!Array.isArray(sesiones)) throw new Error('Formato inválido: se espera un array de sesiones.');
        this.state.setSesiones(sesiones);
        this.snackBar.open(`Horario importado: ${sesiones.length} sesiones.`, 'Cerrar', { duration: 5000 });
      } catch (err: any) {
        this.snackBar.open(`Error al leer el archivo: ${err.message}`, 'Cerrar', { duration: 6000, panelClass: ['snack-error'] });
      }
      (event.target as HTMLInputElement).value = '';
    };
    reader.readAsText(file);
  }

  guardarComoBase() {
    const nombre = window.prompt('Nombre del horario base:', `Base ${new Date().toLocaleDateString('es-CO')}`);
    if (!nombre?.trim()) return;
    const base = this.state.guardarHorarioBase(nombre);
    this.snackBar.open(`Horario base "${base.nombre}" guardado (${base.sesiones.length} sesiones).`, 'Cerrar', { duration: 4000 });
  }

  toggleBase(id: string) { this.state.seleccionarBase(this.state.baseSeleccionadaId() === id ? null : id); }
  eliminarBase(id: string) {
    const base = this.state.horariosBases().find(b => b.id === id);
    if (!base || !window.confirm(`¿Eliminar la base "${base.nombre}"?`)) return;
    this.state.eliminarHorarioBase(id);
    this.snackBar.open(`Base "${base.nombre}" eliminada.`, '', { duration: 3000 });
  }

  generarHorario() {
    if (!this.backendReady()) { this.snackBar.open('Conecta el backend antes de generar el horario.', 'Cerrar', { duration: 4000 }); return; }
    if (this.state.asignaturas().length === 0 || this.state.espacios().length === 0 || this.state.docentes().length === 0) {
      this.snackBar.open('Carga asignaturas, docentes y espacios antes de generar el horario.', 'Cerrar', { duration: 4000 });
      return;
    }
    const asignaturas = this.state.asignaturas();
    const dialogRef = this.dialog.open(ProgressDialogComponent, { disableClose: true, width: '340px' });
    this.horarioApi.generarHorario(asignaturas, this.state.docentes(), this.state.espacios(), this.state.configuracionAlgoritmo(), '2026-1', this.state.baseSeleccionada() ?? undefined)
      .subscribe({
        next: (respuesta) => {
          dialogRef.close();
          const sesiones = this.horarioApi.mapearSesiones(respuesta.sesiones);
          this.state.setSesiones(sesiones);
          this.state.setExecutionLogs(respuesta.logs || []);
          this.snackBar.open(`Horario generado: ${sesiones.length} sesiones (fitness: ${respuesta.puntajeFitness.toFixed(2)}).`, 'Cerrar', { duration: 6000 });
        },
        error: (err: any) => {
          dialogRef.close();
          const mensaje = err.mensajeError || err.message || err.error || 'Error desconocido';
          if (err.logs && Array.isArray(err.logs)) this.state.setExecutionLogs(err.logs);
          const texto = /factible|infeasible/i.test(mensaje)
            ? `No se encontró un horario factible para ${asignaturas.length} asignatura(s) en ${this.state.espacios().length} espacio(s).`
            : mensaje;
          this.snackBar.open(texto, 'Cerrar', { duration: 9000, panelClass: ['snack-error'] });
        }
      });
  }

  abrirCrearSesion() {
    const ref = this.dialog.open(CrearSesionDialogComponent, {
      width: '300px', maxHeight: '92vh',
      data: { asignaturas: this.state.asignaturas(), docentes: this.state.docentes(), espacios: this.state.espacios(), grupos: this.state.grupos(), sesiones: this.state.sesiones(), programaById: this.state.programaById() }
    });
    ref.afterClosed().subscribe((nuevas: Sesion[] | undefined) => {
      if (!nuevas?.length) return;
      this.state.sesiones.update(prev => [...prev, ...nuevas]);
      this.snackBar.open(`Sesión creada (${nuevas.length} fila${nuevas.length > 1 ? 's' : ''} añadidas).`, 'Cerrar', { duration: 5000 });
    });
  }

  abrirSesionFija() {
    const ref = this.dialog.open(SesionFijaDialogComponent, {
      width: '300px', maxHeight: '92vh',
      data: { asignaturas: this.state.asignaturas(), docentes: this.state.docentes(), espacios: this.state.espacios(), grupos: this.state.grupos(), programaById: this.state.programaById() }
    });
    ref.afterClosed().subscribe((sesion: Sesion | undefined) => {
      if (!sesion) return;
      const nombre = window.prompt('Nombre del horario base a crear/actualizar:', 'Sesiones fijas');
      if (!nombre?.trim()) return;
      // Se agrega la sesión fija a un horario base nuevo (restricción de igualdad para CP-SAT).
      const previa = this.state.baseSeleccionada();
      const sesiones = previa ? [...previa.sesiones, sesion] : [sesion];
      const antes = this.state.sesiones();
      this.state.setSesiones(sesiones);
      const base = this.state.guardarHorarioBase(nombre);
      this.state.setSesiones(antes);
      this.state.seleccionarBase(base.id);
      this.snackBar.open(`Sesión fija agregada a la base "${base.nombre}" (${base.sesiones.length} en total).`, 'Cerrar', { duration: 5000 });
    });
  }
}

// ═══ Diálogo: Editar sesión (REQUISITOS §2/§3 — incluye asignar docente con 409) ═══
interface Check { ok: boolean; texto: string; }

interface EditarSesionDialogData {
  merged: MergedSesion; sesion: Sesion; asignaturas: Asignatura[]; docentes: Docente[]; espacios: Espacio[];
  sesiones: Sesion[]; programaById: Map<string, { id: string; nombre: string; facultadId: string }>; facultadById: Map<string, { id: string; nombre: string }>;
}
interface EditarSesionResult { sesion: Sesion; advertencias: string[]; }

@Component({
  selector: 'app-editar-sesion-dialog',
  standalone: true,
  imports: [CommonModule, TitleCasePipe, FormsModule, MatDialogModule],
  template: `
    <div class="pophd">Editar sesión <i (click)="cancelar()">✕</i></div>
    <div class="popbd" style="max-height:80vh;overflow:auto">
      <div class="text-muted" style="font-size:12px">{{ asignatura()?.nombre }} · {{ data.sesion.duracionHoras }}h {{ data.sesion.virtual ? '· virtual' : '· presencial' }}</div>

      <div class="dfield"><label>Docente</label>
        <select class="input" [ngModel]="docenteId()" (ngModelChange)="docenteId.set($event)">
          <option value="">— Sin asignar —</option>
          @for (d of data.docentes; track d.id) { <option [value]="d.id">{{ d.nombre }}</option> }
        </select>
        @if (hayCambioDocente()) { <small class="text-muted" style="font-size:11px">Se guardará en el servidor (solape duro → 409; disponibilidad → aviso).</small> }
      </div>

      <div class="dfield"><label>Día · Hora inicio</label>
        <div style="display:flex;gap:6px">
          <select class="input" style="flex:1" [ngModel]="dia()" (ngModelChange)="dia.set($event)">
            @for (d of diasOpciones; track d.valor) { <option [value]="d.valor">{{ d.valor | titlecase }}</option> }
          </select>
          <select class="input" style="width:80px" [ngModel]="horaInicio()" (ngModelChange)="horaInicio.set($event)">
            @for (h of horasDisponibles; track h) { <option [value]="h">{{ h }}</option> }
          </select>
        </div>
      </div>

      <div class="dfield"><label>Espacio</label>
        <select class="input" [ngModel]="espacioId()" (ngModelChange)="espacioId.set($event)">
          <option value="">— Sin espacio (virtual) —</option>
          @for (e of data.espacios; track e.id) { <option [value]="e.id">{{ e.nombre }}</option> }
        </select>
      </div>

      @if (esLaboratorio()) {
        <div class="dfield"><label>Alternancia</label>
          <div class="seg" style="align-self:flex-start">
            <label class="seg-opt" [class.on]="alternancia()==='TipoA'" (click)="alternancia.set('TipoA')">A</label>
            <label class="seg-opt" [class.on]="alternancia()==='TipoB'" (click)="alternancia.set('TipoB')">B</label>
            <label class="seg-opt" [class.on]="alternancia()==='SinAlternancia'" (click)="alternancia.set('SinAlternancia')">Sin</label>
          </div>
        </div>
        @if (alternancia() !== 'SinAlternancia') {
          <div class="dfield"><label>Semana</label>
            <div class="seg" style="align-self:flex-start">
              <label class="seg-opt" [class.on]="semana()==='A'" (click)="semana.set('A')">A</label>
              <label class="seg-opt" [class.on]="semana()==='B'" (click)="semana.set('B')">B</label>
              <label class="seg-opt" [class.on]="semana()===undefined" (click)="semana.set(undefined)">Ambas</label>
            </div>
          </div>
        }
      }

      @for (c of validaciones(); track c.texto) {
        <div [class]="c.ok ? 'okb' : 'errb'">{{ c.ok ? '✓' : '✕' }} {{ c.texto }}</div>
      }
      @for (w of advertencias(); track w) { <div class="soft"><b>⚠ Aviso (no bloquea):</b> {{ w }}</div> }
      @if (errorServidor()) { <div class="errb"><b>✕ {{ errorServidor() }}</b> El popup no se cierra.</div> }

      <div class="popfoot">
        <button class="btn btn-secondary" (click)="cancelar()" [disabled]="guardando()">Cancelar</button>
        <button class="btn btn-primary" [disabled]="!puedeGuardar() || guardando()" (click)="guardar()">{{ guardando() ? 'Guardando…' : 'Guardar' }}</button>
      </div>
    </div>
  `
})
export class EditarSesionDialogComponent {
  private dialogRef = inject(MatDialogRef<EditarSesionDialogComponent>);
  readonly data: EditarSesionDialogData = inject(MAT_DIALOG_DATA);
  private persistencia = inject(PersistenciaService);
  private readonly orig = this.data.merged;

  docenteId = signal(this.orig.docenteId ?? '');
  dia = signal(this.orig.dia);
  horaInicio = signal(this.orig.horaInicio);
  espacioId = signal(this.orig.espacioId ?? '');
  alternancia = signal<'TipoA' | 'TipoB' | 'SinAlternancia'>(this.orig.alternancia as any);
  semana = signal<'A' | 'B' | undefined>(this.orig.semana);
  guardando = signal(false);
  advertencias = signal<string[]>([]);
  errorServidor = signal('');

  asignatura = computed(() => this.data.asignaturas.find(a => a.id === this.orig.asignaturaId));
  esLaboratorio = computed(() => this.data.sesion.tipoFlujo === 'Laboratorio');
  hayCambioDocente = computed(() => this.docenteId() !== (this.orig.docenteId ?? ''));
  hayCambios = computed(() =>
    this.hayCambioDocente() || this.dia() !== this.orig.dia || this.horaInicio() !== this.orig.horaInicio ||
    this.espacioId() !== (this.orig.espacioId ?? '') || this.alternancia() !== (this.orig.alternancia as string) || this.semana() !== this.orig.semana);

  readonly horasDisponibles = ['06:00','07:00','08:00','09:00','10:00','11:00','12:00','13:00','14:00','15:00','16:00','17:00','18:00','19:00','20:00','21:00'];
  readonly diasOpciones = ['lunes','martes','miercoles','jueves','viernes','sabado'].map(v => ({ valor: v }));

  validaciones = computed<Check[]>(() => {
    const dia = this.dia(), inicio = this.horaInicio(), espacioId = this.espacioId(), docenteId = this.docenteId();
    const sesionId = this.data.sesion.id, dur = this.data.sesion.duracionHoras ?? 2;
    const chks: Check[] = [];
    if (!dia || !inicio) return chks;
    const startIdx = this.horasDisponibles.indexOf(inicio), endIdx = startIdx + Math.round(dur);
    if (dia === 'sabado' && endIdx > this.horasDisponibles.indexOf('13:00')) {
      chks.push({ ok: false, texto: 'Sábado solo tiene jornada hasta las 13:00' });
    }
    if (espacioId && !this.data.sesion.virtual) {
      const conflicto = this.data.sesiones.find(s => s.id !== sesionId && s.espacioId === espacioId && s.dia === dia && !s.virtual && this.overlaps(s, startIdx, endIdx));
      const nombre = this.data.espacios.find(e => e.id === espacioId)?.nombre ?? espacioId;
      chks.push({ ok: !conflicto, texto: conflicto ? `${nombre} ya está ocupado en esa franja` : `${nombre} está libre` });
    }
    if (docenteId) {
      const conflicto = this.data.sesiones.find(s => s.id !== sesionId && s.docenteId === docenteId && s.dia === dia && this.overlaps(s, startIdx, endIdx));
      const nombre = this.data.docentes.find(d => d.id === docenteId)?.nombre ?? 'El docente';
      chks.push({ ok: !conflicto, texto: conflicto ? `${nombre} ya tiene otra sesión en esa franja` : `${nombre} está libre en esa franja` });
    }
    return chks;
  });

  conflictosDuros = computed(() => this.validaciones().some(c => !c.ok));
  puedeGuardar = computed(() => this.hayCambios() && !this.conflictosDuros() && !this.guardando());

  guardar() {
    if (!this.puedeGuardar()) return;
    this.guardando.set(true); this.errorServidor.set(''); this.advertencias.set([]);
    if (this.hayCambioDocente()) {
      this.persistencia.asignarDocente(this.data.sesion.id, this.docenteId() || null).subscribe({
        next: (resp) => { this.advertencias.set(resp.advertencias ?? []); this.commitLocal(); },
        error: (err: any) => {
          this.guardando.set(false);
          const msg = err?.error?.error ?? err?.message ?? 'Error al asignar el docente.';
          this.errorServidor.set(err?.status === 409 ? `Conflicto de horario (409): ${msg}` : msg);
        }
      });
    } else { this.commitLocal(); }
  }

  private commitLocal() {
    const dur = Math.round(this.data.sesion.duracionHoras ?? 2);
    const startIdx = this.horasDisponibles.indexOf(this.horaInicio()), endIdx = startIdx + dur;
    const horaFin = endIdx < this.horasDisponibles.length ? this.horasDisponibles[endIdx] : this.addH(this.horaInicio(), dur);
    const updated: Sesion = {
      ...this.data.sesion, docenteId: this.docenteId() || undefined, dia: this.dia(), horaInicio: this.horaInicio(),
      horaFin, espacioId: this.espacioId() || undefined, alternancia: this.alternancia(), semana: this.semana()
    };
    // Si sólo hubo aviso blando, se dejó ver 1.2s antes de cerrar.
    if (this.advertencias().length) { setTimeout(() => { this.guardando.set(false); this.dialogRef.close({ sesion: updated, advertencias: this.advertencias() }); }, 1200); }
    else { this.guardando.set(false); this.dialogRef.close({ sesion: updated, advertencias: [] }); }
  }

  cancelar() { this.dialogRef.close(); }
  private overlaps(s: Sesion, newStart: number, newEnd: number): boolean {
    const sStart = this.horasDisponibles.indexOf(s.horaInicio);
    if (sStart < 0) return false;
    const sDur = Math.max(1, Math.round(s.duracionHoras ?? this.diffH(s.horaInicio, s.horaFin)));
    return newStart < sStart + sDur && sStart < newEnd;
  }
  private addH(hora: string, h: number): string { const [hh, mm] = hora.split(':').map(Number); return `${String(hh + h).padStart(2, '0')}:${String(mm).padStart(2, '0')}`; }
  private diffH(i: string, f: string): number { const [hi, mi] = i.split(':').map(Number); const [hf, mf] = f.split(':').map(Number); return Math.max(1, (hf * 60 + mf - (hi * 60 + mi)) / 60); }
}

// ═══ Diálogo: Crear sesión manual (REQUISITOS §2) ═══
interface DialogData { asignaturas: Asignatura[]; docentes: Docente[]; espacios: Espacio[]; grupos: Grupo[]; sesiones: Sesion[]; programaById: Map<string, { id: string; nombre: string }>; }

@Component({
  selector: 'app-crear-sesion-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatSnackBarModule, SearchableSelectComponent],
  template: `
    <div class="pophd">Nueva sesión manual <i (click)="cancelar()">✕</i></div>
    <div class="popbd" style="max-height:80vh;overflow:auto">
      <div class="dfield"><label>Asignatura <span class="rq">*</span></label>
        <app-searchable-select [(ngModel)]="asignaturaId" (ngModelChange)="onAsignaturaChange($event)"
                                [options]="asignaturaOptions" placeholder="— Seleccione —"></app-searchable-select>
      </div>

      @if (asignaturaSeleccionada()) {
        <div class="dfield"><label>Grupo <span class="rq">*</span></label>
          <app-searchable-select [(ngModel)]="grupoId" (ngModelChange)="onGrupoChange()"
                                  [options]="grupoOptions()" placeholder="— Seleccione grupo —"></app-searchable-select>
        </div>
        <div class="dfield"><label>Tipo de sesión <span class="rq">*</span></label>
          <div class="seg" style="flex-wrap:wrap">
            @for (t of tiposDisponibles(); track t.tipo) { <label class="seg-opt" [class.on]="tipoSesion() === t.tipo" (click)="setTipoSesion(t.tipo)">{{ t.label }}</label> }
          </div>
        </div>
        <div class="text-muted" style="font-size:12px">👤 {{ docenteDelGrupo() }} · ⏱ {{ duracionSeleccionada() }}h por sesión (fijo)</div>
      }

      <div class="dfield"><label>Día <span class="rq">*</span></label>
        <select class="input" [(ngModel)]="dia" (ngModelChange)="recheck()">
          <option value="">— Seleccione —</option>
          @for (d of dias; track d.valor) { <option [value]="d.valor">{{ d.etiqueta }}</option> }
        </select>
      </div>
      <div style="display:flex;gap:8px">
        <div class="dfield" style="flex:1"><label>Inicio <span class="rq">*</span></label>
          <select class="input" [(ngModel)]="horaInicio" (ngModelChange)="recheck()">
            <option value="">—</option>
            @for (h of horasDisponibles; track h) { <option [value]="h">{{ h }}</option> }
          </select>
        </div>
        <div class="dfield" style="flex:1"><label>Espacio {{ tipoSesion() === 'TeoriaVirtual' ? '(virtual)' : '' }}</label>
          @if (tipoSesion() === 'TeoriaVirtual') { <div class="selval text-muted">Sin espacio físico</div> }
          @else {
            <app-searchable-select [(ngModel)]="espacioId" (ngModelChange)="recheck()" [disabled]="espacioFijoBloqueado()"
                                    [options]="espacioOptions()" placeholder="— Seleccione —"></app-searchable-select>
          }
        </div>
      </div>

      @if (tipoSesion() === 'Laboratorio') {
        <div class="dfield"><label>Alternancia <span class="rq">*</span></label>
          <div class="seg" style="flex-wrap:wrap">
            <label class="seg-opt" [class.on]="alternancia==='TipoA'" (click)="alternancia='TipoA'; recheck()">TipoA</label>
            <label class="seg-opt" [class.on]="alternancia==='TipoB'" (click)="alternancia='TipoB'; recheck()">TipoB</label>
            <label class="seg-opt" [class.on]="alternancia==='SinAlternancia'" (click)="alternancia='SinAlternancia'; recheck()">Sin</label>
          </div>
        </div>
      }

      @if (asignaturaId && dia && horaInicio && (espacioId || tipoSesion() === 'TeoriaVirtual')) {
        @for (c of checks(); track c.texto) { <div [class]="c.ok ? 'okb' : 'errb'">{{ c.ok ? '✓' : '✕' }} {{ c.texto }}</div> }
      }
      @if (errorServidor()) { <div class="errb"><b>✕ {{ errorServidor() }}</b></div> }

      <div class="popfoot">
        <button class="btn btn-secondary" (click)="cancelar()" [disabled]="guardando()">Cancelar</button>
        <button class="btn btn-primary" [disabled]="!puedeCrear() || guardando()" (click)="crear()">{{ guardando() ? 'Creando…' : 'Crear' }}</button>
      </div>
    </div>
  `
})
export class CrearSesionDialogComponent {
  private dialogRef = inject(MatDialogRef<CrearSesionDialogComponent>);
  private data: DialogData = inject(MAT_DIALOG_DATA);
  private persistencia = inject(PersistenciaService);

  asignaturaId = ''; dia = ''; horaInicio = ''; espacioId = '';
  alternancia: 'TipoA' | 'TipoB' | 'SinAlternancia' = 'SinAlternancia';
  guardando = signal(false); errorServidor = signal('');

  readonly dias = [{ valor: 'lunes', etiqueta: 'Lunes' }, { valor: 'martes', etiqueta: 'Martes' }, { valor: 'miercoles', etiqueta: 'Miércoles' }, { valor: 'jueves', etiqueta: 'Jueves' }, { valor: 'viernes', etiqueta: 'Viernes' }, { valor: 'sabado', etiqueta: 'Sábado' }];
  readonly horasDisponibles = ['06:00','07:00','08:00','09:00','10:00','11:00','12:00','13:00','14:00','15:00','16:00','17:00','18:00','19:00','20:00','21:00'];

  asignaturaSeleccionada = signal<Asignatura | undefined>(undefined);
  tipoSesion = signal<TipoSesionUi>('Laboratorio');
  espacioFijoBloqueado = signal(false);
  checks = signal<Check[]>([]);
  checksOk = signal(false);

  // item 9: lista plana buscable (sin optgroup por programa) — el programa va como "sub".
  readonly asignaturaOptions: SearchableOption[] = this.data.asignaturas
    .map(a => ({
      value: a.id,
      label: a.nombre + (a.codigo ? ` (${a.codigo})` : ''),
      sub: this.data.programaById.get(a.programaId)?.nombre
    }))
    .sort((a, b) => a.label.localeCompare(b.label));

  espacioOptions = computed<SearchableOption[]>(() =>
    this.espaciosDisponibles().map(e => ({ value: e.id, label: e.nombre })));

  // Fase 2: el docente se deriva del GRUPO (la misma asignatura la dictan docentes distintos).
  grupoId = '';
  grupoIdSel = signal('');
  grupoOptions = computed<SearchableOption[]>(() => {
    const asigId = this.asignaturaSeleccionada()?.id;
    if (!asigId) return [];
    return this.data.grupos.filter(g => g.asignaturaId === asigId)
      .map(g => ({ value: g.id, label: g.nombre, sub: g.docenteId ? this.nombreDocente(g.docenteId) : 'sin docente' }));
  });
  private docenteIdDelGrupo(): string {
    return this.data.grupos.find(g => g.id === this.grupoIdSel())?.docenteId ?? '';
  }
  docenteDelGrupo(): string {
    const id = this.docenteIdDelGrupo();
    return id ? this.nombreDocente(id) : '— sin docente en el grupo —';
  }
  onGrupoChange() { this.grupoIdSel.set(this.grupoId); this.recheck(); }

  tiposDisponibles = computed<{ tipo: TipoSesionUi; label: string }[]>(() => {
    const a = this.asignaturaSeleccionada();
    if (!a) return [];
    const list: { tipo: TipoSesionUi; label: string }[] = [];
    if (a.sesionesTeoriaPresencialSemana > 0) list.push({ tipo: 'TeoriaPresencial', label: 'Teoría pres.' });
    if (a.sesionesTeoriaVirtualSemana > 0) list.push({ tipo: 'TeoriaVirtual', label: 'Teoría virt.' });
    if (a.sesionesLaboratorioSemana > 0) list.push({ tipo: 'Laboratorio', label: 'Laboratorio' });
    return list;
  });

  duracionSeleccionada = computed(() => {
    const a = this.asignaturaSeleccionada();
    if (!a) return 2;
    switch (this.tipoSesion()) { case 'TeoriaVirtual': return a.horasTeoriaVirtual; case 'Laboratorio': return a.horasLaboratorio; default: return a.horasTeoriaPresencial; }
  });

  espaciosDisponibles = computed(() => {
    const a = this.asignaturaSeleccionada(), tipo = this.tipoSesion();
    if (tipo === 'TeoriaVirtual') return [];
    if (!a) return this.data.espacios;
    if (a.espacioFijoId) return this.data.espacios.filter(e => e.id === a.espacioFijoId);
    return tipo === 'Laboratorio' ? this.data.espacios.filter(e => e.tipo === 'Laboratorio') : this.data.espacios.filter(e => e.tipo !== 'Laboratorio');
  });

  puedeCrear = computed(() => !!this.asignaturaId && !!this.grupoIdSel() && !!this.dia && !!this.horaInicio && (!!this.espacioId || this.tipoSesion() === 'TeoriaVirtual') && this.checksOk() && !this.guardando());

  onAsignaturaChange(id: string) {
    const a = this.data.asignaturas.find(x => x.id === id);
    this.asignaturaSeleccionada.set(a);
    // Reiniciar el grupo: sus opciones dependen de la asignatura elegida.
    this.grupoId = ''; this.grupoIdSel.set('');
    this.tipoSesion.set(a && a.sesionesTeoriaPresencialSemana > 0 ? 'TeoriaPresencial' : a && a.sesionesLaboratorioSemana > 0 ? 'Laboratorio' : a && a.sesionesTeoriaVirtualSemana > 0 ? 'TeoriaVirtual' : 'TeoriaPresencial');
    this.aplicarReglasTipo(); this.recheck();
  }
  setTipoSesion(tipo: TipoSesionUi) { this.tipoSesion.set(tipo); this.aplicarReglasTipo(); this.recheck(); }

  private aplicarReglasTipo() {
    const a = this.asignaturaSeleccionada(), tipo = this.tipoSesion();
    if (tipo === 'Laboratorio' && a?.alternancia && a.alternancia !== 'SinAlternancia') this.alternancia = a.alternancia as 'TipoA' | 'TipoB';
    else this.alternancia = 'SinAlternancia';
    if (tipo === 'TeoriaVirtual') { this.espacioId = ''; this.espacioFijoBloqueado.set(false); }
    else if (a?.espacioFijoId) { this.espacioId = a.espacioFijoId; this.espacioFijoBloqueado.set(true); }
    else { this.espacioFijoBloqueado.set(false); this.espacioId = ''; }
  }

  recheck() {
    const a = this.asignaturaSeleccionada(), dur = this.duracionSeleccionada(), esVirtual = this.tipoSesion() === 'TeoriaVirtual';
    const chks: Check[] = []; let ok = true;
    if (!a || !this.dia || !this.horaInicio || (!this.espacioId && !esVirtual)) { this.checks.set([]); this.checksOk.set(false); return; }
    const startIdx = this.horasDisponibles.indexOf(this.horaInicio), endIdx = startIdx + dur;
    if (this.dia === 'sabado' && endIdx > this.horasDisponibles.indexOf('13:00')) {
      chks.push({ ok: false, texto: 'Sábado solo tiene jornada hasta las 13:00' });
      ok = false;
    }
    const docenteId = this.docenteIdDelGrupo();
    if (docenteId) {
      const conflictoDocente = this.data.sesiones.find(s => s.docenteId === docenteId && s.dia === this.dia && this.overlaps(s, startIdx, endIdx));
      if (conflictoDocente) ok = false;
      chks.push({ ok: !conflictoDocente, texto: conflictoDocente ? 'El docente ya tiene otra sesión en esa franja' : 'El docente está libre en esa franja' });
    }
    if (!esVirtual) {
      const conflictoEspacio = this.data.sesiones.find(s => s.espacioId === this.espacioId && s.dia === this.dia && !s.virtual && this.overlaps(s, startIdx, endIdx));
      if (conflictoEspacio) ok = false;
      const espNombre = this.data.espacios.find(e => e.id === this.espacioId)?.nombre ?? this.espacioId;
      chks.push({ ok: !conflictoEspacio, texto: conflictoEspacio ? `${espNombre} ya está ocupado en esa franja` : `${espNombre} está libre en esa franja` });
    }
    this.checks.set(chks); this.checksOk.set(ok);
  }

  crear() {
    if (!this.puedeCrear()) return;
    this.guardando.set(true); this.errorServidor.set('');
    const a = this.asignaturaSeleccionada()!, tipo = this.tipoSesion();
    this.persistencia.crearSesionManual({
      asignaturaId: a.id, docenteId: this.docenteIdDelGrupo(), espacioId: tipo === 'TeoriaVirtual' ? null : (this.espacioId || null),
      dia: this.dia, horaInicio: this.horaInicio, duracionHoras: this.duracionSeleccionada(), alternancia: this.alternancia,
      tipoFlujo: tipoFlujoDesde(tipo), esVirtual: esVirtualDesde(tipo)
    }).subscribe({
      next: (sesiones: Sesion[]) => { this.guardando.set(false); this.dialogRef.close(sesiones); },
      error: (err: any) => { this.guardando.set(false); this.errorServidor.set(err?.error?.error ?? err?.message ?? 'Error al guardar la sesión.'); }
    });
  }

  cancelar() { this.dialogRef.close(); }
  private overlaps(s: Sesion, newStart: number, newEnd: number): boolean {
    const sStart = this.horasDisponibles.indexOf(s.horaInicio);
    if (sStart < 0) return false;
    const sDur = Math.max(1, Math.round(s.duracionHoras ?? this.diffH(s.horaInicio, s.horaFin)));
    return newStart < sStart + sDur && sStart < newEnd;
  }
  nombreDocente(id?: string): string { return this.data.docentes.find(d => d.id === id)?.nombre ?? '—'; }
  private diffH(i: string, f: string): number { const [hi, mi] = i.split(':').map(Number); const [hf, mf] = f.split(':').map(Number); return Math.max(1, (hf * 60 + mf - (hi * 60 + mi)) / 60); }
}

// ═══ Diálogo: Agregar sesión fija (horario base, REQUISITOS §2) ═══
interface FijaData { asignaturas: Asignatura[]; docentes: Docente[]; espacios: Espacio[]; grupos: Grupo[]; programaById: Map<string, { id: string; nombre: string }>; }

@Component({
  selector: 'app-sesion-fija-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, SearchableSelectComponent],
  template: `
    <div class="pophd">Agregar sesión fija <i (click)="ref.close()">✕</i></div>
    <div class="popbd" style="max-height:80vh;overflow:auto">
      <div class="dfield"><label>Asignatura <span class="rq">*</span></label>
        <app-searchable-select [(ngModel)]="asignaturaId" (ngModelChange)="onAsig($event)"
                                [options]="asignaturaOptions" placeholder="— Seleccione —"></app-searchable-select>
      </div>
      <div class="dfield"><label>Docente <span class="rq">*</span></label>
        <app-searchable-select [(ngModel)]="docenteId" [options]="docenteOptions" placeholder="— Seleccione —"></app-searchable-select>
      </div>
      <div class="dfield"><label>Espacio <span class="text-muted" style="font-size:11px">(opc. si virtual)</span></label>
        <app-searchable-select [(ngModel)]="espacioId" [disabled]="virtual" [options]="espacioOptions" placeholder="—"></app-searchable-select>
      </div>
      <div style="display:flex;gap:8px">
        <div class="dfield" style="flex:1"><label>Día <span class="rq">*</span></label>
          <select class="input" [(ngModel)]="dia"><option value="">—</option>@for (d of diasOpc; track d.v) { <option [value]="d.v">{{ d.l }}</option> }</select></div>
        <div class="dfield" style="width:80px"><label>Inicio <span class="rq">*</span></label>
          <select class="input" [(ngModel)]="horaInicio"><option value="">—</option>@for (h of horas; track h) { <option [value]="h">{{ h }}</option> }</select></div>
      </div>
      <div style="display:flex;gap:8px;align-items:flex-end">
        <div class="dfield" style="flex:1"><label>Duración (h) <span class="rq">*</span></label>
          <input class="input" type="number" min="1" max="8" [(ngModel)]="duracion"></div>
        <div class="dfield"><label>Alternancia</label>
          <select class="input" [(ngModel)]="alternancia"><option value="SinAlternancia">Sin</option><option value="TipoA">TipoA</option><option value="TipoB">TipoB</option></select></div>
      </div>
      <label class="radio" style="cursor:pointer"><input type="checkbox" [(ngModel)]="virtual" (ngModelChange)="onVirtual($event)" style="position:static;width:auto;height:auto;opacity:1"> Virtual</label>
      <p class="text-muted" style="font-size:11.5px;margin:0;border-top:1px dashed var(--color-neutral-300);padding-top:8px">Se fija en el horario base; el generador la respeta y agenda el resto alrededor.</p>
      <div class="popfoot">
        <button class="btn btn-secondary" (click)="ref.close()">Cancelar</button>
        <button class="btn btn-primary" [disabled]="!valido()" (click)="fijar()">Fijar sesión</button>
      </div>
    </div>
  `
})
export class SesionFijaDialogComponent {
  ref = inject(MatDialogRef<SesionFijaDialogComponent>);
  data: FijaData = inject(MAT_DIALOG_DATA);
  asignaturas = this.data.asignaturas; docentes = this.data.docentes; espacios = this.data.espacios;

  asignaturaId = ''; docenteId = ''; espacioId = ''; dia = ''; horaInicio = ''; duracion = 2;
  alternancia: 'TipoA' | 'TipoB' | 'SinAlternancia' = 'SinAlternancia'; virtual = false;

  readonly diasOpc = [{ v: 'lunes', l: 'Lunes' }, { v: 'martes', l: 'Martes' }, { v: 'miercoles', l: 'Miércoles' }, { v: 'jueves', l: 'Jueves' }, { v: 'viernes', l: 'Viernes' }, { v: 'sabado', l: 'Sábado' }];
  readonly horas = ['06:00','07:00','08:00','09:00','10:00','11:00','12:00','13:00','14:00','15:00','16:00','17:00','18:00','19:00','20:00','21:00'];

  readonly asignaturaOptions: SearchableOption[] = this.asignaturas
    .map(a => ({
      value: a.id, label: a.nombre + (a.codigo ? ` (${a.codigo})` : ''),
      sub: this.data.programaById.get(a.programaId)?.nombre
    }))
    .sort((a, b) => a.label.localeCompare(b.label));
  readonly docenteOptions: SearchableOption[] = this.docentes.map(d => ({ value: d.id, label: d.nombre }));
  readonly espacioOptions: SearchableOption[] = this.espacios.map(e => ({ value: e.id, label: e.nombre }));

  onAsig(id: string) {
    // Auto-completar el docente desde el primer grupo (con docente) de la asignatura; editable.
    const g = this.data.grupos.find(x => x.asignaturaId === id && x.docenteId);
    if (g?.docenteId) this.docenteId = g.docenteId;
  }
  onVirtual(v: boolean) { if (v) this.espacioId = ''; }
  valido(): boolean {
    if (!this.asignaturaId || !this.docenteId || !this.dia || !this.horaInicio) return false;
    if (this.duracion < 1 || this.duracion > 8) return false;
    if (!this.virtual && !this.espacioId) return false;
    const startIdx = this.horas.indexOf(this.horaInicio);
    const endIdx = startIdx + Math.round(this.duracion);
    if (endIdx > this.horas.length) return false;
    if (this.dia === 'sabado' && endIdx > this.horas.indexOf('13:00')) return false;
    return true;
  }

  fijar() {
    if (!this.valido()) return;
    const [hh, mm] = this.horaInicio.split(':').map(Number);
    const horaFin = `${String(hh + this.duracion).padStart(2, '0')}:${String(mm).padStart(2, '0')}`;
    const sesion: Sesion = {
      id: crypto.randomUUID(), asignaturaId: this.asignaturaId, docenteId: this.docenteId,
      dia: this.dia, horaInicio: this.horaInicio, horaFin, duracionHoras: this.duracion,
      espacioId: this.virtual ? undefined : (this.espacioId || undefined), virtual: this.virtual,
      alternancia: this.alternancia, tipoFlujo: this.virtual ? 'AulaVirtual' : 'Laboratorio'
    };
    this.ref.close(sesion);
  }
}

// ═══ Diálogo de progreso (HF-3 · B — generando) ═══
@Component({
  selector: 'app-progress-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule],
  template: `
    <div class="popbd" style="align-items:center;text-align:center;gap:13px;padding:26px 22px">
      <div class="spinner"></div>
      <div class="opt">OPTIMIZANDO…</div>
      <div class="prog"><i></i></div>
      <div class="text-muted" style="font-size:12px">Fase {{ phase() }}/3 · {{ faseLabel() }}</div>
      <div class="text-muted" style="font-size:11px">Puede tardar hasta 2 min. No cierres la ventana.</div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .spinner { width: 44px; height: 44px; border: 3px solid var(--color-accent-200); border-top-color: var(--color-accent); border-radius: 50%; animation: soea-spin .9s linear infinite; }
    @keyframes soea-spin { to { transform: rotate(360deg); } }
    .opt { font: 600 15px var(--font-heading); letter-spacing: .06em; color: var(--color-accent); }
    .prog { width: 100%; height: 12px; border: 1px solid var(--color-neutral-700); position: relative; overflow: hidden; }
    .prog > i { position: absolute; inset: 0 auto 0 0; width: 30%; background: var(--color-accent); animation: soea-prog 2.4s ease-in-out infinite; }
    @keyframes soea-prog { 0% { width: 12%; } 50% { width: 70%; } 100% { width: 92%; } }
  `]
})
export class ProgressDialogComponent implements OnInit, OnDestroy {
  phase = signal(1);
  faseLabel = computed(() => ({ 1: 'GraphColoring', 2: 'CP-SAT', 3: 'Algoritmo genético' }[this.phase()] ?? ''));
  private timers: ReturnType<typeof setTimeout>[] = [];
  ngOnInit() {
    this.timers.push(setTimeout(() => { if (this.phase() === 1) this.phase.set(2); }, 2000));
    this.timers.push(setTimeout(() => { if (this.phase() === 2) this.phase.set(3); }, 10000));
  }
  ngOnDestroy() { this.timers.forEach(t => clearTimeout(t)); }
}
