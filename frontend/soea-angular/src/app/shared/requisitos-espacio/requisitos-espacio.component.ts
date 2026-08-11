import { Component, forwardRef, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ControlValueAccessor, FormsModule, NG_VALUE_ACCESSOR } from '@angular/forms';
import { SearchableSelectComponent, SearchableOption } from '../searchable-select/searchable-select.component';
import { Espacio, RequisitoEspacio, TipoSesionUi } from '../../core/models';

/**
 * Requisito de espacio por tipo de sesión (HC-S03/HC-S05, petición 4): una fila por track
 * activo de la asignatura (teoría presencial / laboratorio — teoría virtual no consume
 * espacio, ver CalculadorEspaciosSesion.CumpleTipo). Cada fila deja restringir por tipo de
 * espacio, por un espacio concreto, o dejar la regla por defecto del dominio (sin entrada
 * en el array — no forzamos un requisito si el usuario no lo pide explícitamente, porque
 * eso silenciosamente estrecharía el default del backend, p. ej. excluir auditorios).
 */
@Component({
  selector: 'app-requisitos-espacio',
  standalone: true,
  imports: [CommonModule, FormsModule, SearchableSelectComponent],
  providers: [{
    provide: NG_VALUE_ACCESSOR,
    useExisting: forwardRef(() => RequisitosEspacioComponent),
    multi: true
  }],
  template: `
    @for (tipo of tipos(); track tipo) {
      <div class="track">
        <span class="tlabel">{{ etiqueta(tipo) }}</span>
        <select class="input" style="width:170px" [ngModel]="tipoEspacioDe(tipo)" (ngModelChange)="setTipoEspacio(tipo, $event)">
          <option value="">Cualquiera (regla por defecto)</option>
          <option value="Salon">Solo salón</option>
          <option value="Laboratorio">Solo laboratorio</option>
          <option value="Auditorio">Solo auditorio</option>
        </select>
        <div class="dfield" style="flex:1;margin:0">
          <app-searchable-select [ngModel]="espacioIdDe(tipo)" (ngModelChange)="setEspacioId(tipo, $event)"
            [options]="espacioOptions(tipo)" placeholder="Ninguno (usar tipo)"></app-searchable-select>
        </div>
      </div>
    }
  `,
  styles: [`
    .track { display: flex; align-items: center; gap: 10px; padding: 8px 10px; border: 1px solid var(--color-divider); }
    .tlabel { flex: 0 0 130px; font: 600 12px var(--font-heading); }
  `]
})
export class RequisitosEspacioComponent implements ControlValueAccessor {
  tipos = input<TipoSesionUi[]>([]);
  espacios = input<Espacio[]>([]);

  private value: RequisitoEspacio[] = [];
  private onChange: (v: RequisitoEspacio[]) => void = () => {};
  private onTouched: () => void = () => {};

  writeValue(v: RequisitoEspacio[] | null): void { this.value = v ?? []; }
  registerOnChange(fn: any): void { this.onChange = fn; }
  registerOnTouched(fn: any): void { this.onTouched = fn; }

  etiqueta(tipo: TipoSesionUi): string { return tipo === 'Laboratorio' ? 'Laboratorio' : 'Teoría presencial'; }

  private de(tipo: TipoSesionUi): RequisitoEspacio | undefined { return this.value.find(r => r.tipoSesion === tipo); }

  tipoEspacioDe(tipo: TipoSesionUi): string { return this.de(tipo)?.tipoEspacio ?? ''; }
  espacioIdDe(tipo: TipoSesionUi): string { return this.de(tipo)?.espacioId ?? ''; }

  espacioOptions(tipo: TipoSesionUi): SearchableOption[] {
    const filtroTipo = this.tipoEspacioDe(tipo);
    const lista = filtroTipo
      ? this.espacios().filter(e => e.tipo === this.aTipoEspacioLabel(filtroTipo))
      : this.espacios();
    return [{ value: '', label: 'Ninguno (usar tipo)' }, ...lista.map(e => ({ value: e.id, label: e.nombre, sub: e.edificio }))];
  }

  setTipoEspacio(tipo: TipoSesionUi, tipoEspacio: string) {
    if (!tipoEspacio) { this.quitar(tipo); return; }
    this.actualizar(tipo, { tipoEspacio: tipoEspacio as RequisitoEspacio['tipoEspacio'], espacioId: undefined });
  }

  setEspacioId(tipo: TipoSesionUi, espacioId: string) {
    if (!espacioId) {
      const existente = this.de(tipo);
      existente?.tipoEspacio ? this.actualizar(tipo, { espacioId: undefined }) : this.quitar(tipo);
      return;
    }
    const esp = this.espacios().find(e => e.id === espacioId);
    this.actualizar(tipo, { espacioId, tipoEspacio: esp ? this.aTipoEspacioSentinel(esp.tipo) : (this.tipoEspacioDe(tipo) as any || 'Salon') });
  }

  private quitar(tipo: TipoSesionUi) {
    this.value = this.value.filter(r => r.tipoSesion !== tipo);
    this.onChange(this.value);
    this.onTouched();
  }

  private actualizar(tipo: TipoSesionUi, patch: Partial<RequisitoEspacio>) {
    const existente = this.de(tipo);
    const nuevo: RequisitoEspacio = {
      tipoSesion: tipo,
      tipoEspacio: existente?.tipoEspacio ?? 'Salon',
      espacioId: existente?.espacioId,
      // ponytail: Sesiones no se lee en ningún punto del motor hoy (solo se transporta) —
      // sin control editable hasta que exista lógica que lo consuma.
      sesiones: existente?.sesiones ?? 1,
      ...patch
    };
    this.value = [...this.value.filter(r => r.tipoSesion !== tipo), nuevo];
    this.onChange(this.value);
    this.onTouched();
  }

  private aTipoEspacioLabel(t: string): Espacio['tipo'] { return t === 'Salon' ? 'Salón' : (t as Espacio['tipo']); }
  private aTipoEspacioSentinel(t: Espacio['tipo']): RequisitoEspacio['tipoEspacio'] { return t === 'Salón' ? 'Salon' : t; }
}
