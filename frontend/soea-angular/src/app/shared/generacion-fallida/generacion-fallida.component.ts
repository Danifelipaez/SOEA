import { Component, computed, inject } from '@angular/core';
import { RouterModule } from '@angular/router';
import { StateService } from '../../core/state.service';
import { CIERRE_NO_CABEN, INTRO_NO_CABEN, gruposNoCabenDetalle } from '../../features/horario/horario.reglas';

/**
 * Aviso persistente del último intento fallido de generar (lo pintan /horario y /revisar). Antes, con
 * un horario ya guardado, el fallo solo salía en un snackbar de 9 s con los grupos en un párrafo
 * separado por «;», sin camino para corregirlos. Lo que se proyecta (<ng-content>) son acciones
 * propias de cada pantalla.
 */
@Component({
  selector: 'app-generacion-fallida',
  standalone: true,
  imports: [RouterModule],
  template: `
    @if (state.errorGeneracion()) {
      <div class="errb gf" role="alert">
        <b>✕ No se pudo generar el horario.</b>
        @if (gruposNoCaben().length > 0) {
          <span>{{ intro }}:</span>
          <ul>@for (g of gruposNoCaben(); track g) { <li>{{ g }}</li> }</ul>
          <span>{{ cierre }}</span>
        } @else {
          <span>{{ state.errorGeneracion() }}</span>
        }
        @if (state.sesiones().length > 0) {
          <span class="gf-nota">El horario guardado anterior no cambió.</span>
        }
        <div class="gf-acc">
          @if (state.gruposEnConflictoSet().size > 0) {
            <a class="btn btn-secondary" routerLink="/catalogo" [queryParams]="{ estado: 'incompletas' }">Ver estos grupos en Catálogo</a>
          }
          <ng-content />
        </div>
      </div>
    }
  `,
  styles: [`
    .gf { display: flex; flex-direction: column; gap: 6px; align-items: flex-start; font-size: 13px; }
    .gf ul { margin: 0; padding-left: 18px; max-height: 200px; overflow-y: auto; }
    .gf-nota { font-style: italic; }
    .gf-acc { display: flex; gap: 10px; flex-wrap: wrap; align-items: center; margin-top: 4px; }
  `]
})
export class GeneracionFallidaComponent {
  state = inject(StateService);
  readonly intro = INTRO_NO_CABEN;
  readonly cierre = CIERRE_NO_CABEN;

  gruposNoCaben = computed(() => this.state.motivoInfactibilidad() === 'FranjaGrupo'
    ? gruposNoCabenDetalle(this.state.gruposEnConflicto(), this.state.grupos(), this.state.contextoCapacidad())
    : []);
}
