import { Component, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { StateService } from '../../core/state.service';
import { HorarioApiService } from '../../core/horario-api.service';
import { PersistenciaService } from '../../core/persistencia.service';
import { Asignatura, Docente, Espacio, Sesion, TipoSesionUi } from '../../core/models';
import { HORA_CIERRE_SABADO } from '../../core/jornada';
import { mensajeErrorHttp } from '../../core/http-error.util';
import { MergedSesion, describirSesionConflicto, hhmm, HORAS_GRILLA, DIAS_GRILLA, seSolapanHorarios, nuncaCoexisteEnSemana, finDeJornadaOk, espaciosPermitidosPara, avisosDocentePorSesion, Check } from './horario.reglas';

// ═══ Diálogo: Editar sesión (REQUISITOS §2/§3 — incluye asignar docente con 409) ═══

export interface EditarSesionDialogData {
  merged: MergedSesion; sesion: Sesion; asignaturas: Asignatura[]; docentes: Docente[]; espacios: Espacio[];
  sesiones: Sesion[]; programaById: Map<string, { id: string; nombre: string; facultadId: string }>; facultadById: Map<string, { id: string; nombre: string }>;
}
/** `sesion` ausente = un cambio de día/hora/espacio ya refrescó el StateService completo (P5). */
export interface EditarSesionResult { sesion?: Sesion; advertencias: string[]; }

@Component({
  selector: 'app-editar-sesion-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule],
  template: `
    <div class="pophd">Editar sesión <button type="button" class="pop-close" (click)="cancelar()" aria-label="Cerrar">✕</button></div>
    <div class="popbd" style="max-height:80vh;overflow:auto">
      <div class="text-muted" style="font-size:12px">{{ asignatura()?.nombre }} · {{ data.sesion.duracionHoras }}h {{ data.sesion.virtual ? '· virtual' : '· presencial' }}</div>

      <div class="dfield"><label>Docente</label>
        <select class="input" [ngModel]="docenteId()" (ngModelChange)="docenteId.set($event)">
          <option value="">— Sin asignar —</option>
          @for (d of data.docentes; track d.id) { <option [value]="d.id">{{ d.nombre }}</option> }
        </select>
        @if (hayCambioDocente()) { <small class="text-muted" style="font-size:11px">Si el docente ya tiene clase a esa hora no se podrá guardar; si está fuera de su disponibilidad, verá un aviso.</small> }
      </div>
      @for (w of avisosDocente(); track w) { <div class="soft"><b>⚠ Aviso (no bloquea):</b> {{ w }}</div> }

      <div class="dfield"><label>Día · Hora inicio</label>
        <div style="display:flex;gap:6px">
          <select class="input" style="flex:1" [ngModel]="dia()" (ngModelChange)="dia.set($event)">
            @for (d of diasOpciones; track d.valor) { <option [value]="d.valor">{{ d.etiqueta }}</option> }
          </select>
          <select class="input" style="width:80px" [ngModel]="horaInicio()" (ngModelChange)="horaInicio.set($event)">
            @for (h of horasDisponibles; track h) { <option [value]="h">{{ h }}</option> }
          </select>
        </div>
      </div>

      <div class="dfield"><label>Espacio</label>
        <select class="input" [ngModel]="espacioId()" (ngModelChange)="espacioId.set($event)">
          <option value="">— Sin espacio (virtual) —</option>
          @for (e of espaciosDisponibles(); track e.id) { <option [value]="e.id">{{ e.nombre }}</option> }
        </select>
      </div>

      @if (esLaboratorio()) {
        <div class="dfield" data-testid="alternancia-solo-lectura"><label>Semanas en que usa el espacio</label>
          <div style="font-size:13px">{{ etiquetaAlternancia }}</div>
          <small class="text-muted" style="font-size:11px">Se define al generar el horario; no se cambia desde aquí.</small>
        </div>
      }

      @for (c of validaciones(); track c.texto) {
        <div [class]="c.ok ? 'okb' : 'errb'">{{ c.ok ? '✓' : '✕' }} {{ c.texto }}</div>
      }
      @for (w of advertencias(); track w) { <div class="soft"><b>⚠ Aviso (no bloquea):</b> {{ w }}</div> }
      @if (errorServidor()) { <div class="errb"><b>✕ {{ errorServidor() }}</b></div> }

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
  private horarioApi = inject(HorarioApiService);
  private state = inject(StateService);
  private readonly orig = this.data.merged;

  docenteId = signal(this.orig.docenteId ?? '');
  dia = signal(this.orig.dia);
  horaInicio = signal(this.orig.horaInicio);
  espacioId = signal(this.orig.espacioId ?? '');
  /** NEW-8 auditoría 2026-09-28: de solo lectura. Antes se podía cambiar en este diálogo, pero el
   *  cambio solo se aplicaba en memoria (ni /reacomodar ni el PATCH de docente la reciben) y se perdía
   *  al recargar. Por ALT-05 la alternancia la fija el generador al emparejar sesiones; un cambio a
   *  mano tampoco podría dejar pareja y aula coherentes (HC-ALT). */
  readonly alternancia = this.orig.alternancia as 'TipoA' | 'TipoB' | 'SinAlternancia';
  /** Derivada: TipoA ocupa el aula en la semana A, TipoB en la B, y lo que no alterna la ocupa en las dos. */
  readonly semana: 'A' | 'B' | undefined =
    this.alternancia === 'TipoA' ? 'A' : this.alternancia === 'TipoB' ? 'B' : undefined;
  readonly etiquetaAlternancia =
    this.alternancia === 'TipoA' ? 'Semanas 1, 3, 5… (las demás, virtual)' : this.alternancia === 'TipoB' ? 'Semanas 2, 4, 6… (las demás, virtual)' : 'Todas las semanas';
  guardando = signal(false);
  advertencias = signal<string[]>([]);
  errorServidor = signal('');

  asignatura = computed(() => this.data.asignaturas.find(a => a.id === this.orig.asignaturaId));
  esLaboratorio = computed(() => this.data.sesion.tipoFlujo === 'Laboratorio');
  private tipoSesionActual = computed<TipoSesionUi>(() =>
    this.esLaboratorio() ? 'Laboratorio' : this.data.sesion.virtual ? 'TeoriaVirtual' : 'TeoriaPresencial');
  /** FE17 auditoría: antes listaba data.espacios sin filtrar — permitía mover un laboratorio a
   *  un salón cualquiera, o una sesión con aula fija de grupo a un aula distinta de la exigida. */
  espaciosDisponibles = computed(() => {
    const grupo = this.orig.grupoId ? this.state.grupos().find(g => g.id === this.orig.grupoId) : undefined;
    return espaciosPermitidosPara(this.tipoSesionActual(), grupo, this.data.espacios);
  });
  hayCambioDocente = computed(() => this.docenteId() !== (this.orig.docenteId ?? ''));
  /** Petición 13: mover día/hora/espacio ya no es una mutación local — pasa por /reacomodar. */
  hayCambioSlot = computed(() =>
    this.dia() !== this.orig.dia || this.horaInicio() !== this.orig.horaInicio || this.espacioId() !== (this.orig.espacioId ?? ''));
  hayCambios = computed(() => this.hayCambioDocente() || this.hayCambioSlot());

  readonly horasDisponibles = HORAS_GRILLA;
  readonly diasOpciones = DIAS_GRILLA;

  validaciones = computed<Check[]>(() => {
    const dia = this.dia(), inicio = this.horaInicio(), espacioId = this.espacioId(), docenteId = this.docenteId();
    const semanaActual = this.semana;
    const sesionId = this.data.sesion.id, dur = this.data.sesion.duracionHoras ?? 2;
    const chks: Check[] = [];
    if (!dia || !inicio) return chks;
    const startIdx = this.horasDisponibles.indexOf(inicio), endIdx = startIdx + Math.round(dur);
    // FE16 auditoría: antes solo se acotaba el sábado — un fin de jornada fuera de rango en L-V
    // pasaba sin aviso y horaFinNueva caía a "?? inicio" (un "fin = inicio" silencioso).
    if (!finDeJornadaOk(dia, endIdx, this.horasDisponibles)) {
      chks.push({ ok: false, texto: dia === 'sabado' ? `Sábado solo tiene jornada hasta las ${hhmm(HORA_CIERRE_SABADO)}` : 'La sesión no cabe en la jornada de ese día.' });
    }
    const horaFinNueva = this.horasDisponibles[endIdx] ?? inicio;
    const sesion1 = () => describirSesionConflicto(
      { asignaturaId: this.orig.asignaturaId, grupoId: this.orig.grupoId, dia, horaInicio: inicio, horaFin: horaFinNueva },
      this.data.asignaturas, this.state.grupos());
    const sesion2 = (s: Sesion) => describirSesionConflicto(s, this.data.asignaturas, this.state.grupos());

    if (espacioId && !this.data.sesion.virtual) {
      const conflicto = this.data.sesiones.find(s => s.id !== sesionId && s.espacioId === espacioId && s.dia === dia && !s.virtual && !nuncaCoexisteEnSemana(s.semana, semanaActual) && seSolapanHorarios(s, startIdx, endIdx, this.horasDisponibles));
      const nombre = this.data.espacios.find(e => e.id === espacioId)?.nombre ?? espacioId;
      const texto = conflicto
        ? `${nombre} ya está ocupado en esa franja — Sesión 1: ${sesion1()}; Sesión 2: ${sesion2(conflicto)}. ` +
          'Elija otro espacio o cambie el horario de una de las dos sesiones.'
        : `${nombre} está libre`;
      chks.push({ ok: !conflicto, texto });
    }
    if (docenteId && this.cambiaDocenteOFranja()) {
      const conflicto = this.data.sesiones.find(s => s.id !== sesionId && s.docenteId === docenteId && s.dia === dia && !nuncaCoexisteEnSemana(s.semana, semanaActual) && seSolapanHorarios(s, startIdx, endIdx, this.horasDisponibles));
      const nombre = this.data.docentes.find(d => d.id === docenteId)?.nombre ?? 'El docente';
      const texto = conflicto
        ? `${nombre} ya tiene otra sesión en esa franja — Sesión 1: ${sesion1()}; Sesión 2: ${sesion2(conflicto)}. ` +
          'Elija otro docente o cambie el horario de una de las dos sesiones.'
        : `${nombre} está libre en esa franja`;
      chks.push({ ok: !conflicto, texto });
    }
    return chks;
  });

  /** Un choque de docente que introduce ESTA edición (cambia el docente o la franja) bloquea en
   *  validaciones(), igual que el 409 de asignar docente y /reacomodar. El que la sesión ya traía de
   *  la generación (CR-08) solo se avisa: bloquearlo impedía hasta cambiarle el aula. */
  private cambiaDocenteOFranja = computed(() =>
    this.hayCambioDocente() || this.dia() !== this.orig.dia || this.horaInicio() !== this.orig.horaInicio);

  /** Avisos del docente con los valores editados — ver avisosDocentePorSesion. */
  avisosDocente = computed<string[]>(() => {
    const inicio = this.horaInicio();
    const editada: Sesion = {
      ...this.data.sesion, esContraparteVirtual: false, docenteId: this.docenteId() || undefined, dia: this.dia(),
      horaInicio: inicio, horaFin: hhmm(parseInt(inicio, 10) + Math.round(this.data.sesion.duracionHoras ?? 2)),
    };
    const av = avisosDocentePorSesion([...this.data.sesiones.filter(s => s.id !== editada.id), editada],
      this.data.docentes, this.data.asignaturas, this.state.grupos()).get(editada.id);
    return [...(this.cambiaDocenteOFranja() ? [] : av?.choques ?? []), ...(av?.fueraDeDisponibilidad ? [av.fueraDeDisponibilidad] : [])];
  });

  conflictosDuros = computed(() => this.validaciones().some(c => !c.ok));
  puedeGuardar = computed(() => this.hayCambios() && !this.conflictosDuros() && !this.guardando());

  guardar() {
    if (!this.puedeGuardar()) return;
    this.guardando.set(true); this.errorServidor.set(''); this.advertencias.set([]);
    if (this.hayCambioDocente()) {
      this.persistencia.asignarDocente(this.data.sesion.id, this.docenteId() || null).subscribe({
        next: (resp) => {
          this.advertencias.set(resp.advertencias ?? []);
          // FE4 auditoría: el PATCH de docente ya quedó comprometido en el backend en este punto.
          // Antes, si el reacomodo que sigue fallaba, el store seguía mostrando el docente viejo
          // — divergía de lo que ya había en BD sin que nada lo avisara. Se refleja de inmediato.
          this.state.updateSesion({ ...this.data.sesion, docenteId: this.docenteId() || undefined });
          this.continuar();
        },
        error: (err: any) => {
          this.guardando.set(false);
          const msg = mensajeErrorHttp(err);
          this.errorServidor.set(err?.status === 409 ? `Cruce de horario: ${msg}` : msg);
        }
      });
    } else { this.continuar(); }
  }

  private continuar() {
    if (this.hayCambioSlot()) this.reacomodar();
    else this.commitLocal();
  }

  /** Petición 13: mover día/hora/espacio ya no muta memoria — pasa por /reacomodar y refresca
   * el StateService completo desde la respuesta (puede haber liberado y reubicado otras sesiones). */
  private reacomodar() {
    const horarioId = this.state.horarioId();
    if (!horarioId) {
      this.guardando.set(false);
      this.errorServidor.set('Primero genere el horario para poder mover clases.');
      return;
    }
    this.horarioApi.reacomodar({
      horarioId,
      sesionEditadaId: this.data.sesion.id,
      dia: this.dia(),
      horaInicio: this.horaInicio(),
      espacioId: this.espacioId() || undefined
    }).subscribe({
      next: (resp) => {
        if (!resp.esFactible) {
          this.guardando.set(false);
          this.errorServidor.set(resp.mensajeError || 'No se pudo mover la clase a ese horario.');
          return;
        }
        this.state.setSesiones(this.horarioApi.mapearSesiones(resp.sesiones));
        const avisos = [...this.advertencias(), ...resp.advertencias];
        this.guardando.set(false);
        this.dialogRef.close({ advertencias: avisos } satisfies EditarSesionResult);
      },
      error: (err: any) => {
        this.guardando.set(false);
        // ERR3 auditoría: manejarError() de HorarioApiService reenvía el body 422 (esFactible:false)
        // TAL CUAL (no anidado bajo `.error`) — err.mensajeError se comprueba antes por eso;
        // mensajeErrorHttp cubre el resto de formas (ProblemDetails, { error }, Error de red).
        this.errorServidor.set(err?.mensajeError ?? mensajeErrorHttp(err));
      }
    });
  }

  /** Solo el docente: ya quedó persistido por PATCH, esto refleja el resultado en el StateService. */
  private commitLocal() {
    const updated: Sesion = { ...this.data.sesion, docenteId: this.docenteId() || undefined };
    // Si sólo hubo aviso blando, se dejó ver 1.2s antes de cerrar.
    if (this.advertencias().length) { setTimeout(() => { this.guardando.set(false); this.dialogRef.close({ sesion: updated, advertencias: this.advertencias() }); }, 1200); }
    else { this.guardando.set(false); this.dialogRef.close({ sesion: updated, advertencias: [] }); }
  }

  cancelar() { this.dialogRef.close(); }
  // FE5 auditoría: overlaps/nuncaCoexiste vivían duplicados aquí y en CrearSesionDialogComponent
  // — ahora son las funciones de módulo seSolapanHorarios/nuncaCoexisteEnSemana, usadas por
  // validaciones() arriba. addH/diffH quedaban sin uso salvo ese overlaps().
}
