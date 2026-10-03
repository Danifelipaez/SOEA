import { Component, computed, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { FilaCurriculum, ImportExcelStatsDto, IncoherenciaFila, PersistenciaService, RevisionImport } from '../../core/persistencia.service';
import { mensajeErrorHttp } from '../../core/http-error.util';

type CampoEditable = Exclude<keyof FilaCurriculum, 'fila'>;

/** Columnas que el operador puede corregir en el popup (mismo orden que el Excel de Rosa). */
const CAMPOS: { campo: CampoEditable; etiqueta: string; ancho: string }[] = [
  { campo: 'facultad', etiqueta: 'Facultad', ancho: '1.4fr' },
  { campo: 'programa', etiqueta: 'Programa', ancho: '1.6fr' },
  { campo: 'asignatura', etiqueta: 'Asignatura', ancho: '1.8fr' },
  { campo: 'grupo', etiqueta: 'Grupo', ancho: '.55fr' },
  { campo: 'docente', etiqueta: 'Docente', ancho: '1.6fr' },
  { campo: 'duracion', etiqueta: 'Horas', ancho: '.55fr' },
  { campo: 'espacio', etiqueta: 'Espacio', ancho: '.8fr' },
  { campo: 'dia', etiqueta: 'Día', ancho: '.9fr' },
  { campo: 'hora', etiqueta: 'Hora', ancho: '.7fr' },
  { campo: 'final', etiqueta: 'Final', ancho: '.7fr' },
];

/**
 * Revisión previa al import del Excel (2026-10-03): muestra solo las filas con problemas para que
 * el operador las corrija ahí mismo o las borre antes de guardar nada. Los errores impiden importar;
 * los avisos no. Las comprobaciones las hace el servidor (única fuente de las reglas): "Comprobar"
 * vuelve a pedirlas tras editar. Cierra con el ImportExcelStatsDto si se importó, o undefined.
 */
@Component({
  selector: 'app-import-revision-dialog',
  standalone: true,
  imports: [MatDialogModule],
  template: `
    <div class="pophd">Importar Excel — revisar antes de guardar <button type="button" class="pop-close" (click)="ref.close()" aria-label="Cerrar">✕</button></div>
    <div class="popbd">
      <p class="intro">
        Se leyeron <b>{{ totalLeidas }}</b> fila(s). Estas tienen datos que no cuadran:
        corríjalos aquí o borre la fila. <b>Nada se ha guardado todavía</b> y el archivo original no cambia.
      </p>

      @for (a of avisosGenerales; track a) { <div class="soft">⚠ {{ a }}</div> }

      <div class="lista">
        @for (f of filasConProblemas(); track f.fila) {
          <div class="fila" [class.con-error]="tieneError(f.fila)" [class.ok]="!sucio() && incoherenciasDe(f.fila).length === 0">
            <div class="fila-hd">
              <b>Fila {{ f.fila }}</b>
              <span class="text-muted">{{ f.asignatura || 'sin asignatura' }}{{ f.grupo ? ' · G' + f.grupo : '' }}{{ f.programa ? ' · ' + f.programa : '' }}</span>
              <button type="button" class="btn btn-secondary borrar" (click)="borrar(f.fila)">Borrar fila</button>
            </div>
            <ul class="msgs">
              @for (i of incoherenciasDe(f.fila); track $index) {
                <li [class.err]="i.esError" [class.warn]="!i.esError">{{ i.esError ? '✕' : '⚠' }} {{ i.mensaje }}</li>
              } @empty {
                @if (!sucio()) { <li class="ok">✓ Corregida.</li> }
              }
            </ul>
            <div class="campos" [style.grid-template-columns]="columnas">
              @for (c of campos; track c.campo) {
                <label>
                  <span>{{ c.etiqueta }}</span>
                  <input [value]="f[c.campo]" [class.mal]="campoMarcado(f.fila, c.campo)"
                         [attr.aria-label]="c.etiqueta + ', fila ' + f.fila"
                         (input)="editar(f.fila, c.campo, $any($event.target).value)">
                </label>
              }
            </div>
          </div>
        } @empty {
          <div class="okb">✓ No quedan filas con problemas.</div>
        }
      </div>

      @if (borradas().length > 0) {
        <div class="text-muted nota">{{ borradas().length }} fila(s) borrada(s) no se importarán: {{ borradas().join(', ') }}.
          <button type="button" class="btn-link" (click)="deshacerBorrado()">Deshacer</button></div>
      }
      @if (errorServidor()) { <div class="soft err-msg">{{ errorServidor() }}</div> }

      <div class="popfoot">
        <span class="resumen">
          @if (sucio()) { Hay cambios sin comprobar. }
          @else if (errores() > 0) { {{ errores() }} error(es): corrija o borre esas filas para poder importar. }
          @else if (avisos() > 0) { {{ avisos() }} aviso(s): puede importar igual. }
        </span>
        <button class="btn btn-secondary" (click)="ref.close()" [disabled]="ocupado()">Cancelar</button>
        <button class="btn btn-secondary" (click)="comprobar()" [disabled]="ocupado() || !sucio()">Comprobar cambios</button>
        <button class="btn btn-primary" (click)="importar()" [disabled]="ocupado() || sucio() || errores() > 0 || filas().length === 0">
          {{ ocupado() ? 'Procesando…' : 'Importar ' + filas().length + ' fila(s)' }}
        </button>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .intro { margin: 0 0 10px; font-size: 13px; }
    .lista { max-height: 60vh; overflow-y: auto; display: flex; flex-direction: column; gap: 10px; }
    .fila { border: 1px solid var(--warn-bd); border-left-width: 4px; padding: 8px 10px; background: var(--color-neutral-0, #fff); }
    .fila.con-error { border-color: var(--err-bd); }
    .fila.ok { border-color: var(--ok-bd); }
    .fila-hd { display: flex; gap: 10px; align-items: center; font-size: 13px; }
    .fila-hd .borrar { margin-left: auto; font-size: 12px; padding: 3px 10px; }
    .msgs { margin: 6px 0; padding-left: 0; list-style: none; font-size: 12.5px; }
    .msgs li { margin-bottom: 2px; }
    .msgs .err { color: var(--err-fg); }
    .msgs .warn { color: var(--warn-fg); }
    .msgs .ok { color: var(--ok-bd); }
    .campos { display: grid; gap: 6px; }
    .campos label { display: flex; flex-direction: column; gap: 2px; font-size: 11px; min-width: 0; }
    .campos input { font-size: 12.5px; padding: 3px 5px; min-width: 0; width: 100%; box-sizing: border-box; }
    .campos input.mal { border-color: var(--err-bd); background: var(--err-bg); }
    .nota { font-size: 12px; margin-top: 8px; }
    .err-msg { color: var(--err-fg); margin-top: 8px; }
    .popfoot { align-items: center; }
    .resumen { margin-right: auto; font-size: 12.5px; }
    @media (max-width: 720px) { .campos { grid-template-columns: 1fr 1fr !important; } }
  `]
})
export class ImportRevisionDialogComponent {
  private readonly persistencia = inject(PersistenciaService);
  readonly ref = inject(MatDialogRef<ImportRevisionDialogComponent, ImportExcelStatsDto | undefined>);
  private readonly data = inject(MAT_DIALOG_DATA) as RevisionImport;

  readonly campos = CAMPOS;
  readonly columnas = CAMPOS.map(c => c.ancho).join(' ');
  readonly avisosGenerales = this.data.avisos;
  readonly totalLeidas = this.data.filas.length;

  /** Todas las filas que se importarían (las borradas ya no están). */
  readonly filas = signal<FilaCurriculum[]>(this.data.filas);
  private readonly original = this.data.filas;
  readonly incoherencias = signal<IncoherenciaFila[]>(this.data.incoherencias);
  /** Las incoherencias mostradas son de antes de la última edición. */
  readonly sucio = signal(false);
  readonly ocupado = signal(false);
  readonly errorServidor = signal('');

  /** Filas con problemas según la última comprobación; las editadas siguen visibles hasta comprobar. */
  private readonly filasMostradas = signal(new Set(this.data.incoherencias.map(i => i.fila)));
  readonly filasConProblemas = computed(() => this.filas().filter(f => this.filasMostradas().has(f.fila)));
  readonly borradas = computed(() => {
    const vivas = new Set(this.filas().map(f => f.fila));
    return this.original.filter(f => !vivas.has(f.fila)).map(f => f.fila);
  });
  readonly errores = computed(() => this.incoherencias().filter(i => i.esError).length);
  readonly avisos = computed(() => this.incoherencias().filter(i => !i.esError).length);

  incoherenciasDe(fila: number) { return this.incoherencias().filter(i => i.fila === fila); }
  tieneError(fila: number) { return this.incoherencias().some(i => i.fila === fila && i.esError); }
  campoMarcado(fila: number, campo: string) { return this.incoherencias().some(i => i.fila === fila && i.campo === campo); }

  editar(fila: number, campo: CampoEditable, valor: string) {
    this.filas.update(fs => fs.map(f => f.fila === fila ? { ...f, [campo]: valor } : f));
    this.sucio.set(true);
  }

  borrar(fila: number) {
    this.filas.update(fs => fs.filter(f => f.fila !== fila));
    // Las incoherencias de otras filas pueden depender de esta (repetida, choque): hay que comprobar.
    this.incoherencias.update(is => is.filter(i => i.fila !== fila));
    this.sucio.set(true);
  }

  deshacerBorrado() {
    const vivas = new Map(this.filas().map(f => [f.fila, f]));
    this.filas.set(this.original.map(f => vivas.get(f.fila) ?? f));
    this.sucio.set(true);
  }

  comprobar() {
    if (this.filas().length === 0) { this.incoherencias.set([]); this.sucio.set(false); return; }
    this.ocupado.set(true);
    this.errorServidor.set('');
    this.persistencia.revisarFilas(this.filas()).subscribe({
      next: r => this.aplicarRevision(r.incoherencias),
      error: err => { this.ocupado.set(false); this.errorServidor.set(`No se pudo comprobar: ${mensajeErrorHttp(err)}`); },
    });
  }

  importar() {
    this.ocupado.set(true);
    this.errorServidor.set('');
    this.persistencia.importarFilas(this.filas()).subscribe({
      next: stats => this.ref.close(stats),
      error: err => {
        // 422: alguien cambió las reglas o los datos entre medias — se muestran los nuevos problemas.
        if (err?.status === 422 && err.error?.incoherencias) { this.aplicarRevision(err.error.incoherencias); return; }
        this.ocupado.set(false);
        this.errorServidor.set(`No se pudo importar: ${mensajeErrorHttp(err)}`);
      },
    });
  }

  private aplicarRevision(incoherencias: IncoherenciaFila[]) {
    this.incoherencias.set(incoherencias);
    // Se siguen mostrando las filas ya revisadas (aunque queden bien) para que se vea el resultado.
    this.filasMostradas.update(s => new Set([...s, ...incoherencias.map(i => i.fila)]));
    this.sucio.set(false);
    this.ocupado.set(false);
  }
}
