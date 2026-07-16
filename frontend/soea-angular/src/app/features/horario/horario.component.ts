import { Component, inject, signal, computed, OnInit, OnDestroy } from '@angular/core';
import { CommonModule, DatePipe, TitleCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { RouterModule } from '@angular/router';
import { StateService } from '../../core/state.service';
import { HorarioApiService } from '../../core/horario-api.service';
import { PersistenciaService } from '../../core/persistencia.service';
import { CatalogoService } from '../../core/catalogo.service';
import { Asignatura, ConfiguracionAlgoritmo, Docente, Espacio, Sesion, TipoSesionUi, tipoFlujoDesde, esVirtualDesde } from '../../core/models';

/** Representación visual de una sesión atómica multi-slot. */
interface MergedSesion {
  key: string;
  sesiones: Sesion[];     // siempre length=1 con el nuevo backend
  dia: string;
  horaInicio: string;
  horaFin: string;
  duracionSlots: number;
  virtual: boolean;
  alternancia: string;
  /** Semana del ciclo de alternancia ('A'/'B'). Presente desde el modelo bi-semanal. */
  semana?: 'A' | 'B';
  asignaturaId: string;
  docenteId?: string;
  espacioId?: string;
  /** Lab de origen (donde es presencial); usado para ubicar la fila virtual en su lab. */
  espacioIdHogar?: string;
  tipoFlujo?: 'Laboratorio' | 'AulaVirtual';
}

@Component({
  selector: 'app-horario',
  standalone: true,
  imports: [
    CommonModule, DatePipe, TitleCasePipe, FormsModule, MatButtonModule, MatDialogModule, MatSnackBarModule,
    RouterModule, MatProgressSpinnerModule, MatIconModule, MatMenuModule
  ],
  template: `
    <div class="horario-container">
      <div class="header-actions">
        <h1 class="page-title text-primary">Horario</h1>
        <div class="header-buttons">
          @if (state.sesiones().length > 0) {
            <button mat-stroked-button (click)="guardarComoBase()" title="Guardar este horario como base para la próxima generación">
              <mat-icon>bookmark_add</mat-icon> Guardar base
            </button>
            <button mat-stroked-button color="primary" (click)="abrirCrearSesion()" [disabled]="loadingBackend()">
              <mat-icon>add_circle_outline</mat-icon> Crear sesión
            </button>
          }
          <!-- Exportar/Importar: opción avanzada oculta en menú, no en la barra principal (decisión de diseño) -->
          <button mat-icon-button [matMenuTriggerFor]="avanzadoMenu" title="Más opciones">
            <mat-icon>more_vert</mat-icon>
          </button>
          <mat-menu #avanzadoMenu="matMenu">
            <button mat-menu-item (click)="exportarHorario()" [disabled]="state.sesiones().length === 0">
              <mat-icon>upload</mat-icon> Exportar como JSON
            </button>
            <button mat-menu-item (click)="importarInput.click()">
              <mat-icon>download</mat-icon> Importar desde JSON
            </button>
          </mat-menu>
          <input #importarInput type="file" accept=".json" style="display:none"
                 (change)="importarHorario($event)">
          <button mat-flat-button color="primary" class="primary-button" (click)="generarHorario()" [disabled]="loadingBackend()">
            <mat-icon>auto_awesome</mat-icon> Generar Horario
          </button>
        </div>
      </div>

      <!-- Parámetros avanzados de generación (colapsable) — fusiona lo que antes era Dashboard Developer -->
      <div class="advanced-panel">
        <button class="advanced-toggle" (click)="avanzadoAbierto.set(!avanzadoAbierto())">
          <mat-icon>{{ avanzadoAbierto() ? 'expand_less' : 'expand_more' }}</mat-icon>
          Parámetros avanzados <span class="advanced-hint">(opcional — todo tiene default)</span>
        </button>
        @if (avanzadoAbierto()) {
          <div class="advanced-fields">
            <label class="adv-field">
              <span>Tamaño población</span>
              <input type="number" [ngModel]="gaConfig().pobSize" (ngModelChange)="patchGaConfig({ pobSize: $event })">
            </label>
            <label class="adv-field">
              <span>Máx. generaciones</span>
              <input type="number" [ngModel]="gaConfig().maxGen" (ngModelChange)="patchGaConfig({ maxGen: $event })">
            </label>
            <label class="adv-field">
              <span>Mutación (0–1)</span>
              <input type="number" step="0.01" min="0" max="1" [ngModel]="gaConfig().mutRate" (ngModelChange)="patchGaConfig({ mutRate: $event })">
            </label>
            <label class="adv-field">
              <span>Cruce (0–1)</span>
              <input type="number" step="0.01" min="0" max="1" [ngModel]="gaConfig().crossRate" (ngModelChange)="patchGaConfig({ crossRate: $event })">
            </label>
          </div>
        }
      </div>

      <!-- Sin solución factible: logs de ejecución, solo aquí (no panel permanente) — extraído del diseño -->
      @if (state.sesiones().length === 0 && state.executionLogs().length > 0) {
        <div class="fail-panel">
          <div class="fail-msg"><mat-icon>error_outline</mat-icon> No se encontró un horario factible con el catálogo actual.</div>
          <div class="fail-hint">Logs de ejecución:</div>
          <pre class="fail-logs">{{ state.executionLogs().join('\n') }}</pre>
        </div>
      }

      @if (state.espacios().length > 0) {
        <!-- Selector de espacio -->
        <div class="space-selector">
          @for (esp of state.espacios(); track esp.id) {
            <button class="pill-button"
                    [class.active]="activeSpace()?.id === esp.id"
                    (click)="selectSpace(esp)">
              {{ esp.nombre }}
            </button>
          }
        </div>

        <!-- Selector de semana A/B -->
        <div class="week-selector">
          <span class="week-label">Semana</span>
          <button class="pill-button week-btn"
                  [class.active]="activeWeek() === 'A'"
                  (click)="selectWeek('A')">
            <span class="week-letter">A</span>
            <span class="week-sub">pares</span>
          </button>
          <button class="pill-button week-btn"
                  [class.active]="activeWeek() === 'B'"
                  (click)="selectWeek('B')">
            <span class="week-letter">B</span>
            <span class="week-sub">impares</span>
          </button>
          @if (activeWeek() === 'A') {
            <span class="week-desc">TipoA presencial · TipoB virtual</span>
          }
          @if (activeWeek() === 'B') {
            <span class="week-desc">TipoB presencial · TipoA virtual</span>
          }
        </div>

        <!-- Horarios base guardados -->
        @if (state.horariosBases().length > 0) {
          <div class="bases-panel">
            <div class="bases-header">
              <mat-icon class="bases-icon">bookmarks</mat-icon>
              <span class="bases-title">Horarios base guardados</span>
              <span class="bases-hint">Selecciona uno para usarlo en la próxima generación</span>
            </div>
            <div class="bases-list">
              @for (base of state.horariosBases(); track base.id) {
                <div class="base-item" [class.selected]="state.baseSeleccionadaId() === base.id">
                  <button class="base-radio"
                          [class.active]="state.baseSeleccionadaId() === base.id"
                          (click)="toggleBase(base.id)"
                          [title]="state.baseSeleccionadaId() === base.id ? 'Quitar selección' : 'Usar como base'">
                    <mat-icon>{{ state.baseSeleccionadaId() === base.id ? 'radio_button_checked' : 'radio_button_unchecked' }}</mat-icon>
                  </button>
                  <div class="base-info">
                    <span class="base-nombre">{{ base.nombre }}</span>
                    <span class="base-meta">{{ base.sesiones.length }} sesiones · {{ base.creadoEn | date:'dd/MM/yyyy HH:mm' }}</span>
                  </div>
                  <button mat-icon-button class="base-delete" (click)="eliminarBase(base.id)" title="Eliminar base">
                    <mat-icon>delete_outline</mat-icon>
                  </button>
                </div>
              }
            </div>
            @if (state.baseSeleccionada(); as base) {
              <div class="base-activa-hint">
                <mat-icon>check_circle</mat-icon>
                Usando base "{{ base.nombre }}" — {{ base.sesiones.length }} sesiones fijas en la próxima generación
              </div>
            }
          </div>
        }

        @if (!backendReady()) {
          <div class="backend-alert">
            <mat-icon>cloud_off</mat-icon>
            <span>Sin conexión al backend. Carga datos desde la API para continuar.</span>
            <button mat-stroked-button (click)="syncFromBackend()" [disabled]="loadingBackend()">Reintentar</button>
          </div>
        }

        <!-- Matriz de horario -->
        <div class="matrix-scroll">
          <table class="horario-matrix">
            <thead>
              <tr>
                <th class="time-col">Hora</th>
                @for (dia of dias; track dia) {
                  <th>{{ dia | titlecase }}</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (franja of franjas; track franja) {
                <tr>
                  <td class="time-cell">{{ franja }}</td>
                  @for (dia of dias; track dia) {
                    @if (!isCoveredByMergedPrior(dia, franja)) {
                      <td class="matrix-cell"
                          [attr.rowspan]="getMergedRowspan(dia, franja)"
                          [class.out-of-hours]="isOutOfHours(dia, franja)">

                        <div class="cell-container">

                          @for (merged of getMergedCellSesiones(dia, franja); track merged.key) {
                            <div class="session-card"
                                 (click)="abrirEditarSesion(merged)"
                                 [class.presencial]="!merged.virtual"
                                 [class.virtual]="merged.virtual"
                                 [class.tipo-a]="esTipoA(merged)"
                                 title="Clic para editar sesión">

                              @if (esTipoA(merged)) {
                                <div class="tipo-a-badge">Tipo A</div>
                              }
                              <div class="card-context">{{ getContextLabel(merged) }}</div>
                              <div class="card-title">{{ getAsignaturaName(merged) }}</div>
                              <div class="card-sub">{{ getDocenteName(merged) }}</div>
                              <div class="card-duration">{{ merged.horaInicio }} – {{ merged.horaFin }}</div>
                              <div class="card-badges">
                                <span class="badge-tipo" [class.badge-tipo-lab]="merged.tipoFlujo === 'Laboratorio'">{{ tipoLabel(merged) }}</span>
                                @if (merged.virtual) {
                                  <span class="badge-virtual">Virtual</span>
                                }
                                @if (merged.semana) {
                                  <span class="badge-semana">S.{{ merged.semana }}</span>
                                }
                                @if (merged.alternancia !== 'SinAlternancia' && !merged.semana) {
                                  <span class="badge-alt">{{ merged.alternancia }}</span>
                                }
                                @if (getGrupoLabel(merged); as g) {
                                  <span class="badge-grupo">{{ g }}</span>
                                }
                              </div>
                            </div>
                          }

                        </div>
                      </td>
                    }
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>
      } @else {
        <div class="empty-state">
          <mat-icon>event_busy</mat-icon>
          <p>No hay datos cargados. Ve a <strong>Catálogo</strong> para comenzar.</p>
          <button mat-stroked-button routerLink="/catalogo">Ir a Catálogo</button>
        </div>
      }
    </div>
  `,
  styles: [`
    .horario-container { padding: 16px; background: white; border-radius: 8px; border: 1px solid #e0e0e0; min-height: 500px; }
    .header-actions { display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px; }
    .header-buttons { display: flex; gap: 10px; align-items: center; }
    .page-title { margin: 0; font-weight: 500; font-size: 24px; }
    .advanced-panel { border: 1px solid #e0e0e0; border-radius: 6px; margin-bottom: 16px; overflow: hidden; }
    .advanced-toggle { display: flex; align-items: center; gap: 6px; width: 100%; padding: 8px 12px; background: #fafafa;
      border: none; cursor: pointer; font-size: 13px; font-weight: 500; color: #424242; text-align: left; }
    .advanced-hint { font-weight: 400; color: #9e9e9e; font-size: 11.5px; }
    .advanced-fields { display: flex; gap: 16px; flex-wrap: wrap; padding: 12px; }
    .adv-field { display: flex; flex-direction: column; gap: 4px; font-size: 12px; color: #757575; width: 140px; }
    .adv-field input { padding: 6px 8px; border: 1px solid #d0d0d0; border-radius: 4px; font-size: 13px; }
    .fail-panel { border: 1px solid #ffcdd2; background: #fff5f5; border-radius: 6px; padding: 12px 14px; margin-bottom: 16px; }
    .fail-msg { display: flex; align-items: center; gap: 8px; color: #c62828; font-weight: 500; font-size: 13.5px; margin-bottom: 10px; }
    .fail-hint { font-size: 11.5px; color: #757575; margin-bottom: 4px; }
    .fail-logs { background: #1e1e1e; color: #c9c9c9; font: 11px/1.6 ui-monospace, Menlo, monospace;
      padding: 10px 12px; border-radius: 4px; max-height: 200px; overflow: auto; margin: 0; white-space: pre-wrap; }
    .space-selector { display: flex; gap: 8px; margin-bottom: 24px; overflow-x: auto; padding-bottom: 8px; flex-wrap: wrap; }
    .pill-button { padding: 8px 16px; border-radius: 20px; border: 1px solid #e0e0e0; background: white; cursor: pointer; transition: 0.2s; white-space: nowrap; }
    .pill-button.active { background: #1976d2; color: white; border-color: #1976d2; }
    .week-selector { display: flex; gap: 8px; align-items: center; margin-bottom: 16px; flex-wrap: wrap; }
    .week-label { font-size: 11px; letter-spacing: 0.08em; text-transform: uppercase; color: #757575; }
    .week-btn { display: flex; flex-direction: column; align-items: center; padding: 6px 18px; min-width: 72px; }
    .week-letter { font-size: 15px; font-weight: 700; line-height: 1; }
    .week-sub { font-size: 9px; color: #9e9e9e; letter-spacing: 0.05em; margin-top: 1px; }
    .pill-button.week-btn.active .week-sub { color: rgba(255,255,255,0.75); }
    .week-desc { font-size: 11px; color: #9e9e9e; align-self: center; margin-left: 4px; font-style: italic; }
    .bases-panel { background: #f8f9fa; border: 1px solid #e0e0e0; border-radius: 8px; padding: 12px 16px; margin-bottom: 16px; }
    .bases-header { display: flex; align-items: center; gap: 8px; margin-bottom: 10px; }
    .bases-icon { font-size: 18px; width: 18px; height: 18px; color: #1976d2; }
    .bases-title { font-weight: 600; font-size: 13px; }
    .bases-hint { font-size: 11px; color: #9e9e9e; margin-left: 4px; }
    .bases-list { display: flex; flex-direction: column; gap: 6px; }
    .base-item { display: flex; align-items: center; gap: 8px; padding: 6px 10px; border-radius: 6px; background: white; border: 1px solid #e0e0e0; transition: 0.15s; }
    .base-item.selected { border-color: #1976d2; background: #e3f2fd; }
    .base-radio { background: none; border: none; cursor: pointer; padding: 4px; display: flex; align-items: center; color: #9e9e9e; }
    .base-radio.active { color: #1976d2; }
    .base-info { flex: 1; display: flex; flex-direction: column; gap: 2px; }
    .base-nombre { font-size: 13px; font-weight: 500; }
    .base-meta { font-size: 11px; color: #9e9e9e; }
    .base-delete { color: #bdbdbd !important; }
    .base-delete:hover { color: #f44336 !important; }
    .base-activa-hint { display: flex; align-items: center; gap: 6px; margin-top: 8px; font-size: 12px; color: #1976d2; font-weight: 500; }
    .base-activa-hint mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .backend-alert {
      display: flex; align-items: center; gap: 12px; padding: 8px 12px; margin-bottom: 16px;
      border: 1px solid #ffe0b2; border-radius: 8px; background: #fff3e0; color: #8d6e63;
    }
    .backend-alert mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .matrix-scroll { overflow-x: auto; }
    .horario-matrix { width: 100%; border-collapse: collapse; min-width: 820px; table-layout: fixed; }
    .horario-matrix th, .horario-matrix td { border: 1px solid #e0e0e0; padding: 4px; text-align: center; vertical-align: top; }
    .time-col { width: 72px; }
    .time-cell { font-size: 12px; font-weight: 500; color: #757575; vertical-align: middle; white-space: nowrap; }
    .matrix-cell { height: 72px; position: relative; }
    .out-of-hours { background: repeating-linear-gradient(45deg, #f5f5f5, #f5f5f5 8px, #eeeeee 8px, #eeeeee 16px); }

    .cell-container { min-height: 64px; height: 100%; display: flex; flex-direction: column; gap: 3px; padding: 2px; }

    .session-card {
      padding: 6px 8px; border-radius: 4px; text-align: left; font-size: 11px;
      cursor: pointer; position: relative; box-shadow: 0 1px 3px rgba(0,0,0,0.15);
      user-select: none; flex: 1; transition: box-shadow 0.15s, transform 0.1s;
    }
    .session-card:hover { box-shadow: 0 3px 8px rgba(0,0,0,0.22); transform: translateY(-1px); }

    .presencial { background-color: #e8f5e9; border-left: 4px solid #388e3c; }
    .virtual    { background-color: #fafafa;  border-left: 4px solid #9e9e9e; }
    .tipo-a            { border-left-color: #f57c00; }
    .tipo-a.presencial { background-color: #fff8e1; }

    .card-context  { font-size: 9px; color: #9e9e9e; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; margin-bottom: 1px; }
    .card-title    { font-weight: 600; margin-bottom: 2px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .card-sub      { color: #616161; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .card-duration { color: #9e9e9e; font-size: 10px; margin-top: 2px; }
    .card-badges   { display: flex; gap: 4px; margin-top: 3px; flex-wrap: wrap; }
    .badge-virtual { padding: 1px 5px; background: #e0e0e0; border-radius: 10px; font-size: 9px; }
    .badge-semana  { padding: 1px 5px; background: #ede7f6; color: #512da8; border-radius: 10px; font-size: 9px; font-weight: 600; }
    .badge-tipo     { padding: 1px 5px; background: #eceff1; color: #37474f; border-radius: 10px; font-size: 9px; font-weight: 600; }
    .badge-tipo-lab { background: #e0f2f1; color: #00695c; }
    .badge-alt     { padding: 1px 5px; background: #e3f2fd; color: #1565c0; border-radius: 10px; font-size: 9px; }
    .badge-grupo   { padding: 1px 5px; background: #e8f5e9; color: #2e7d32; border-radius: 10px; font-size: 9px; font-weight: 600; }
    .tipo-a-badge  { position: absolute; top: 2px; right: 2px; font-size: 9px; background: #ff9800; color: white; padding: 1px 4px; border-radius: 2px; }

    .empty-state { text-align: center; padding: 64px 32px; color: #757575; }
    .empty-state mat-icon { font-size: 64px; width: 64px; height: 64px; margin-bottom: 16px; }
    .empty-state p { font-size: 16px; margin-bottom: 16px; }
  `]
})
export class HorarioComponent implements OnInit {
  state     = inject(StateService);
  dialog    = inject(MatDialog);
  snackBar  = inject(MatSnackBar);
  horarioApi = inject(HorarioApiService);
  persistencia = inject(PersistenciaService);
  catalogo  = inject(CatalogoService);

  dias = ['lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado'];
  franjas = [
    '06:00','07:00','08:00','09:00','10:00','11:00','12:00',
    '13:00','14:00','15:00','16:00','17:00','18:00','19:00','20:00'
  ];

  activeSpace = signal<Espacio | null>(null);
  activeWeek = signal<'A' | 'B'>('A');
  loadingBackend = signal(false);
  backendReady = signal(false);
  avanzadoAbierto = signal(false);

  gaConfig = this.state.configuracionAlgoritmo;
  patchGaConfig(patch: Partial<ConfiguracionAlgoritmo>) {
    this.state.setConfiguracionAlgoritmo({ ...this.gaConfig(), ...patch });
  }

  ngOnInit() {
    this.syncFromBackend();
  }

  constructor() {
    const espacios = this.state.espacios();
    if (espacios.length > 0) this.activeSpace.set(espacios[0]);
  }

  selectSpace(esp: Espacio) { this.activeSpace.set(esp); }
  selectWeek(week: 'A' | 'B') { this.activeWeek.set(week); }

  syncFromBackend() {
    this.loadingBackend.set(true);
    this.catalogo.cargarTodo().subscribe({
      next: () => {
        this.loadingBackend.set(false);
        this.backendReady.set(true);

        const espacios = this.state.espacios();
        const current = this.activeSpace();
        if (!current || !espacios.find(e => e.id === current.id)) {
          this.activeSpace.set(espacios[0] ?? null);
        }
      },
      error: () => {
        this.loadingBackend.set(false);
        this.backendReady.set(false);
        this.snackBar.open(
          'No se pudo conectar con el backend. Verifica que la API esté activa.',
          'Cerrar',
          { duration: 5000, panelClass: ['snack-error'] }
        );
      }
    });
  }

  // ── Merged session computation ────────────────────────────────────────────────

  /**
   * Cada sesión es atómica y trae su duración real desde el backend.
   * `duracionSlots` proviene de `sesion.duracionHoras` (1, 2, 3 horas, ...).
   * Si por error vinieran dos sesiones consecutivas legítimas, se renderizan como
   * cards separados — NO se fusionan (eso era el bug visual viejo).
   */
  private computeMergedMap(spaceId: string | undefined, allSesiones: Sesion[]): Map<string, MergedSesion[]> {
    const map = new Map<string, MergedSesion[]>();

    // Una sesión pertenece a un lab si es presencial ahí (espacioId) o si es su lab de origen
    // (espacioIdHogar) cuando esa semana se dicta virtual. Así la fila virtual aparece SOLO en su
    // laboratorio, no en todos. Fallback a la lógica previa si el backend no envía el lab de origen.
    const visible = allSesiones.filter(s =>
      this.sesionPerteneceAlEspacio(s, spaceId) && this.sesionVisibleEnSemana(s)
    );

    for (const s of visible) {
      const dur = Math.max(1, Math.round(s.duracionHoras ?? this.diffHoras(s.horaInicio, s.horaFin)));
      const merged: MergedSesion = {
        key:           s.id,
        sesiones:      [s],
        dia:           s.dia,
        horaInicio:    s.horaInicio,
        horaFin:       s.horaFin,
        duracionSlots: dur,
        virtual:       s.virtual,
        alternancia:   s.alternancia,
        semana:        s.semana,
        asignaturaId:  s.asignaturaId,
        docenteId:     s.docenteId,
        espacioId:     s.espacioId,
        espacioIdHogar: s.espacioIdHogar,
        tipoFlujo:     s.tipoFlujo,
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

  /**
   * Una sesión se muestra en la matriz de un lab si:
   *   - es presencial en ese lab (espacioId === spaceId), o
   *   - ese lab es su origen (espacioIdHogar === spaceId) cuando la fila es virtual.
   * Sin spaceId seleccionado no filtra por espacio. Fallback: si no llega espacioIdHogar,
   * conserva la regla previa (toda virtual visible) para datos anteriores al campo.
   */
  private sesionPerteneceAlEspacio(s: Sesion, spaceId: string | undefined): boolean {
    if (!spaceId) return true;
    if (s.espacioId === spaceId) return true;
    if (s.espacioIdHogar) return s.espacioIdHogar === spaceId;
    return s.virtual; // fallback datos legacy sin lab de origen
  }

  private sesionVisibleEnSemana(s: Sesion): boolean {
    // Modelo bi-semanal (Incremento 1): cada DTO ya trae su semana explícita.
    if (s.semana) return s.semana === this.activeWeek();
    // Fallback para datos previos al modelo bi-semanal.
    if (s.alternancia === 'SinAlternancia') return true;
    return this.activeWeek() === 'A' ? s.alternancia === 'TipoA' : s.alternancia === 'TipoB';
  }

  mergedByCell = computed(() =>
    this.computeMergedMap(this.activeSpace()?.id, this.state.sesiones())
  );

  /** Set of cellIds that are "swallowed" by a prior rowspan and must not render a <td>. */
  coveredCells = computed(() => {
    const covered = new Set<string>();
    for (const mergedList of this.mergedByCell().values()) {
      for (const m of mergedList) {
        if (m.duracionSlots <= 1) continue;
        const startIdx = this.franjas.indexOf(m.horaInicio);
        for (let k = 1; k < m.duracionSlots; k++) {
          const idx = startIdx + k;
          if (idx < this.franjas.length) {
            covered.add(this.cellId(m.dia, this.franjas[idx]));
          }
        }
      }
    }
    return covered;
  });

  getMergedCellSesiones(dia: string, franja: string): MergedSesion[] {
    return this.mergedByCell().get(this.cellId(dia, franja)) ?? [];
  }

  isCoveredByMergedPrior(dia: string, franja: string): boolean {
    return this.coveredCells().has(this.cellId(dia, franja));
  }

  getMergedRowspan(dia: string, franja: string): number {
    const merged = this.getMergedCellSesiones(dia, franja);
    if (merged.length === 0) return 1;
    return Math.max(...merged.map(m => m.duracionSlots));
  }

  // ── Helpers de celda ─────────────────────────────────────────────────────────

  cellId(dia: string, franja: string): string {
    return `cell-${dia}-${franja.replace(':', '')}`;
  }

  /** Celdas fuera de horario — computado una sola vez (dias/franjas son estáticos). */
  private readonly outOfHoursCells = computed(() => {
    const set = new Set<string>();
    const sabadoLimit = this.franjas.indexOf('13:00');
    this.franjas.forEach((franja, idx) => {
      if (idx >= sabadoLimit) set.add(this.cellId('sabado', franja));
    });
    return set;
  });

  isOutOfHours(dia: string, franja: string): boolean {
    return this.outOfHoursCells().has(this.cellId(dia, franja));
  }

  esTipoA(merged: MergedSesion): boolean {
    return merged.alternancia === 'TipoA';
  }

  tipoLabel(merged: MergedSesion): string {
    return merged.tipoFlujo === 'Laboratorio' ? 'Lab' : 'Teoría';
  }

  getAsignaturaName(merged: MergedSesion): string {
    return this.state.asignaturaById().get(merged.asignaturaId)?.nombre ?? 'Desconocida';
  }

  getDocenteName(merged: MergedSesion): string {
    if (!merged.docenteId) return '— Sin docente —';
    return this.state.docenteById().get(merged.docenteId)?.nombre ?? '';
  }

  getContextLabel(merged: MergedSesion): string {
    const asig = this.state.asignaturaById().get(merged.asignaturaId);
    if (!asig) return '';
    const prog = this.state.programaById().get(asig.programaId);
    if (!prog) return '';
    const fac = this.state.facultadById().get(prog.facultadId);
    return fac ? `${fac.nombre} · ${prog.nombre}` : prog.nombre;
  }

  getGrupoLabel(merged: MergedSesion): string | null {
    const asig = this.state.asignaturaById().get(merged.asignaturaId);
    return asig?.grupoNumero ? `G${asig.grupoNumero}` : null;
  }

  // ── Editar sesión (clic en card) ─────────────────────────────────────────────

  abrirEditarSesion(merged: MergedSesion) {
    const ref = this.dialog.open(EditarSesionDialogComponent, {
      width: '580px',
      maxHeight: '90vh',
      data: {
        merged,
        sesion:       merged.sesiones[0],
        asignaturas:  this.state.asignaturas(),
        docentes:     this.state.docentes(),
        espacios:     this.state.espacios(),
        sesiones:     this.state.sesiones(),
        programaById: this.state.programaById(),
        facultadById: this.state.facultadById(),
      } satisfies EditarSesionDialogData
    });

    ref.afterClosed().subscribe((result: EditarSesionResult | undefined) => {
      if (!result) return;
      this.state.updateSesion(result.sesion);
      if (result.advertencias?.length) {
        this.snackBar.open(
          `Sesión actualizada con avisos: ${result.advertencias.join(' · ')}`,
          'Cerrar', { duration: 8000 }
        );
      } else {
        this.snackBar.open('Sesión actualizada.', '', { duration: 2500 });
      }
    });
  }

  // ── Exportar / Importar horario JSON ─────────────────────────────────────────

  exportarHorario() {
    const sesiones = this.state.sesiones();
    const payload = {
      version: 1,
      exportadoEn: new Date().toISOString(),
      sesiones
    };
    const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
    const url  = URL.createObjectURL(blob);
    const a    = document.createElement('a');
    a.href     = url;
    a.download = `horario-soea-${new Date().toISOString().slice(0, 10)}.json`;
    a.click();
    URL.revokeObjectURL(url);
    this.snackBar.open(`✅ Horario exportado (${sesiones.length} sesiones).`, '', { duration: 3000 });
  }

  importarHorario(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = (e) => {
      try {
        const json = JSON.parse(e.target!.result as string);
        const sesiones = json.sesiones ?? json; // soporta array plano o { sesiones: [...] }
        if (!Array.isArray(sesiones)) throw new Error('Formato inválido: se espera un array de sesiones.');
        this.state.setSesiones(sesiones);
        this.snackBar.open(`✅ Horario importado: ${sesiones.length} sesiones cargadas.`, 'Cerrar', { duration: 5000 });
      } catch (err: any) {
        this.snackBar.open(`❌ Error al leer el archivo: ${err.message}`, 'Cerrar', { duration: 6000, panelClass: ['snack-error'] });
      }
      (event.target as HTMLInputElement).value = '';
    };
    reader.readAsText(file);
  }

  // ── Horarios base ─────────────────────────────────────────────────────────────

  guardarComoBase() {
    const nombre = window.prompt('Nombre del horario base:', `Base ${new Date().toLocaleDateString('es-CO')}`);
    if (!nombre?.trim()) return;
    const base = this.state.guardarHorarioBase(nombre);
    this.snackBar.open(`✅ Horario base "${base.nombre}" guardado (${base.sesiones.length} sesiones).`, 'Cerrar', { duration: 4000 });
  }

  toggleBase(id: string) {
    this.state.seleccionarBase(this.state.baseSeleccionadaId() === id ? null : id);
  }

  eliminarBase(id: string) {
    const base = this.state.horariosBases().find(b => b.id === id);
    if (!base) return;
    if (!window.confirm(`¿Eliminar la base "${base.nombre}"?`)) return;
    this.state.eliminarHorarioBase(id);
    this.snackBar.open(`Base "${base.nombre}" eliminada.`, '', { duration: 3000 });
  }

  // ── Generación de horario ────────────────────────────────────────────────────

  generarHorario() {
    if (!this.backendReady()) {
      this.snackBar.open('Conecta el backend antes de generar el horario.', 'Cerrar', { duration: 4000 });
      return;
    }
    if (this.state.asignaturas().length === 0 || this.state.espacios().length === 0 || this.state.docentes().length === 0) {
      this.snackBar.open('Carga asignaturas, docentes y espacios antes de generar el horario.', 'Cerrar', { duration: 4000 });
      return;
    }

    const asignaturas = this.state.asignaturas();

    const dialogRef = this.dialog.open(ProgressDialogComponent, { disableClose: true, width: '500px' });

    this.horarioApi
      .generarHorario(
        asignaturas,
        this.state.docentes(),
        this.state.espacios(),
        this.state.configuracionAlgoritmo(),
        '2026-1',
        this.state.baseSeleccionada() ?? undefined
      )
      .subscribe({
        next: (respuesta) => {
          dialogRef.close();
          const sesiones = this.horarioApi.mapearSesiones(respuesta.sesiones);
          this.state.setSesiones(sesiones);
          this.state.setExecutionLogs(respuesta.logs || []);
          this.snackBar.open(
            `✅ Horario generado: ${sesiones.length} sesiones (fitness: ${respuesta.puntajeFitness.toFixed(2)}).`,
            'Cerrar',
            { duration: 6000 }
          );
        },
        error: (err: any) => {
          dialogRef.close();
          const mensaje = err.mensajeError || err.message || err.error || 'Error desconocido';
          if (err.logs && Array.isArray(err.logs)) {
             this.state.setExecutionLogs(err.logs);
          }
          const texto = /factible|infeasible/i.test(mensaje)
            ? `❌ No se encontró un horario factible para ${asignaturas.length} asignatura(s) en ` +
              `${this.state.espacios().length} espacio(s). Revisa la cantidad/capacidad de laboratorios.`
            : `❌ ${mensaje}`;
          this.snackBar.open(texto, 'Cerrar', { duration: 9000, panelClass: ['snack-error'] });
        }
      });
  }

  // ── Crear sesión manual ──────────────────────────────────────────────────────

  abrirCrearSesion() {
    const ref = this.dialog.open(CrearSesionDialogComponent, {
      width: '580px',
      maxHeight: '90vh',
      data: {
        asignaturas: this.state.asignaturas(),
        docentes:    this.state.docentes(),
        espacios:    this.state.espacios(),
        sesiones:    this.state.sesiones(),
        programaById: this.state.programaById()
      }
    });

    ref.afterClosed().subscribe((nuevas: Sesion[] | undefined) => {
      if (!nuevas?.length) return;
      this.state.sesiones.update(prev => [...prev, ...nuevas]);
      this.snackBar.open(
        `✅ Sesión creada correctamente (${nuevas.length} fila${nuevas.length > 1 ? 's' : ''} añadidas al horario).`,
        'Cerrar', { duration: 5000 }
      );
    });
  }

}

// ─── Interfaces compartidas ───────────────────────────────────────────────────

interface Check { ok: boolean; texto: string; }

// ─── Diálogo: Editar sesión (click en card) ───────────────────────────────────

interface EditarSesionDialogData {
  merged:       MergedSesion;
  sesion:       Sesion;
  asignaturas:  Asignatura[];
  docentes:     Docente[];
  espacios:     Espacio[];
  sesiones:     Sesion[];
  programaById: Map<string, { id: string; nombre: string; facultadId: string }>;
  facultadById: Map<string, { id: string; nombre: string }>;
}

interface EditarSesionResult {
  sesion:       Sesion;
  advertencias: string[];
}

@Component({
  selector: 'app-editar-sesion-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule,
            MatProgressSpinnerModule, MatIconModule],
  template: `
<h2 mat-dialog-title class="dlg-title">
  <mat-icon>edit_calendar</mat-icon>
  <span>Editar sesión</span>
  <span class="dlg-asig-name">{{ asignatura()?.nombre ?? 'Sesión' }}</span>
</h2>

<mat-dialog-content class="dlg-content">

  <!-- ── Bloque de información ── -->
  <div class="info-strip">
    <div class="info-item">
      <mat-icon class="info-icon">schedule</mat-icon>
      <span>{{ data.sesion.duracionHoras }}h por sesión <em>(fijo por plan)</em></span>
    </div>
    <div class="info-item">
      <mat-icon class="info-icon">{{ data.sesion.virtual ? 'videocam' : 'science' }}</mat-icon>
      <span>{{ data.sesion.virtual ? 'Virtual (sincrónica)' : 'Presencial' }}</span>
    </div>
    <div class="info-item">
      <mat-icon class="info-icon">{{ data.sesion.tipoFlujo === 'Laboratorio' ? 'biotech' : 'menu_book' }}</mat-icon>
      <span>{{ data.sesion.tipoFlujo === 'Laboratorio' ? 'Laboratorio' : 'Teoría' }}</span>
    </div>
    @if (contexto()) {
      <div class="info-item">
        <mat-icon class="info-icon">account_tree</mat-icon>
        <span>{{ contexto() }}</span>
      </div>
    }
  </div>

  <!-- ── Sección 1: Docente ── -->
  <section class="form-section">
    <div class="section-label"><span class="step-num">1</span> Docente</div>
    <div class="field-group">
      <label class="field-label">Asignar docente</label>
      <select class="field-select" [ngModel]="docenteId()" (ngModelChange)="docenteId.set($event)">
        <option value="">— Sin asignar —</option>
        @for (d of data.docentes; track d.id) {
          <option [value]="d.id">{{ d.nombre }}</option>
        }
      </select>
      @if (hayCambioDocente()) {
        <div class="change-hint"><mat-icon class="hint-icon">sync</mat-icon> Se guardará en el servidor (HC-I01 duro; disponibilidad como aviso).</div>
      }
    </div>
  </section>

  <!-- ── Sección 2: Cuándo y dónde ── -->
  <section class="form-section">
    <div class="section-label"><span class="step-num">2</span> Cuándo y dónde</div>

    <div class="day-picker">
      <span class="field-label">Día</span>
      <div class="day-pills">
        @for (d of diasOpciones; track d.valor) {
          <button type="button" class="day-pill" [class.active]="dia() === d.valor"
                  (click)="dia.set(d.valor)">{{ d.etiqueta }}</button>
        }
      </div>
    </div>

    <div class="two-col">
      <div class="field-group">
        <label class="field-label">Hora de inicio</label>
        <select class="field-select" [ngModel]="horaInicio()" (ngModelChange)="horaInicio.set($event)">
          @for (h of horasDisponibles; track h) {
            <option [value]="h">{{ h }}</option>
          }
        </select>
      </div>
      <div class="field-group">
        <label class="field-label">Espacio</label>
        <select class="field-select" [ngModel]="espacioId()" (ngModelChange)="espacioId.set($event)">
          <option value="">— Sin espacio (virtual) —</option>
          @for (e of data.espacios; track e.id) {
            <option [value]="e.id">{{ e.nombre }}</option>
          }
        </select>
      </div>
    </div>
  </section>

  <!-- ── Sección 3: Alternancia y semana ── -->
  <!-- Solo el track de Laboratorio alterna (decisión de diseño); teoría queda fija en
       Sin alternancia — ver ModalidadSemanal.Derivar en el backend. -->
  @if (esLaboratorio()) {
    <section class="form-section">
      <div class="section-label"><span class="step-num">3</span> Alternancia</div>
      <div class="alt-pills">
        <button type="button" class="alt-pill" [class.active]="alternancia() === 'TipoA'"
                (click)="alternancia.set('TipoA')">
          <strong>Tipo A</strong><br><small>Presencial S.A · Virtual S.B</small>
        </button>
        <button type="button" class="alt-pill" [class.active]="alternancia() === 'TipoB'"
                (click)="alternancia.set('TipoB')">
          <strong>Tipo B</strong><br><small>Virtual S.A · Presencial S.B</small>
        </button>
        <button type="button" class="alt-pill" [class.active]="alternancia() === 'SinAlternancia'"
                (click)="alternancia.set('SinAlternancia')">
          <strong>Sin alternancia</strong><br><small>Presencial siempre</small>
        </button>
      </div>

      @if (alternancia() !== 'SinAlternancia') {
        <div class="field-group" style="margin-top:10px">
          <span class="field-label">Semana del ciclo</span>
          <div class="day-pills">
            <button type="button" class="day-pill" [class.active]="semana() === 'A'" (click)="semana.set('A')">A (pares)</button>
            <button type="button" class="day-pill" [class.active]="semana() === 'B'" (click)="semana.set('B')">B (impares)</button>
            <button type="button" class="day-pill" [class.active]="semana() === undefined" (click)="semana.set(undefined)">Ambas</button>
          </div>
        </div>
      }
    </section>
  }

  <!-- ── Panel de validación ── -->
  @if (validaciones().length > 0) {
    <section class="checks-panel">
      <div class="checks-title">Validación</div>
      @for (c of validaciones(); track c.texto) {
        <div class="check-row" [class.ok]="c.ok" [class.fail]="!c.ok">
          <mat-icon>{{ c.ok ? 'check_circle' : 'cancel' }}</mat-icon>
          <span>{{ c.texto }}</span>
        </div>
      }
    </section>
  }

  <!-- ── Advertencias del servidor (blandas) ── -->
  @if (advertencias().length > 0) {
    <div class="warn-panel">
      @for (w of advertencias(); track w) {
        <div class="warn-row"><mat-icon>warning</mat-icon><span>{{ w }}</span></div>
      }
    </div>
  }

  <!-- ── Error duro del servidor ── -->
  @if (errorServidor()) {
    <div class="server-error">{{ errorServidor() }}</div>
  }

  @if (guardando()) {
    <div class="saving-row"><mat-spinner diameter="20"></mat-spinner><span>Guardando…</span></div>
  }

</mat-dialog-content>

<mat-dialog-actions align="end" class="dlg-actions">
  <button mat-stroked-button (click)="cancelar()" [disabled]="guardando()">Cancelar</button>
  <button mat-flat-button color="primary"
          [disabled]="!puedeGuardar() || guardando()"
          (click)="guardar()">
    <mat-icon>save</mat-icon>
    {{ guardando() ? 'Guardando…' : 'Guardar cambios' }}
  </button>
</mat-dialog-actions>
  `,
  styles: [`
    .dlg-title { display: flex; align-items: center; gap: 8px; font-size: 18px; flex-wrap: wrap; }
    .dlg-asig-name { font-size: 14px; color: #1976d2; font-weight: 600; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .dlg-content { display: flex; flex-direction: column; gap: 16px; padding: 12px 0; min-width: 0; }
    .dlg-content > * { flex-shrink: 0; }
    .dlg-actions { padding: 12px 0 0; gap: 10px; }

    .info-strip { display: flex; flex-wrap: wrap; gap: 12px; padding: 10px 14px; background: #f5f5f5; border-radius: 8px; }
    .info-item { display: flex; align-items: center; gap: 6px; font-size: 12px; color: #616161; }
    .info-icon { font-size: 15px; width: 15px; height: 15px; color: #9e9e9e; }
    em { color: #bdbdbd; }

    .form-section { background: #fafafa; border: 1px solid #f0f0f0; border-radius: 8px; padding: 14px; display: flex; flex-direction: column; gap: 12px; }
    .section-label { display: flex; align-items: center; gap: 8px; font-weight: 600; font-size: 12px; color: #424242; text-transform: uppercase; letter-spacing: 0.05em; }
    .step-num { width: 20px; height: 20px; background: #1976d2; color: white; border-radius: 50%; display: flex; align-items: center; justify-content: center; font-size: 10px; font-weight: 700; flex-shrink: 0; }

    .field-group { display: flex; flex-direction: column; gap: 4px; }
    .field-label { font-size: 12px; color: #757575; font-weight: 500; }
    .field-select { padding: 8px 10px; border: 1px solid #d0d0d0; border-radius: 6px; font-size: 13px; background: white; width: 100%; cursor: pointer; outline: none; }
    .field-select:focus { border-color: #1976d2; box-shadow: 0 0 0 2px rgba(25,118,210,.2); }

    .change-hint { display: flex; align-items: center; gap: 5px; font-size: 11px; color: #1565c0; background: #e3f2fd; padding: 5px 8px; border-radius: 4px; }
    .hint-icon { font-size: 13px; width: 13px; height: 13px; }

    .day-picker { display: flex; flex-direction: column; gap: 6px; }
    .day-pills { display: flex; gap: 6px; flex-wrap: wrap; }
    .day-pill { padding: 6px 12px; border: 1px solid #e0e0e0; border-radius: 20px; background: white; cursor: pointer; font-size: 12px; font-weight: 500; transition: 0.15s; }
    .day-pill:hover { background: #e3f2fd; border-color: #90caf9; }
    .day-pill.active { background: #1976d2; color: white; border-color: #1976d2; }

    .two-col { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }

    .alt-pills { display: flex; gap: 8px; flex-wrap: wrap; }
    .alt-pill { flex: 1; min-width: 120px; padding: 9px 10px; border: 1px solid #e0e0e0; border-radius: 8px; background: white; cursor: pointer; text-align: center; line-height: 1.4; font-size: 12px; transition: 0.15s; }
    .alt-pill:hover { background: #f5f5f5; }
    .alt-pill.active { background: #e3f2fd; border-color: #1976d2; color: #1565c0; }

    .checks-panel { border: 1px solid #e0e0e0; border-radius: 8px; overflow: hidden; }
    .checks-title { padding: 8px 12px; background: #f5f5f5; font-size: 11px; font-weight: 600; color: #616161; text-transform: uppercase; letter-spacing: 0.05em; }
    .check-row { display: flex; align-items: center; gap: 8px; padding: 7px 12px; font-size: 12px; border-top: 1px solid #f5f5f5; }
    .check-row.ok mat-icon  { color: #2e7d32; }
    .check-row.fail mat-icon { color: #c62828; }
    .check-row.fail { background: #fff8f8; }

    .warn-panel { border: 1px solid #ffe0b2; border-radius: 8px; background: #fff8e1; }
    .warn-row { display: flex; align-items: flex-start; gap: 8px; padding: 8px 12px; font-size: 12px; color: #e65100; }
    .warn-row mat-icon { font-size: 16px; width: 16px; height: 16px; margin-top: 1px; color: #f57c00; flex-shrink: 0; }

    .server-error { background: #ffebee; color: #c62828; padding: 10px 14px; border-radius: 6px; font-size: 12px; }
    .saving-row { display: flex; align-items: center; gap: 10px; color: #1565c0; font-size: 13px; }
  `]
})
export class EditarSesionDialogComponent {
  private dialogRef = inject(MatDialogRef<EditarSesionDialogComponent>);
  readonly data: EditarSesionDialogData = inject(MAT_DIALOG_DATA);
  private persistencia = inject(PersistenciaService);

  private readonly orig = this.data.merged;

  // ── Estado editable (signals) ─────────────────────────────────────────────
  docenteId   = signal(this.orig.docenteId ?? '');
  dia         = signal(this.orig.dia);
  horaInicio  = signal(this.orig.horaInicio);
  espacioId   = signal(this.orig.espacioId ?? '');
  alternancia = signal<'TipoA' | 'TipoB' | 'SinAlternancia'>(
    this.orig.alternancia as 'TipoA' | 'TipoB' | 'SinAlternancia'
  );
  semana      = signal<'A' | 'B' | undefined>(this.orig.semana);

  guardando     = signal(false);
  advertencias  = signal<string[]>([]);
  errorServidor = signal('');

  // ── Computed (read-only) ──────────────────────────────────────────────────
  asignatura = computed(() =>
    this.data.asignaturas.find(a => a.id === this.orig.asignaturaId)
  );

  contexto = computed(() => {
    const a = this.asignatura();
    if (!a) return '';
    const prog = this.data.programaById.get(a.programaId);
    if (!prog) return '';
    const fac = this.data.facultadById.get(prog.facultadId);
    return fac ? `${fac.nombre} · ${prog.nombre}` : prog.nombre;
  });

  /** Solo el track de laboratorio alterna — teoría (presencial o virtual) queda fija. */
  esLaboratorio = computed(() => this.data.sesion.tipoFlujo === 'Laboratorio');

  hayCambioDocente = computed(() => this.docenteId() !== (this.orig.docenteId ?? ''));

  hayCambios = computed(() =>
    this.hayCambioDocente() ||
    this.dia()         !== this.orig.dia ||
    this.horaInicio()  !== this.orig.horaInicio ||
    this.espacioId()   !== (this.orig.espacioId ?? '') ||
    this.alternancia() !== (this.orig.alternancia as string) ||
    this.semana()      !== this.orig.semana
  );

  // ── Validación en tiempo real ─────────────────────────────────────────────
  readonly horasDisponibles = [
    '06:00','07:00','08:00','09:00','10:00','11:00','12:00',
    '13:00','14:00','15:00','16:00','17:00','18:00','19:00','20:00'
  ];

  readonly diasOpciones = [
    { valor: 'lunes',     etiqueta: 'Lun' },
    { valor: 'martes',    etiqueta: 'Mar' },
    { valor: 'miercoles', etiqueta: 'Mié' },
    { valor: 'jueves',    etiqueta: 'Jue' },
    { valor: 'viernes',   etiqueta: 'Vie' },
    { valor: 'sabado',    etiqueta: 'Sáb' },
  ];

  validaciones = computed<Check[]>(() => {
    const dia        = this.dia();
    const inicio     = this.horaInicio();
    const espacioId  = this.espacioId();
    const docenteId  = this.docenteId();
    const sesionId   = this.data.sesion.id;
    const dur        = this.data.sesion.duracionHoras ?? 2;
    const chks: Check[] = [];

    if (!dia || !inicio) return chks;

    const startIdx = this.horasDisponibles.indexOf(inicio);
    const endIdx   = startIdx + Math.round(dur);

    // HC-S01: conflicto de espacio (presencial)
    if (espacioId && !this.data.sesion.virtual) {
      const conflicto = this.data.sesiones.find(s =>
        s.id !== sesionId &&
        s.espacioId === espacioId &&
        s.dia === dia &&
        !s.virtual &&
        this.overlaps(s, startIdx, endIdx)
      );
      const nombre = this.data.espacios.find(e => e.id === espacioId)?.nombre ?? espacioId;
      chks.push({
        ok: !conflicto,
        texto: conflicto
          ? `${nombre} ya está ocupado en esa franja`
          : `${nombre} está libre`
      });
    }

    // HC-I01 (en memoria, sin API): conflicto del docente seleccionado
    if (docenteId) {
      const conflicto = this.data.sesiones.find(s =>
        s.id !== sesionId &&
        s.docenteId === docenteId &&
        s.dia === dia &&
        this.overlaps(s, startIdx, endIdx)
      );
      const nombre = this.data.docentes.find(d => d.id === docenteId)?.nombre ?? 'El docente';
      chks.push({
        ok: !conflicto,
        texto: conflicto
          ? `${nombre} ya tiene otra sesión en esa franja`
          : `${nombre} está libre en esa franja`
      });
    }

    return chks;
  });

  conflictosDuros = computed(() => this.validaciones().some(c => !c.ok));

  puedeGuardar = computed(() =>
    this.hayCambios() && !this.conflictosDuros() && !this.guardando()
  );

  // ── Acciones ──────────────────────────────────────────────────────────────
  guardar() {
    if (!this.puedeGuardar()) return;
    this.guardando.set(true);
    this.errorServidor.set('');
    this.advertencias.set([]);

    if (this.hayCambioDocente()) {
      const docenteIdValue = this.docenteId() || null;
      this.persistencia.asignarDocente(this.data.sesion.id, docenteIdValue).subscribe({
        next: (resp) => {
          this.advertencias.set(resp.advertencias ?? []);
          this.commitLocal();
        },
        error: (err: any) => {
          this.guardando.set(false);
          const msg = err?.error?.error ?? err?.message ?? 'Error al asignar el docente.';
          this.errorServidor.set(err?.status === 409 ? `Solape (HC-I01): ${msg}` : msg);
        }
      });
    } else {
      this.commitLocal();
    }
  }

  private commitLocal() {
    const dur      = Math.round(this.data.sesion.duracionHoras ?? 2);
    const startIdx = this.horasDisponibles.indexOf(this.horaInicio());
    const endIdx   = startIdx + dur;
    const horaFin  = endIdx < this.horasDisponibles.length
      ? this.horasDisponibles[endIdx]
      : this.addH(this.horaInicio(), dur);

    const updated: Sesion = {
      ...this.data.sesion,
      docenteId:   this.docenteId() || undefined,
      dia:         this.dia(),
      horaInicio:  this.horaInicio(),
      horaFin,
      espacioId:   this.espacioId() || undefined,
      alternancia: this.alternancia(),
      semana:      this.semana(),
    };

    this.guardando.set(false);
    this.dialogRef.close({ sesion: updated, advertencias: this.advertencias() } satisfies EditarSesionResult);
  }

  cancelar() { this.dialogRef.close(); }

  // ── Helpers ───────────────────────────────────────────────────────────────
  private overlaps(s: Sesion, newStart: number, newEnd: number): boolean {
    const sStart = this.horasDisponibles.indexOf(s.horaInicio);
    if (sStart < 0) return false;
    const sDur = Math.max(1, Math.round(s.duracionHoras ?? this.diffH(s.horaInicio, s.horaFin)));
    return newStart < sStart + sDur && sStart < newEnd;
  }

  private addH(hora: string, h: number): string {
    const [hh, mm] = hora.split(':').map(Number);
    return `${String(hh + h).padStart(2, '0')}:${String(mm).padStart(2, '0')}`;
  }

  private diffH(i: string, f: string): number {
    const [hi, mi] = i.split(':').map(Number);
    const [hf, mf] = f.split(':').map(Number);
    return Math.max(1, (hf * 60 + mf - (hi * 60 + mi)) / 60);
  }
}

// ─── Diálogo: Crear sesión manual ────────────────────────────────────────────

interface DialogData {
  asignaturas: Asignatura[];
  docentes:    Docente[];
  espacios:    Espacio[];
  sesiones:    Sesion[];
  programaById: Map<string, { id: string; nombre: string }>;
}

interface Check { ok: boolean; texto: string; }

@Component({
  selector: 'app-crear-sesion-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule,
            MatProgressSpinnerModule, MatIconModule, MatSnackBarModule],
  template: `
<h2 mat-dialog-title class="dlg-title">
  <mat-icon>add_circle_outline</mat-icon> Nueva sesión manual
</h2>

<mat-dialog-content class="dlg-content">

  <!-- ── Paso 1: Asignatura ── -->
  <section class="form-section">
    <div class="section-label">
      <span class="step-num">1</span> ¿Qué asignatura?
    </div>
    <div class="field-group">
      <label class="field-label">Asignatura <span class="req">*</span></label>
      <select class="field-select"
              [(ngModel)]="asignaturaId"
              (ngModelChange)="onAsignaturaChange($event)">
        <option value="">— Seleccione una asignatura —</option>
        @for (grupo of gruposAsignatura; track grupo.programa) {
          <optgroup [label]="grupo.programa">
            @for (a of grupo.items; track a.id) {
              <option [value]="a.id">{{ a.nombre }}{{ a.codigo ? ' (' + a.codigo + ')' : '' }}</option>
            }
          </optgroup>
        }
      </select>

      @if (asignaturaSeleccionada()) {
        <div class="field-group" style="margin-top:8px">
          <label class="field-label">Tipo de sesión <span class="req">*</span></label>
          <div class="alt-pills">
            @for (t of tiposDisponibles(); track t.tipo) {
              <button type="button" class="alt-pill" [class.active]="tipoSesion() === t.tipo"
                      (click)="setTipoSesion(t.tipo)">{{ t.label }}</button>
            }
          </div>
        </div>

        <div class="asig-hint">
          <mat-icon class="hint-icon">person</mat-icon>
          <span>{{ nombreDocente(asignaturaSeleccionada()?.docenteId) }}</span>
          <span class="sep">·</span>
          <mat-icon class="hint-icon">timer</mat-icon>
          <span>{{ duracionSeleccionada() }}h por sesión
            <em>(fijo por plan de estudios, no editable)</em>
          </span>
        </div>
      }
    </div>
  </section>

  <!-- ── Paso 2: Cuándo y dónde ── -->
  <section class="form-section">
    <div class="section-label">
      <span class="step-num">2</span> ¿Cuándo y dónde?
    </div>

    <div class="day-picker">
      <span class="field-label">Día <span class="req">*</span></span>
      <div class="day-pills">
        @for (d of dias; track d.valor) {
          <button type="button" class="day-pill"
                  [class.active]="dia === d.valor"
                  (click)="dia = d.valor; recheck()">
            {{ d.etiqueta }}
          </button>
        }
      </div>
    </div>

    <div class="two-col">
      <div class="field-group">
        <label class="field-label">Hora de inicio <span class="req">*</span></label>
        <select class="field-select" [(ngModel)]="horaInicio" (ngModelChange)="recheck()">
          <option value="">— Seleccione —</option>
          @for (h of horasDisponibles; track h) {
            <option [value]="h">{{ h }}</option>
          }
        </select>
      </div>
      <div class="field-group">
        <label class="field-label">Espacio {{ tipoSesion() === 'TeoriaVirtual' ? '' : '(requerido)' }}</label>
        @if (tipoSesion() === 'TeoriaVirtual') {
          <div class="lock-hint">
            <mat-icon class="hint-icon lock">videocam</mat-icon>
            Sincrónica online — sin espacio físico
          </div>
        } @else {
          <select class="field-select" [(ngModel)]="espacioId" (ngModelChange)="recheck()"
                  [disabled]="espacioFijoBloqueado()">
            <option value="">— Seleccione —</option>
            @for (e of espaciosDisponibles(); track e.id) {
              <option [value]="e.id">{{ e.nombre }}</option>
            }
          </select>
          @if (espacioFijoBloqueado()) {
            <div class="lock-hint">
              <mat-icon class="hint-icon lock">lock</mat-icon>
              Fijo por currículum — no se puede cambiar
            </div>
          }
        }
      </div>
    </div>
  </section>

  <!-- ── Paso 3: Alternancia ── -->
  <!-- Solo el track de Laboratorio alterna (decisión de diseño); teoría queda fija en
       Sin alternancia — ver ModalidadSemanal.Derivar en el backend. -->
  @if (tipoSesion() === 'Laboratorio') {
    <section class="form-section">
      <div class="section-label">
        <span class="step-num">3</span> ¿Cómo alterna?
      </div>
      <div class="alt-pills">
        <button type="button" class="alt-pill" [class.active]="alternancia === 'TipoA'"
                (click)="alternancia = 'TipoA'; recheck()">
          <strong>Tipo A</strong><br>
          <small>Presencial S.A · Virtual S.B</small>
        </button>
        <button type="button" class="alt-pill" [class.active]="alternancia === 'TipoB'"
                (click)="alternancia = 'TipoB'; recheck()">
          <strong>Tipo B</strong><br>
          <small>Virtual S.A · Presencial S.B</small>
        </button>
        <button type="button" class="alt-pill" [class.active]="alternancia === 'SinAlternancia'"
                (click)="alternancia = 'SinAlternancia'; recheck()">
          <strong>Sin alternancia</strong><br>
          <small>Presencial todas las semanas</small>
        </button>
      </div>
    </section>
  }

  <!-- ── Panel de verificación ── -->
  @if (asignaturaId && dia && horaInicio && (espacioId || tipoSesion() === 'TeoriaVirtual')) {
    <section class="checks-panel">
      <div class="checks-title">Verificación de restricciones</div>
      @for (c of checks(); track c.texto) {
        <div class="check-row" [class.ok]="c.ok" [class.fail]="!c.ok">
          <mat-icon>{{ c.ok ? 'check_circle' : 'cancel' }}</mat-icon>
          <span>{{ c.texto }}</span>
        </div>
      }
      @if (resumen()) {
        <div class="preview-hint">{{ resumen() }}</div>
      }
    </section>
  }

  @if (guardando()) {
    <div class="saving-row"><mat-spinner diameter="20"></mat-spinner><span>Guardando en la base de datos…</span></div>
  }
  @if (errorServidor()) {
    <div class="server-error">⚠ {{ errorServidor() }}</div>
  }

</mat-dialog-content>

<mat-dialog-actions align="end" class="dlg-actions">
  <button mat-stroked-button (click)="cancelar()" [disabled]="guardando()">Cancelar</button>
  <button mat-flat-button color="primary"
          [disabled]="!puedeCrear() || guardando()"
          (click)="crear()">
    <mat-icon>add</mat-icon>
    {{ guardando() ? 'Creando…' : 'Crear sesión' }}
  </button>
</mat-dialog-actions>
  `,
  styles: [`
    .dlg-title { display: flex; align-items: center; gap: 8px; font-size: 18px; }
    .dlg-content { display: flex; flex-direction: column; gap: 20px; padding: 16px 0; min-width: 0; }
    .dlg-actions { padding: 12px 0 0; gap: 10px; }

    .form-section { background: #fafafa; border: 1px solid #f0f0f0; border-radius: 8px; padding: 16px; display: flex; flex-direction: column; gap: 12px; }
    .section-label { display: flex; align-items: center; gap: 8px; font-weight: 600; font-size: 13px; color: #424242; text-transform: uppercase; letter-spacing: 0.05em; }
    .step-num { width: 22px; height: 22px; background: #1976d2; color: white; border-radius: 50%; display: flex; align-items: center; justify-content: center; font-size: 11px; font-weight: 700; flex-shrink: 0; }

    .field-group { display: flex; flex-direction: column; gap: 4px; }
    .field-label { font-size: 13px; color: #616161; font-weight: 500; }
    .req { color: #c62828; }
    .field-select { padding: 9px 10px; border: 1px solid #d0d0d0; border-radius: 6px; font-size: 14px; background: white; width: 100%; cursor: pointer; outline: none; }
    .field-select:focus { border-color: #1976d2; box-shadow: 0 0 0 2px rgba(25,118,210,.2); }
    .field-select:disabled { background: #f5f5f5; cursor: not-allowed; color: #9e9e9e; }

    .asig-hint { display: flex; align-items: center; gap: 6px; font-size: 12px; color: #616161; background: #e3f2fd; padding: 7px 10px; border-radius: 5px; flex-wrap: wrap; }
    .hint-icon { font-size: 14px; width: 14px; height: 14px; }
    .sep { color: #bdbdbd; }
    em { color: #9e9e9e; }

    .day-picker { display: flex; flex-direction: column; gap: 6px; }
    .day-pills { display: flex; gap: 6px; flex-wrap: wrap; }
    .day-pill { padding: 7px 14px; border: 1px solid #e0e0e0; border-radius: 20px; background: white; cursor: pointer; font-size: 13px; font-weight: 500; transition: 0.15s; }
    .day-pill:hover { background: #e3f2fd; border-color: #90caf9; }
    .day-pill.active { background: #1976d2; color: white; border-color: #1976d2; }

    .two-col { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
    .lock-hint { display: flex; align-items: center; gap: 4px; font-size: 11px; color: #9e9e9e; }
    .lock { color: #bdbdbd !important; }

    .alt-pills { display: flex; gap: 8px; flex-wrap: wrap; }
    .alt-pill { flex: 1; min-width: 130px; padding: 10px 12px; border: 1px solid #e0e0e0; border-radius: 8px; background: white; cursor: pointer; text-align: center; line-height: 1.4; font-size: 13px; transition: 0.15s; }
    .alt-pill:hover { background: #f5f5f5; }
    .alt-pill.active { background: #e3f2fd; border-color: #1976d2; color: #1565c0; }

    .checks-panel { border: 1px solid #e0e0e0; border-radius: 8px; overflow: hidden; }
    .checks-title { padding: 10px 14px; background: #f5f5f5; font-size: 12px; font-weight: 600; color: #616161; text-transform: uppercase; letter-spacing: 0.05em; }
    .check-row { display: flex; align-items: center; gap: 8px; padding: 8px 14px; font-size: 13px; border-top: 1px solid #f5f5f5; }
    .check-row.ok mat-icon  { color: #2e7d32; }
    .check-row.fail mat-icon { color: #c62828; }
    .check-row.fail { background: #fff8f8; }
    .preview-hint { padding: 8px 14px; background: #e8f5e9; font-size: 12px; color: #1b5e20; border-top: 1px solid #c8e6c9; }

    .saving-row { display: flex; align-items: center; gap: 10px; color: #1565c0; font-size: 13px; }
    .server-error { background: #ffebee; color: #c62828; padding: 10px 14px; border-radius: 6px; font-size: 13px; }
  `]
})
export class CrearSesionDialogComponent {
  private dialogRef = inject(MatDialogRef<CrearSesionDialogComponent>);
  private data: DialogData = inject(MAT_DIALOG_DATA);
  private persistencia = inject(PersistenciaService);

  // ── Estado del formulario ─────────────────────────────────────────────────
  asignaturaId  = '';
  dia           = '';
  horaInicio    = '';
  espacioId     = '';
  alternancia: 'TipoA' | 'TipoB' | 'SinAlternancia' = 'SinAlternancia';

  guardando     = signal(false);
  errorServidor = signal('');

  readonly dias = [
    { valor: 'lunes',     etiqueta: 'Lun' },
    { valor: 'martes',    etiqueta: 'Mar' },
    { valor: 'miercoles', etiqueta: 'Mié' },
    { valor: 'jueves',    etiqueta: 'Jue' },
    { valor: 'viernes',   etiqueta: 'Vie' },
    { valor: 'sabado',    etiqueta: 'Sáb' },
  ];

  readonly horasDisponibles = [
    '06:00','07:00','08:00','09:00','10:00','11:00','12:00',
    '13:00','14:00','15:00','16:00','17:00','18:00','19:00','20:00'
  ];

  // ── Computed helpers ──────────────────────────────────────────────────────
  asignaturaSeleccionada = signal<Asignatura | undefined>(undefined);
  /** Cuál de los 3 tracks combinables representa esta sesión manual. */
  tipoSesion              = signal<TipoSesionUi>('Laboratorio');
  espacioFijoBloqueado   = signal(false);
  checks                 = signal<Check[]>([]);
  checksOk               = signal(false);

  // Grupos de asignaturas por programa para el <optgroup>
  readonly gruposAsignatura = this.buildGrupos();

  /** Solo los tracks que la asignatura realmente tiene configurados (conteo > 0) — evitar
   *  ofrecer un tipo con 0 sesiones/0 horas, que no corresponde a nada real. */
  tiposDisponibles = computed<{ tipo: TipoSesionUi; label: string }[]>(() => {
    const a = this.asignaturaSeleccionada();
    if (!a) return [];
    const list: { tipo: TipoSesionUi; label: string }[] = [];
    if (a.sesionesTeoriaPresencialSemana > 0) list.push({ tipo: 'TeoriaPresencial', label: 'Teoría presencial' });
    if (a.sesionesTeoriaVirtualSemana > 0) list.push({ tipo: 'TeoriaVirtual', label: 'Teoría virtual' });
    if (a.sesionesLaboratorioSemana > 0) list.push({ tipo: 'Laboratorio', label: 'Laboratorio' });
    return list;
  });

  /** Duración fija del track seleccionado (CLAUDE.md regla 6: input fijo, no editable). */
  duracionSeleccionada = computed(() => {
    const a = this.asignaturaSeleccionada();
    if (!a) return 2;
    switch (this.tipoSesion()) {
      case 'TeoriaVirtual': return a.horasTeoriaVirtual;
      case 'Laboratorio':   return a.horasLaboratorio;
      default:              return a.horasTeoriaPresencial;
    }
  });

  // Teoría virtual no usa espacio (regla 9); Laboratorio exige tipo Laboratorio;
  // teoría presencial exige un espacio no-laboratorio (salvo espacio fijo del currículum).
  espaciosDisponibles = computed(() => {
    const a = this.asignaturaSeleccionada();
    const tipo = this.tipoSesion();
    if (tipo === 'TeoriaVirtual') return [];
    if (!a) return this.data.espacios;
    if (a.espacioFijoId) {
      return this.data.espacios.filter(e => e.id === a.espacioFijoId);
    }
    return tipo === 'Laboratorio'
      ? this.data.espacios.filter(e => e.tipo === 'Laboratorio')
      : this.data.espacios.filter(e => e.tipo !== 'Laboratorio');
  });

  resumen = signal('');

  puedeCrear = computed(() =>
    !!this.asignaturaId &&
    !!this.dia &&
    !!this.horaInicio &&
    (!!this.espacioId || this.tipoSesion() === 'TeoriaVirtual') &&
    this.checksOk() &&
    !this.guardando()
  );

  // ── Ciclo de vida ─────────────────────────────────────────────────────────
  onAsignaturaChange(id: string) {
    const a = this.data.asignaturas.find(x => x.id === id);
    this.asignaturaSeleccionada.set(a);

    this.tipoSesion.set(
      a && a.sesionesTeoriaPresencialSemana > 0 ? 'TeoriaPresencial' :
      a && a.sesionesLaboratorioSemana > 0      ? 'Laboratorio' :
      a && a.sesionesTeoriaVirtualSemana > 0    ? 'TeoriaVirtual' :
      'TeoriaPresencial'
    );
    this.aplicarReglasTipo();
    this.recheck();
  }

  setTipoSesion(tipo: TipoSesionUi) {
    this.tipoSesion.set(tipo);
    this.aplicarReglasTipo();
    this.recheck();
  }

  /** Decisión de diseño: solo Laboratorio alterna; teoría (presencial o virtual) nunca tiene espacio propio si es virtual. */
  private aplicarReglasTipo() {
    const a = this.asignaturaSeleccionada();
    const tipo = this.tipoSesion();

    if (tipo === 'Laboratorio' && a?.alternancia && a.alternancia !== 'SinAlternancia') {
      this.alternancia = a.alternancia as 'TipoA' | 'TipoB';
    } else {
      this.alternancia = 'SinAlternancia';
    }

    if (tipo === 'TeoriaVirtual') {
      this.espacioId = '';
      this.espacioFijoBloqueado.set(false);
    } else if (a?.espacioFijoId) {
      this.espacioId = a.espacioFijoId;
      this.espacioFijoBloqueado.set(true);
    } else {
      this.espacioFijoBloqueado.set(false);
      this.espacioId = '';
    }
  }

  recheck() {
    const a   = this.asignaturaSeleccionada();
    const dur = this.duracionSeleccionada();
    const esVirtual = this.tipoSesion() === 'TeoriaVirtual';
    const chks: Check[] = [];
    let ok = true;

    if (!a || !this.dia || !this.horaInicio || (!this.espacioId && !esVirtual)) {
      this.checks.set([]);
      this.checksOk.set(false);
      this.resumen.set('');
      return;
    }

    const startIdx  = this.horasDisponibles.indexOf(this.horaInicio);
    const endIdx    = startIdx + dur;

    const franjasFin = endIdx < this.horasDisponibles.length
      ? this.horasDisponibles[endIdx]
      : this.addH(this.horaInicio, dur);

    // ── HC-I02: docente disponible ese día ──────────────────────────────────
    const docente = this.data.docentes.find(d => d.id === a.docenteId);
    let docenteDisp = true;
    if (docente?.disponibilidad) {
      const diaDisp = docente.disponibilidad[this.dia];
      if (diaDisp?.noDisponible) {
        docenteDisp = false;
      } else if (diaDisp?.tipo === 'Franja específica') {
        const toM = (t: string) => { const [h, m] = t.split(':').map(Number); return h * 60 + m; };
        const sM = toM(this.horaInicio), eM = toM(franjasFin);
        const dM = toM(diaDisp.desde ?? '00:00'), hM = toM(diaDisp.hasta ?? '23:59');
        if (sM < dM || eM > hM) docenteDisp = false;
      }
    }
    if (!docenteDisp) ok = false;
    chks.push({
      ok: docenteDisp,
      texto: docenteDisp
        ? `${this.nombreDocente(a.docenteId)} tiene disponibilidad ese día y hora`
        : `${this.nombreDocente(a.docenteId)} NO tiene disponibilidad en ese horario`
    });

    // ── HC-I01: docente sin conflictos ─────────────────────────────────────
    const conflictoDocente = this.data.sesiones.find(s =>
      s.docenteId === a.docenteId &&
      s.dia === this.dia &&
      this.overlaps(s, startIdx, endIdx)
    );
    if (conflictoDocente) ok = false;
    chks.push({
      ok: !conflictoDocente,
      texto: conflictoDocente
        ? `El docente ya tiene otra sesión: ${this.nomAsig(conflictoDocente.asignaturaId)} (${conflictoDocente.horaInicio}–${conflictoDocente.horaFin})`
        : 'El docente está libre en esa franja horaria'
    });

    // ── HC-S01 + HC-S05: espacio (solo aplica a sesiones presenciales) ─────
    if (!esVirtual) {
      const presencialSemana = (s: Sesion): boolean => {
        if (s.virtual) return false;
        if (!s.semana) return s.alternancia === 'SinAlternancia';
        // Para la semana presencial de la nueva sesión
        const semPres = this.alternancia === 'TipoA' ? 'A'
                      : this.alternancia === 'TipoB' ? 'B'
                      : null; // SinAlternancia: ambas
        return semPres === null || s.semana === semPres;
      };

      const conflictoEspacio = this.data.sesiones.find(s =>
        s.espacioId === this.espacioId &&
        s.dia === this.dia &&
        presencialSemana(s) &&
        this.overlaps(s, startIdx, endIdx)
      );
      if (conflictoEspacio) ok = false;
      const espNombre = this.data.espacios.find(e => e.id === this.espacioId)?.nombre ?? this.espacioId;
      chks.push({
        ok: !conflictoEspacio,
        texto: conflictoEspacio
          ? `${espNombre} ya está ocupado por ${this.nomAsig(conflictoEspacio.asignaturaId)} (${conflictoEspacio.horaInicio}–${conflictoEspacio.horaFin})`
          : `${espNombre} está libre en esa franja`
      });

      if (a.espacioFijoId && this.espacioId && this.espacioId !== a.espacioFijoId) {
        ok = false;
        const nomFijo = this.data.espacios.find(e => e.id === a.espacioFijoId)?.nombre ?? 'lab asignado';
        chks.push({ ok: false, texto: `Esta asignatura tiene laboratorio fijo: ${nomFijo}` });
      }
    }

    this.checks.set(chks);
    this.checksOk.set(ok);

    if (ok) {
      const filas = this.alternancia === 'SinAlternancia' ? 2 : 2;
      const presDesc = this.alternancia === 'TipoA' ? 'presencial S.A + virtual S.B'
                     : this.alternancia === 'TipoB' ? 'virtual S.A + presencial S.B'
                     : 'presencial en ambas semanas';
      this.resumen.set(`✓ Todo correcto — se agregarán ${filas} filas al horario (${presDesc}).`);
    } else {
      this.resumen.set('');
    }
  }

  // ── Crear ─────────────────────────────────────────────────────────────────
  crear() {
    if (!this.puedeCrear()) return;
    this.guardando.set(true);
    this.errorServidor.set('');

    const a = this.asignaturaSeleccionada()!;
    const tipo = this.tipoSesion();
    const payload = {
      asignaturaId:  a.id,
      docenteId:     a.docenteId ?? '',
      espacioId:     tipo === 'TeoriaVirtual' ? null : (this.espacioId || null),
      dia:           this.dia,
      horaInicio:    this.horaInicio,
      duracionHoras: this.duracionSeleccionada(),
      alternancia:   this.alternancia,
      tipoFlujo:     tipoFlujoDesde(tipo),
      esVirtual:     esVirtualDesde(tipo)
    };

    this.persistencia.crearSesionManual(payload).subscribe({
      next: (sesiones: Sesion[]) => {
        this.guardando.set(false);
        this.dialogRef.close(sesiones);
      },
      error: (err: any) => {
        this.guardando.set(false);
        const msg = err?.error?.error ?? err?.message ?? 'Error al guardar la sesión.';
        this.errorServidor.set(msg);
      }
    });
  }

  cancelar() { this.dialogRef.close(); }

  // ── Helpers internos ──────────────────────────────────────────────────────
  private overlaps(s: Sesion, newStart: number, newEnd: number): boolean {
    const sStart = this.horasDisponibles.indexOf(s.horaInicio);
    if (sStart < 0) return false;
    const sDur = Math.max(1, Math.round(s.duracionHoras ?? this.diffH(s.horaInicio, s.horaFin)));
    return newStart < sStart + sDur && sStart < newEnd;
  }

  nombreDocente(id?: string): string {
    return this.data.docentes.find(d => d.id === id)?.nombre ?? '—';
  }
  private nomAsig(id: string): string {
    return this.data.asignaturas.find(a => a.id === id)?.nombre ?? 'desconocida';
  }
  private addH(hora: string, h: number): string {
    const [hh, mm] = hora.split(':').map(Number);
    return `${String(hh + h).padStart(2, '0')}:${String(mm).padStart(2, '0')}`;
  }
  private diffH(i: string, f: string): number {
    const [hi, mi] = i.split(':').map(Number);
    const [hf, mf] = f.split(':').map(Number);
    return Math.max(1, (hf * 60 + mf - (hi * 60 + mi)) / 60);
  }

  private buildGrupos(): { programa: string; items: Asignatura[] }[] {
    const map = new Map<string, { programa: string; items: Asignatura[] }>();
    for (const a of this.data.asignaturas) {
      const prog = this.data.programaById.get(a.programaId)?.nombre ?? 'Sin programa';
      if (!map.has(prog)) map.set(prog, { programa: prog, items: [] });
      map.get(prog)!.items.push(a);
    }
    return [...map.values()].sort((a, b) => a.programa.localeCompare(b.programa));
  }
}

// ─── Diálogo de progreso ─────────────────────────────────────────────────────

@Component({
  selector: 'app-progress-dialog',
  standalone: true,
  imports: [MatProgressSpinnerModule, MatDialogModule],
  template: `
    <h2 mat-dialog-title>Optimizando Horario</h2>
    <mat-dialog-content class="prog-dialog">
      <div class="phase-item" [class.active]="phase() === 1" [class.done]="phase() > 1">
        @if (phase() === 1) { <mat-spinner diameter="20"></mat-spinner> }
        @if (phase() > 1)  { <span class="icon-done">✓</span> }
        <span>Fase 1: Pre-procesamiento (coloración de grafos)</span>
      </div>
      <div class="phase-item" [class.active]="phase() === 2" [class.done]="phase() > 2" [class.pending]="phase() < 2">
        @if (phase() === 2) { <mat-spinner diameter="20"></mat-spinner> }
        @if (phase() > 2)  { <span class="icon-done">✓</span> }
        @if (phase() < 2)  { <span class="icon-pending">○</span> }
        <span>Fase 2: Viabilidad (constraint programming)</span>
      </div>
      <div class="phase-item" [class.active]="phase() === 3" [class.done]="phase() > 3" [class.pending]="phase() < 3">
        @if (phase() === 3) { <mat-spinner diameter="20"></mat-spinner> }
        @if (phase() > 3)  { <span class="icon-done">✓</span> }
        @if (phase() < 3)  { <span class="icon-pending">○</span> }
        <span>Fase 3: Optimización (algoritmo genético)</span>
      </div>
    </mat-dialog-content>
  `,
  styles: [`
    .prog-dialog { display: flex; flex-direction: column; gap: 20px; padding: 16px 0; min-width: 340px; }
    .phase-item { display: flex; align-items: center; gap: 12px; color: #757575; }
    .phase-item.active { color: #1976d2; font-weight: 500; }
    .phase-item.done { color: #388e3c; }
    .icon-done, .icon-pending { font-weight: bold; width: 20px; text-align: center; }
  `]
})
export class ProgressDialogComponent implements OnInit, OnDestroy {
  phase = signal(1);
  private timers: ReturnType<typeof setTimeout>[] = [];

  ngOnInit() {
    this.timers.push(setTimeout(() => { if (this.phase() === 1) this.phase.set(2); }, 2000));
    this.timers.push(setTimeout(() => { if (this.phase() === 2) this.phase.set(3); }, 10000));
  }

  ngOnDestroy() {
    this.timers.forEach(t => clearTimeout(t));
  }
}
