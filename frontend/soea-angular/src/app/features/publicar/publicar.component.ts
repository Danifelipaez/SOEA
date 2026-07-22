import { Component } from '@angular/core';
import { RouterModule } from '@angular/router';

/** Paso 5 del journey — reservado, bloqueado hasta que exista PublicarHorarioService en el backend. */
@Component({
  selector: 'app-publicar',
  standalone: true,
  imports: [RouterModule],
  template: `
    <div class="blueprint elev-md publicar">
      <i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>
      <span class="soea-tag">Paso 5 · bloqueado</span>
      <div class="lock">🔒</div>
      <h1 class="title">Publicar</h1>
      <p class="desc">
        Esta acción todavía no está disponible: el backend no tiene implementado el servicio de
        publicación de horarios. El lugar en el flujo queda reservado para cuando exista.
      </p>
      <a class="btn btn-secondary" routerLink="/revisar">← Volver a Revisar</a>
    </div>
  `,
  styles: [`
    .publicar { max-width: 480px; margin: 40px auto; text-align: center; padding: 40px 28px;
      display: flex; flex-direction: column; align-items: center; gap: 12px; background: var(--color-bg); }
    .soea-tag { font: 600 11px var(--font-heading); letter-spacing: .1em; text-transform: uppercase;
      background: var(--color-neutral-400); color: #fff; padding: 4px 10px; }
    .lock { font-size: 40px; opacity: .5; margin-top: 6px; }
    .title { margin: 0; font-size: 26px; color: var(--color-neutral-700); }
    .desc { color: var(--color-neutral-600); font-size: 14px; line-height: 1.5; margin: 0 0 8px; }
  `]
})
export class PublicarComponent {}
