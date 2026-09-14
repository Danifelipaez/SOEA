import { Component, forwardRef, input } from '@angular/core';
import { CommonModule, TitleCasePipe } from '@angular/common';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

export interface FranjaOption {
  value: string;
  /** También es el texto exacto guardado como `franjaGeneral` al serializar (B2). */
  label: string;
}

export const FRANJAS_DEFECTO: FranjaOption[] = [
  { value: 'todo', label: 'Todo el día (06:00–22:00)' },
  { value: 'matutino', label: 'Matutino (06:00–12:00)' },
  { value: 'vespertino', label: 'Vespertino (12:00–18:00)' },
  { value: 'nocturno', label: 'Nocturno (18:00–22:00)' },
];

/**
 * Editor de disponibilidad por día (B2): markup + lógica que antes vivían duplicados en
 * docentes-tab (DocenteDialogComponent) y grupo-tab (GrupoDialogComponent). Consume/emite el
 * mismo objeto `{ lunes: {...}, martes: {...}, ... }` que ya persisten ambos (`Docente.disponibilidad`
 * y `Grupo.disponibilidadUiJson` parseado) — cada consumidor serializa a su propio formato de
 * transporte (objeto vs JSON string) en su propio `save()`.
 */
@Component({
  selector: 'app-disponibilidad-editor',
  standalone: true,
  imports: [CommonModule, TitleCasePipe],
  providers: [{
    provide: NG_VALUE_ACCESSOR,
    useExisting: forwardRef(() => DisponibilidadEditorComponent),
    multi: true
  }],
  template: `
    <div class="disp-table">
      <div class="disp-row hd">
        <span class="c-dia">Día</span><span class="c-nd">No disp.</span><span class="c-tipo">Franja</span><span class="c-times">Horario</span>
      </div>
      @for (dia of dias; track dia) {
        <div class="disp-row">
          <span class="c-dia">{{ dia | titlecase }}</span>
          <span class="c-nd">
            <input type="checkbox" [checked]="getDisp(dia,'noDisponible')" (change)="setDisp(dia,'noDisponible',$any($event.target).checked)">
          </span>
          <span class="c-tipo">
            @if (!getDisp(dia,'noDisponible')) {
              <select class="input" style="min-height:30px;padding:4px 8px"
                      [value]="getDisp(dia,'tipo')" (change)="setDisp(dia,'tipo',$any($event.target).value)">
                @for (o of opciones(); track o.value) { <option [value]="o.value">{{ o.label }}</option> }
                <option value="especifico">Franja específica</option>
              </select>
            } @else { <span class="text-muted">—</span> }
          </span>
          <span class="c-times">
            @if (!getDisp(dia,'noDisponible') && getDisp(dia,'tipo') === 'especifico') {
              <input class="input time" type="time" [value]="getDisp(dia,'desde')" (input)="setDisp(dia,'desde',$any($event.target).value)">
              <span class="text-muted">–</span>
              <input class="input time" type="time" [value]="getDisp(dia,'hasta')" (input)="setDisp(dia,'hasta',$any($event.target).value)">
            } @else if (!getDisp(dia,'noDisponible')) {
              <span class="text-muted">{{ tipoLabel(dia) }}</span>
            } @else { <span class="text-muted">—</span> }
          </span>
        </div>
      }
    </div>
  `,
  styles: [`
    .disp-table { border: 1px solid var(--color-divider); }
    .disp-row { display: flex; gap: 8px; align-items: center; padding: 7px 11px; border-top: 1px solid color-mix(in srgb, var(--color-text) 8%, transparent); min-height: 44px; }
    .disp-row.hd { border-top: 0; background: var(--color-neutral-100); font: 600 10px var(--font-heading); letter-spacing: .08em; text-transform: uppercase; color: var(--color-neutral-600); min-height: 32px; }
    .c-dia { width: 82px; font-size: 13px; }
    .c-nd { width: 60px; display: flex; justify-content: center; }
    .c-tipo { width: 200px; }
    .c-times { flex: 1; display: flex; gap: 6px; align-items: center; font-size: 12.5px; }
    .time { width: 96px; min-height: 30px; padding: 3px 6px; }
  `]
})
export class DisponibilidadEditorComponent implements ControlValueAccessor {
  /** Franjas generales ofrecidas (además de "Franja específica", siempre disponible). */
  opciones = input<FranjaOption[]>(FRANJAS_DEFECTO);
  /** Estado por defecto de un día sin dato previo: docentes parten disponibles, grupos no. */
  defaultNoDisponible = input(false);
  /** Variantes de texto legado de `franjaGeneral` (p. ej. horarios de Excel con otros límites)
   *  que ya no coinciden con el label vigente de `opciones()`. */
  legacyMap = input<Record<string, string>>({});

  dias = ['lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado'];
  disp: Record<string, { noDisponible: boolean; tipo: string; desde: string; hasta: string }> = {};

  private onChange: (v: Record<string, unknown>) => void = () => {};
  private onTouched: () => void = () => {};

  writeValue(value: Record<string, any> | null): void {
    const src = value ?? {};
    this.dias.forEach(dia => {
      const d = src[dia] ?? {};
      const tipoRaw = d.tipo ?? 'todo';
      const tipo = tipoRaw === 'Franja específica' ? 'especifico'
        : tipoRaw === 'Franja general' ? this.tipoDesdeFranjaGeneral(d.franjaGeneral)
        : tipoRaw;
      this.disp[dia] = { noDisponible: d.noDisponible ?? this.defaultNoDisponible(), tipo, desde: d.desde ?? '06:00', hasta: d.hasta ?? '22:00' };
    });
    // FE2 auditoría: writeValue solo normalizaba `disp` en memoria — nunca llamaba a onChange.
    // Si el usuario aceptaba los valores por defecto sin tocar ningún día, el FormControl del
    // padre se quedaba con el valor ORIGINAL (null/{} para un docente nuevo), y Guardar persistía
    // eso en vez de la disponibilidad normalizada que la tabla mostraba en pantalla — un docente
    // nuevo se guardaba "sin disponibilidad declarada" aunque la UI mostrara los seis días.
    // Sin onTouched(): esto es Angular escribiéndole un valor al componente, no una interacción
    // del usuario, y marcarlo touched mostraría errores de validación antes de que el usuario
    // haga nada.
    this.onChange(this.construirValor());
  }
  registerOnChange(fn: any): void { this.onChange = fn; }
  registerOnTouched(fn: any): void { this.onTouched = fn; }

  getDisp(dia: string, field: string): any { return this.disp[dia]?.[field as keyof typeof this.disp[string]]; }

  setDisp(dia: string, field: string, value: any): void {
    this.disp[dia] = { ...this.disp[dia], [field]: value };
    this.emitir();
  }

  tipoLabel(dia: string): string {
    const label = this.opciones().find(o => o.value === this.disp[dia]?.tipo)?.label ?? '';
    return label.match(/\(([^)]+)\)/)?.[1] ?? '';
  }

  private tipoDesdeFranjaGeneral(franjaGeneral: string | undefined): string {
    if (!franjaGeneral) return 'todo';
    return this.opciones().find(o => o.label === franjaGeneral)?.value
      ?? this.legacyMap()[franjaGeneral]
      ?? 'todo';
  }

  private construirValor(): Record<string, unknown> {
    const out: Record<string, unknown> = {};
    this.dias.forEach(dia => {
      const d = this.disp[dia];
      if (d.noDisponible) { out[dia] = { noDisponible: true }; }
      else if (d.tipo === 'especifico') { out[dia] = { noDisponible: false, tipo: 'Franja específica', desde: d.desde, hasta: d.hasta }; }
      else {
        const label = this.opciones().find(o => o.value === d.tipo)?.label ?? this.opciones()[0]?.label ?? 'Todo el día (06:00–22:00)';
        out[dia] = { noDisponible: false, tipo: 'Franja general', franjaGeneral: label };
      }
    });
    return out;
  }

  private emitir(): void {
    this.onChange(this.construirValor());
    this.onTouched();
  }
}
