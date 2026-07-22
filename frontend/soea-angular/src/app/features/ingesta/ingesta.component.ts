import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTabsModule } from '@angular/material/tabs';
import { MatSnackBar } from '@angular/material/snack-bar';
import { CatalogoService } from '../../core/catalogo.service';
import { EspaciosTabComponent } from './espacios-tab/espacios-tab.component';
import { GrupoTabComponent } from './grupo-tab/grupo-tab.component';
import { AsignaturasTabComponent } from './asignaturas-tab/asignaturas-tab.component';
import { DocentesTabComponent } from './docentes-tab/docentes-tab.component';
import { AlternanciaTabComponent } from './alternancia-tab/alternancia-tab.component';

@Component({
  selector: 'app-ingesta',
  standalone: true,
  imports: [CommonModule, MatTabsModule, EspaciosTabComponent, GrupoTabComponent, AsignaturasTabComponent, DocentesTabComponent, AlternanciaTabComponent],
  template: `
    <div class="cat-head">
      <span class="soea-tag">Paso 1</span>
      <h1 class="cat-title">Catálogo del semestre</h1>
      <span class="text-muted cat-sub">Asignaturas, docentes, espacios, grupos y alternancia en una sola vista.</span>
    </div>
    <div class="blueprint elev-md ingesta-container">
      <i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>
      <mat-tab-group>
        <mat-tab label="Asignaturas">
          <app-asignaturas-tab></app-asignaturas-tab>
        </mat-tab>
        <mat-tab label="Docentes">
          <app-docentes-tab></app-docentes-tab>
        </mat-tab>
        <mat-tab label="Espacios">
          <app-espacios-tab></app-espacios-tab>
        </mat-tab>
        <mat-tab label="Grupos">
          <app-grupo-tab></app-grupo-tab>
        </mat-tab>
        <mat-tab label="Alternancia">
          <app-alternancia-tab></app-alternancia-tab>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: [`
    .cat-head { display: flex; align-items: baseline; gap: 12px; margin-bottom: 18px; flex-wrap: wrap; }
    .soea-tag { font: 600 11px var(--font-heading); letter-spacing: .1em; text-transform: uppercase;
      background: var(--color-accent-800); color: #fff; padding: 4px 10px; }
    .cat-title { margin: 0; font-size: 26px; letter-spacing: .01em; }
    .cat-sub { font-size: 13px; }
    .ingesta-container { background: var(--color-bg); padding: 4px 22px 22px; }
    .ingesta-container ::ng-deep .mat-mdc-tab-body-content { overflow: visible; }
  `]
})
export class IngestaComponent implements OnInit {
  private catalogo = inject(CatalogoService);
  private snackBar = inject(MatSnackBar);

  ngOnInit() {
    // Hidratar el catálogo completo al entrar: sin esto, las tabs muestran lo que
    // haya dejado otra vista en el StateService (p. ej. asignaturas sin facultades).
    this.catalogo.cargarTodo().subscribe({
      error: () => this.snackBar.open(
        'No se pudieron cargar los datos desde la BD. Verifica que la API esté activa.',
        'Cerrar', { duration: 5000 }
      )
    });
  }
}
