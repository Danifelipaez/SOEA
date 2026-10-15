import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatMenuModule } from '@angular/material/menu';
import { RouterModule } from '@angular/router';
import { StateService } from '../../core/state.service';
import { HorarioApiService } from '../../core/horario-api.service';
import { PersistenciaService } from '../../core/persistencia.service';
import { CatalogoService } from '../../core/catalogo.service';
import { Espacio, Sesion } from '../../core/models';
import { HORA_APERTURA, HORA_CIERRE, HORA_CIERRE_SABADO } from '../../core/jornada';
import { mensajeErrorHttp, limpiarEtiquetaInterna } from '../../core/http-error.util';
import { SearchableSelectComponent, SearchableOption } from '../../shared/searchable-select/searchable-select.component';
import { ConfirmDeleteDialogComponent } from '../../shared/confirm-delete-dialog/confirm-delete-dialog.component';
import { porNombre } from '../../core/orden';
import { from, of } from 'rxjs';
import { catchError, concatMap, map, toArray } from 'rxjs/operators';
import { MergedSesion, SesionFuera, DiaGrilla, GrillaSemanal, ItemGrilla, describirSesionConflicto, hhmm, HORAS_GRILLA, DIAS_GRILLA, fueraDisponibilidadGrupo, avisosDocentePorSesion, choquesEspacioPorSesion, filasDeChoques, ubicarEnGrilla, distribuirEnCarriles, mensajeInfactibilidadAmigable } from './horario.reglas';
import { EditarSesionDialogComponent, EditarSesionDialogData, EditarSesionResult } from './editar-sesion-dialog.component';
import { CrearSesionDialogComponent, CrearSesionDialogData } from './crear-sesion-dialog.component';
import { ProgressDialogComponent } from './progress-dialog.component';

@Component({
  selector: 'app-horario',
  standalone: true,
  imports: [CommonModule, DatePipe, FormsModule, MatDialogModule, MatSnackBarModule, RouterModule, MatMenuModule, SearchableSelectComponent],
  template: `
    <div class="hz-head">
      <div class="hz-head-l"><span class="soea-tag">Paso 2</span><h1 class="hz-title">Horario semanal</h1></div>
      <div class="hz-head-r">
        <span class="text-muted adv-menu" [matMenuTriggerFor]="avanzadoMenu" title="Más opciones">
          <span class="tag tag-outline" style="font-size:9px">Más opciones</span> ⭳ ⭱ ⋯
        </span>
        <mat-menu #avanzadoMenu="matMenu">
          <button mat-menu-item (click)="exportarHorario()" [disabled]="state.sesiones().length === 0">⭳ Descargar copia del horario</button>
          <button mat-menu-item (click)="importarInput.click()">⭱ Abrir copia guardada</button>
        </mat-menu>
        <input #importarInput type="file" accept=".json" style="display:none" (change)="importarHorario($event)">
      </div>
    </div>

    <div class="blueprint elev-md frame">
      <i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>

      <!-- toolbar -->
      <div class="toolbar">
        @if (hayAlternancia()) {
          <span class="tb-lbl" id="lbl-semana">Semana</span>
          <div class="seg" role="radiogroup" aria-labelledby="lbl-semana">
            <label class="seg-opt" role="radio" [attr.aria-checked]="semanaVista() === 'A'" [class.on]="semanaVista() === 'A'" (click)="selectWeek('A')">Semana A · horario completo</label>
            <label class="seg-opt" role="radio" [attr.aria-checked]="semanaVista() === 'B'" [class.on]="semanaVista() === 'B'" (click)="selectWeek('B')">Semana B · lo que alterna (el resto en gris)</label>
          </div>
        }
        <div class="legend">
          <span><span class="lg-box pres"></span> Presencial</span>
          <span><span class="lg-box virt"></span> ⌁ Virtual</span>
        </div>
        @if (!enBorrador()) {
          <div class="tb-right">
            <button class="btn btn-secondary" (click)="abrirSesionFija()">＋ Fijar una clase</button>
            @if (state.sesiones().length > 0) {
              <button class="btn btn-secondary" (click)="abrirCrearSesion()" [disabled]="loadingBackend()">＋ Sesión manual</button>
              <button class="btn btn-secondary" (click)="guardarComoBase()">Guardar como punto de partida</button>
              <button class="btn btn-secondary" (click)="entrarBorrador()" title="Pruebe agregar clases sin tocar el horario guardado">✎ Modo borrador</button>
              <button class="btn btn-secondary btn-peligro" (click)="limpiarHorario()" [disabled]="loadingBackend() || generandoHorario()">Limpiar horario</button>
            }
            <button class="btn btn-primary" (click)="generarHorario()" [disabled]="loadingBackend() || generandoHorario()">▶ Generar horario</button>
          </div>
        }
      </div>

      <!-- Modo borrador: copia en memoria del horario a la que se agregan clases para probar. -->
      @if (enBorrador()) {
        <div class="borrador-bar" role="region" aria-label="Modo borrador">
          <div><b>✎ Modo borrador</b> — pruebe agregar clases sin tocar el horario guardado. {{ clasesBorrador().length }} clase(s) agregada(s); clic en una para quitarla.</div>
          <div class="borrador-acc">
            <button class="btn btn-secondary" (click)="agregarAlBorrador()">＋ Agregar clase</button>
            <button class="btn btn-primary" (click)="convertirBorrador()" [disabled]="clasesBorrador().length === 0 || convirtiendo()">{{ convirtiendo() ? 'Guardando…' : 'Convertir en horario real' }}</button>
            <button class="btn btn-secondary" (click)="salirBorrador()" [disabled]="convirtiendo()">Salir del borrador</button>
          </div>
          @if (erroresBorrador().length > 0) {
            <div class="errb">No se pudieron agregar al horario real (siguen en el borrador):
              <ul>@for (e of erroresBorrador(); track $index) { <li>{{ e }}</li> }</ul>
            </div>
          }
        </div>
      }

      <!-- Filtros: limitan lo que se ve en la cuadrícula (no cambian el horario). -->
      @if (sesiones().length > 0) {
        <div class="filtros" role="search" aria-label="Filtrar el horario">
          <span class="tb-lbl">Filtrar</span>
          <select class="input fsel" aria-label="Facultad" [value]="fFacultad()" (change)="setFiltro('facultad', $any($event.target).value)">
            <option value="">Todas las facultades</option>
            @for (f of opcFacultades(); track f.id) { <option [value]="f.id">{{ f.nombre }}</option> }
          </select>
          <select class="input fsel" aria-label="Programa" [value]="fPrograma()" (change)="setFiltro('programa', $any($event.target).value)">
            <option value="">Todos los programas</option>
            @for (p of opcProgramas(); track p.id) { <option [value]="p.id">{{ p.nombre }}</option> }
          </select>
          <app-searchable-select class="fss" aria-label="Grupo" [ngModel]="fGrupo()" (ngModelChange)="setFiltro('grupo', $event)"
                                 [options]="opcGrupos()" placeholder="Todos los grupos"></app-searchable-select>
          <app-searchable-select class="fss" aria-label="Docente" [ngModel]="fDocente()" (ngModelChange)="setFiltro('docente', $event)"
                                 [options]="opcDocentes()" placeholder="Todos los docentes"></app-searchable-select>
          @if (hayFiltros()) { <button type="button" class="btn-link" (click)="limpiarFiltros()">Quitar filtros</button> }
        </div>
      }

      <!-- Estado C (HF-3): sin solución. El registro técnico del backend (Fase 1/2/3, HC-…, ms)
           nunca se muestra en pantalla — solo se puede copiar para adjuntar a un caso de soporte. -->
      @if (state.sesiones().length === 0 && state.executionLogs().length > 0) {
        <div class="fail">
          <div class="errb"><b>✕ Horario no generado.</b> {{ mensajeInfactible() }}</div>
          <button type="button" class="btn-link" (click)="copiarDetalleSoporte()">Copiar detalle para soporte</button>
        </div>
      }

      @if (state.espacios().length > 0) {
        <div class="grid-area">
          <div class="grid-main">
            <!-- selector de espacio -->
            <div class="space-sel">
              <button class="chip space-chip" [class.on]="todosEspacios()" (click)="selectTodos()">Todos los espacios</button>
              @for (esp of state.espacios(); track esp.id) {
                <button class="chip space-chip" [class.on]="!todosEspacios() && !modoVirtual() && activeSpace()?.id === esp.id" (click)="selectSpace(esp)">{{ esp.nombre }}</button>
              }
              <button class="chip space-chip" [class.on]="!todosEspacios() && modoVirtual()" (click)="selectVirtual()">⌁ Virtual (sin espacio)</button>
            </div>

            @if (!backendReady()) {
              <div class="soft backend-alert">No hay conexión con el sistema. <button class="btn btn-secondary" style="font-size:12px;padding:3px 10px" (click)="syncFromBackend()" [disabled]="loadingBackend()">Reintentar</button></div>
            }

            @if (sesiones().length === 0) {
              <p class="vacio">Todavía no hay horario. Pulse «Generar horario» o abra una copia guardada desde «Más opciones».</p>
            } @else if (grilla().total === 0) {
              <p class="vacio">
                @if (hayFiltros()) {
                  No hay clases con estos filtros{{ todosEspacios() ? '' : ' en esta vista' }}.
                } @else if (todosEspacios()) {
                  No hay sesiones{{ hayAlternancia() ? ' en la Semana ' + semanaVista() : '' }}.
                } @else if (modoVirtual()) {
                  No hay sesiones virtuales{{ hayAlternancia() ? ' en la Semana ' + semanaVista() : '' }}.
                } @else {
                  {{ activeSpace()?.nombre }} no tiene sesiones{{ hayAlternancia() ? ' en la Semana ' + semanaVista() : '' }}.
                }
              </p>
            }

            <div class="wk-scroll">
              <div class="wk">
                @for (d of grilla().dias; track d.valor; let i = $index) {
                  <div class="wk-dhdr" [style.grid-column]="i + 2">{{ d.corto }}</div>
                }
                <div class="wk-horas" [style.grid-template-rows]="filasCss" aria-hidden="true">
                  @for (h of horas; track h) { <span>{{ h.slice(0,2) }}</span> }
                </div>
                @for (d of grilla().dias; track d.valor; let i = $index) {
                  <div class="wk-dia" role="group" [attr.aria-label]="d.etiqueta" [style.grid-column]="i + 2" [style.grid-template-rows]="filasCss">
                    @if (d.filaCierre) {
                      <div class="wk-cerrado" [style.grid-row]="d.filaCierre + ' / -1'" aria-hidden="true">Cerrado</div>
                    }
                    @for (t of d.tarjetas; track t.key) {
                      <button type="button" class="gcell" [class.gvirt]="t.m.virtual" [class.gsiempre]="t.siempre" [class.g1h]="t.g1h" [class.gnueva]="t.m.sesiones[0].deBorrador"
                              [style.grid-row]="t.fila" [style.grid-column]="t.col"
                              [style.background]="t.siempre ? null : gcellBg(t.m)" [style.border-left-color]="altColor(t.m)"
                              [title]="t.texto" [attr.aria-label]="t.texto" (click)="abrirEditarSesion(t.m)">
                        <span class="s">@if (tieneChoque(t.m)) { <span class="nodoc">⚠</span> }@if (t.m.virtual) { ⌁ }{{ getAsignaturaName(t.m) }}</span>
                        <span class="g">{{ t.siempre ? 'todas las semanas' : (grupoSuffix(t.m).slice(3) || '—') }}</span>
                        <span class="det">
                          <span [class.nodoc]="!t.m.docenteId">{{ t.m.docenteId ? getDocenteName(t.m) : 'sin docente' }}</span>
                          <span class="t">{{ celdaEspacio(t.m) }} · {{ t.m.horaInicio }}–{{ t.m.horaFin }}</span>
                          @if (t.m.contraparte; as otra) { <span class="gsub">⌁ {{ getAsignaturaName(otra) }}{{ grupoSuffix(otra) }} · virtual</span> }
                        </span>
                      </button>
                    }
                    @for (x of d.mas; track x.fila) {
                      <button type="button" class="gmas" [style.grid-row]="x.fila" style="grid-column: 5 / span 2"
                              [matMenuTriggerFor]="masMenu" [matMenuTriggerData]="{ lista: x.items }" [attr.aria-label]="x.texto">+{{ x.items.length }}</button>
                    }
                  </div>
                }
              </div>
            </div>
            <div class="grid-foot text-muted">⋯ jornada {{ horaAperturaTexto }} – {{ horaCierreTexto }} (Sáb: {{ horaAperturaTexto }} – {{ horaCierreSabadoTexto }}) ⋯</div>

            <!-- Avisos debajo de la matriz, colapsables (cerrados por defecto: el resumen ya dice cuántos hay). -->
            <div class="avisos">
              @if (grilla().fuera.length > 0) {
                <details class="soft fuera">
                  <summary><b>{{ grilla().fuera.length }} sesión(es) que no se pueden ubicar en la cuadrícula</b></summary>
                  <ul>
                    @for (f of grilla().fuera; track f.m.key) {
                      <li>
                        <button type="button" class="btn-link" (click)="abrirEditarSesion(f.m)">{{ getAsignaturaName(f.m) }}{{ grupoSuffix(f.m) }}</button>
                        — {{ f.motivo }}
                      </li>
                    }
                  </ul>
                </details>
              }

              @if (choquesEspacio().length > 0) {
                <details class="soft fuera" [open]="enBorrador()">
                  <summary><b>⚠ {{ choquesEspacio().length }} clase(s) en un espacio ya ocupado a esa hora</b></summary>
                  <ul>
                    @for (c of choquesEspacio(); track c.s.id) {
                      <li><button type="button" class="btn-link" (click)="abrirSesion(c.s)">{{ c.etiqueta }}</button> — {{ c.detalle }}</li>
                    }
                  </ul>
                </details>
              }

              @if (fueraGrupo().length > 0) {
                <details class="soft fuera">
                  <summary><b>⚠ {{ fueraGrupo().length }} clase(s) fuera del horario disponible de su grupo</b></summary>
                  <div>Vuelva a generar el horario, o ajuste la disponibilidad del grupo en Catálogo si cambió.</div>
                  <ul>
                    @for (f of fueraGrupo(); track f.s.id) {
                      <li><button type="button" class="btn-link" (click)="abrirSesion(f.s)">{{ f.etiqueta }}</button> — {{ f.motivo }}</li>
                    }
                  </ul>
                </details>
              }

              @if (choquesDocente().length > 0 || fueraDeDisponibilidad() > 0) {
                <details class="soft fuera" [open]="enBorrador() && choquesDocente().length > 0">
                  <summary><b>⚠ Avisos del docente ({{ choquesDocente().length + fueraDeDisponibilidad() }}) — no impiden usar el horario</b></summary>
                  @if (choquesDocente().length > 0) {
                    <ul>
                      @for (c of choquesDocente(); track c.s.id) {
                        <li><button type="button" class="btn-link" (click)="abrirSesion(c.s)">{{ c.etiqueta }}</button> — {{ c.detalle }}</li>
                      }
                    </ul>
                  }
                  @if (fueraDeDisponibilidad() > 0) {
                    <div>{{ fueraDeDisponibilidad() }} clase(s) fuera de la disponibilidad declarada de su docente. Abra la clase para ver el detalle.</div>
                  }
                </details>
              }
            </div>

            <mat-menu #masMenu="matMenu">
              <ng-template matMenuContent let-lista="lista">
                @for (m of lista; track m.key) {
                  <button mat-menu-item (click)="abrirEditarSesion(m)" [title]="describir(m)">{{ getAsignaturaName(m) }}{{ grupoSuffix(m) }} · {{ m.horaInicio }}–{{ m.horaFin }}</button>
                }
              </ng-template>
            </mat-menu>
          </div>

          <!-- panel lateral: horarios base -->
          @if (state.horariosBases().length > 0) {
            <div class="blueprint side">
              <i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>
              <div class="pophd" style="font-size:13px">Puntos de partida</div>
              <div class="side-bd">
                @for (base of state.horariosBases(); track base.id) {
                  <div class="base-item" [class.on]="state.baseSeleccionadaId() === base.id">
                    <label class="radio" style="flex:1">
                      <input type="radio" name="base" [checked]="state.baseSeleccionadaId() === base.id" (click)="toggleBase(base.id)">
                      <span class="dot"></span>
                      <span class="base-info"><span class="base-name">{{ base.nombre }}</span><span class="text-muted base-meta">{{ base.sesiones.length }} ses · {{ base.creadoEn | date:'dd/MM HH:mm' }}</span></span>
                    </label>
                    <button type="button" class="material-icons ic-del" (click)="eliminarBase(base.id)" [attr.aria-label]="'Eliminar ' + base.nombre">delete</button>
                  </div>
                }
                @if (state.baseSeleccionada(); as base) {
                  <div class="okb" style="font-size:11.5px">✓ Al generar se respetarán las {{ base.sesiones.length }} clases fijadas en "{{ base.nombre }}"</div>
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
    .btn-peligro { color: var(--err-bd); border-color: var(--err-bd); }
    .filtros { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; padding: 10px 22px; border-bottom: 1px solid var(--color-divider); }
    .fsel { width: auto; max-width: 220px; }
    .fss { width: 220px; }
    .borrador-bar { display: flex; flex-direction: column; gap: 8px; padding: 12px 22px; border-bottom: 1px solid var(--color-divider);
                    background: color-mix(in srgb, var(--color-accent) 8%, var(--color-bg)); font-size: 13px; }
    .borrador-acc { display: flex; gap: 10px; flex-wrap: wrap; }
    .borrador-bar ul { margin: 4px 0 0; padding-left: 18px; }

    .fail { padding: 16px 22px; display: flex; flex-direction: column; gap: 6px; align-items: flex-start; }

    .grid-area { padding: 18px 22px; display: flex; gap: 18px; align-items: flex-start; }
    .grid-main { flex: 1; min-width: 0; }
    .space-sel { display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 14px; }
    .space-chip { cursor: pointer; }
    .space-chip.on { background: var(--color-accent); color: #fff; border-color: var(--color-accent); }
    .backend-alert { margin-bottom: 12px; display: flex; align-items: center; gap: 10px; }

    .avisos { margin-top: 14px; display: flex; flex-direction: column; gap: 10px; }
    .fuera summary { cursor: pointer; }
    .fuera ul { margin: 6px 0 0; padding-left: 18px; max-height: 220px; overflow-y: auto; }
    .fuera li { margin-bottom: 2px; }
    .btn-link { border: 0; background: none; padding: 0; color: var(--color-accent); text-decoration: underline; cursor: pointer; font: inherit; }
    .vacio { color: var(--color-neutral-700); font-size: 13px; padding: 18px 4px; margin: 0 0 12px; }

    /* Cuadrícula por carriles (ver ubicarEnGrilla/distribuirEnCarriles): cada tarjeta se posiciona
     * por grid-row/grid-column ya resueltos en TypeScript — sin rowspan, así que ninguna sesión
     * simultánea/escalonada queda oculta ni descuadra las columnas siguientes (auditoría UI 2026-09). */
    .wk-scroll { overflow: auto; max-height: calc(100vh - 220px); min-height: 420px; }
    .wk { --fila: 46px; display: grid; grid-template-columns: 44px repeat(6, minmax(112px, 1fr)); grid-template-rows: auto auto; column-gap: 4px; min-width: 760px; }
    .wk-dhdr { grid-row: 1; position: sticky; top: 0; z-index: 3; background: var(--color-bg);
               text-align: center; font: 600 13px var(--font-heading); letter-spacing: .04em; color: var(--color-neutral-800); padding-bottom: 4px; }
    .wk-horas { grid-row: 2; grid-column: 1; display: grid; position: sticky; left: 0; z-index: 2; background: var(--color-bg); }
    .wk-horas span { font-size: 11px; color: var(--color-neutral-700); text-align: right; padding-right: 6px; }
    .wk-dia { grid-row: 2; display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); column-gap: 3px; position: relative;
              background: repeating-linear-gradient(to bottom, var(--color-neutral-300) 0 1px, transparent 1px var(--fila)); }
    .wk-cerrado { grid-column: 1 / -1; background: repeating-linear-gradient(-45deg, var(--color-neutral-100) 0 6px, var(--color-neutral-300) 6px 7px);
                  font-size: 11px; color: var(--color-neutral-700); text-align: center; padding-top: 6px; }
    .gcell { margin: 1px 0; min-width: 0; min-height: 0; display: flex; flex-direction: column; gap: 1px; text-align: left; font: 11px/1.25 inherit; color: var(--color-text);
             border: 1px solid var(--color-neutral-800); border-left-width: 5px; background: var(--color-bg); padding: 3px 6px; overflow: hidden; cursor: pointer; }
    .gcell:hover { box-shadow: var(--shadow-sm); }
    .gcell:focus-visible { outline: 2px solid var(--color-accent); outline-offset: 1px; }
    .gvirt { border-style: dashed; background: repeating-linear-gradient(-45deg, var(--color-bg) 0 5px, color-mix(in srgb, var(--color-accent) 12%, transparent) 5px 8px); }
    .gcell .s { font: 600 12.5px/1.15 var(--font-heading); letter-spacing: .01em; display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 2; overflow: hidden; }
    .gcell .g, .gcell .det > span { display: block; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; color: var(--color-neutral-700); font-size: 10px; }
    .gcell .nodoc { color: var(--err-bd); font-weight: 500; }
    .gcell.g1h .s { -webkit-line-clamp: 1; } .gcell.g1h .det { display: none; }
    .gnueva { outline: 2px dashed var(--color-accent); outline-offset: -4px; }
    .gsiempre { background: var(--color-neutral-100); border-color: var(--color-neutral-500); border-left-color: var(--color-neutral-500) !important; }
    .gcell .gsub { margin-top: 2px; padding: 2px 4px; border: 1px dashed var(--color-neutral-500);
                   background: repeating-linear-gradient(-45deg, var(--color-bg) 0 5px, color-mix(in srgb, var(--color-accent) 12%, transparent) 5px 8px);
                   color: var(--color-neutral-700); font-size: 10px; line-height: 1.2; }
    .gmas { margin: 1px 0; border: 1px dashed var(--color-neutral-700); background: var(--color-neutral-100);
            font: 600 13px var(--font-heading); color: var(--color-neutral-800); cursor: pointer; }
    .gmas:focus-visible { outline: 2px solid var(--color-accent); outline-offset: 1px; }
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

  readonly horas = HORAS_GRILLA;
  /** grid-template-rows compartido por la columna de horas y cada día — una fila de grid por
   *  hora de jornada, único lugar donde se traduce HORAS_GRILLA.length a CSS. */
  readonly filasCss = `repeat(${HORAS_GRILLA.length}, var(--fila))`;
  readonly horaAperturaTexto = hhmm(HORA_APERTURA);
  readonly horaCierreTexto = hhmm(HORA_CIERRE);
  readonly horaCierreSabadoTexto = hhmm(HORA_CIERRE_SABADO);

  activeSpace = signal<Espacio | null>(null);
  /** Chip "Virtual (sin espacio)" (petición 8 UI): aísla las teorías virtuales en su propia
   *  vista en vez de dejarlas dispersas/duplicadas entre los chips de espacio físico. */
  modoVirtual = signal(false);
  private readonly ESPACIO_VIRTUAL = '__virtual__';
  activeWeek = signal<'A' | 'B'>('A');
  loadingBackend = signal(false);
  /** Evita disparar dos POST /horario/generar concurrentes por doble-click antes de que
   *  llegue la primera respuesta — generarHorario() no compartía loadingBackend (ese solo lo
   *  toca syncFromBackend). */
  generandoHorario = signal(false);
  backendReady = signal(false);
  /** Guía accionable (sin jerga interna) del banner persistente de infactibilidad — ver
   *  mensajeInfactibilidadAmigable(). Vacío mientras no haya fallado ninguna generación, o
   *  tras una generación exitosa. */
  mensajeInfactible = signal('');

  ngOnInit() { this.syncFromBackend(); }
  constructor() { const espacios = this.state.espacios(); if (espacios.length > 0) this.activeSpace.set(espacios[0]); }

  /** Chip "Todos los espacios": la vista natural para revisar un grupo o un docente filtrado. */
  todosEspacios = signal(false);
  selectTodos() { this.todosEspacios.set(true); this.modoVirtual.set(false); }
  selectSpace(esp: Espacio) { this.todosEspacios.set(false); this.modoVirtual.set(false); this.activeSpace.set(esp); }
  selectVirtual() { this.todosEspacios.set(false); this.modoVirtual.set(true); }
  selectWeek(week: 'A' | 'B') { this.activeWeek.set(week); }
  altColor(m: MergedSesion): string { return m.alternancia === 'TipoA' ? '#5980a6' : m.alternancia === 'TipoB' ? '#a8825a' : '#8a8f94'; }

  /** Fondo de .gcell por asignatura (petición 12). Las virtuales conservan el rayado de
   *  .gvirt (indica modalidad, no asignatura) — null deja que gane la regla CSS de la clase. */
  gcellBg(m: MergedSesion): string | null {
    if (m.virtual) return null;
    return `color-mix(in srgb, ${this.state.colorDeAsignatura(m.asignaturaId)} 20%, white)`;
  }

  /** Lo que pinta la vista: el borrador si está activo, si no el horario real. */
  sesiones = computed(() => this.state.borrador() ?? this.state.sesiones());

  // ── Filtros (Facultad → Programa → Grupo, y Docente) ─────────────────────────
  fFacultad = signal('');
  fPrograma = signal('');
  fGrupo = signal('');
  fDocente = signal('');
  hayFiltros = computed(() => !!(this.fFacultad() || this.fPrograma() || this.fGrupo() || this.fDocente()));

  opcFacultades = computed(() => [...this.state.facultades()].sort(porNombre));
  opcProgramas = computed(() => this.state.programas().filter(p => !this.fFacultad() || p.facultadId === this.fFacultad()).sort(porNombre));
  opcGrupos = computed<SearchableOption[]>(() => {
    const asig = this.state.asignaturaById(), progs = this.state.programaById();
    const grupos = this.state.grupos().filter(g => {
      const programaId = asig.get(g.asignaturaId)?.programaId;
      return (!this.fPrograma() || programaId === this.fPrograma())
        && (!this.fFacultad() || progs.get(programaId ?? '')?.facultadId === this.fFacultad());
    }).sort(porNombre);
    return [{ value: '', label: 'Todos los grupos' }, ...grupos.map(g => ({ value: g.id, label: g.nombre, sub: asig.get(g.asignaturaId)?.nombre }))];
  });
  opcDocentes = computed<SearchableOption[]>(() =>
    [{ value: '', label: 'Todos los docentes' }, ...[...this.state.docentes()].sort(porNombre).map(d => ({ value: d.id, label: d.nombre }))]);

  /** Cambiar un nivel limpia los que dependen de él; al filtrar se pasa a "Todos los espacios" —
   *  ver el horario de un grupo o un docente aula por aula no sirve de mucho. */
  setFiltro(campo: 'facultad' | 'programa' | 'grupo' | 'docente', valor: string) {
    if (campo === 'facultad') { this.fFacultad.set(valor); this.fPrograma.set(''); this.fGrupo.set(''); }
    if (campo === 'programa') { this.fPrograma.set(valor); this.fGrupo.set(''); }
    if (campo === 'grupo') this.fGrupo.set(valor);
    if (campo === 'docente') this.fDocente.set(valor);
    if (valor) this.selectTodos();
  }
  limpiarFiltros() { this.fFacultad.set(''); this.fPrograma.set(''); this.fGrupo.set(''); this.fDocente.set(''); }

  private pasaFiltros = computed(() => {
    const fac = this.fFacultad(), prog = this.fPrograma(), grupo = this.fGrupo(), doc = this.fDocente();
    const asig = this.state.asignaturaById(), progs = this.state.programaById();
    return (s: Sesion) => {
      if (grupo && s.grupoId !== grupo) return false;
      if (doc && s.docenteId !== doc) return false;
      const programaId = asig.get(s.asignaturaId)?.programaId;
      if (prog && programaId !== prog) return false;
      return !fac || progs.get(programaId ?? '')?.facultadId === fac;
    };
  });

  // ── Modo borrador ────────────────────────────────────────────────────────────
  enBorrador = computed(() => this.state.borrador() !== null);
  clasesBorrador = computed(() => (this.state.borrador() ?? []).filter(s => s.deBorrador));
  convirtiendo = signal(false);
  erroresBorrador = signal<string[]>([]);

  entrarBorrador() { this.erroresBorrador.set([]); this.state.borrador.set([...this.state.sesiones()]); }

  salirBorrador() {
    const n = this.clasesBorrador().length;
    if (n > 0 && !window.confirm(`¿Salir del borrador? Se descartarán las ${n} clase(s) agregada(s).`)) return;
    this.state.borrador.set(null);
    this.erroresBorrador.set([]);
  }

  agregarAlBorrador() {
    const ref = this.dialog.open(CrearSesionDialogComponent, {
      width: '320px', maxHeight: '92vh',
      data: { asignaturas: this.state.asignaturas(), docentes: this.state.docentes(), espacios: this.state.espacios(), grupos: this.state.grupos(), sesiones: this.sesiones(), programaById: this.state.programaById(), modoBorrador: true } satisfies CrearSesionDialogData
    });
    ref.afterClosed().subscribe((nuevas: Sesion[] | undefined) => {
      if (!nuevas?.length) return;
      this.state.borrador.update(b => [...(b ?? []), ...nuevas]);
      const choca = nuevas.some(s => this.choquesEspacioMap().get(s.id)?.choques.length || this.avisosDocente().get(s.id)?.choques.length);
      this.snackBar.open(choca ? 'Clase agregada al borrador — ⚠ choca con otra clase (vea los avisos bajo la cuadrícula).' : 'Clase agregada al borrador.', 'Cerrar', { duration: 5000 });
    });
  }

  private quitarDelBorrador(s: Sesion) {
    const ref = this.dialog.open(ConfirmDeleteDialogComponent, {
      width: '320px',
      data: { title: 'Quitar del borrador', message: `Se quitará "${describirSesionConflicto(s, this.state.asignaturas(), this.state.grupos())}" del borrador.`, confirmText: 'Quitar' }
    });
    ref.afterClosed().subscribe(ok => { if (ok) this.state.borrador.update(b => (b ?? []).filter(x => x.id !== s.id)); });
  }

  /** Guarda en el horario real las clases agregadas, una a una, por el mismo camino que «Sesión
   *  manual» (el servidor valida y rechaza las que chocan). Las rechazadas quedan en el borrador. */
  convertirBorrador() {
    const nuevas = this.clasesBorrador();
    const horarioId = this.state.horarioId();
    if (!horarioId) { this.snackBar.open('Genere el horario antes de convertir el borrador.', 'Cerrar', { duration: 5000 }); return; }
    const ref = this.dialog.open(ConfirmDeleteDialogComponent, {
      width: '360px',
      data: {
        title: 'Convertir en horario real',
        message: `Se agregarán ${nuevas.length} clase(s) al horario guardado. Las que choquen con otra clase no se agregarán y seguirán en el borrador.`,
        confirmText: 'Convertir'
      }
    });
    ref.afterClosed().subscribe(ok => {
      if (!ok) return;
      this.convirtiendo.set(true);
      this.erroresBorrador.set([]);
      from(nuevas).pipe(
        concatMap(s => this.persistencia.crearSesionManual({
          horarioId, asignaturaId: s.asignaturaId, docenteId: s.docenteId ?? null, espacioId: s.espacioId ?? null,
          grupoId: s.grupoId!, dia: s.dia, horaInicio: s.horaInicio, duracionHoras: s.duracionHoras, alternancia: s.alternancia,
          tipoFlujo: s.tipoFlujo ?? 'AulaVirtual', esVirtual: s.virtual
        }).pipe(
          map(creadas => ({ s, creadas, error: '' })),
          catchError(err => of({ s, creadas: [] as Sesion[], error: `${describirSesionConflicto(s, this.state.asignaturas(), this.state.grupos())}: ${mensajeErrorHttp(err)}` }))
        )),
        toArray()
      ).subscribe(resultados => {
        this.convirtiendo.set(false);
        const creadas = resultados.flatMap(r => r.creadas);
        this.state.sesiones.update(v => [...v, ...creadas]);
        const fallidas = resultados.filter(r => r.error);
        if (fallidas.length === 0) {
          this.state.borrador.set(null);
          this.snackBar.open(`Borrador convertido: ${nuevas.length} clase(s) agregada(s) al horario.`, 'Cerrar', { duration: 5000 });
          return;
        }
        this.state.borrador.set([...this.state.sesiones(), ...fallidas.map(r => r.s)]);
        this.erroresBorrador.set(fallidas.map(r => r.error));
      });
    });
  }

  // ── Limpiar horario ──────────────────────────────────────────────────────────
  limpiarHorario() {
    const ref = this.dialog.open(ConfirmDeleteDialogComponent, {
      width: '360px',
      data: {
        title: 'Limpiar horario',
        message: `Se borrarán las ${this.state.sesiones().length} clases del horario guardado. Esta acción no se puede deshacer.`,
        confirmText: 'Limpiar horario'
      }
    });
    ref.afterClosed().subscribe(ok => {
      if (!ok) return;
      this.loadingBackend.set(true);
      this.horarioApi.limpiar(this.state.semestre()).subscribe({
        next: () => {
          this.loadingBackend.set(false);
          this.state.setSesiones([]);
          this.state.setExecutionLogs([]);
          this.state.horarioId.set(null);
          this.mensajeInfactible.set('');
          this.snackBar.open('Horario limpiado.', '', { duration: 3000 });
        },
        error: (err: any) => {
          this.loadingBackend.set(false);
          this.snackBar.open(`No se pudo limpiar el horario: ${err?.mensajeError ?? mensajeErrorHttp(err)}`, 'Cerrar', { duration: 6000, panelClass: ['snack-error'] });
        }
      });
    });
  }

  syncFromBackend() {
    this.loadingBackend.set(true);
    this.catalogo.cargarTodo().subscribe({
      next: () => {
        this.loadingBackend.set(false);
        this.backendReady.set(true);
        const espacios = this.state.espacios();
        const current = this.activeSpace();
        if (!current || !espacios.find(e => e.id === current.id)) this.activeSpace.set(espacios[0] ?? null);
        // FE6/FE13 auditoría: la rehidratación del horario (P6) ahora vive en
        // CatalogoService.cargarTodo() — única fuente, ver ficha allí — para que /revisar
        // también la reciba tras un F5. Un fallo real ahí no tumba el resto del catálogo; se
        // guarda en errorHorarioActual y se muestra aquí, que es donde el operador actúa sobre él.
        const errorHorario = this.state.errorHorarioActual();
        if (errorHorario) {
          this.snackBar.open('No se pudo recuperar el último horario. Puede generarlo de nuevo.', 'Cerrar', { duration: 6000, panelClass: ['snack-error'] });
        }
      },
      error: () => {
        this.loadingBackend.set(false);
        this.backendReady.set(false);
        this.snackBar.open('No se pudo conectar con el sistema. Revise su conexión e intente de nuevo.', 'Cerrar', { duration: 5000, panelClass: ['snack-error'] });
      }
    });
  }

  private aMerged(s: Sesion): MergedSesion {
    return {
      // Comparten `id` la presencial y su contraparte virtual (ver state.service.ts) — cuando
      // ambas terminan como tarjetas propias (la contraparte no se plegó, ver sesionesVista())
      // necesitan una clave distinta para no chocar en el @for ni en el mat-menu del "+N".
      key: `${s.id}|${s.semana ?? ''}|${s.virtual ? 'v' : 'p'}`,
      sesiones: [s], dia: s.dia, horaInicio: s.horaInicio, horaFin: s.horaFin,
      virtual: s.virtual, alternancia: s.alternancia, semana: s.semana, asignaturaId: s.asignaturaId,
      grupoId: s.grupoId, docenteId: s.docenteId, espacioId: s.espacioId, espacioIdHogar: s.espacioIdHogar,
      tipoFlujo: s.tipoFlujo, parejaId: s.parejaId
    };
  }

  private sesionPerteneceAlEspacio(s: Sesion, spaceId: string | undefined): boolean {
    if (spaceId === this.ESPACIO_VIRTUAL) return s.virtual;
    if (!spaceId) return true;
    if (s.espacioId === spaceId) return true;
    if (s.espacioIdHogar) return s.espacioIdHogar === spaceId;
    // Antes caía aquí "return s.virtual", lo que duplicaba las virtuales sin hogar en TODOS los
    // chips físicos. Ahora tienen su propia vista dedicada (chip "Virtual (sin espacio)").
    return false;
  }

  /** Hay alternancia activa cuando alguna sesión tiene pareja. Sin ella la Semana B no existe
   *  y el selector ni siquiera se muestra: la Semana A es el horario completo. */
  hayAlternancia = computed(() => this.sesiones().some(s => !!s.parejaId));

  /** La semana efectivamente mostrada: si ya no hay alternancia (p. ej. tras regenerar sin
   *  parejas) no se queda pegada en B sin selector visible para volver a A. */
  semanaVista = computed<'A' | 'B'>(() => this.hayAlternancia() ? this.activeWeek() : 'A');

  /** Semana A: todo lo que no es exclusivamente B. Semana B: todo lo que no es exclusivamente A
   *  — incluye lo que no alterna (`semana` vacío/ausente), que la grilla pinta en gris como
   *  "ocupa el aula todas las semanas" en vez de dejar el aula viéndose libre (antes solo se
   *  mostraba lo emparejado y el resto de las aulas ocupadas parecían disponibles). */
  private sesionVisibleEnSemana(s: Sesion): boolean {
    return this.semanaVista() === 'B' ? s.semana !== 'A' : s.semana !== 'B';
  }

  /** Sesiones visibles en el chip/semana actual, ya plegadas con su contraparte de alternancia.
   *  Reemplaza el mapa por celda de inicio (mergedByCell/coveredCells) de la tabla anterior. */
  sesionesVista = computed<MergedSesion[]>(() => {
    // "Todos los espacios" = sin spaceId: sesionPerteneceAlEspacio deja pasar todo, virtuales incluidas.
    const spaceId = this.todosEspacios() ? undefined : this.modoVirtual() ? this.ESPACIO_VIRTUAL : this.activeSpace()?.id;
    const pasa = this.pasaFiltros();
    const visibles = this.sesiones().filter(s => this.sesionPerteneceAlEspacio(s, spaceId) && this.sesionVisibleEnSemana(s) && pasa(s));

    // La contraparte virtual se pliega como sub-caja DENTRO de la tarjeta presencial solo si
    // coincide exactamente en pareja + día + hora con una presencial visible — antes se indexaba
    // solo por parejaId (un Map que sobrescribía si había más de una franja) y una contraparte sin
    // presencial visible se descartaba sin más (`continue`), perdiéndola de la grilla sin aviso.
    const clave = (s: { parejaId?: string; dia: string; horaInicio: string; horaFin: string }) =>
      `${s.parejaId}|${s.dia}|${s.horaInicio}|${s.horaFin}`;
    const contrapartes = new Map<string, Sesion>();
    for (const s of visibles) if (s.esContraparteVirtual && s.parejaId) contrapartes.set(clave(s), s);

    const plegadas = new Set<string>();
    const resultado: MergedSesion[] = [];
    for (const s of visibles) {
      if (s.esContraparteVirtual) continue;
      const merged = this.aMerged(s);
      const k = s.parejaId ? clave(s) : undefined;
      const otra = k ? contrapartes.get(k) : undefined;
      if (otra) { merged.contraparte = this.aMerged(otra); plegadas.add(k!); }
      resultado.push(merged);
    }
    // Contrapartes que no se plegaron (su presencial no está visible en esta vista/semana, o no
    // coincidió exactamente en franja): tarjeta propia en vez de perderse.
    for (const [k, s] of contrapartes) if (!plegadas.has(k)) resultado.push(this.aMerged(s));
    return resultado;
  });

  /** Presenciales sin aula ni aula de origen: `sesionPerteneceAlEspacio` las excluye de TODOS los
   *  chips de espacio físico (no pertenecen a ninguno), así que sin este parche desaparecían de
   *  la grilla sin aviso. grilla() las suma a la lista "fuera" de cualquier vista de aula. */
  private sesionesSinAula = computed<Sesion[]>(() => {
    const pasa = this.pasaFiltros();
    return this.sesiones().filter(s => !s.virtual && !s.espacioId && !s.espacioIdHogar && this.sesionVisibleEnSemana(s) && pasa(s));
  });

  /** Cuadrícula por carriles (ver distribuirEnCarriles/ubicarEnGrilla): posiciona cada sesión por
   *  minutos de inicio/fin y reparte las simultáneas en columnas lado a lado — reemplaza la tabla
   *  con rowspan que ocultaba sesiones escalonadas o solapadas y descuadraba columnas (auditoría
   *  UI 2026-09). Nada se pierde: lo que no cabe en el rango horario va a `fuera`.
   */
  grilla = computed<GrillaSemanal>(() => {
    const dias: DiaGrilla[] = DIAS_GRILLA.map(d => ({
      valor: d.valor, corto: d.corto, etiqueta: d.etiqueta, tarjetas: [], mas: [],
      filaCierre: d.valor === 'sabado' ? HORA_CIERRE_SABADO - HORA_APERTURA + 1 : undefined,
    }));
    const diaIdx = new Map(dias.map((d, i) => [d.valor, i]));
    const fuera: SesionFuera[] = [];
    const porDia = new Map<string, ItemGrilla[]>();

    for (const m of this.sesionesVista()) {
      const pos = ubicarEnGrilla(m.dia, m.horaInicio, m.horaFin);
      if ('motivo' in pos) { fuera.push({ m, motivo: pos.motivo, texto: this.describir(m) }); continue; }
      const lista = porDia.get(m.dia) ?? []; lista.push({ m, ...pos }); porDia.set(m.dia, lista);
    }
    // En "Todos los espacios" ya están en sesionesVista (y en la cuadrícula): no duplicarlas aquí.
    if (!this.modoVirtual() && !this.todosEspacios()) {
      for (const s of this.sesionesSinAula()) {
        const m = this.aMerged(s);
        fuera.push({ m, motivo: 'sin aula asignada', texto: this.describir(m) });
      }
    }

    let total = 0;
    for (const [diaValor, items] of porDia) {
      const idx = diaIdx.get(diaValor); if (idx === undefined) continue;
      total += items.length;
      const { tarjetas, mas } = distribuirEnCarriles(items);
      dias[idx].tarjetas = tarjetas.map(({ item, carril, carriles }) => {
        const ancho = 6 / carriles;
        return {
          key: item.m.key, m: item.m,
          fila: `${item.fila0} / ${item.fila1}`,
          col: `${carril * ancho + 1} / span ${ancho}`,
          siempre: this.semanaVista() === 'B' && !item.m.semana,
          g1h: (item.fila1 - item.fila0) < 2,
          texto: this.describir(item.m),
        };
      });
      dias[idx].mas = mas.map(grupo => ({
        fila: `${grupo.ini / 60 - HORA_APERTURA + 1} / ${grupo.fin / 60 - HORA_APERTURA + 1}`,
        items: grupo.items.map(x => x.m),
        texto: `${grupo.items.length} sesiones más en ese horario — clic para verlas`,
      }));
    }
    return { dias, fuera, total };
  });

  /** Descripción completa de una sesión — tooltip y aria-label de su tarjeta, y texto de la lista
   *  "fuera de la cuadrícula". No se abrevia: es la única forma de leer el dato completo, ya que
   *  la tarjeta trunca según el alto disponible. */
  describir(m: MergedSesion): string {
    const base = describirSesionConflicto(m, this.state.asignaturas(), this.state.grupos());
    const ctx = this.getContextLabel(m);
    const docente = m.docenteId ? this.getDocenteName(m) : 'sin docente';
    const espacio = this.celdaEspacio(m);
    const semana = m.semana === 'A' ? ' · Semana A' : m.semana === 'B' ? ' · Semana B' : '';
    const siempre = this.semanaVista() === 'B' && !m.semana ? ' · ocupa el aula todas las semanas' : '';
    const contraparte = m.contraparte ? ` · alterna con ${this.getAsignaturaName(m.contraparte)}${this.grupoSuffix(m.contraparte)} (virtual)` : '';
    const choque = this.tieneChoque(m) ? ' · ⚠ el docente tiene otra clase a la misma hora' : '';
    const choqueAula = this.tieneChoqueEspacio(m) ? ' · ⚠ el espacio ya está ocupado a esa hora' : '';
    const accion = this.enBorrador() ? (m.sesiones[0].deBorrador ? 'Clic para quitarla del borrador' : 'Clase del horario real') : 'Clic para editar';
    return `${base}${ctx ? ' · ' + ctx : ''} · ${docente} · ${espacio}${semana}${siempre}${contraparte}${choque}${choqueAula}. ${accion}.`;
  }

  /** Avisos del docente (no bloquean) de todo el horario — ver avisosDocentePorSesion. */
  avisosDocente = computed(() =>
    avisosDocentePorSesion(this.sesiones(), this.state.docentes(), this.state.asignaturas(), this.state.grupos()));
  /** Clases con el docente en dos clases a la vez: se listan bajo la cuadrícula para poder abrirlas. */
  choquesDocente = computed(() => filasDeChoques(this.sesiones(), this.avisosDocente(), this.state.asignaturas(), this.state.grupos()));
  /** Dos clases en el mismo espacio a la vez (sobre todo en el borrador) — ver choquesEspacioPorSesion. */
  choquesEspacioMap = computed(() =>
    choquesEspacioPorSesion(this.sesiones(), this.state.espacios(), this.state.asignaturas(), this.state.grupos()));
  choquesEspacio = computed(() => filasDeChoques(this.sesiones(), this.choquesEspacioMap(), this.state.asignaturas(), this.state.grupos()));
  /** Clases fuera de la disponibilidad de su grupo — ver fueraDisponibilidadGrupo. */
  fueraGrupo = computed(() => {
    const motivos = fueraDisponibilidadGrupo(this.sesiones(), this.state.grupos());
    return this.sesiones()
      .filter(s => motivos.has(s.id) && !s.esContraparteVirtual)
      .map(s => ({ s, etiqueta: describirSesionConflicto(s, this.state.asignaturas(), this.state.grupos()), motivo: motivos.get(s.id)! }));
  });
  fueraDeDisponibilidad = computed(() => [...this.avisosDocente().values()].filter(a => a.fueraDeDisponibilidad).length);
  /** ⚠ de la tarjeta: choque de docente o de espacio. */
  tieneChoque(m: MergedSesion): boolean { return !!this.avisosDocente().get(m.sesiones[0].id)?.choques.length || this.tieneChoqueEspacio(m); }
  private tieneChoqueEspacio(m: MergedSesion): boolean { return !!this.choquesEspacioMap().get(m.sesiones[0].id)?.choques.length; }
  abrirSesion(s: Sesion) { this.abrirEditarSesion(this.aMerged(s)); }

  getAsignaturaName(merged: MergedSesion): string { return this.state.asignaturaById().get(merged.asignaturaId)?.nombre ?? 'Desconocida'; }
  getDocenteName(merged: MergedSesion): string { return merged.docenteId ? (this.state.docenteById().get(merged.docenteId)?.nombre ?? '') : ''; }
  celdaEspacio(merged: MergedSesion): string {
    if (merged.virtual) {
      const hogar = merged.espacioIdHogar ? this.state.espacioById().get(merged.espacioIdHogar)?.nombre : null;
      return hogar ? `aula ${hogar}` : 'virtual';
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
    // G3 (bug reportado "no se muestran todos los grupos"): antes usaba Asignatura.grupoNumero
    // (campo legado de import, no el grupo real) — dos grupos de la misma asignatura se
    // pintaban idénticos. Ahora usa el grupo real de la sesión (Sesion.grupoId).
    const nombre = merged.grupoId && this.state.grupos().find(g => g.id === merged.grupoId)?.nombre;
    return nombre ? ` · ${nombre}` : '';
  }

  abrirEditarSesion(merged: MergedSesion) {
    // En el borrador nada pasa por el servidor: solo se quitan las clases agregadas al borrador.
    if (this.enBorrador()) {
      if (merged.sesiones[0].deBorrador) this.quitarDelBorrador(merged.sesiones[0]);
      else this.snackBar.open('En el modo borrador solo puede quitar las clases que agregó. Salga del borrador para editar el horario real.', 'Cerrar', { duration: 5000 });
      return;
    }
    const ref = this.dialog.open(EditarSesionDialogComponent, {
      width: '300px', maxHeight: '92vh',
      data: {
        merged, sesion: merged.sesiones[0], asignaturas: this.state.asignaturas(), docentes: this.state.docentes(),
        espacios: this.state.espacios(), sesiones: this.state.sesiones(), programaById: this.state.programaById(), facultadById: this.state.facultadById()
      } satisfies EditarSesionDialogData
    });
    ref.afterClosed().subscribe((result: EditarSesionResult | undefined) => {
      if (!result) return;
      // Petición 13: un cambio de día/hora/espacio ya refrescó el StateService completo desde la
      // respuesta de /reacomodar (puede haber movido otras sesiones en conflicto); solo un cambio
      // de docente (ya persistido por PATCH) se refleja como mutación local puntual.
      if (result.sesion) this.state.updateSesion(result.sesion);
      if (result.advertencias?.length) this.snackBar.open(`Sesión actualizada con avisos: ${result.advertencias.join(' · ')}`, 'Cerrar', { duration: 8000 });
      else this.snackBar.open('Sesión actualizada.', '', { duration: 2500 });
    });
  }

  /** El registro técnico (Fase 1/2/3, HC-…, milisegundos) nunca se pinta en pantalla — la
   *  coordinadora no puede accionarlo — pero sigue disponible para pegarlo en un caso de soporte. */
  copiarDetalleSoporte() {
    const texto = this.state.executionLogs().join('\n');
    navigator.clipboard?.writeText(texto).then(
      () => this.snackBar.open('Detalle copiado al portapapeles.', '', { duration: 3000 }),
      () => this.snackBar.open('No se pudo copiar. Seleccione el texto manualmente.', 'Cerrar', { duration: 4000, panelClass: ['snack-error'] })
    );
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
        const crudo = json.sesiones ?? json;
        if (!Array.isArray(crudo)) throw new Error('El archivo no es una copia de horario válida.');
        // FE9 auditoría: antes se inyectaba cualquier fila tal cual — una sesión con un `dia` fuera
        // de la grilla (p. ej. "domingo") entraba al state pero la grilla nunca la podía dibujar:
        // pérdida de datos silenciosa que el conteo del snackbar tampoco reflejaba.
        const diasValidos = new Set(DIAS_GRILLA.map(d => d.valor));
        const validas = crudo.filter((s: any) => diasValidos.has(s?.dia) && typeof s?.horaInicio === 'string' && typeof s?.horaFin === 'string');
        const descartadas = crudo.length - validas.length;
        this.state.setSesiones(validas);
        // Esta vista es solo local: las sesiones importadas no corresponden a ningún horario
        // persistido en BD. Antes horarioId seguía apuntando al horario viejo, así que la
        // siguiente edición (reacomodar/asignar docente) volvía a pedirle al backend ESE horario
        // y su respuesta pisaba el import en silencio. Al limpiarlo, esas acciones piden
        // regenerar en vez de borrar el import sin avisar.
        this.state.horarioId.set(null);
        const detalle = descartadas > 0 ? ` (${descartadas} no se cargaron: día u hora no válidos).` : '.';
        this.snackBar.open(
          `Horario abierto: ${validas.length} clases${detalle} Solo se ve en este equipo; no reemplaza el horario guardado.`,
          'Cerrar', { duration: 7000 });
      } catch (err: any) {
        this.snackBar.open('No se pudo leer el archivo. Use una copia descargada desde SOEA.', 'Cerrar', { duration: 6000, panelClass: ['snack-error'] });
      }
      (event.target as HTMLInputElement).value = '';
    };
    reader.readAsText(file);
  }

  guardarComoBase() {
    const nombre = window.prompt('Nombre del punto de partida:', `Base ${new Date().toLocaleDateString('es-CO')}`);
    if (!nombre?.trim()) return;
    const base = this.state.guardarHorarioBase(nombre);
    this.snackBar.open(`Horario base "${base.nombre}" guardado (${base.sesiones.length} sesiones).`, 'Cerrar', { duration: 4000 });
  }

  toggleBase(id: string) { this.state.seleccionarBase(this.state.baseSeleccionadaId() === id ? null : id); }
  eliminarBase(id: string) {
    const base = this.state.horariosBases().find(b => b.id === id);
    if (!base || !window.confirm(`¿Eliminar el punto de partida "${base.nombre}"?`)) return;
    this.state.eliminarHorarioBase(id);
    this.snackBar.open(`Base "${base.nombre}" eliminada.`, '', { duration: 3000 });
  }

  generarHorario() {
    if (this.generandoHorario()) return;
    if (!this.backendReady()) { this.snackBar.open('No hay conexión con el sistema; no se puede generar ahora.', 'Cerrar', { duration: 4000 }); return; }
    // L-7 auditoría 2026-09-28: ya no se exigen docentes — salieron del pipeline de generación (CR-02/CR-08) y
    // se asignan después de generar; exigirlos bloqueaba el flujo normal (cargar catálogo, generar, asignar).
    if (this.state.asignaturas().length === 0 || this.state.espacios().length === 0) {
      this.snackBar.open('Carga asignaturas y espacios antes de generar el horario.', 'Cerrar', { duration: 4000 });
      return;
    }
    const asignaturas = this.state.asignaturas();
    this.generandoHorario.set(true);
    const dialogRef = this.dialog.open(ProgressDialogComponent, { disableClose: true, width: '340px' });
    // Sin panel de parámetros en la UI (decisión de producto): siempre se generan con los valores
    // por defecto del servidor — ver ConfiguracionOptimizacion en GenerarHorarioService.
    this.horarioApi.generarHorario(asignaturas, this.state.docentes(), this.state.espacios(), undefined, this.state.semestre(), this.state.baseSeleccionada() ?? undefined, this.state.grupos())
      .subscribe({
        next: (respuesta) => {
          this.generandoHorario.set(false);
          dialogRef.close();
          const sesiones = this.horarioApi.mapearSesiones(respuesta.sesiones);
          this.state.setSesiones(sesiones);
          this.state.setExecutionLogs(respuesta.logs || []);
          this.state.horarioId.set(respuesta.horarioId);
          // Una generación exitosa invalida cualquier aviso de un intento previo.
          this.state.setGruposEnConflicto([]);
          this.state.setMotivoInfactibilidad(undefined);
          this.mensajeInfactible.set('');
          const choques = this.choquesDocente().length;
          const aviso = choques ? ` Atención: ${choques} clase(s) tienen al docente en dos clases a la vez — vea los avisos bajo la cuadrícula.` : '';
          this.snackBar.open(`Horario generado con ${sesiones.length} clases.${aviso}`, 'Cerrar', { duration: choques ? 10000 : 6000 });
        },
        error: (err: any) => {
          this.generandoHorario.set(false);
          dialogRef.close();
          // Un 422 (GenerarHorarioResponse) pasa tal cual desde manejarError — mensajeError no
          // pasó por mensajeErrorHttp, así que puede traer el código de regla crudo (HC-SEP…).
          const mensaje = limpiarEtiquetaInterna(err.mensajeError || err.message || err.error || 'Error desconocido');
          if (err.logs && Array.isArray(err.logs)) this.state.setExecutionLogs(err.logs);
          const gruposEnConflicto = Array.isArray(err.gruposEnConflicto) ? err.gruposEnConflicto : [];
          this.state.setGruposEnConflicto(gruposEnConflicto);
          this.state.setMotivoInfactibilidad(err.motivoInfactibilidad);
          this.state.espacioLimitanteId.set(err.espacioLimitanteId);
          // Antes solo se traducía si el mensaje crudo contenía "factible"/"infeasible" — un fallo
          // distinto (p. ej. una violación de reglas post-GA) se colaba tal cual, con jerga interna.
          // Con motivo o grupos estructurados ya hay suficiente señal para traducir igual.
          const esInfeasible = !!err.motivoInfactibilidad || gruposEnConflicto.length > 0 || /factible|infeasible/i.test(mensaje);
          const texto = esInfeasible
            ? mensajeInfactibilidadAmigable(err.motivoInfactibilidad, gruposEnConflicto, this.state.grupos(), asignaturas.length, this.state.espacios().length, this.state.contextoCapacidad())
            : mensaje;
          // El banner persistente de /horario (a diferencia del snackbar) sigue visible hasta la
          // próxima generación — siempre con el mismo texto limpio, nunca vacío ni con jerga.
          this.mensajeInfactible.set(texto);
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
      // "N filas" era jerga interna del modelo de datos: al coordinador solo le importa que la
      // sesión quedó creada.
      this.snackBar.open('Sesión creada.', 'Cerrar', { duration: 5000 });
    });
  }

  abrirSesionFija() {
    // Mismo diálogo que "crear sesión" (grupo, tipo y duración de la asignatura) en modo fija: las
    // comprobaciones se hacen contra la base seleccionada y nada va al servidor hasta generar.
    const ref = this.dialog.open(CrearSesionDialogComponent, {
      width: '300px', maxHeight: '92vh',
      data: { asignaturas: this.state.asignaturas(), docentes: this.state.docentes(), espacios: this.state.espacios(), grupos: this.state.grupos(), sesiones: this.state.baseSeleccionada()?.sesiones ?? [], programaById: this.state.programaById(), modoFija: true }
    });
    ref.afterClosed().subscribe((nuevas: Sesion[] | undefined) => {
      const sesion = nuevas?.[0];
      if (!sesion) return;
      const nombre = window.prompt('Nombre del punto de partida:', 'Sesiones fijas');
      if (!nombre?.trim()) return;
      // Se agrega la sesión fija a un horario base nuevo (restricción de igualdad para CP-SAT).
      const previa = this.state.baseSeleccionada();
      const sesiones = previa ? [...previa.sesiones, sesion] : [sesion];
      const antes = this.state.sesiones();
      this.state.setSesiones(sesiones);
      const base = this.state.guardarHorarioBase(nombre);
      this.state.setSesiones(antes);
      this.state.seleccionarBase(base.id);
      this.snackBar.open(`Clase fijada en "${base.nombre}" (${base.sesiones.length} en total).`, 'Cerrar', { duration: 5000 });
    });
  }
}
