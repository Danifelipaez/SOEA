import { Component, input, forwardRef, computed, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule, ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { toSignal } from '@angular/core/rxjs-interop';

export interface SearchableOption {
  value: string;
  label: string;
  sub?: string;
}

/**
 * Select buscable: filtra opciones al escribir, pero solo acepta un valor de la lista —
 * revierte al último válido si el texto no coincide con ninguna opción al perder foco.
 * Implementa ControlValueAccessor: funciona con [(ngModel)] y formControlName por igual,
 * como reemplazo directo de <select class="input">.
 */
@Component({
  selector: 'app-searchable-select',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MatAutocompleteModule],
  providers: [{
    provide: NG_VALUE_ACCESSOR,
    useExisting: forwardRef(() => SearchableSelectComponent),
    multi: true
  }],
  template: `
    <div class="ss-wrap">
      <input class="input ss-input" #inputEl
             [formControl]="ctrl"
             [matAutocomplete]="auto"
             [placeholder]="placeholder()"
             (blur)="onBlur()"
             (focus)="inputEl.select()">
      <span class="ss-caret">▾</span>
      <mat-autocomplete #auto="matAutocomplete" [displayWith]="displayFn" (optionSelected)="onSelected($event)">
        @for (opt of filtered(); track opt.value) {
          <mat-option [value]="opt.value">
            <span>{{ opt.label }}</span>
            @if (opt.sub) { <span class="ss-sub">{{ opt.sub }}</span> }
          </mat-option>
        }
        @if (filtered().length === 0) {
          <mat-option disabled>Sin resultados</mat-option>
        }
      </mat-autocomplete>
    </div>
  `,
  styles: [`
    .ss-wrap { position: relative; }
    .ss-input { padding-right: 24px; }
    .ss-caret { position: absolute; right: 9px; top: 50%; transform: translateY(-50%); color: var(--color-neutral-500); pointer-events: none; font-size: 11px; }
    .ss-sub { margin-left: 8px; font-size: 11px; color: var(--color-neutral-500); }
  `]
})
export class SearchableSelectComponent implements ControlValueAccessor {
  // Inputs con signal (no @Input clásico): `filtered` necesita leerlos de forma reactiva —
  // un @Input de propiedad no dispara recomputo de computed() cuando cambia sin que el
  // usuario escriba, dejando el panel con opciones obsoletas (o vacío) tras un cambio en
  // cascada (p. ej. Facultad → Programa) hasta que el usuario tecleara algo.
  options = input<SearchableOption[]>([]);
  placeholder = input('— Seleccione —');
  disabled = input(false);

  ctrl = new FormControl<string>('');
  private lastValid = '';
  private raw = toSignal(this.ctrl.valueChanges, { initialValue: '' });

  private onChangeFn: (v: string) => void = () => {};
  private onTouchedFn: () => void = () => {};

  displayFn = (id: string): string => this.options().find(o => o.value === id)?.label ?? '';

  filtered = computed(() => {
    const opts = this.options();
    const value = this.raw() ?? '';
    const isConfirmedSelection = opts.some(o => o.value === value);
    // Selección ya confirmada (no escribiendo) → mostrar todas al reabrir; si no, filtrar por lo tecleado.
    const term = isConfirmedSelection ? '' : value.trim().toLowerCase();
    if (!term) return opts;
    return opts.filter(o =>
      o.label.toLowerCase().includes(term) || (o.sub ?? '').toLowerCase().includes(term));
  });

  constructor() {
    effect(() => { this.disabled() ? this.ctrl.disable() : this.ctrl.enable(); });
  }

  writeValue(value: string): void {
    this.lastValid = value ?? '';
    this.ctrl.setValue(this.lastValid, { emitEvent: false });
  }
  registerOnChange(fn: any): void { this.onChangeFn = fn; }
  registerOnTouched(fn: any): void { this.onTouchedFn = fn; }
  setDisabledState(isDisabled: boolean): void { isDisabled ? this.ctrl.disable() : this.ctrl.enable(); }

  onSelected(event: MatAutocompleteSelectedEvent) {
    const id = event.option.value as string;
    this.lastValid = id;
    this.onChangeFn(id);
  }

  onBlur() {
    const current = this.ctrl.value ?? '';
    if (current !== this.lastValid) {
      // Texto libre sin coincidencia exacta con una opción → revertir (no se acepta texto libre).
      this.ctrl.setValue(this.lastValid, { emitEvent: false });
    }
    this.onTouchedFn();
  }
}
