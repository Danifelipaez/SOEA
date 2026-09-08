import { Component, inject, computed, signal } from '@angular/core';
import { forkJoin, of, Observable } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule, FormBuilder, Validators, AbstractControl, ValidationErrors } from '@angular/forms';
import { MatDialogModule, MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { StateService } from '../../../core/state.service';
import { PersistenciaService } from '../../../core/persistencia.service';
import { CatalogoService } from '../../../core/catalogo.service';
import { mensajeErrorHttp } from '../../../core/http-error.util';
import { ConfirmDeleteDialogComponent } from '../../../shared/confirm-delete-dialog/confirm-delete-dialog.component';
import { ImportResultadoDialogComponent } from '../../../shared/import-resultado-dialog/import-resultado-dialog.component';
import { Asignatura, Facultad, Grupo, Programa, RequisitoEspacio, TipoSesionUi } from '../../../core/models';
import { nuevoId } from '../../../core/id.util';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ImportExcelStatsDto } from '../../../core/persistencia.service';
import { SearchableSelectComponent, SearchableOption } from '../../../shared/searchable-select/searchable-select.component';
import { GrupoDialogComponent, GrupoDialogData } from '../grupo-tab/grupo-tab.component';
import { DisponibilidadEditorComponent } from '../../../shared/disponibilidad-editor/disponibilidad-editor.component';
import { RequisitosEspacioComponent } from '../../../shared/requisitos-espacio/requisitos-espacio.component';

@Component({
  selector: 'app-asignaturas-tab',
  standalone: true,
  imports: [CommonModule, MatDialogModule],
  template: `
    <div class="tab-content">
      <div class="toolbar">
        <div class="filters">
          <input class="input search" placeholder="🔍 Buscar asignatura…" (input)="filterStr.set($any($event.target).value)">
          <span class="text-muted count">{{ filtered().length }} asignaturas</span>
        </div>
        <div class="actions">
          <button class="btn btn-secondary" (click)="fileInput.click()" [disabled]="uploading()">
            {{ uploading() ? 'Subiendo…' : '⬆ Importar Excel' }}
          </button>
          <button class="btn btn-primary" (click)="openDialog()">＋ Nueva asignatura</button>
          <button class="btn btn-secondary" (click)="openGrupoDialog()"
                  [disabled]="state.asignaturas().length === 0"
                  [title]="state.asignaturas().length ? '' : 'Crea una asignatura primero'">＋ Nuevo grupo</button>
        </div>
      </div>
      <input type="file" #fileInput accept=".xlsx,.xls" (change)="onFileSelected($event)" style="display:none">

      <table class="table">
        <thead><tr>
          <th style="width:26px"></th>
          <th style="width:26%">Asignatura</th><th>Código</th><th>Ses/sem</th><th>Programa</th><th style="width:80px">Grupos</th><th style="width:60px"></th>
        </tr></thead>
        <tbody>
          @for (a of filtered(); track a.id) {
            <tr class="asig-row"
                [class.row-incompleto]="asignaturaIncompleta(a)"
                [class.row-conflicto]="asignaturaEnConflicto(a)"
                [title]="tituloAsignatura(a)"
                (click)="toggleExpand(a.id)">
              <td class="chev">{{ expandidos().has(a.id) ? '▾' : '▸' }}</td>
              <td><b>{{ a.nombre }}</b> @if (a.categoria) { <span class="tag tag-neutral" style="font-size:9px">{{ a.categoria }}</span> }</td>
              <td class="text-muted">{{ a.codigo }}</td>
              <td class="text-muted">{{ resumenSesiones(a) }}</td>
              <td>{{ programaNombre(a.programaId) }}</td>
              <td>{{ state.getGruposByAsignatura(a.id).length }}</td>
              <td>
                <span class="material-icons ic-edit" (click)="openDialog(a); $event.stopPropagation()" title="Editar">edit</span>
                <span class="material-icons ic-del" (click)="delete(a); $event.stopPropagation()" title="Eliminar">delete</span>
              </td>
            </tr>
            @if (expandidos().has(a.id)) {
              <tr class="grupos-row">
                <td colspan="7">
                  <table class="table subtable">
                    <thead><tr>
                      <th>Grupo</th><th>Espacio</th><th>Docente</th><th style="width:70px">Estud.</th><th>Disponibilidad</th><th style="width:60px"></th>
                    </tr></thead>
                    <tbody>
                      @for (g of state.getGruposByAsignatura(a.id); track g.id) {
                        <tr [class.row-incompleto]="grupoIncompleto(g)" [class.row-conflicto]="grupoEnConflicto(g)" [title]="tituloGrupo(g)">
                          <td>{{ g.nombre }}</td>
                          <td [class.text-error]="!g.requisitosEspacio?.length" [class.text-muted]="!!g.requisitosEspacio?.length">{{ requisitosResumen(g) }}</td>
                          <td>
                            @if (g.docenteId) { {{ docenteNombre(g.docenteId) }} }
                            @else { <span style="color:var(--err-bd);font-size:12.5px">Sin docente</span> }
                          </td>
                          <td>{{ g.estudiantesInscritos }}</td>
                          <td>
                            @if (g.disponibilidadUiJson) {
                              <span [class.text-muted]="!sinDiasDisponibles(g)" [class.text-error]="sinDiasDisponibles(g)" style="font-size:12.5px">{{ dispResumenGrupo(g) }}</span>
                            } @else { <span style="color:var(--err-bd);font-size:12.5px">Sin declarar</span> }
                          </td>
                          <td>
                            <span class="material-icons ic-edit" (click)="openGrupoDialog(a, g)" title="Editar">edit</span>
                            <span class="material-icons ic-del" (click)="deleteGrupo(g)" title="Eliminar">delete</span>
                          </td>
                        </tr>
                      }
                      @if (state.getGruposByAsignatura(a.id).length === 0) {
                        <tr><td colspan="6" class="empty-sub">Sin grupos.</td></tr>
                      }
                    </tbody>
                  </table>
                  <button class="btn btn-secondary btn-sm" style="margin-top:8px" (click)="openGrupoDialog(a)">＋ Nuevo grupo</button>
                </td>
              </tr>
            }
          }
          @if (filtered().length === 0) {
            <tr><td colspan="7" class="empty">No hay asignaturas. Usa "⬆ Importar Excel" o "＋ Nueva asignatura".</td></tr>
          }
        </tbody>
      </table>

      @if (state.gruposHuerfanos().length > 0) {
        <h3 class="sec" style="margin-top:18px">⚠ {{ state.gruposHuerfanos().length }} grupo(s) sin asignatura válida</h3>
        <table class="table subtable tabla-huerfanos">
          <thead><tr><th>Grupo</th><th>Id</th><th style="width:60px"></th></tr></thead>
          <tbody>
            @for (g of state.gruposHuerfanos(); track g.id) {
              <tr>
                <td>{{ g.nombre }}</td>
                <td class="text-muted" style="font-size:11px">{{ g.id.slice(0, 8) }}…</td>
                <td>
                  <span class="material-icons ic-edit" (click)="openGrupoDialog(undefined, g)" title="Editar">edit</span>
                  <span class="material-icons ic-del" (click)="deleteGrupo(g)" title="Eliminar">delete</span>
                </td>
              </tr>
            }
          </tbody>
        </table>
      }
    </div>
  `,
  styles: [`
    .tab-content { padding: 20px 0; display: flex; flex-direction: column; gap: 14px; }
    .toolbar { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    .filters { display: flex; align-items: center; gap: 10px; }
    .search { width: 240px; }
    .count { font-size: 12.5px; }
    .actions { display: flex; gap: 8px; flex-wrap: wrap; }
    .empty { text-align: center; color: var(--color-neutral-500); padding: 28px; }
    .asig-row { cursor: pointer; }
    .chev { text-align: center; color: var(--color-neutral-500); }
    .grupos-row td { padding: 10px 10px 14px 34px; background: var(--color-neutral-100); }
    .subtable { background: var(--color-bg); }
    .empty-sub { text-align: center; color: var(--color-neutral-500); padding: 12px; }
    .btn-sm { font-size: 12px; padding: 4px 10px; }
    /* Grupo con datos incompletos (sin espacio o sin disponibilidad): borde rojo fijo.
       Se propaga a la fila de la asignatura para que sea visible con el desplegable cerrado. */
    .row-incompleto { outline: 2px solid var(--err-bd); outline-offset: -2px; }
    /* Grupo señalado por el backend como responsable de una infactibilidad: mismo borde, parpadeando. */
    .row-conflicto { outline: 2px solid var(--err-bd); outline-offset: -2px; animation: parpadeo-conflicto 1s step-start infinite; }
    @keyframes parpadeo-conflicto { 50% { outline-color: transparent; } }
  `]
})
export class AsignaturasTabComponent {
  state = inject(StateService);
  dialog = inject(MatDialog);
  snackBar = inject(MatSnackBar);
  persistencia = inject(PersistenciaService);
  catalogo = inject(CatalogoService);

  saving = signal(false);
  uploading = signal(false);
  filterStr = signal('');
  expandidos = signal<Set<string>>(new Set());

  filtered = computed(() => {
    const f = this.filterStr().toLowerCase();
    return this.state.asignaturas().filter(a =>
      !f || a.nombre.toLowerCase().includes(f) || a.codigo.toLowerCase().includes(f) ||
      this.programaNombre(a.programaId).toLowerCase().includes(f)
    );
  });

  programaNombre(programaId: string): string { return this.state.getProgramaById(programaId)?.nombre ?? '—'; }

  toggleExpand(asignaturaId: string) {
    this.expandidos.update(s => {
      const n = new Set(s);
      n.has(asignaturaId) ? n.delete(asignaturaId) : n.add(asignaturaId);
      return n;
    });
  }

  docenteNombre(id?: string): string { return id ? (this.state.docentes().find(d => d.id === id)?.nombre ?? '—') : '—'; }

  /** Resumen del requisito de espacio de un grupo (petición 4). '—' si usa la regla por defecto. */
  requisitosResumen(g: Grupo): string {
    const reqs = g.requisitosEspacio ?? [];
    if (!reqs.length) return '—';
    return reqs.map(r => {
      const label = r.tipoSesion === 'Laboratorio' ? 'Lab' : 'Pres.';
      const detalle = r.espacioId ? (this.state.espacioById().get(r.espacioId)?.nombre ?? '—') : r.tipoEspacio;
      return `${label}: ${detalle}`;
    }).join(' · ');
  }

  dispResumenGrupo(g: Grupo): string {
    const n = this.diasDisponibles(g);
    return n ? `${n} día(s) disponibles` : 'Sin días disponibles';
  }

  /** true si el grupo declaró disponibilidad pero ningún día quedó marcado como disponible. */
  sinDiasDisponibles(g: Grupo): boolean {
    return !!g.disponibilidadUiJson && this.diasDisponibles(g) === 0;
  }

  /** Nº de días con disponibilidad declarada. 0 si no hay JSON o si no parsea. */
  private diasDisponibles(g: Grupo): number {
    if (!g.disponibilidadUiJson) return 0;
    try {
      const disp = JSON.parse(g.disponibilidadUiJson);
      const dias = ['lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado'];
      return dias.filter(d => disp[d] && !disp[d].noDisponible).length;
    } catch { return 0; }
  }

  /** Sin requisito de espacio o sin ningún día disponible: el grupo quedaría fuera del
   *  pipeline de generación en la práctica (regla por defecto de espacio, o HC-G01 vacío). */
  grupoIncompleto(g: Grupo): boolean {
    return !g.requisitosEspacio?.length || this.diasDisponibles(g) === 0;
  }

  /** El backend (diagnóstico opcional de Fase 2) señaló este grupo como responsable de que la
   *  última generación de horario fuera infactible. */
  grupoEnConflicto(g: Grupo): boolean {
    return this.state.gruposEnConflictoSet().has(g.id);
  }

  asignaturaIncompleta(a: Asignatura): boolean {
    return this.state.getGruposByAsignatura(a.id).some(g => this.grupoIncompleto(g));
  }

  asignaturaEnConflicto(a: Asignatura): boolean {
    return this.state.getGruposByAsignatura(a.id).some(g => this.grupoEnConflicto(g));
  }

  /** Motivo concreto del borde rojo de un grupo — el borde solo no dice qué falta. '' si está completo. */
  private motivoIncompleto(g: Grupo): string {
    const motivos: string[] = [];
    if (!g.requisitosEspacio?.length) motivos.push('sin requisito de espacio declarado (se usará la regla por defecto)');
    if (!g.disponibilidadUiJson) motivos.push('sin disponibilidad horaria declarada');
    else if (this.diasDisponibles(g) === 0) motivos.push('sin ningún día habilitado en su disponibilidad horaria');
    return motivos.length ? `Datos incompletos: ${motivos.join('; ')}.` : '';
  }

  /** Motivo concreto del parpadeo de un grupo. '' si no está en conflicto. */
  private motivoConflicto(g: Grupo): string {
    return this.grupoEnConflicto(g)
      ? 'El sistema señaló este grupo como responsable de que el último intento de generar el horario fallara — revise si comparte un espacio fijo con otro grupo o si su disponibilidad es demasiado restrictiva, y vuelva a generar.'
      : '';
  }

  /** Texto del tooltip (title) de la fila de un grupo. '' si no hay ningún aviso activo. */
  tituloGrupo(g: Grupo): string {
    return [this.motivoIncompleto(g), this.motivoConflicto(g)].filter(Boolean).join(' ');
  }

  /**
   * Texto del tooltip (title) de la fila de asignatura — nombra los grupos afectados, porque con
   * el desplegable cerrado el borde rojo por sí solo no dice cuál de los grupos es el problema.
   */
  tituloAsignatura(a: Asignatura): string {
    const grupos = this.state.getGruposByAsignatura(a.id);
    const incompletos = grupos.filter(g => this.grupoIncompleto(g));
    const enConflicto = grupos.filter(g => this.grupoEnConflicto(g));
    const partes: string[] = [];
    if (incompletos.length) partes.push(`${incompletos.length} grupo(s) con datos incompletos: ${incompletos.map(g => g.nombre).join(', ')}.`);
    if (enConflicto.length) partes.push(`${enConflicto.length} grupo(s) señalado(s) en el último intento fallido: ${enConflicto.map(g => g.nombre).join(', ')}.`);
    return partes.length ? `${partes.join(' ')} Expanda para revisar cada uno.` : '';
  }

  /** Sin `asignatura` (botón del toolbar) el diálogo abre en modo completo: el usuario elige la jerarquía. */
  openGrupoDialog(asignatura?: Asignatura, grupo?: Grupo) {
    const data: GrupoDialogData = grupo
      ? { grupo }
      : asignatura ? { asignaturaId: asignatura.id, programaId: asignatura.programaId } : {};
    const ref = this.dialog.open(GrupoDialogComponent, { width: '620px', maxWidth: '95vw', data });
    ref.afterClosed().subscribe(result => {
      if (!result) return;
      const entidad: Grupo = grupo ? { ...grupo, ...result } : { id: nuevoId(), ...result };
      this.catalogo.guardar('grupo', entidad).subscribe({
        next: () => this.snackBar.open(grupo ? 'Grupo actualizado' : 'Grupo agregado', '', { duration: 2500 }),
        error: (err) => this.snackBar.open(`Error al guardar: ${mensajeErrorHttp(err)}`, 'Cerrar', { duration: 4000 })
      });
    });
  }

  deleteGrupo(grupo: Grupo) {
    const enBd = this.catalogo.estaEnBd('grupo', grupo.id);
    const ref = this.dialog.open(ConfirmDeleteDialogComponent, {
      width: '320px',
      data: {
        title: 'Eliminar grupo',
        message: enBd
          ? `Se eliminará "${grupo.nombre}" de la base de datos. Esta acción es irreversible.`
          : `Se eliminará "${grupo.nombre}" (aún no está guardado en la BD).`
      }
    });
    ref.afterClosed().subscribe(confirmado => {
      if (!confirmado) return;
      if (!enBd) { this.state.deleteGrupo(grupo.id); this.snackBar.open('Grupo eliminado localmente.', '', { duration: 2500 }); return; }
      this.persistencia.eliminarGrupoBD(grupo.id).subscribe({
        next: () => { this.catalogo.quitarDeBd('grupo', grupo.id); this.state.deleteGrupo(grupo.id); this.snackBar.open('Grupo eliminado de la BD.', '', { duration: 2500 }); },
        error: (err) => this.snackBar.open(`Error al eliminar: ${mensajeErrorHttp(err)}`, 'Cerrar', { duration: 5000 })
      });
    });
  }

  resumenSesiones(a: Asignatura): string {
    const p: string[] = [];
    if (a.sesionesTeoriaPresencialSemana > 0) p.push(`${a.sesionesTeoriaPresencialSemana}×${a.horasTeoriaPresencial}h pres.`);
    if (a.sesionesTeoriaVirtualSemana > 0) p.push(`${a.sesionesTeoriaVirtualSemana}×${a.horasTeoriaVirtual}h virt.`);
    if (a.sesionesLaboratorioSemana > 0) p.push(`${a.sesionesLaboratorioSemana}×${a.horasLaboratorio}h lab`);
    return p.join(' · ') || '—';
  }

  onFileSelected(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    (event.target as HTMLInputElement).value = '';
    this.uploading.set(true);
    this.persistencia.importarExcel(file).subscribe({
      next: (stats: ImportExcelStatsDto) => {
        this.uploading.set(false);
        this.cargarDesdeBD();
        this.dialog.open(ImportResultadoDialogComponent, { width: '340px', data: stats });
      },
      error: (err) => {
        this.uploading.set(false);
        this.snackBar.open(`Error al importar: ${mensajeErrorHttp(err)}`, 'Cerrar', { duration: 5000 });
      }
    });
  }

  openDialog(asignatura?: Asignatura) {
    const dialogRef = this.dialog.open(AsignaturaDialogComponent, { width: '540px', maxWidth: '95vw', data: asignatura });
    dialogRef.afterClosed().subscribe(result => {
      if (!result) return;
      const { grupos: gruposPendientes, ...asignaturaFields } = result;
      const entidad: Asignatura = asignatura ? { ...asignatura, ...asignaturaFields } : { id: nuevoId(), ...asignaturaFields };
      this.guardarConDependencias(entidad, !!asignatura, gruposPendientes ?? []);
    });
  }

  /**
   * El diálogo de asignatura puede crear una facultad/programa nuevos localmente
   * (ver AsignaturaDialogComponent.save()). Persistirlos primero es obligatorio: la
   * asignatura referencia programaId por FK y el backend la rechaza si no existe.
   * Los grupos de la cápsula "nuevo grupo +" (petición 2) se crean después, una vez que
   * la asignatura tiene id real (o ya lo tenía, en edición).
   */
  private guardarConDependencias(entidad: Asignatura, esEdicion: boolean, gruposPendientes: Partial<Grupo>[] = []) {
    const previas$: Observable<unknown>[] = [];
    const programa = this.state.getProgramaById(entidad.programaId);
    if (programa) {
      const facultad = this.state.getFacultadById(programa.facultadId);
      if (facultad && !this.catalogo.estaEnBd('facultad', facultad.id)) previas$.push(this.catalogo.guardar('facultad', facultad));
      if (!this.catalogo.estaEnBd('programa', programa.id)) previas$.push(this.catalogo.guardar('programa', programa));
    }
    const previas$$: Observable<unknown> = previas$.length ? forkJoin(previas$) : of(null);
    previas$$.pipe(
      switchMap((): Observable<Asignatura> => this.catalogo.guardar('asignatura', entidad)),
      switchMap((): Observable<unknown> => {
        if (!gruposPendientes.length) return of(null);
        // facultadId/codigo se perdían aquí (G6 auditoría): la cápsula no los recogía en el
        // objeto del grupo aunque el modelo los soporta.
        const facultadId = this.state.getProgramaById(entidad.programaId)?.facultadId;
        return forkJoin(gruposPendientes.map(g => this.catalogo.guardar('grupo', {
          id: nuevoId(),
          asignaturaId: entidad.id,
          programaId: entidad.programaId,
          facultadId,
          nombre: g.nombre ?? 'Grupo',
          codigo: g.codigo,
          estudiantesInscritos: g.estudiantesInscritos ?? 30,
          docenteId: g.docenteId,
          disponibilidadUiJson: g.disponibilidadUiJson,
          requisitosEspacio: g.requisitosEspacio
        } as Grupo)));
      })
    ).subscribe({
      next: () => this.snackBar.open(esEdicion ? 'Asignatura actualizada' : 'Asignatura agregada', '', { duration: 2500 }),
      error: (err) => this.snackBar.open(`Error al guardar: ${mensajeErrorHttp(err)}`, 'Cerrar', { duration: 4000 })
    });
  }

  delete(asignatura: Asignatura) {
    const enBd = this.catalogo.estaEnBd('asignatura', asignatura.id);
    const ref = this.dialog.open(ConfirmDeleteDialogComponent, {
      width: '320px',
      data: {
        title: 'Eliminar asignatura',
        message: enBd
          ? `Se eliminará "${asignatura.nombre}" de la base de datos. Esta acción es irreversible.`
          : `Se eliminará "${asignatura.nombre}" (aún no está guardada en la BD).`
      }
    });
    ref.afterClosed().subscribe(confirmado => {
      if (!confirmado) return;
      if (!enBd) { this.state.deleteAsignatura(asignatura.id); this.snackBar.open('Asignatura eliminada localmente.', '', { duration: 2500 }); return; }
      this.persistencia.eliminarAsignatura(asignatura.id).subscribe({
        next: () => {
          this.catalogo.quitarDeBd('asignatura', asignatura.id);
          this.state.deleteAsignatura(asignatura.id);
          this.catalogo.cargarTodo().subscribe();
          this.snackBar.open('Asignatura eliminada de la BD.', '', { duration: 2500 });
        },
        error: (err) => this.snackBar.open(`Error al eliminar: ${mensajeErrorHttp(err)}`, 'Cerrar', { duration: 5000 })
      });
    });
  }

  cargarDesdeBD() {
    this.saving.set(true);
    this.catalogo.cargarTodo().subscribe({
      next: (resumen) => { this.saving.set(false); this.snackBar.open(`${resumen.asignaturas} asignatura(s) · ${resumen.docentes} docente(s) cargados.`, '', { duration: 3500 }); },
      error: () => { this.saving.set(false); this.snackBar.open('Error al cargar desde la BD.', 'Cerrar', { duration: 4000 }); }
    });
  }
}

// ─── Popup: Crear/Editar asignatura (REQUISITOS §1.1) ─────────────────────────
type CategoriaSesion = 'presencial' | 'virtual' | 'lab';

/** HC-VH: horaInicioMin debe ser anterior a horaFinMax. Sin declarar ninguna de las dos
 *  (sin restricción de ventana) es válido — solo se exige el orden cuando ambas están presentes. */
function ventanaHorariaValidaValidator(group: AbstractControl): ValidationErrors | null {
  const inicio = group.get('horaInicioMin')?.value;
  const fin = group.get('horaFinMax')?.value;
  if (!inicio || !fin) return null;
  return inicio < fin ? null : { ventanaHorariaInvertida: true };
}

@Component({
  selector: 'app-asignatura-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule, MatDialogModule, SearchableSelectComponent, DisponibilidadEditorComponent, RequisitosEspacioComponent],
  template: `
    <div class="pophd">{{ data ? 'Editar asignatura' : 'Nueva asignatura' }} <i (click)="ref.close()">✕</i></div>
    <form class="popbd" [formGroup]="form" style="max-height:74vh;overflow:auto">

      <div style="display:flex;gap:8px">
        <div class="dfield" style="flex:1"><label>Facultad <span class="rq">*</span></label>
          <app-searchable-select formControlName="facultadId" [options]="facultadOptions()" placeholder="— Seleccione —"></app-searchable-select></div>
        <div class="dfield" style="flex:1"><label>Programa <span class="rq">*</span></label>
          <app-searchable-select formControlName="programaId" [options]="programaOptions()" placeholder="— Seleccione —"></app-searchable-select></div>
      </div>
      @if (form.get('facultadId')?.value === '__nueva__') {
        <div class="dfield"><label>Nombre de la nueva facultad</label><input class="input" formControlName="nuevaFacultad"></div>
      }
      @if (form.get('programaId')?.value === '__nuevo__') {
        <div class="dfield"><label>Nombre del nuevo programa</label><input class="input" formControlName="nuevoPrograma"></div>
      }

      <div style="display:flex;gap:8px">
        <div class="dfield" style="flex:1"><label>Código <span class="rq">*</span></label><input class="input" formControlName="codigo"></div>
        <div class="dfield" style="flex:1.4"><label>Nombre <span class="rq">*</span></label><input class="input" formControlName="nombre"></div>
        <div class="dfield" style="width:120px"><label>Tipo</label>
          <select class="input" formControlName="categoria">
            <option value="">—</option><option value="Obligatoria">Obligatoria</option><option value="Optativa">Optativa</option><option value="Electiva">Electiva</option>
          </select></div>
      </div>

      <div style="display:flex;gap:8px;align-items:flex-end">
        <div class="dfield" style="width:140px"><label>Ventana desde</label><input class="input" type="time" formControlName="horaInicioMin"></div>
        <div class="dfield" style="width:140px"><label>Ventana hasta</label><input class="input" type="time" formControlName="horaFinMax"></div>
        <p class="text-muted" style="font-size:11px;margin:0 0 9px">Opcional — acota el horario en que puede programarse esta asignatura (HC-VH, Secretaría Académica).</p>
      </div>

      <div style="display:flex;gap:8px;align-items:flex-end">
        <div class="dfield" style="width:170px"><label>Sesiones por semana <span class="rq">*</span></label>
          <input class="input" type="number" min="0" [value]="sesionesPorSemana()" (input)="onNChange($any($event.target).value)"></div>
        <p class="text-muted" style="font-size:11px;margin:0 0 9px">Máximo combinado entre presencial, virtual y laboratorio.</p>
      </div>

      <h3 class="sec" style="margin-top:2px">Desglose por tipo de sesión</h3>
      <div class="track">
        <span class="tlabel">Teoría presencial</span>
        <div class="stepper">
          <button type="button" class="btn btn-secondary step-btn" (click)="dec('presencial')" [disabled]="sesiones().presencial<=0">−</button>
          <span class="step-val">{{ sesiones().presencial }}</span>
          <button type="button" class="btn btn-secondary step-btn" (click)="inc('presencial')">+</button>
        </div>
        @if (sesiones().presencial > 0) {
          <div class="dfield" style="width:96px"><label>Horas/ses</label><input class="input" type="number" min="1" formControlName="horasTeoriaPresencial"></div>
        }
      </div>
      <div class="track">
        <span class="tlabel">Teoría virtual</span>
        <div class="stepper">
          <button type="button" class="btn btn-secondary step-btn" (click)="dec('virtual')" [disabled]="sesiones().virtual<=0">−</button>
          <span class="step-val">{{ sesiones().virtual }}</span>
          <button type="button" class="btn btn-secondary step-btn" (click)="inc('virtual')">+</button>
        </div>
        @if (sesiones().virtual > 0) {
          <div class="dfield" style="width:96px"><label>Horas/ses</label><input class="input" type="number" min="1" formControlName="horasTeoriaVirtual"></div>
        }
      </div>
      <div class="track">
        <span class="tlabel">Laboratorio</span>
        <div class="stepper">
          <button type="button" class="btn btn-secondary step-btn" (click)="dec('lab')" [disabled]="sesiones().lab<=0">−</button>
          <span class="step-val">{{ sesiones().lab }}</span>
          <button type="button" class="btn btn-secondary step-btn" (click)="inc('lab')">+</button>
        </div>
        @if (sesiones().lab > 0) {
          <div class="dfield" style="width:96px"><label>Horas/ses</label><input class="input" type="number" min="1" formControlName="horasLaboratorio"></div>
        }
      </div>
      <p class="text-muted" style="font-size:11px;margin:0">Asignadas: {{ sesiones().presencial + sesiones().virtual + sesiones().lab }} / {{ sesionesPorSemana() }}</p>

      <p class="text-muted" style="font-size:11px;margin:0;border-top:1px dashed var(--color-neutral-300);padding-top:8px">
        El docente se asigna por <b>grupo</b>, no aquí — la misma asignatura la dictan docentes distintos en grupos distintos.
        Disponibilidad y requisito de espacio (p. ej. laboratorio concreto) se declaran al agregar cada grupo abajo, o después desde la fila desplegada.
      </p>

      <h3 class="sec" style="margin-top:2px">Grupos <span class="text-muted" style="font-size:11px;text-transform:none;letter-spacing:0">(opcional — puedes agregarlos después)</span></h3>
      @for (g of gruposPendientes(); track $index; let i = $index) {
        <div class="track">
          <span class="tlabel">{{ g.nombre }}</span>
          <span class="text-muted" style="flex:1;font-size:12px">
            {{ g.estudiantesInscritos }} estudiantes @if (g.docenteId) { · {{ nombreDocente(g.docenteId) }} }
          </span>
          <span class="material-icons ic-del" (click)="quitarGrupoPendiente(i)" title="Quitar">delete</span>
        </div>
      }
      @if (agregandoGrupo()) {
        <div style="display:flex;gap:8px;align-items:flex-end">
          <div class="dfield" style="flex:1"><label>Nombre</label><input class="input" [(ngModel)]="nuevoGrupoNombre" [ngModelOptions]="{standalone:true}" placeholder="Ej. G1"></div>
          <div class="dfield" style="width:110px"><label>Estudiantes</label><input class="input" type="number" min="1" [(ngModel)]="nuevoGrupoEstudiantes" [ngModelOptions]="{standalone:true}"></div>
          <div class="dfield" style="flex:1"><label>Docente</label>
            <app-searchable-select [(ngModel)]="nuevoGrupoDocenteId" [ngModelOptions]="{standalone:true}" [options]="docenteOptions()" placeholder="— Sin asignar —"></app-searchable-select></div>
        </div>

        <h3 class="sec" style="margin-top:2px">Disponibilidad del grupo</h3>
        <app-disponibilidad-editor [defaultNoDisponible]="true"
          [ngModel]="nuevoGrupoDisponibilidad()" (ngModelChange)="nuevoGrupoDisponibilidad.set($event)" [ngModelOptions]="{standalone:true}"></app-disponibilidad-editor>

        @if (tiposRequisitoPendiente().length > 0) {
          <h3 class="sec" style="margin-top:2px">Requisito de espacio <span class="text-muted" style="font-size:11px;text-transform:none;letter-spacing:0">(opcional)</span></h3>
          <app-requisitos-espacio [tipos]="tiposRequisitoPendiente()" [espacios]="state.espacios()"
            [ngModel]="nuevoGrupoRequisitos()" (ngModelChange)="nuevoGrupoRequisitos.set($event)" [ngModelOptions]="{standalone:true}"></app-requisitos-espacio>
        }

        <div style="display:flex;gap:8px;justify-content:flex-end">
          <button type="button" class="btn btn-secondary step-btn" (click)="confirmarGrupoPendiente()" [disabled]="!nuevoGrupoNombre.trim()">Agregar</button>
          <button type="button" class="btn btn-secondary step-btn" (click)="cancelarGrupoPendiente()">✕</button>
        </div>
      } @else {
        <button type="button" class="btn btn-secondary" (click)="agregandoGrupo.set(true)">＋ nuevo grupo</button>
      }

      <div class="popfoot">
        <button type="button" class="btn btn-secondary" (click)="ref.close()">Cancelar</button>
        <button type="button" class="btn btn-primary" [disabled]="!canSave()" (click)="save()">Guardar</button>
      </div>
    </form>
  `,
  styles: [`
    .track { display: flex; align-items: flex-end; gap: 10px; padding: 8px 10px; border: 1px solid var(--color-divider); }
    .tlabel { flex: 1; font: 600 12px var(--font-heading); align-self: center; }
    .stepper { display: flex; align-items: center; gap: 6px; }
    .step-btn { min-height: 26px; min-width: 26px; padding: 0; font-size: 15px; line-height: 1; }
    .step-val { min-width: 20px; text-align: center; font: 600 14px var(--font-heading); }
  `]
})
export class AsignaturaDialogComponent {
  fb = inject(FormBuilder);
  ref = inject(MatDialogRef<AsignaturaDialogComponent>);
  data = inject(MAT_DIALOG_DATA) as Asignatura | undefined;
  state = inject(StateService);

  programasFiltrados = signal<Programa[]>([]);

  // ── Petición 2: cápsula "nuevo grupo +" — crea la asignatura y sus grupos en un solo flujo,
  // incluida disponibilidad y requisito de espacio (mismos componentes que usa GrupoDialogComponent).
  // Versión ligera (no reutiliza GrupoDialogComponent completo): la cascada Facultad→Programa→Asignatura
  // no aplica aquí (la asignatura es la que se está creando), así que este es un formulario propio.
  // tiposRequisitoPendiente() deriva los tipos de sesión del desglose en vivo (sesiones()) en vez de
  // un Asignatura ya persistido, porque todavía no existe uno.
  gruposPendientes = signal<Partial<Grupo>[]>([]);
  agregandoGrupo = signal(false);
  nuevoGrupoNombre = '';
  nuevoGrupoEstudiantes = 30;
  nuevoGrupoDocenteId = '';
  nuevoGrupoDisponibilidad = signal<Record<string, any>>({});
  nuevoGrupoRequisitos = signal<RequisitoEspacio[]>([]);

  tiposRequisitoPendiente = computed<TipoSesionUi[]>(() => {
    const s = this.sesiones();
    const tipos: TipoSesionUi[] = [];
    if (s.presencial > 0) tipos.push('TeoriaPresencial');
    if (s.lab > 0) tipos.push('Laboratorio');
    return tipos;
  });

  docenteOptions = computed<SearchableOption[]>(() => [
    { value: '', label: 'Sin asignar' },
    ...this.state.docentes().map(d => ({ value: d.id, label: d.nombre }))
  ]);

  nombreDocente(id: string): string { return this.state.docentes().find(d => d.id === id)?.nombre ?? '—'; }

  confirmarGrupoPendiente() {
    if (!this.nuevoGrupoNombre.trim()) return;
    this.gruposPendientes.update(v => [...v, {
      nombre: this.nuevoGrupoNombre.trim(),
      estudiantesInscritos: Number(this.nuevoGrupoEstudiantes) || 30,
      docenteId: this.nuevoGrupoDocenteId || undefined,
      disponibilidadUiJson: JSON.stringify(this.nuevoGrupoDisponibilidad()),
      requisitosEspacio: this.nuevoGrupoRequisitos()
    }]);
    this.resetCapsulaGrupo();
  }

  cancelarGrupoPendiente() {
    this.resetCapsulaGrupo();
  }

  private resetCapsulaGrupo() {
    this.nuevoGrupoNombre = '';
    this.nuevoGrupoEstudiantes = 30;
    this.nuevoGrupoDocenteId = '';
    this.nuevoGrupoDisponibilidad.set({});
    this.nuevoGrupoRequisitos.set([]);
    this.agregandoGrupo.set(false);
  }

  quitarGrupoPendiente(i: number) {
    this.gruposPendientes.update(v => v.filter((_, idx) => idx !== i));
  }

  // ── Presupuesto de sesiones semanales (item 3): las 3 categorías comparten un tope N.
  // ponytail: al incrementar en el tope se "roba" de la categoría con mayor conteo entre
  // las otras dos; en empate se prioriza presencial > virtual > lab (orden de PRIORIDAD).
  private readonly PRIORIDAD: Record<CategoriaSesion, number> = { presencial: 0, virtual: 1, lab: 2 };
  sesionesPorSemana = signal(this.sumaInicial());
  sesiones = signal<Record<CategoriaSesion, number>>(this.sesionesIniciales());

  facultadOptions = computed<SearchableOption[]>(() => [
    ...this.state.facultades().map(f => ({ value: f.id, label: f.nombre })),
    { value: '__nueva__', label: '+ Nueva facultad…' }
  ]);
  programaOptions = computed<SearchableOption[]>(() => [
    ...this.programasFiltrados().map(p => ({ value: p.id, label: p.nombre })),
    { value: '__nuevo__', label: '+ Nuevo programa…' }
  ]);
  form = this.fb.group({
    facultadId: ['', Validators.required],
    programaId: ['', Validators.required],
    nuevaFacultad: [''],
    nuevoPrograma: [''],
    codigo: [this.data?.codigo ?? '', Validators.required],
    nombre: [this.data?.nombre ?? '', Validators.required],
    categoria: [this.data?.categoria ?? ''],
    horaInicioMin: [this.data?.horaInicioMin ?? ''],
    horaFinMax: [this.data?.horaFinMax ?? ''],
    horasTeoriaPresencial: [this.data?.horasTeoriaPresencial ?? 2, [Validators.required, Validators.min(1)]],
    horasTeoriaVirtual: [this.data?.horasTeoriaVirtual ?? 2, [Validators.required, Validators.min(1)]],
    horasLaboratorio: [this.data?.horasLaboratorio ?? 2, [Validators.required, Validators.min(1)]]
  }, { validators: ventanaHorariaValidaValidator });

  constructor() {
    if (this.data?.programaId) {
      const prog = this.state.getProgramaById(this.data.programaId);
      if (prog) {
        this.form.patchValue({ facultadId: prog.facultadId, programaId: prog.id }, { emitEvent: false });
        this.programasFiltrados.set(this.state.getProgramasByFacultad(prog.facultadId));
      }
    }
    // Suscripción registrada después del patch inicial: solo reacciona a cambios del usuario.
    this.form.get('facultadId')!.valueChanges.subscribe(fid => this.onFacultadChange(fid ?? ''));
  }

  private sumaInicial(): number {
    const a = this.data;
    const s = (a?.sesionesTeoriaPresencialSemana ?? 0) + (a?.sesionesTeoriaVirtualSemana ?? 0) + (a?.sesionesLaboratorioSemana ?? 0);
    return s > 0 ? s : 2;
  }

  private sesionesIniciales(): Record<CategoriaSesion, number> {
    const a = this.data;
    if (!a) return { presencial: 2, virtual: 0, lab: 0 };
    return {
      presencial: a.sesionesTeoriaPresencialSemana ?? 0,
      virtual: a.sesionesTeoriaVirtualSemana ?? 0,
      lab: a.sesionesLaboratorioSemana ?? 0
    };
  }

  inc(cat: CategoriaSesion) {
    const s = { ...this.sesiones() };
    const suma = s.presencial + s.virtual + s.lab;
    if (suma >= this.sesionesPorSemana()) {
      const otras = (['presencial', 'virtual', 'lab'] as CategoriaSesion[]).filter(c => c !== cat);
      otras.sort((a, b) => (s[b] - s[a]) || (this.PRIORIDAD[a] - this.PRIORIDAD[b]));
      const victima = otras[0];
      if (s[victima] <= 0) return; // presupuesto agotado, nada que quitarle a las otras
      s[victima]--;
    }
    s[cat]++;
    this.sesiones.set(s);
  }

  dec(cat: CategoriaSesion) {
    const s = { ...this.sesiones() };
    if (s[cat] <= 0) return;
    s[cat]--;
    this.sesiones.set(s);
  }

  onNChange(value: string) {
    const n = Math.max(0, Math.round(Number(value)) || 0);
    const s = { ...this.sesiones() };
    const suma = s.presencial + s.virtual + s.lab;
    if (suma > n) {
      // El faltante recae primero en presencial; si no alcanza, sigue con virtual y luego lab.
      let excedente = suma - n;
      const quitar = (cat: CategoriaSesion) => { const q = Math.min(excedente, s[cat]); s[cat] -= q; excedente -= q; };
      quitar('presencial');
      if (excedente > 0) quitar('virtual');
      if (excedente > 0) quitar('lab');
    } else if (suma < n) {
      s.presencial += (n - suma);
    }
    this.sesionesPorSemana.set(n);
    this.sesiones.set(s);
  }

  private onFacultadChange(facultadId: string) {
    this.programasFiltrados.set(facultadId === '__nueva__' || !facultadId ? [] : this.state.getProgramasByFacultad(facultadId));
    this.form.patchValue({ programaId: '' }, { emitEvent: false });
  }

  canSave(): boolean {
    const v = this.form.value;
    if (!v.nombre || !v.codigo || !v.facultadId) return false;
    if (v.facultadId === '__nueva__' && !v.nuevaFacultad) return false;
    if (!v.programaId) return false;
    if (v.programaId === '__nuevo__' && !v.nuevoPrograma) return false;
    // HC-VH: ventana horaria invertida (ver ventanaHorariaValidaValidator) no debe guardarse —
    // canSave() no consulta this.form.valid (rehace sus propios checks), así que el validador
    // del FormGroup por sí solo no bastaba para deshabilitar "Guardar".
    if (v.horaInicioMin && v.horaFinMax && v.horaInicioMin >= v.horaFinMax) return false;
    const s = this.sesiones();
    return (s.presencial + s.virtual + s.lab) > 0;
  }

  save() {
    if (!this.canSave()) return;
    // G6 (bug reportado "no se guarda, no crea grupo"): si el usuario llenó la cápsula "＋ nuevo
    // grupo" pero pulsó "Guardar" sin pulsar antes "Agregar", su contenido se descartaba en
    // silencio — confirmarGrupoPendiente() es un no-op si el nombre está vacío, así que es
    // seguro llamarla siempre aquí.
    this.confirmarGrupoPendiente();
    const v = this.form.value;
    let facultadId = v.facultadId!;
    if (facultadId === '__nueva__') { const fac: Facultad = { id: nuevoId(), nombre: v.nuevaFacultad! }; this.state.addFacultad(fac); facultadId = fac.id; }
    let programaId = v.programaId!;
    if (programaId === '__nuevo__') { const prog: Programa = { id: nuevoId(), nombre: v.nuevoPrograma!, facultadId }; this.state.addPrograma(prog); programaId = prog.id; }

    const s = this.sesiones();

    this.ref.close({
      codigo: v.codigo!, nombre: v.nombre!,
      categoria: (v.categoria as 'Obligatoria' | 'Optativa' | 'Electiva') || undefined,
      horaInicioMin: v.horaInicioMin || undefined,
      horaFinMax: v.horaFinMax || undefined,
      sesionesTeoriaPresencialSemana: s.presencial,
      horasTeoriaPresencial: Number(v.horasTeoriaPresencial) || 2,
      sesionesTeoriaVirtualSemana: s.virtual,
      horasTeoriaVirtual: Number(v.horasTeoriaVirtual) || 2,
      sesionesLaboratorioSemana: s.lab,
      horasLaboratorio: Number(v.horasLaboratorio) || 2,
      // ponytail: alternancia ya no se infiere de "lab/semestre" (item 2 del debug) — se
      // conserva el valor existente y se edita aparte en la pestaña Alternancia (PATCH).
      // sesionesLaboratorioSemestre se sigue enviando solo porque el backend aún lo exige
      // (columna NOT NULL); se retira del dominio en la Fase 2 de backend.
      sesionesLaboratorioSemestre: this.data?.sesionesLaboratorioSemestre ?? 0,
      alternancia: this.data?.alternancia ?? 'SinAlternancia',
      programaId,
      grupos: this.gruposPendientes()
    } as Partial<Asignatura> & { grupos: Partial<Grupo>[] });
  }
}
