import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTabsModule } from '@angular/material/tabs';
import { MatSnackBar } from '@angular/material/snack-bar';
import { CatalogoService } from '../../core/catalogo.service';
import { EspaciosTabComponent } from './espacios-tab/espacios-tab.component';
import { DocentesTabComponent } from './docentes-tab/docentes-tab.component';
import { AsignaturasTabComponent } from './asignaturas-tab/asignaturas-tab.component';

@Component({
  selector: 'app-ingesta',
  standalone: true,
  imports: [CommonModule, MatTabsModule, EspaciosTabComponent, DocentesTabComponent, AsignaturasTabComponent],
  template: `
    <div class="ingesta-container">
      <h1 class="page-title text-primary">Ingesta de Datos</h1>
      <mat-tab-group>
        <mat-tab label="Espacios">
          <app-espacios-tab></app-espacios-tab>
        </mat-tab>
        <mat-tab label="Docentes">
          <app-docentes-tab></app-docentes-tab>
        </mat-tab>
        <mat-tab label="Asignaturas">
          <app-asignaturas-tab></app-asignaturas-tab>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: [`
    .ingesta-container { padding: 16px; background: white; border-radius: 8px; border: 1px solid #e0e0e0; }
    .page-title { margin-bottom: 24px; font-weight: 500; font-size: 24px; }
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
