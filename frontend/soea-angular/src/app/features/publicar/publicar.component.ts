import { Component } from '@angular/core';
import { RouterModule } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

/** Paso 5 del journey — reservado, bloqueado hasta que exista PublicarHorarioService en el backend. */
@Component({
  selector: 'app-publicar',
  standalone: true,
  imports: [RouterModule, MatButtonModule, MatIconModule],
  template: `
    <div class="publicar-container">
      <mat-icon class="icon">lock_clock</mat-icon>
      <h1 class="title">Publicar</h1>
      <p class="desc">
        Esta acción todavía no está disponible: el backend no tiene implementado el servicio de
        publicación de horarios. El lugar en el flujo queda reservado para cuando exista.
      </p>
      <button mat-stroked-button routerLink="/revisar">
        <mat-icon>arrow_back</mat-icon> Volver a Revisar
      </button>
    </div>
  `,
  styles: [`
    .publicar-container { max-width: 480px; margin: 64px auto; text-align: center; padding: 40px 24px;
      background: #fff; border: 1px solid #e0e0e0; border-radius: 8px; }
    .icon { font-size: 48px; width: 48px; height: 48px; color: #bdbdbd; margin-bottom: 12px; }
    .title { margin: 0 0 8px; font-size: 22px; font-weight: 500; color: #616161; }
    .desc { color: #757575; font-size: 14px; line-height: 1.5; margin-bottom: 24px; }
  `]
})
export class PublicarComponent {}
