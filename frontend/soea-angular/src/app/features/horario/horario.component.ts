import { Component, inject, signal, computed, OnInit, HostListener } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatMenuModule } from '@angular/material/menu';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { RouterModule } from '@angular/router';
import { StateService } from '../../core/state.service';
import { HorarioApiService } from '../../core/horario-api.service';
import { CatalogoService } from '../../core/catalogo.service';
import { DimFiltro, Espacio, FiltroHorario, HorarioBase, Sesion } from '../../core/models';
import { claveTipoSesion } from '../../core/state.service';
import { nuevoId } from '../../core/id.util';
import { HORA_APERTURA, HORA_CIERRE, HORA_CIERRE_SABADO } from '../../core/jornada';
import { mensajeErrorHttp, limpiarEtiquetaInterna } from '../../core/http-error.util';
import { ConfirmDeleteDialogComponent } from '../../shared/confirm-delete-dialog/confirm-delete-dialog.component';
import { porTexto } from '../../core/orden';
import { MergedSesion, SesionFuera, DiaGrilla, GrillaSemanal, ItemGrilla, describirSesionConflicto, hhmm, HORAS_GRILLA, DIAS_GRILLA, fueraDisponibilidadGrupo, avisosDocentePorSesion, choquesEspacioPorSesion, filasDeChoques, ubicarEnGrilla, distribuirEnCarriles, mensajeInfactibilidadAmigable, DIMS_FILTRO, clavesDeSesion, pasaFiltrosHorario, conteosFiltro, clasesPendientes, ClasePendiente, ETIQUETA_TIPO_SESION } from './horario.reglas';
import { EditarSesionDialogComponent, EditarSesionDialogData, EditarSesionResult } from './editar-sesion-dialog.component';
import { CrearSesionDialogComponent, CrearSesionDialogData } from './crear-sesion-dialog.component';
import { ProgressDialogComponent } from './progress-dialog.component';
import { GeneracionFallidaComponent } from '../../shared/generacion-fallida/generacion-fallida.component';

@Component({
  selector: 'app-horario',
  standalone: true,
  imports: [CommonModule, DatePipe, FormsModule, MatDialogModule, MatSnackBarModule, RouterModule, MatMenuModule, MatAutocompleteModule, GeneracionFallidaComponent],
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
          <span class="tb-lbl" id="lbl-semana">Ver</span>
          <div class="seg" role="radiogroup" aria-labelledby="lbl-semana">
            <label class="seg-opt" role="radio" [attr.aria-checked]="semanaVista() === 'A'" [class.on]="semanaVista() === 'A'" (click)="selectWeek('A')">Horario completo</label>
            <label class="seg-opt" role="radio" [attr.aria-checked]="semanaVista() === 'B'" [class.on]="semanaVista() === 'B'" (click)="selectWeek('B')">Semana siguiente · solo lo que alterna</label>
          </div>
        }
        <div class="legend">
          <span><span class="lg-box pres"></span> Presencial</span>
          <span><span class="lg-box virt"></span> ⌁ Virtual</span>
        </div>
        @if (!enPrueba()) {
          <div class="tb-right">
            <button class="btn btn-secondary" [class.on]="panelAbierto()" [attr.aria-pressed]="panelAbierto()" (click)="alternarPanel()">🧪 Ajustes del horario</button>
            @if (state.sesiones().length > 0) {
              <button class="btn btn-secondary btn-peligro" (click)="limpiarHorario()" [disabled]="loadingBackend() || generandoHorario()">Limpiar horario</button>
            }
            <button class="btn btn-primary" (click)="generarHorario()" [disabled]="loadingBackend() || generandoHorario()">▶ Generar horario</button>
          </div>
        }
      </div>

      <!-- Prueba de horario: resultado de "Probar sin guardar", en lugar del horario guardado. -->
      @if (enPrueba()) {
        <div class="prueba-bar" role="region" aria-label="Prueba de horario">
          <div><b>🧪 Prueba de horario</b> — esto NO se guardó: el horario guardado sigue igual. Clic en una clase para fijarla en su franja y volver a probar.</div>
          <div class="prueba-acc">
            <button class="btn btn-primary" (click)="guardarPrueba()" [disabled]="generandoHorario()">Guardar este horario</button>
            <button class="btn btn-secondary" (click)="probarOtraVez()" [disabled]="generandoHorario()">↻ Probar otra vez</button>
            <button class="btn btn-secondary" (click)="descartarPrueba()" [disabled]="generandoHorario()">Descartar prueba</button>
            <button class="btn btn-secondary" [class.on]="panelAbierto()" [attr.aria-pressed]="panelAbierto()" (click)="alternarPanel()">🧪 Ajustes del horario</button>
          </div>
        </div>
      }

      <!-- Filtros por facetas: limitan lo que se ve en la cuadrícula (nunca cambian el horario). -->
      @if (sesiones().length > 0) {
        <div class="filtros" role="search" aria-label="Filtrar el horario">
          <span class="tb-lbl">Ver solo</span>
          <input #filtroInput class="input fbusca" type="search" aria-label="Buscar facultad, programa, asignatura, grupo o docente"
                 placeholder="Buscar facultad, programa, asignatura, grupo o docente…"
                 [matAutocomplete]="filtroAuto" (input)="terminoFiltro.set(filtroInput.value)">
          <mat-autocomplete #filtroAuto="matAutocomplete" [displayWith]="sinTexto" panelWidth="440px"
                            (optionSelected)="agregarFiltro($event.option.value); filtroInput.value = ''; terminoFiltro.set('')">
            @for (g of opcionesFiltro(); track g.dim) {
              <mat-optgroup [label]="g.etiqueta">
                @for (o of g.opciones; track o.id) {
                  <mat-option [value]="{ dim: g.dim, id: o.id }"><span>{{ o.label }}</span><span class="fsub">{{ o.sub }}</span></mat-option>
                }
                @if (g.mas > 0) { <mat-option disabled>… y {{ g.mas }} más: escriba para acotar</mat-option> }
              </mat-optgroup>
            }
            @if (opcionesFiltro().length === 0) { <mat-option disabled>Sin resultados</mat-option> }
          </mat-autocomplete>
          @for (c of chipsFiltro(); track c.dim + c.id) {
            <span class="fchip"><small>{{ c.dimLabel }}</small>{{ c.label }}
              <button type="button" (click)="quitarFiltro(c)" [attr.aria-label]="'Quitar filtro ' + c.label">✕</button></span>
          }
          @if (hayFiltros()) {
            <div class="seg" role="radiogroup" aria-label="Qué hacer con las demás clases">
              <label class="seg-opt" role="radio" [attr.aria-checked]="!state.filtroAtenuar()" [class.on]="!state.filtroAtenuar()" (click)="state.filtroAtenuar.set(false)">Ocultar las demás</label>
              <label class="seg-opt" role="radio" [attr.aria-checked]="state.filtroAtenuar()" [class.on]="state.filtroAtenuar()" (click)="state.filtroAtenuar.set(true)">Atenuar las demás</label>
            </div>
            <span class="text-muted fcount">{{ clasesFiltradas() }} de {{ clasesTotales() }} clases</span>
            <button type="button" class="btn-link" (click)="limpiarFiltros()">Quitar filtros</button>
          }
        </div>
      }

      <!-- Una prueba que no encontró horario: el horario guardado y el aviso de la última generación
           real no cambian, así que su motivo se muestra aparte. -->
      @if (errorPrueba()) {
        <div class="fail">
          <div class="errb" role="alert"><b>✕ La prueba no encontró un horario</b> (el horario guardado no cambió). {{ errorPrueba() }}
            <button type="button" class="btn-link" (click)="errorPrueba.set('')">Cerrar</button></div>
        </div>
      }

      <!-- Estado C (HF-3): sin solución, haya o no un horario guardado. El registro técnico del backend
           (Fase 1/2/3, HC-…, ms) nunca se muestra en pantalla — solo se puede copiar para soporte.
           También con una prueba abierta: si falla «Guardar este horario», el motivo tiene que verse. -->
      @if (mensajeInfactible()) {
        <div class="fail">
          <app-generacion-fallida>
            @if (state.executionLogs().length > 0) {
              <button type="button" class="btn-link" (click)="copiarDetalleSoporte()">Copiar detalle para soporte</button>
            }
          </app-generacion-fallida>
        </div>
      }

      <!-- Horario guardado que ya no cumple la disponibilidad de los grupos (catálogo cambiado después de
           generarlo): antes solo lo decía un ⚠ plegado bajo la cuadrícula. -->
      @if (!enPrueba() && !mensajeInfactible() && fueraGrupo().length > 0) {
        <div class="fail">
          <div class="soft desact" role="status">
            <span><b>⚠ Este horario está desactualizado.</b> {{ fueraGrupo().length }} clase(s) quedaron fuera del horario
              disponible de su grupo, probablemente porque esa disponibilidad cambió después de generarlo. Vuelva a generarlo;
              el detalle está en los avisos bajo la cuadrícula.</span>
            <button class="btn btn-primary" (click)="generarHorario()" [disabled]="loadingBackend() || generandoHorario()">▶ Volver a generar</button>
          </div>
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
                  No hay sesiones{{ semanaVista() === 'B' ? ' que alternen' : '' }}.
                } @else if (modoVirtual()) {
                  No hay sesiones virtuales{{ semanaVista() === 'B' ? ' que alternen' : '' }}.
                } @else {
                  {{ activeSpace()?.nombre }} no tiene sesiones{{ semanaVista() === 'B' ? ' que alternen' : '' }}.
                }
              </p>
            }

            @if (seleccion(); as modo) {
              <div class="soft eligiendo-aviso" role="status">
                {{ modo === 'fijar' ? '📌 Haga clic en la clase que quiere fijar donde está.' : '✂ Haga clic en la clase que quiere sacar del horario.' }}
                Esc o un clic fuera de la cuadrícula cancela.
              </div>
            }
            <div class="wk-scroll" (click)="clicEnGrilla()">
              <div class="wk" [class.eligiendo]="seleccion()">
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
                      <button type="button" class="gcell" [class.gvirt]="t.m.virtual" [class.g1h]="t.g1h" [class.gdim]="t.m.atenuada"
                              [style.grid-row]="t.fila" [style.grid-column]="t.col"
                              [style.background]="gcellBg(t.m)" [style.border-left-color]="altColor(t.m)"
                              [title]="t.texto" [attr.aria-label]="t.texto" (click)="clicClase(t.m, $event)">
                        <span class="s">@if (tieneChoque(t.m)) { <span class="nodoc">⚠</span> }@if (t.m.virtual) { ⌁ }@if (t.m.contraparte) { <span class="galt" aria-hidden="true">⇄</span> }{{ getAsignaturaName(t.m) }}</span>
                        <span class="g">{{ grupoSuffix(t.m).slice(3) || '—' }}</span>
                        <span class="det">
                          <span [class.nodoc]="!t.m.docenteId">{{ t.m.docenteId ? getDocenteName(t.m) : 'sin docente' }}</span>
                          <span class="t">{{ celdaEspacio(t.m) }} · {{ t.m.horaInicio }}–{{ t.m.horaFin }}</span>
                          @if (t.m.contraparte; as otra) { <span class="gsub"><span aria-hidden="true">⇄</span> Próx. semana: {{ getAsignaturaName(otra) }}{{ grupoSuffix(otra) }}</span> }
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

              @if (avisosGlobalesVisibles() && choquesEspacio().length > 0) {
                <details class="soft fuera">
                  <summary><b>⚠ {{ choquesEspacio().length }} clase(s) en un espacio ya ocupado a esa hora</b></summary>
                  <ul>
                    @for (c of choquesEspacio(); track c.s.id) {
                      <li><button type="button" class="btn-link" (click)="abrirSesion(c.s)">{{ c.etiqueta }}</button> — {{ c.detalle }}</li>
                    }
                  </ul>
                </details>
              }

              @if (avisosGlobalesVisibles() && fueraGrupo().length > 0) {
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

              @if (avisosGlobalesVisibles() && (choquesDocente().length > 0 || fueraDeDisponibilidad() > 0)) {
                <details class="soft fuera">
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
                  <button mat-menu-item (click)="clicClase(m)" [title]="describir(m)">{{ getAsignaturaName(m) }}{{ grupoSuffix(m) }} · {{ m.horaInicio }}–{{ m.horaFin }}</button>
                }
              </ng-template>
            </mat-menu>
          </div>

          <!-- panel lateral (botón «Ajustes del horario»): pruebas sin guardar, cambios al horario guardado y puntos de partida -->
          @if (panelAbierto()) {
          <div class="side-col">
            <div class="blueprint side">
              <i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>
              <div class="pophd" style="font-size:13px">Ajustes del horario</div>
              <div class="side-bd">
                <div class="side-sec">Probar sin guardar</div>
                <p class="text-muted side-ayuda">Fije clases donde le convengan y pruebe: el resto se acomoda alrededor, sin tocar el horario guardado.</p>
                @for (c of clasesFijasVista(); track c.id) {
                  <div class="base-item" [class.on]="c.activa">
                    <label class="fija" [title]="c.texto">
                      <input type="checkbox" [checked]="c.activa" (change)="state.alternarClaseFija(c.id)" [attr.aria-label]="'Usar ' + c.texto">
                      <span class="base-info"><span class="base-name">{{ c.titulo }}</span><span class="text-muted base-meta">{{ c.detalle }}</span></span>
                    </label>
                    <button type="button" class="material-icons ic-del" (click)="state.quitarClaseFija(c.id)" [attr.aria-label]="'Quitar ' + c.texto">delete</button>
                  </div>
                }
                <button class="btn btn-secondary" [class.on]="seleccion() === 'fijar'" [attr.aria-pressed]="seleccion() === 'fijar'"
                        (click)="elegirEnGrilla('fijar', $event)" [disabled]="sesiones().length === 0">📌 Fijar una clase</button>
                <button class="btn btn-primary" (click)="probarHorario()" [disabled]="loadingBackend() || generandoHorario()"
                        title="Genera un horario con estas clases fijas sin reemplazar el guardado">🧪 Probar sin guardar</button>
                @if (state.sesiones().length > 0 && !enPrueba()) {
                  <button class="btn btn-secondary" (click)="guardarComoBase()"
                          title="Guarda el horario actual para generar los próximos respetando sus clases">Guardar como punto de partida</button>
                }
                @if (fijasActivas() > 0) {
                  <div class="okb" style="font-size:11.5px">✓ Al generar o probar se respetarán {{ fijasActivas() }} clase(s) fija(s).</div>
                }

                @if (state.sesiones().length > 0 && !enPrueba()) {
                  <div class="side-sec">Cambios al horario guardado</div>
                  <p class="text-muted side-ayuda">Estos cambios sí se guardan.</p>
                  <button class="btn btn-secondary" (click)="abrirCrearSesion()" [disabled]="loadingBackend()">＋ Sesión manual</button>
                  <button class="btn btn-secondary" [class.on]="seleccion() === 'liberar'" [attr.aria-pressed]="seleccion() === 'liberar'"
                          (click)="elegirEnGrilla('liberar', $event)" [disabled]="!state.horarioId() || loadingBackend()"
                          [title]="state.horarioId() ? 'Saca una clase del horario y deja libre su hora y su aula' : 'Solo se puede en el horario guardado, no en una copia abierta'">✂ Liberar espacio del horario</button>
                  @if (pendientesVista().length > 0) {
                    <div class="side-sub">Pendientes de ubicar ({{ totalPendientes() }})</div>
                    <div class="pend-lista">
                      @for (p of pendientesVista(); track p.clave) {
                        <div class="base-item">
                          <span class="base-info" style="flex:1;min-width:0"><span class="base-name">{{ p.titulo }}</span><span class="text-muted base-meta">{{ p.detalle }}</span></span>
                          <button type="button" class="btn-link" (click)="ubicarPendiente(p.p)" [attr.aria-label]="'Elegir hora para ' + p.titulo">Elegir hora</button>
                        </div>
                      }
                    </div>
                    <button class="btn btn-primary" (click)="buscarLugarPendientes()" [disabled]="loadingBackend() || generandoHorario()"
                            title="Prueba un horario que deja todas las demás clases donde están y solo busca hora y aula para las pendientes">🔎 Buscar lugar para las pendientes</button>
                  }
                }
              </div>
            </div>

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
    .fbusca { width: 340px; max-width: 100%; }
    .fsub { margin-left: 8px; font-size: 11px; color: var(--color-neutral-500); }
    .fchip { display: inline-flex; align-items: center; gap: 6px; padding: 3px 4px 3px 9px; font-size: 12.5px;
             border: 1px solid var(--color-accent); background: var(--color-accent-100); }
    .fchip small { font-size: 10px; text-transform: uppercase; letter-spacing: .06em; color: var(--color-neutral-700); }
    .fchip button { border: 0; background: none; cursor: pointer; padding: 0 4px; color: var(--color-neutral-700); font: inherit; }
    .fcount { font-size: 12px; }
    .prueba-bar { display: flex; flex-direction: column; gap: 8px; padding: 12px 22px; border-bottom: 1px solid var(--color-divider);
                    background: color-mix(in srgb, var(--color-accent) 8%, var(--color-bg)); font-size: 13px; }
    .prueba-acc { display: flex; gap: 10px; flex-wrap: wrap; }

    .fail { padding: 16px 22px 0; }
    .desact { display: flex; align-items: center; gap: 14px; flex-wrap: wrap; font-size: 13px; }
    .desact span { flex: 1; min-width: 280px; }

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
    .gdim { opacity: .28; filter: grayscale(1); }
    .gcell .galt { color: var(--color-accent); font-weight: 700; margin-right: 2px; }
    .gcell .gsub { margin-top: 2px; padding: 2px 4px; border: 1px dashed var(--color-neutral-500);
                   background: repeating-linear-gradient(-45deg, var(--color-bg) 0 5px, color-mix(in srgb, var(--color-accent) 12%, transparent) 5px 8px);
                   color: var(--color-neutral-700); font-size: 10px; line-height: 1.2; }
    .gmas { margin: 1px 0; border: 1px dashed var(--color-neutral-700); background: var(--color-neutral-100);
            font: 600 13px var(--font-heading); color: var(--color-neutral-800); cursor: pointer; }
    .gmas:focus-visible { outline: 2px solid var(--color-accent); outline-offset: 1px; }
    .grid-foot { text-align: center; font-size: 11px; margin-top: 8px; }

    .side-col { width: 250px; flex: none; display: flex; flex-direction: column; gap: 14px; }
    .side { background: var(--color-surface); }
    .side-ayuda { font-size: 11.5px; margin: 0; }
    .fija { flex: 1; display: flex; gap: 6px; align-items: flex-start; cursor: pointer; min-width: 0; }
    .fija .base-name { font-size: 12px; overflow-wrap: anywhere; }
    .side-bd { padding: 11px 13px; display: flex; flex-direction: column; gap: 8px; }
    .base-item { display: flex; align-items: center; gap: 6px; padding: 5px 7px; border: 1px solid var(--color-divider); }
    .base-item.on { border-color: var(--color-accent); background: var(--color-accent-100); }
    .base-info { display: flex; flex-direction: column; }
    .base-name { font-size: 13px; } .base-meta { font-size: 11px; }
    .btn.on { background: var(--color-accent-100); border-color: var(--color-accent); }
    .side-sec { font: 600 11px var(--font-heading); letter-spacing: .08em; text-transform: uppercase; color: var(--color-neutral-700); }
    .side-sec:not(:first-child) { margin-top: 8px; padding-top: 10px; border-top: 1px dashed var(--color-neutral-300); }
    .side-sub { font-size: 12px; font-weight: 600; margin-top: 4px; }
    .pend-lista { display: flex; flex-direction: column; gap: 6px; max-height: 260px; overflow-y: auto; }
    .pend-lista .base-name { font-size: 12px; overflow-wrap: anywhere; }

    /* Fijar / liberar: las clases pierden contraste y la que está bajo el puntero resalta. */
    .eligiendo-aviso { font-size: 12.5px; margin-bottom: 10px; }
    .wk.eligiendo .gcell { opacity: .4; cursor: crosshair; }
    .wk.eligiendo .gcell:hover, .wk.eligiendo .gcell:focus-visible { opacity: 1; filter: none; outline: 2px solid var(--color-accent); outline-offset: 1px; box-shadow: var(--shadow-md); }

    .empty-state { text-align: center; padding: 56px 32px; color: var(--color-neutral-600); }
  `]
})
export class HorarioComponent implements OnInit {
  state = inject(StateService);
  dialog = inject(MatDialog);
  snackBar = inject(MatSnackBar);
  horarioApi = inject(HorarioApiService);
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
  /** Guía accionable (sin jerga interna) del aviso persistente de infactibilidad — ver
   *  mensajeInfactibilidadAmigable(). Vive en StateService para que /revisar muestre lo mismo. */
  mensajeInfactible = this.state.errorGeneracion;
  /** Motivo (ya traducido) de la última prueba sin solución; no toca el estado de la generación real. */
  errorPrueba = signal('');

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

  /** Lo que pinta la vista: la prueba abierta o el horario real. */
  sesiones = computed(() => this.state.prueba() ?? this.state.sesiones());

  // ── Pruebas de horario (clases fijas + "Probar sin guardar") ─────────────────
  enPrueba = computed(() => this.state.prueba() !== null);
  fijasActivas = computed(() => this.state.clasesFijas().filter(c => c.activa).length);
  clasesFijasVista = computed(() => this.state.clasesFijas().map(({ sesion: s, activa }) => {
    const asig = this.state.asignaturaById().get(s.asignaturaId)?.nombre ?? '(asignatura eliminada)';
    const grupo = this.state.grupos().find(g => g.id === s.grupoId)?.nombre ?? '(grupo eliminado)';
    const espacio = s.virtual ? 'virtual' : (s.espacioId ? this.state.espacioById().get(s.espacioId)?.nombre ?? '' : 'cualquier aula');
    const dia = DIAS_GRILLA.find(d => d.valor === s.dia)?.etiqueta ?? s.dia;
    const titulo = grupo.startsWith(asig) ? grupo : `${asig} · ${grupo}`;
    const detalle = `${dia} ${s.horaInicio}–${s.horaFin} · ${espacio}`;
    return { id: s.id, activa, titulo, detalle, texto: `${titulo}, ${detalle}` };
  }));

  /** Fija una clase para las pruebas. Un grupo no puede fijar más clases de un tipo de las que tiene:
   *  la que sobra el servidor la sumaría como una clase extra. Conserva su pareja de alternancia: el
   *  servidor la rearma solo si las dos quedan fijas (si no, alterna sola nunca: vuelve a todas las
   *  semanas). Quitársela hacía que fijar las dos dejara dos clases en la misma aula y franja. */
  /** `origen`: la clase tal como estaba antes de moverla en «Editar» (ver ClaseFija.origen). */
  private fijar(s: Sesion, origen: Sesion = s) {
    const tope = this.state.topeClasesDelTipo(s);
    const mismas = this.state.clasesFijas().filter(c => claveTipoSesion(c.sesion) === claveTipoSesion(s));
    if (mismas.some(c => c.sesion.dia === s.dia && c.sesion.horaInicio === s.horaInicio)) {
      this.snackBar.open('Esa clase ya está fijada en esa franja.', 'Cerrar', { duration: 4000 }); return;
    }
    if (mismas.length >= tope) {
      this.snackBar.open(`Ese grupo ya tiene fijadas sus ${tope} clase(s) de ese tipo. Quite una en «Ajustes del horario» antes de fijar otra.`, 'Cerrar', { duration: 6000 }); return;
    }
    this.state.fijarClase({
      ...s, id: nuevoId(), semana: undefined, esContraparteVirtual: undefined,
      espacioIdHogar: undefined, motivoConflicto: undefined,
    }, { dia: origen.dia, horaInicio: origen.horaInicio });
    this.snackBar.open('Clase fijada. Pulse «Probar sin guardar» para ver cómo queda el resto.', 'Cerrar', { duration: 5000 });
  }

  // ── Panel «Ajustes del horario» y selección en la cuadrícula ─────────────────
  /** Cerrado por defecto; se abre con su botón, solo al abrir una prueba, y un clic en la cuadrícula lo cierra. */
  panelAbierto = signal(false);
  /** «Fijar una clase» / «Liberar espacio»: el próximo clic en una clase de la cuadrícula la elige. */
  seleccion = signal<'fijar' | 'liberar' | null>(null);
  alternarPanel() { this.panelAbierto.update(v => !v); this.seleccion.set(null); }

  /** stopPropagation: si el clic del botón llegara a document, onClicDocumento cancelaría la selección recién pedida. */
  elegirEnGrilla(modo: 'fijar' | 'liberar', ev: Event) {
    ev.stopPropagation();
    this.seleccion.update(v => v === modo ? null : modo);
  }

  /** Un clic en la cuadrícula cierra el panel, salvo eligiendo una clase o con una prueba abierta
   *  (ahí cada clic fija una clase y la lista del panel hace falta). */
  clicEnGrilla() { if (!this.seleccion() && !this.enPrueba()) this.panelAbierto.set(false); }

  /** Clic fuera de la cuadrícula (o de su menú «+N») cancela la selección. */
  @HostListener('document:click', ['$event'])
  onClicDocumento(ev: MouseEvent) {
    if (this.seleccion() && !(ev.target as Element | null)?.closest?.('.wk, .cdk-overlay-container')) this.seleccion.set(null);
  }
  @HostListener('document:keydown.escape') cancelarSeleccion() { this.seleccion.set(null); }

  /** Clic en una tarjeta (ev) o en el menú «+N» (sin ev: cortar la propagación impediría que el menú se cierre). */
  clicClase(m: MergedSesion, ev?: Event) {
    const modo = this.seleccion();
    if (!modo) { this.abrirEditarSesion(m); return; }
    ev?.stopPropagation(); // el panel sigue abierto para ver la clase fijada o la pendiente
    this.seleccion.set(null);
    if (modo === 'fijar') this.fijar(m.sesiones[0]);
    else this.liberar(m.sesiones[0]);
  }

  private liberar(s: Sesion) {
    const horarioId = this.state.horarioId();
    if (!horarioId) return;
    const nombre = describirSesionConflicto(s, this.state.asignaturas(), this.state.grupos(), this.state.programaById());
    const ref = this.dialog.open(ConfirmDeleteDialogComponent, {
      width: '380px',
      data: {
        title: 'Liberar espacio del horario',
        message: `"${nombre}" saldrá del horario guardado y quedará pendiente de ubicar.` +
          (s.parejaId ? ' Esta clase alterna con otra: las dos quedarán pendientes.' : ''),
        confirmText: 'Liberar'
      }
    });
    ref.afterClosed().subscribe(ok => {
      if (!ok) return;
      this.loadingBackend.set(true);
      this.horarioApi.liberarSesion(horarioId, s.id).subscribe({
        next: ids => {
          this.loadingBackend.set(false);
          this.state.sesiones.update(prev => prev.filter(x => !ids.includes(x.id)));
          this.panelAbierto.set(true);
          this.snackBar.open(`${ids.length > 1 ? 'Clases liberadas' : 'Clase liberada'}: quedó pendiente de ubicar en «Ajustes del horario».`, 'Cerrar', { duration: 5000 });
        },
        error: (err: any) => {
          this.loadingBackend.set(false);
          this.snackBar.open(`No se pudo liberar la clase: ${err?.mensajeError ?? mensajeErrorHttp(err)}`, 'Cerrar', { duration: 6000, panelClass: ['snack-error'] });
        }
      });
    });
  }

  // ── Pendientes de ubicar ────────────────────────────────────────────────────
  private pendientes = computed(() => this.state.sesiones().length === 0 ? []
    : clasesPendientes(this.state.sesiones(), this.state.grupos(), this.state.asignaturaById()));
  totalPendientes = computed(() => this.pendientes().reduce((n, p) => n + p.faltan, 0));
  pendientesVista = computed(() => this.pendientes().map(p => {
    const asig = this.state.asignaturaById().get(p.asignaturaId)?.nombre ?? '';
    const grupo = this.state.grupos().find(g => g.id === p.grupoId)?.nombre ?? '';
    return {
      p, clave: `${p.grupoId}|${p.tipo}`, titulo: grupo.startsWith(asig) ? grupo : `${asig} · ${grupo}`,
      detalle: `${ETIQUETA_TIPO_SESION[p.tipo]} · ${p.horas} h${p.faltan > 1 ? ` · faltan ${p.faltan}` : ''}`,
    };
  }));

  /** Opción 1: hora a mano, con el diálogo de sesión manual ya en ese grupo y tipo (valida y guarda). */
  ubicarPendiente(p: ClasePendiente) { this.abrirCrearSesion({ asignaturaId: p.asignaturaId, grupoId: p.grupoId, tipo: p.tipo }); }

  /** Opción 2: prueba con TODAS las clases actuales fijas — el generador solo completa lo que le falta a
   *  cada grupo, es decir, las pendientes. Las clases fijas del panel se suman (fijasSobre); el punto
   *  de partida no: el horario guardado ya hace ese papel. */
  buscarLugarPendientes() { this.probarSobre(this.state.sesiones().filter(s => !s.esContraparteVirtual)); }

  /** Horario de partida de la prueba abierta (solo «Buscar lugar…»): «Probar otra vez» lo vuelve a
   *  fijar junto con las clases fijas actuales, incluidas las que se fijaron desde la prueba. */
  private fijasDeLaPrueba?: Sesion[];
  probarHorario() { this.ejecutarGeneracion(true); }
  probarOtraVez() { if (this.fijasDeLaPrueba) this.probarSobre(this.fijasDeLaPrueba); else this.probarHorario(); }
  private probarSobre(snapshot: Sesion[]) { this.ejecutarGeneracion(true, this.state.fijasSobre(snapshot), snapshot); }
  descartarPrueba() { this.state.prueba.set(null); this.errorPrueba.set(''); }

  /** Guarda la prueba tal cual: se regenera con TODAS sus clases fijas (restricciones de igualdad),
   *  así el horario guardado es exactamente el que se vio. */
  guardarPrueba() {
    const prueba = this.state.prueba();
    if (!prueba) return;
    const ref = this.dialog.open(ConfirmDeleteDialogComponent, {
      width: '360px',
      data: { title: 'Guardar este horario', message: 'La prueba reemplazará al horario guardado.', confirmText: 'Guardar' }
    });
    ref.afterClosed().subscribe(ok => { if (ok) this.ejecutarGeneracion(false, prueba.filter(s => !s.esContraparteVirtual)); });
  }


  // ── Filtros por facetas (Facultad, Programa, Asignatura, Grupo, Docente) ──────
  // Un solo buscador agrega chips de cualquier dimensión: dentro de una dimensión basta con
  // coincidir con uno (dos grupos = ambos), entre dimensiones con todas — ver pasaFiltrosHorario.
  // Cada opción dice cuántas clases dejaría a la vista; las que vaciarían la cuadrícula no se ofrecen.
  private readonly ETIQUETA_DIM: Record<DimFiltro, [string, string]> = {
    facultad: ['Facultades', 'Facultad'], programa: ['Programas', 'Programa'], asignatura: ['Asignaturas', 'Asignatura'],
    grupo: ['Grupos', 'Grupo'], docente: ['Docentes', 'Docente'],
  };
  private readonly MAX_OPCIONES = 40;
  terminoFiltro = signal('');
  sinTexto = () => '';
  hayFiltros = computed(() => this.state.filtrosHorario().length > 0);

  /** Facultad, programa… de cada sesión, por id (la contraparte virtual comparte id y claves). */
  private clavesPorSesion = computed(() => {
    const asig = this.state.asignaturaById(), progs = this.state.programaById();
    return new Map(this.sesiones().map(s => [s.id, clavesDeSesion(s, asig, progs)]));
  });
  private clasesReales = computed(() => this.sesiones().filter(s => !s.esContraparteVirtual));
  clasesTotales = computed(() => this.clasesReales().length);
  clasesFiltradas = computed(() => this.clasesReales().filter(this.pasaFiltros()).length);

  private pasaFiltros = computed(() => {
    const filtros = this.state.filtrosHorario(), claves = this.clavesPorSesion();
    return (s: Sesion) => !filtros.length || pasaFiltrosHorario(claves.get(s.id) ?? {}, filtros);
  });

  /** Nombre y contexto (sub) de una opción, o undefined si ya no existe en el catálogo. */
  private describirOpcion(dim: DimFiltro, id: string): { label: string; sub: string } | undefined {
    const st = this.state;
    const programaDe = (programaId?: string) => programaId ? st.programaById().get(programaId)?.nombre ?? '' : '';
    switch (dim) {
      case 'facultad': { const f = st.facultadById().get(id); return f && { label: f.nombre, sub: '' }; }
      case 'programa': { const p = st.programaById().get(id); return p && { label: p.nombre, sub: st.facultadById().get(p.facultadId)?.nombre ?? '' }; }
      case 'asignatura': { const a = st.asignaturaById().get(id); return a && { label: a.nombre, sub: programaDe(a.programaId) }; }
      case 'grupo': {
        const g = st.grupos().find(x => x.id === id); if (!g) return undefined;
        // El programa distingue "BIOQUIMICA - Grupo 1" de Medicina del de Biología (el import repite el nombre).
        const a = st.asignaturaById().get(g.asignaturaId);
        return { label: g.nombre, sub: [a && !g.nombre.startsWith(a.nombre) ? a.nombre : '', programaDe(a?.programaId)].filter(Boolean).join(' · ') };
      }
      case 'docente': { const d = st.docenteById().get(id); return d && { label: d.nombre, sub: '' }; }
    }
  }

  opcionesFiltro = computed(() => {
    const filtros = this.state.filtrosHorario();
    const elegidos = new Set(filtros.map(f => `${f.dim}:${f.id}`));
    const claves = this.clasesReales().map(s => this.clavesPorSesion().get(s.id) ?? {});
    const conteos = conteosFiltro(claves, filtros);
    // Sin tildes ni mayúsculas: "biologia" encuentra "BIOLOGÍA".
    const normalizar = (t: string) => t.normalize('NFD').replace(/\p{M}/gu, '').toLocaleLowerCase('es');
    const termino = normalizar(this.terminoFiltro().trim());
    return DIMS_FILTRO.map(dim => {
      // Con un chip de esta misma dimensión la opción SUMA clases (uno u otro): se muestra "+N".
      const suma = filtros.some(f => f.dim === dim) ? '+' : '';
      const todas = [...conteos[dim]]
        .filter(([id]) => !elegidos.has(`${dim}:${id}`))
        .flatMap(([id, n]) => { const d = this.describirOpcion(dim, id); return d ? [{ id, n, ...d }] : []; })
        .filter(o => !termino || normalizar(`${o.label} ${o.sub}`).includes(termino))
        .sort((a, b) => porTexto(a.label, b.label) || porTexto(a.sub, b.sub))
        .map(o => ({ id: o.id, label: o.label, sub: [o.sub, `${suma}${o.n} clase${o.n === 1 ? '' : 's'}`].filter(Boolean).join(' · ') }));
      return { dim, etiqueta: this.ETIQUETA_DIM[dim][0], opciones: todas.slice(0, this.MAX_OPCIONES), mas: Math.max(0, todas.length - this.MAX_OPCIONES) };
    }).filter(g => g.opciones.length > 0);
  });

  /** Asignaturas y grupos se repiten entre programas: el chip lleva el contexto para distinguirlos. */
  chipsFiltro = computed(() => this.state.filtrosHorario().map(f => {
    const d = this.describirOpcion(f.dim, f.id);
    const conContexto = (f.dim === 'asignatura' || f.dim === 'grupo') && d?.sub;
    return { ...f, dimLabel: this.ETIQUETA_DIM[f.dim][1], label: d ? (conContexto ? `${d.label} · ${d.sub}` : d.label) : '(ya no existe)' };
  }));

  /** Al filtrar se pasa a "Todos los espacios": ver el horario de un grupo o un docente aula por
   *  aula no sirve de mucho. */
  agregarFiltro(f: FiltroHorario) {
    if (!this.state.filtrosHorario().some(x => x.dim === f.dim && x.id === f.id)) this.state.filtrosHorario.update(v => [...v, f]);
    this.selectTodos();
  }
  quitarFiltro(f: FiltroHorario) { this.state.filtrosHorario.update(v => v.filter(x => x.dim !== f.dim || x.id !== f.id)); }
  limpiarFiltros() { this.state.filtrosHorario.set([]); }

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

  /** Hay alternancia activa cuando alguna sesión tiene pareja. Sin ella no hay "semana siguiente"
   *  distinta y el selector ni siquiera se muestra: el horario completo es todo. */
  hayAlternancia = computed(() => this.sesiones().some(s => !!s.parejaId));

  /** La semana efectivamente mostrada: si ya no hay alternancia (p. ej. tras regenerar sin
   *  parejas) no se queda pegada en B sin selector visible para volver a A. */
  semanaVista = computed<'A' | 'B'>(() => this.hayAlternancia() ? this.activeWeek() : 'A');

  /** Horario completo: todo lo que no es exclusivamente de la semana siguiente (lo que no alterna
   *  y cada pareja con el miembro que usa el aula esta semana). Semana siguiente: SOLO las parejas
   *  que alternan — el resto de la cuadrícula queda vacío a propósito (es el mismo horario).
   *  Una TipoA/TipoB SIN pareja (laboratorio que heredó la alternancia de su asignatura, o datos
   *  viejos) no tiene "semana siguiente": se muestra siempre en el horario completo, sin importar
   *  en qué semana caiga su fila; si no, una TipoB suelta desaparecía de la cuadrícula. */
  private sesionVisibleEnSemana(s: Sesion): boolean {
    const deLaSiguiente = s.semana === 'B' && !!s.parejaId;
    return this.semanaVista() === 'B' ? deLaSiguiente : !deLaSiguiente;
  }

  /** Sesiones visibles en el chip/semana actual, ya plegadas con su contraparte de alternancia.
   *  Reemplaza el mapa por celda de inicio (mergedByCell/coveredCells) de la tabla anterior. */
  sesionesVista = computed<MergedSesion[]>(() => {
    // "Todos los espacios" = sin spaceId: sesionPerteneceAlEspacio deja pasar todo, virtuales incluidas.
    const spaceId = this.todosEspacios() ? undefined : this.modoVirtual() ? this.ESPACIO_VIRTUAL : this.activeSpace()?.id;
    const filtra = this.pasaFiltros();
    // Atenuar: nada se oculta, lo que no coincide se pinta en gris (deja ver los huecos libres).
    const atenuar = this.state.filtroAtenuar();
    const pasa = atenuar ? () => true : filtra;
    // Los filtros (grupo, docente…) deciden qué tarjetas se ven, no con quién alterna cada una: la
    // pareja suele ser de otro grupo, y filtrarla antes del pliegue borraba el ⇄ de la tarjeta.
    const enVista = this.sesiones().filter(s => this.sesionPerteneceAlEspacio(s, spaceId) && this.sesionVisibleEnSemana(s));
    const visibles = enVista.filter(pasa);

    // La contraparte virtual se pliega como sub-caja DENTRO de la tarjeta presencial solo si
    // coincide exactamente en pareja + día + hora con una presencial visible — antes se indexaba
    // solo por parejaId (un Map que sobrescribía si había más de una franja) y una contraparte sin
    // presencial visible se descartaba sin más (`continue`), perdiéndola de la grilla sin aviso.
    const clave = (s: { parejaId?: string; dia: string; horaInicio: string; horaFin: string }) =>
      `${s.parejaId}|${s.dia}|${s.horaInicio}|${s.horaFin}`;
    const contrapartes = new Map<string, Sesion>();
    for (const s of enVista) if (s.esContraparteVirtual && s.parejaId) contrapartes.set(clave(s), s);

    const plegadas = new Set<string>();
    const resultado: MergedSesion[] = [];
    for (const s of visibles) {
      if (s.esContraparteVirtual) continue;
      const merged = this.aMerged(s);
      merged.atenuada = atenuar && !filtra(s);
      const k = s.parejaId ? clave(s) : undefined;
      const otra = k ? contrapartes.get(k) : undefined;
      if (otra) { merged.contraparte = this.aMerged(otra); plegadas.add(k!); }
      resultado.push(merged);
    }
    // Contrapartes que no se plegaron (su presencial no está visible en esta vista/semana, o no
    // coincidió exactamente en franja): tarjeta propia en vez de perderse.
    for (const [k, s] of contrapartes) if (!plegadas.has(k) && pasa(s)) resultado.push({ ...this.aMerged(s), atenuada: atenuar && !filtra(s) });
    return resultado;
  });

  /** Presenciales sin aula ni aula de origen: `sesionPerteneceAlEspacio` las excluye de TODOS los
   *  chips de espacio físico (no pertenecen a ninguno), así que sin este parche desaparecían de
   *  la grilla sin aviso. grilla() las suma a la lista "fuera" de cualquier vista de aula. */
  private sesionesSinAula = computed<Sesion[]>(() => {
    const pasa = this.state.filtroAtenuar() ? () => true : this.pasaFiltros();
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
          g1h: (item.fila1 - item.fila0) < 2,
          texto: this.describir(item.m),
        };
      });
      dias[idx].mas = mas.map(grupo => ({
        fila: `${grupo.ini / 60 - HORA_APERTURA + 1} / ${grupo.fin / 60 - HORA_APERTURA + 1}`,
        items: grupo.items.map(x => x.m).sort((a, b) => porTexto(this.getAsignaturaName(a), this.getAsignaturaName(b))),
        texto: `${grupo.items.length} sesiones más en ese horario — clic para verlas`,
      }));
    }
    fuera.sort((a, b) => porTexto(a.texto, b.texto));
    return { dias, fuera, total };
  });

  /** Descripción completa de una sesión — tooltip y aria-label de su tarjeta, y texto de la lista
   *  "fuera de la cuadrícula". No se abrevia: es la única forma de leer el dato completo, ya que
   *  la tarjeta trunca según el alto disponible. */
  describir(m: MergedSesion): string {
    const base = describirSesionConflicto(m, this.state.asignaturas(), this.state.grupos(), this.state.programaById());
    const ctx = this.getContextLabel(m);
    const docente = m.docenteId ? this.getDocenteName(m) : 'sin docente';
    const espacio = this.celdaEspacio(m);
    const contraparte = m.contraparte ? ` · alterna con ${this.getAsignaturaName(m.contraparte)}${this.grupoSuffix(m.contraparte)}: la próxima semana este espacio es suyo y esta clase pasa a virtual` : '';
    const choque = this.tieneChoque(m) ? ' · ⚠ el docente tiene otra clase a la misma hora' : '';
    const choqueAula = this.tieneChoqueEspacio(m) ? ' · ⚠ el espacio ya está ocupado a esa hora' : '';
    const accion = this.enPrueba() ? 'Clic para fijarla en esta franja' : 'Clic para editar';
    return `${base}${ctx ? ' · ' + ctx : ''} · ${docente} · ${espacio}${contraparte}${choque}${choqueAula}. ${accion}.`;
  }

  /** Los avisos de debajo de la cuadrícula cuentan TODO el horario: en la vista "semana siguiente"
   *  (solo lo que alterna) confundirían, así que solo se muestran en el horario completo. */
  avisosGlobalesVisibles = computed(() => this.semanaVista() === 'A');
  /** Avisos del docente (no bloquean) de todo el horario — ver avisosDocentePorSesion. */
  avisosDocente = computed(() =>
    avisosDocentePorSesion(this.sesiones(), this.state.docentes(), this.state.asignaturas(), this.state.grupos(), this.state.programaById()));
  /** Clases con el docente en dos clases a la vez: se listan bajo la cuadrícula para poder abrirlas. */
  choquesDocente = computed(() => filasDeChoques(this.sesiones(), this.avisosDocente(), this.state.asignaturas(), this.state.grupos(), this.state.programaById()));
  /** Dos clases en el mismo espacio a la vez — ver choquesEspacioPorSesion. */
  choquesEspacioMap = computed(() =>
    choquesEspacioPorSesion(this.sesiones(), this.state.espacios(), this.state.asignaturas(), this.state.grupos(), this.state.programaById()));
  choquesEspacio = computed(() => filasDeChoques(this.sesiones(), this.choquesEspacioMap(), this.state.asignaturas(), this.state.grupos(), this.state.programaById()));
  /** Clases fuera de la disponibilidad de su grupo — ver fueraDisponibilidadGrupo. */
  fueraGrupo = computed(() => {
    const motivos = fueraDisponibilidadGrupo(this.sesiones(), this.state.grupos());
    return this.sesiones()
      .filter(s => motivos.has(s.id) && !s.esContraparteVirtual)
      .map(s => ({ s, etiqueta: describirSesionConflicto(s, this.state.asignaturas(), this.state.grupos(), this.state.programaById()), motivo: motivos.get(s.id)! }))
      .sort((a, b) => porTexto(a.etiqueta, b.etiqueta));
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
    // En una prueba nada se edita: clic = fijar esa clase donde quedó, para la próxima prueba.
    if (this.enPrueba()) {
      const s = merged.sesiones[0];
      const ref = this.dialog.open(ConfirmDeleteDialogComponent, {
        width: '340px',
        data: { title: 'Fijar esta clase', message: `"${describirSesionConflicto(s, this.state.asignaturas(), this.state.grupos(), this.state.programaById())}" quedará fija en esa franja en las próximas pruebas.`, confirmText: 'Fijar' }
      });
      ref.afterClosed().subscribe(ok => { if (ok) this.fijar(s); });
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
      if (result.fijar) { this.fijar(result.fijar, merged.sesiones[0]); return; }
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

  generarHorario() { this.ejecutarGeneracion(false); }

  /** `soloPrueba`: el servidor no guarda nada y el resultado se muestra como prueba. `fijas`: en lugar
   *  del punto de partida y las clases fijas (lo usa guardarPrueba para guardar la prueba tal cual).
   *  `snapshot`: horario de partida que «Probar otra vez» debe volver a fijar (ver fijasDeLaPrueba). */
  private ejecutarGeneracion(soloPrueba: boolean, fijas?: Sesion[], snapshot?: Sesion[]) {
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
    const sesionesFijas = fijas ?? this.state.sesionesFijasParaGenerar();
    const base: HorarioBase | undefined = sesionesFijas.length ? { id: 'fijas', nombre: 'Clases fijas', creadoEn: '', sesiones: sesionesFijas } : undefined;
    this.horarioApi.generarHorario(asignaturas, this.state.docentes(), this.state.espacios(), undefined, this.state.semestre(), base, this.state.grupos(),
      { tipoEspacioAlternancia: this.state.tipoEspacioAlternancia(), soloPrueba })
      .subscribe({
        next: (respuesta) => {
          this.generandoHorario.set(false);
          dialogRef.close();
          const sesiones = this.horarioApi.mapearSesiones(respuesta.sesiones);
          const omitidas = respuesta.sesionesFijasOmitidas ? ` ${respuesta.sesionesFijasOmitidas} clase(s) fija(s) no se pudieron respetar (su grupo o asignatura ya no existe, o no cabe en la jornada).` : '';
          this.errorPrueba.set('');
          // Una prueba no cambia el horario guardado: el aviso de la última generación real se queda.
          if (soloPrueba) {
            this.state.prueba.set(sesiones);
            this.fijasDeLaPrueba = snapshot;
            this.panelAbierto.set(true);
            this.selectTodos();
            this.snackBar.open(`Prueba lista: ${sesiones.length} clases. No se guardó nada.${omitidas}`, 'Cerrar', { duration: 7000 });
            return;
          }
          this.state.setGruposEnConflicto([]);
          this.state.setMotivoInfactibilidad(undefined);
          this.mensajeInfactible.set('');
          this.state.prueba.set(null);
          this.state.setSesiones(sesiones);
          this.state.setExecutionLogs(respuesta.logs || []);
          this.state.horarioId.set(respuesta.horarioId);
          const choques = this.choquesDocente().length;
          const aviso = choques ? ` Atención: ${choques} clase(s) tienen al docente en dos clases a la vez — vea los avisos bajo la cuadrícula.` : '';
          this.snackBar.open(`Horario generado con ${sesiones.length} clases.${aviso}${omitidas}`, 'Cerrar', { duration: choques || omitidas ? 10000 : 6000 });
        },
        error: (err: any) => {
          this.generandoHorario.set(false);
          dialogRef.close();
          // Un 422 (GenerarHorarioResponse) pasa tal cual desde manejarError — mensajeError no
          // pasó por mensajeErrorHttp, así que puede traer el código de regla crudo (HC-SEP…).
          const mensaje = limpiarEtiquetaInterna(err.mensajeError || err.message || err.error || 'Error desconocido');
          const gruposEnConflicto = Array.isArray(err.gruposEnConflicto) ? err.gruposEnConflicto : [];
          // Antes solo se traducía si el mensaje crudo contenía "factible"/"infeasible" — un fallo
          // distinto (p. ej. una violación de reglas post-GA) se colaba tal cual, con jerga interna.
          // Con motivo o grupos estructurados ya hay suficiente señal para traducir igual.
          const esInfeasible = !!err.motivoInfactibilidad || gruposEnConflicto.length > 0 || /factible|infeasible/i.test(mensaje);
          const texto = esInfeasible
            ? mensajeInfactibilidadAmigable(err.motivoInfactibilidad, gruposEnConflicto, this.state.grupos(), asignaturas.length, this.state.espacios().length, this.state.contextoCapacidad())
            : mensaje;
          // Una prueba fallida no es una generación fallida: el horario guardado sigue igual, así que
          // no marca grupos en conflicto ni cambia el aviso que comparten /horario y /revisar.
          if (soloPrueba) {
            this.errorPrueba.set(texto);
            this.snackBar.open('La prueba no encontró un horario. Vea el motivo en el aviso de arriba.', 'Cerrar', { duration: 6000, panelClass: ['snack-error'] });
            return;
          }
          if (err.logs && Array.isArray(err.logs)) this.state.setExecutionLogs(err.logs);
          this.state.setGruposEnConflicto(gruposEnConflicto);
          this.state.setMotivoInfactibilidad(err.motivoInfactibilidad);
          this.state.espacioLimitanteId.set(err.espacioLimitanteId);
          // El banner persistente de /horario (a diferencia del snackbar) sigue visible hasta la
          // próxima generación — siempre con el mismo texto limpio, nunca vacío ni con jerga.
          this.mensajeInfactible.set(texto);
          // El detalle (a veces una lista larga de grupos) queda en el aviso persistente de arriba.
          this.snackBar.open('No se pudo generar el horario. Vea el detalle en el aviso de arriba.', 'Cerrar', { duration: 6000, panelClass: ['snack-error'] });
        }
      });
  }

  abrirCrearSesion(prefill?: CrearSesionDialogData['prefill']) {
    const ref = this.dialog.open(CrearSesionDialogComponent, {
      width: '300px', maxHeight: '92vh',
      data: { asignaturas: this.state.asignaturas(), docentes: this.state.docentes(), espacios: this.state.espacios(), grupos: this.state.grupos(), sesiones: this.state.sesiones(), programaById: this.state.programaById(), prefill } satisfies CrearSesionDialogData
    });
    ref.afterClosed().subscribe((nuevas: Sesion[] | undefined) => {
      if (!nuevas?.length) return;
      this.state.sesiones.update(prev => [...prev, ...nuevas]);
      // "N filas" era jerga interna del modelo de datos: al coordinador solo le importa que la
      // sesión quedó creada.
      this.snackBar.open('Sesión creada.', 'Cerrar', { duration: 5000 });
    });
  }
}
