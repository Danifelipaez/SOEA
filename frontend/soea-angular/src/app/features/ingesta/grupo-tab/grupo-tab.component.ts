import { Component, inject, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule, FormBuilder, Validators, FormGroup } from '@angular/forms';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { StateService } from '../../../core/state.service';
import { Grupo, Programa, RequisitoEspacio, TipoSesionUi } from '../../../core/models';
import { SearchableSelectComponent, SearchableOption } from '../../../shared/searchable-select/searchable-select.component';
import { RequisitosEspacioComponent } from '../../../shared/requisitos-espacio/requisitos-espacio.component';
import { DisponibilidadEditorComponent } from '../../../shared/disponibilidad-editor/disponibilidad-editor.component';

// ─── Popup: Crear/Editar grupo + disponibilidad (REQUISITOS §1.4) ──────────────
// Dos puntos de entrada, ambos en asignaturas-tab.component.ts:
//   1. Fila desplegada de una asignatura (crear/editar) → modo contextual: la asignatura
//      viene fijada por el contexto, así que la jerarquía se muestra como texto, no como selects.
//   2. Botón "＋ Nuevo grupo" del toolbar → modo completo con la cascada
//      Facultad → Programa → Asignatura → Grupo (item 6): cada nivel filtra al siguiente
//      (item 10) para no listar asignaturas de facultades ajenas.
export interface GrupoDialogData {
  grupo?: Grupo;
  /** Preselección al crear desde la fila desplegada de una asignatura (petición 1). */
  asignaturaId?: string;
  programaId?: string;
}

@Component({
  selector: 'app-grupo-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule, SearchableSelectComponent, RequisitosEspacioComponent, DisponibilidadEditorComponent],
  template: `
    <div class="pophd">{{ data?.grupo ? 'Editar grupo' : 'Nuevo grupo' }} <button type="button" class="pop-close" (click)="ref.close()" aria-label="Cerrar">✕</button></div>
    <div class="popbd" style="max-height:74vh;overflow:auto">
      <form [formGroup]="form" style="display:flex;flex-direction:column;gap:10px">
        @if (asignaturaFija) {
          <p class="text-muted" style="font-size:12px;margin:0">
            Grupo de <b>{{ asignaturaCtx()?.nombre }}</b>
            · {{ asignaturaCtx()?.codigo }} · {{ programaCtxNombre() }}
          </p>
        } @else {
          <div style="display:flex;gap:8px">
            <div class="dfield" style="flex:1"><label>Facultad</label>
              <app-searchable-select formControlName="facultadId" [options]="facultadOptions()" placeholder="— Seleccione —"></app-searchable-select></div>
            <div class="dfield" style="flex:1"><label>Programa <span class="rq">*</span></label>
              <app-searchable-select formControlName="programaId" [options]="programaOptions()" placeholder="— Seleccione facultad primero —"></app-searchable-select></div>
          </div>
        }
        <div style="display:flex;gap:8px">
          @if (!asignaturaFija) {
            <div class="dfield" style="flex:1.4"><label>Asignatura <span class="rq">*</span></label>
              <app-searchable-select formControlName="asignaturaId" [options]="asignaturaOptions()" placeholder="— Seleccione programa primero —"></app-searchable-select></div>
          }
          <div class="dfield" style="flex:1"><label>Docente</label>
            <app-searchable-select formControlName="docenteId" [options]="docenteOptions()" placeholder="— Sin asignar —"></app-searchable-select></div>
        </div>
        <div style="display:flex;gap:8px">
          <div class="dfield" style="flex:1.4"><label>Nombre del grupo <span class="rq">*</span></label>
            <input class="input" formControlName="nombre" placeholder="Ej. G1"></div>
          <div class="dfield" style="flex:1"><label>Código</label>
            <input class="input" formControlName="codigo"></div>
          <div class="dfield" style="width:110px"><label>Estudiantes <span class="rq">*</span></label>
            <input class="input" type="number" min="1" formControlName="estudiantesInscritos"></div>
        </div>
      </form>

      <h3 class="sec" style="margin-top:4px">Disponibilidad del grupo</h3>
      <app-disponibilidad-editor [defaultNoDisponible]="true"
        [ngModel]="disponibilidad()" (ngModelChange)="disponibilidad.set($event)"></app-disponibilidad-editor>

      @if (tiposRequisito().length > 0) {
        <h3 class="sec" style="margin-top:4px">Requisito de espacio <span class="text-muted" style="font-size:11px;text-transform:none;letter-spacing:0">(opcional — sin elegir nada rige la regla por defecto)</span></h3>
        <app-requisitos-espacio [tipos]="tiposRequisito()" [espacios]="state.espacios()"
          [ngModel]="requisitosEspacio()" (ngModelChange)="requisitosEspacio.set($event)"></app-requisitos-espacio>
      }

      <div class="popfoot">
        <button type="button" class="btn btn-secondary" (click)="ref.close()">Cancelar</button>
        <button type="button" class="btn btn-primary" [disabled]="form.invalid" (click)="save()">Guardar</button>
      </div>
    </div>
  `
})
export class GrupoDialogComponent {
  fb = inject(FormBuilder);
  ref = inject(MatDialogRef<GrupoDialogComponent>);
  /** `grupo` presente = editar; ausente = nuevo, opcionalmente preseleccionando asignaturaId/programaId
   *  (creación desde la fila desplegada de una asignatura — petición 1). */
  data = inject(MAT_DIALOG_DATA) as GrupoDialogData | undefined;
  state = inject(StateService);

  private grupo = this.data?.grupo;

  /**
   * Asignatura fijada por el contexto (diálogo abierto desde la fila desplegada de una
   * asignatura). Vacío = modo completo: el usuario elige la jerarquía con los selects.
   * Un grupo huérfano (asignaturaId no resuelve, p. ej. la asignatura fue eliminada) no cuenta
   * como "fija" — sin esto asignaturaCtx() da undefined y el diálogo queda sin forma de reasignar.
   */
  readonly asignaturaFija =
    (this.grupo?.asignaturaId && this.state.asignaturaById().has(this.grupo.asignaturaId))
      ? this.grupo.asignaturaId
      : (this.data?.asignaturaId ?? '');

  form: FormGroup;
  disponibilidad = signal<Record<string, any>>(
    this.grupo?.disponibilidadUiJson ? this.parseDisponibilidad(this.grupo.disponibilidadUiJson) : {});

  programasFiltrados = signal<Programa[]>([]);
  private programaIdActual = signal('');
  private asignaturaIdActual = signal(this.asignaturaFija);
  requisitosEspacio = signal<RequisitoEspacio[]>(this.grupo?.requisitosEspacio ?? []);

  // Contexto de solo lectura mostrado en lugar de la cascada cuando la asignatura viene fijada.
  asignaturaCtx = computed(() => this.state.asignaturaById().get(this.asignaturaFija));
  programaCtxNombre = computed(() => this.state.getProgramaById(this.programaIdActual())?.nombre ?? '—');

  tiposRequisito = computed<TipoSesionUi[]>(() => {
    const asig = this.state.asignaturaById().get(this.asignaturaIdActual());
    if (!asig) return [];
    const tipos: TipoSesionUi[] = [];
    if (asig.sesionesTeoriaPresencialSemana > 0) tipos.push('TeoriaPresencial');
    if (asig.sesionesLaboratorioSemana > 0) tipos.push('Laboratorio');
    return tipos;
  });

  facultadOptions = computed<SearchableOption[]>(() =>
    this.state.facultades().map(f => ({ value: f.id, label: f.nombre })));
  programaOptions = computed<SearchableOption[]>(() =>
    this.programasFiltrados().map(p => ({ value: p.id, label: p.nombre })));
  asignaturaOptions = computed<SearchableOption[]>(() => {
    const programaId = this.programaIdActual();
    if (!programaId) return [];
    return this.state.asignaturas()
      .filter(a => a.programaId === programaId)
      .map(a => ({ value: a.id, label: a.nombre, sub: a.codigo }));
  });
  docenteOptions = computed<SearchableOption[]>(() => [
    { value: '', label: 'Sin asignar' },
    ...this.state.docentes().map(d => ({ value: d.id, label: d.nombre }))
  ]);

  constructor() {
    const programaInicial = this.grupo?.programaId ?? this.data?.programaId ?? '';
    // Deriva la facultad inicial desde el programa si el grupo existente no la trae explícita.
    const facultadInicial = this.grupo?.facultadId
      ?? (programaInicial ? this.state.getProgramaById(programaInicial)?.facultadId : undefined)
      ?? '';

    this.form = this.fb.group({
      facultadId: [facultadInicial],
      programaId: [programaInicial, Validators.required],
      asignaturaId: [this.asignaturaFija, Validators.required],
      docenteId: [this.grupo?.docenteId ?? ''],
      nombre: [this.grupo?.nombre ?? '', Validators.required],
      codigo: [this.grupo?.codigo ?? ''],
      estudiantesInscritos: [this.grupo?.estudiantesInscritos ?? 30, [Validators.required, Validators.min(1)]]
    });

    this.programasFiltrados.set(facultadInicial ? this.state.getProgramasByFacultad(facultadInicial) : []);
    this.programaIdActual.set(programaInicial);

    // Suscripciones registradas después del estado inicial: solo reaccionan a cambios del usuario.
    this.form.get('facultadId')!.valueChanges.subscribe(fid => this.onFacultadChange(fid ?? ''));
    this.form.get('programaId')!.valueChanges.subscribe(pid => this.onProgramaChange(pid ?? ''));
    this.form.get('asignaturaId')!.valueChanges.subscribe(aid => this.asignaturaIdActual.set(aid ?? ''));
  }

  private onFacultadChange(facultadId: string) {
    this.programasFiltrados.set(facultadId ? this.state.getProgramasByFacultad(facultadId) : []);
    this.form.patchValue({ programaId: '', asignaturaId: '' }, { emitEvent: false });
    this.programaIdActual.set('');
  }

  private onProgramaChange(programaId: string) {
    this.programaIdActual.set(programaId);
    const asig = this.state.asignaturas().find(a => a.id === this.form.get('asignaturaId')?.value);
    if (asig && asig.programaId !== programaId) {
      this.form.patchValue({ asignaturaId: '' }, { emitEvent: false });
    }
  }

  private parseDisponibilidad(json: string): Record<string, any> {
    try { return JSON.parse(json); } catch { return {}; }
  }

  save() {
    if (this.form.invalid) return;
    const v = this.form.value;
    this.ref.close({
      ...v,
      facultadId: v.facultadId || undefined,
      docenteId: v.docenteId || undefined,
      codigo: v.codigo || undefined,
      disponibilidadUiJson: JSON.stringify(this.disponibilidad()),
      requisitosEspacio: this.requisitosEspacio()
    });
  }
}
