import { Component, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { StateService } from '../../core/state.service';
import { PersistenciaService } from '../../core/persistencia.service';
import { Asignatura, Docente, Espacio, Grupo, Sesion, TipoSesionUi, tipoFlujoDesde, esVirtualDesde } from '../../core/models';
import { nuevoId } from '../../core/id.util';
import { HORA_CIERRE_SABADO } from '../../core/jornada';
import { mensajeErrorHttp } from '../../core/http-error.util';
import { SearchableSelectComponent, SearchableOption } from '../../shared/searchable-select/searchable-select.component';
import { porNombre } from '../../core/orden';
import { describirSesionConflicto, hhmm, HORAS_GRILLA, DIAS_GRILLA, seSolapanHorarios, nuncaCoexisteEnSemana, finDeJornadaOk, espaciosPermitidosPara, avisosDocentePorSesion, Check } from './horario.reglas';

// ═══ Diálogo: Crear sesión manual (REQUISITOS §2) ═══
/** `modoFija`: la sesión va a un horario base (se devuelve a quien abrió el diálogo) en vez de crearse en el servidor.
 *  `modoBorrador`: igual, pero va al borrador; los choques avisan en vez de bloquear y el docente se puede cambiar. */
export interface CrearSesionDialogData { asignaturas: Asignatura[]; docentes: Docente[]; espacios: Espacio[]; grupos: Grupo[]; sesiones: Sesion[]; programaById: Map<string, { id: string; nombre: string }>; modoFija?: boolean; modoBorrador?: boolean; }

@Component({
  selector: 'app-crear-sesion-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatSnackBarModule, SearchableSelectComponent],
  template: `
    <div class="pophd">{{ modoFija ? 'Fijar clase antes de generar' : modoBorrador ? 'Agregar clase al borrador' : 'Agregar clase' }} <button type="button" class="pop-close" (click)="cancelar()" aria-label="Cerrar">✕</button></div>
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
        @if (modoBorrador) {
          <div class="dfield"><label>Docente</label>
            <app-searchable-select [ngModel]="docenteActual()" (ngModelChange)="docenteElegido = $event; recheck()"
                                    [options]="docenteOptions" placeholder="— Sin docente —"></app-searchable-select>
          </div>
          <div class="text-muted" style="font-size:12px">⏱ {{ duracionSeleccionada() }}h por sesión (fijo)</div>
        } @else {
          <div class="text-muted" style="font-size:12px">👤 {{ docenteDelGrupo() }} · ⏱ {{ duracionSeleccionada() }}h por sesión (fijo)</div>
        }
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


      @if (asignaturaId && dia && horaInicio && (espacioId || tipoSesion() === 'TeoriaVirtual')) {
        <!-- En el borrador un choque no bloquea: se avisa y la clase se puede agregar igual. -->
        @for (c of checks(); track c.texto) { <div [class]="c.ok ? 'okb' : modoBorrador ? 'soft' : 'errb'">{{ c.ok ? '✓' : modoBorrador ? '⚠' : '✕' }} {{ c.texto }}</div> }
        @for (w of avisos(); track w) { <div class="soft"><b>⚠ Aviso (no bloquea):</b> {{ w }}</div> }
      }
      @if (errorServidor()) { <div class="errb"><b>✕ {{ errorServidor() }}</b></div> }
      @if (modoFija) {
        <p class="text-muted" style="font-size:11.5px;margin:0;border-top:1px dashed var(--color-neutral-300);padding-top:8px">Esta clase quedará fija; al generar, el resto del horario se acomoda alrededor.</p>
      }

      <div class="popfoot">
        <button class="btn btn-secondary" (click)="cancelar()" [disabled]="guardando()">Cancelar</button>
        <button class="btn btn-primary" [disabled]="!puedeCrear() || guardando()" (click)="crear()">{{ guardando() ? 'Creando…' : modoFija ? 'Fijar sesión' : modoBorrador ? 'Agregar al borrador' : 'Crear' }}</button>
      </div>
    </div>
  `
})
export class CrearSesionDialogComponent {
  private dialogRef = inject(MatDialogRef<CrearSesionDialogComponent>);
  private data: CrearSesionDialogData = inject(MAT_DIALOG_DATA);
  private persistencia = inject(PersistenciaService);
  private state = inject(StateService);
  readonly modoFija = !!this.data.modoFija;
  readonly modoBorrador = !!this.data.modoBorrador;
  /** Solo en el borrador: docente elegido a mano. null = el del grupo; '' = sin docente. */
  docenteElegido: string | null = null;
  readonly docenteOptions: SearchableOption[] = [{ value: '', label: 'Sin docente' },
    ...[...this.data.docentes].sort(porNombre).map(d => ({ value: d.id, label: d.nombre }))];
  docenteActual(): string { return this.docenteElegido ?? this.docenteIdDelGrupo(); }

  asignaturaId = ''; dia = ''; horaInicio = ''; espacioId = '';
  /** Una clase agregada a mano nunca alterna: emparejar es cosa del generador (regla 10). */
  readonly alternancia = 'SinAlternancia' as const;
  guardando = signal(false); errorServidor = signal('');

  readonly dias = DIAS_GRILLA;
  readonly horasDisponibles = HORAS_GRILLA;

  asignaturaSeleccionada = signal<Asignatura | undefined>(undefined);
  tipoSesion = signal<TipoSesionUi>('Laboratorio');
  espacioFijoBloqueado = signal(false);
  checks = signal<Check[]>([]);
  checksOk = signal(false);
  /** Disponibilidad del docente del grupo (no bloquea) — ver avisosDocentePorSesion. El choque de
   *  docente no va aquí: una clase nueva que lo provoca ya se bloquea en checks. */
  avisos = signal<string[]>([]);

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
  onGrupoChange() { this.grupoIdSel.set(this.grupoId); this.espacioId = ''; this.docenteElegido = null; this.recheck(); }

  tiposDisponibles = computed<{ tipo: TipoSesionUi; label: string }[]>(() => {
    const a = this.asignaturaSeleccionada();
    if (!a) return [];
    const list: { tipo: TipoSesionUi; label: string }[] = [];
    if (a.sesionesTeoriaPresencialSemana > 0) list.push({ tipo: 'TeoriaPresencial', label: 'Teoría presencial' });
    if (a.sesionesTeoriaVirtualSemana > 0) list.push({ tipo: 'TeoriaVirtual', label: 'Teoría virtual' });
    if (a.sesionesLaboratorioSemana > 0) list.push({ tipo: 'Laboratorio', label: 'Laboratorio' });
    return list;
  });

  duracionSeleccionada = computed(() => {
    const a = this.asignaturaSeleccionada();
    if (!a) return 2;
    switch (this.tipoSesion()) { case 'TeoriaVirtual': return a.horasTeoriaVirtual; case 'Laboratorio': return a.horasLaboratorio; default: return a.horasTeoriaPresencial; }
  });

  // FE17: espejo (cliente) de CalculadorEspaciosSesion.Candidatos — ahora la función de módulo
  // compartida espaciosPermitidosPara, también usada por EditarSesionDialogComponent.
  espaciosDisponibles = computed(() =>
    espaciosPermitidosPara(this.tipoSesion(), this.data.grupos.find(g => g.id === this.grupoIdSel()), this.data.espacios));

  /** Método, no computed(): lee campos planos (asignaturaId, dia…). Como computed, su primera evaluación
   *  cortaba en `!!this.asignaturaId` sin leer ningún signal, quedaba sin dependencias y congelado en
   *  false — el botón Crear/Fijar nunca se habilitaba. */
  puedeCrear(): boolean {
    return !!this.asignaturaId && !!this.grupoIdSel() && !!this.dia && !!this.horaInicio && (!!this.espacioId || this.tipoSesion() === 'TeoriaVirtual') && this.checksOk() && !this.guardando();
  }

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
    this.espacioFijoBloqueado.set(false);
    if (tipo === 'TeoriaVirtual') this.espacioId = '';
  }

  recheck() {
    const a = this.asignaturaSeleccionada(), dur = this.duracionSeleccionada(), esVirtual = this.tipoSesion() === 'TeoriaVirtual';
    const chks: Check[] = []; let ok = true;
    if (!a || !this.dia || !this.horaInicio || (!this.espacioId && !esVirtual)) { this.checks.set([]); this.checksOk.set(false); this.avisos.set([]); return; }
    const startIdx = this.horasDisponibles.indexOf(this.horaInicio), endIdx = startIdx + dur;
    // FE16 auditoría: antes solo se acotaba el sábado — ver mismo fix en EditarSesionDialogComponent.
    if (!finDeJornadaOk(this.dia, endIdx, this.horasDisponibles)) {
      chks.push({ ok: false, texto: this.dia === 'sabado' ? `Sábado solo tiene jornada hasta las ${hhmm(HORA_CIERRE_SABADO)}` : 'La sesión no cabe en la jornada de ese día.' });
      ok = false;
    }
    const horaFinNueva = this.horasDisponibles[endIdx] ?? this.horaInicio;
    const sesion1 = () => describirSesionConflicto(
      { asignaturaId: this.asignaturaId, grupoId: this.grupoIdSel(), dia: this.dia, horaInicio: this.horaInicio, horaFin: horaFinNueva },
      this.data.asignaturas, this.data.grupos, this.data.programaById);
    const sesion2 = (s: Sesion) => describirSesionConflicto(s, this.data.asignaturas, this.data.grupos, this.data.programaById);
    // La clase nueva no alterna: ocupa todas las semanas, así que choca con cualquier otra.
    const semanaActual: Sesion['semana'] = undefined;

    const docenteId = this.docenteActual();
    if (docenteId) {
      const conflictoDocente = this.data.sesiones.find(s => s.docenteId === docenteId && s.dia === this.dia && !nuncaCoexisteEnSemana(s.semana, semanaActual) && seSolapanHorarios(s, startIdx, endIdx, this.horasDisponibles));
      if (conflictoDocente) ok = false;
      const texto = conflictoDocente
        ? `El docente ya tiene otra sesión en esa franja — Sesión 1: ${sesion1()}; Sesión 2: ${sesion2(conflictoDocente)}. ` +
          'Elija otro docente o cambie el horario de una de las dos sesiones.'
        : 'El docente está libre en esa franja';
      chks.push({ ok: !conflictoDocente, texto });
    }
    if (!esVirtual) {
      // FE5 auditoría: faltaba la exención de semana que Editar ya tenía — una TipoB nueva en el
      // mismo bloque/aula que una TipoA existente es válida (nunca comparten semana), pero esto
      // la bloqueaba igual por mirar solo solape de horario.
      const conflictoEspacio = this.data.sesiones.find(s => s.espacioId === this.espacioId && s.dia === this.dia && !s.virtual && !nuncaCoexisteEnSemana(s.semana, semanaActual) && seSolapanHorarios(s, startIdx, endIdx, this.horasDisponibles));
      if (conflictoEspacio) ok = false;
      const espNombre = this.data.espacios.find(e => e.id === this.espacioId)?.nombre ?? this.espacioId;
      const texto = conflictoEspacio
        ? `${espNombre} ya está ocupado en esa franja — Sesión 1: ${sesion1()}; Sesión 2: ${sesion2(conflictoEspacio)}. ` +
          'Elija otro espacio o cambie el horario de una de las dos sesiones.'
        : `${espNombre} está libre en esa franja`;
      chks.push({ ok: !conflictoEspacio, texto });
    }
    // En el borrador un choque solo avisa: con los campos completos siempre se puede agregar.
    this.checks.set(chks); this.checksOk.set(ok || this.modoBorrador);
    const nueva: Sesion = {
      id: '', asignaturaId: a.id, grupoId: this.grupoIdSel(), docenteId: docenteId || undefined, dia: this.dia, horaInicio: this.horaInicio,
      horaFin: hhmm(parseInt(this.horaInicio, 10) + Math.round(dur)), duracionHoras: dur, virtual: esVirtual, alternancia: this.alternancia,
    };
    const fuera = avisosDocentePorSesion([nueva], this.data.docentes, [], []).get('')?.fueraDeDisponibilidad;
    this.avisos.set(fuera ? [fuera] : []);
  }

  crear() {
    if (!this.puedeCrear()) return;
    const a = this.asignaturaSeleccionada()!, tipo = this.tipoSesion(), duracion = this.duracionSeleccionada();
    // Grupo sin docente → null: '' no es un Guid y el servidor lo rechazaba con 400.
    const docenteId = this.docenteActual() || null;
    const espacioId = tipo === 'TeoriaVirtual' ? null : (this.espacioId || null);
    if (this.modoFija || this.modoBorrador) {
      const [hh, mm] = this.horaInicio.split(':').map(Number);
      const sesion: Sesion = {
        id: nuevoId(), asignaturaId: a.id, grupoId: this.grupoIdSel(), docenteId: docenteId ?? undefined,
        dia: this.dia, horaInicio: this.horaInicio, duracionHoras: duracion,
        horaFin: `${String(hh + duracion).padStart(2, '0')}:${String(mm).padStart(2, '0')}`,
        espacioId: espacioId ?? undefined, virtual: esVirtualDesde(tipo), alternancia: this.alternancia, tipoFlujo: tipoFlujoDesde(tipo),
        ...(this.modoBorrador ? { deBorrador: true } : {})
      };
      this.dialogRef.close([sesion]);
      return;
    }
    // P0-5 auditoría: la sesión se agrega al horario vigente; sin uno generado no hay dónde guardarla.
    const horarioId = this.state.horarioId();
    if (!horarioId) { this.errorServidor.set('Genere el horario antes de agregar clases.'); return; }
    this.guardando.set(true); this.errorServidor.set('');
    this.persistencia.crearSesionManual({
      horarioId, asignaturaId: a.id, docenteId, espacioId,
      // R2 auditoría: el diálogo ya exige elegir grupo (puedeCrear()) — antes se descartaba aquí.
      grupoId: this.grupoIdSel(),
      dia: this.dia, horaInicio: this.horaInicio, duracionHoras: duracion,
      tipoFlujo: tipoFlujoDesde(tipo), esVirtual: esVirtualDesde(tipo)
    }).subscribe({
      next: (sesiones: Sesion[]) => { this.guardando.set(false); this.dialogRef.close(sesiones); },
      error: (err: any) => { this.guardando.set(false); this.errorServidor.set(mensajeErrorHttp(err)); }
    });
  }

  cancelar() { this.dialogRef.close(); }
  // FE5 auditoría: overlaps/diffH vivían duplicados aquí y en EditarSesionDialogComponent — ahora
  // son las funciones de módulo seSolapanHorarios/diffHorasEntre, usadas por recheck() arriba.
  nombreDocente(id?: string): string { return this.data.docentes.find(d => d.id === id)?.nombre ?? '—'; }
}
