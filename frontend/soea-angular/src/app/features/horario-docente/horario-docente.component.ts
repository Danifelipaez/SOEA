import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { StateService } from '../../core/state.service';
import { CatalogoService } from '../../core/catalogo.service';
import { HorarioGridComponent } from '../../shared/horario-grid/horario-grid.component';
import { Docente } from '../../core/models';

@Component({
  selector: 'app-horario-docente',
  standalone: true,
  imports: [
    CommonModule, RouterModule, MatButtonModule, MatIconModule,
    MatSnackBarModule, HorarioGridComponent
  ],
  template: `
    <div class="page-container">
      <div class="header-actions">
        <h1 class="page-title text-primary">Horario por Docente</h1>
        <button mat-stroked-button (click)="recargar()" [disabled]="cargando()">
          <mat-icon>refresh</mat-icon> Recargar datos
        </button>
      </div>

      @if (cargando()) {
        <div class="loading-hint">Cargando datos…</div>
      }

      @if (!cargando() && state.docentes().length === 0) {
        <div class="empty-state">
          <mat-icon>person_off</mat-icon>
          <p>No hay docentes cargados. Ve a <strong>Ingesta de Datos</strong> para comenzar.</p>
          <button mat-stroked-button routerLink="/ingesta">Ir a Ingesta</button>
        </div>
      }

      @if (!cargando() && state.docentes().length > 0) {
        <!-- Selector semana A/B -->
        <div class="week-selector">
          <span class="week-label">Semana</span>
          <button class="pill-button week-btn"
                  [class.active]="activeWeek() === 'A'"
                  (click)="activeWeek.set('A')">
            <span class="week-letter">A</span>
            <span class="week-sub">pares</span>
          </button>
          <button class="pill-button week-btn"
                  [class.active]="activeWeek() === 'B'"
                  (click)="activeWeek.set('B')">
            <span class="week-letter">B</span>
            <span class="week-sub">impares</span>
          </button>
          @if (activeWeek() === 'A') {
            <span class="week-desc">TipoA presencial · TipoB virtual</span>
          } @else {
            <span class="week-desc">TipoB presencial · TipoA virtual</span>
          }
        </div>

        <!-- Selector de docente -->
        <div class="docente-selector-wrap">
          <span class="selector-label">Docente</span>
          <div class="docente-pills">
            @for (d of state.docentes(); track d.id) {
              <button class="pill-button"
                      [class.active]="activeDocente()?.id === d.id"
                      (click)="activeDocente.set(d)">
                {{ d.nombre }}
                @if (contarSesiones(d.id) > 0) {
                  <span class="count-badge">{{ contarSesiones(d.id) }}</span>
                }
              </button>
            }
          </div>
        </div>

        @if (!activeDocente()) {
          <div class="select-hint">
            <mat-icon>touch_app</mat-icon>
            <span>Selecciona un docente para ver su horario semanal.</span>
          </div>
        }

        @if (activeDocente()) {
          <div class="grid-header">
            <span class="docente-nombre">{{ activeDocente()!.nombre }}</span>
            <span class="sesiones-count">
              {{ sesionesDocente().length }} sesión(es) en semana {{ activeWeek() }}
            </span>
          </div>

          @if (sesionesDocente().length === 0) {
            <div class="no-sesiones">
              <mat-icon>event_busy</mat-icon>
              <span>Este docente no tiene sesiones asignadas en semana {{ activeWeek() }}.</span>
            </div>
          } @else {
            <app-horario-grid
              [sesiones]="todasSesionesDocente()"
              [activeWeek]="activeWeek()"
              [modoDocente]="true">
            </app-horario-grid>
          }
        }
      }
    </div>
  `,
  styles: [`
    .page-container { padding: 16px; background: white; border-radius: 8px; border: 1px solid #e0e0e0; min-height: 500px; }
    .header-actions { display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px; }
    .page-title { margin: 0; font-weight: 500; font-size: 24px; }
    .loading-hint { color: #757575; font-style: italic; margin-bottom: 16px; }

    .week-selector { display: flex; gap: 8px; align-items: center; margin-bottom: 20px; flex-wrap: wrap; }
    .week-label { font-size: 11px; letter-spacing: .08em; text-transform: uppercase; color: #757575; }
    .week-btn { display: flex; flex-direction: column; align-items: center; padding: 6px 18px; min-width: 72px; }
    .week-letter { font-size: 15px; font-weight: 700; line-height: 1; }
    .week-sub { font-size: 9px; color: #9e9e9e; letter-spacing: .05em; margin-top: 1px; }
    .pill-button.week-btn.active .week-sub { color: rgba(255,255,255,.75); }
    .week-desc { font-size: 11px; color: #9e9e9e; align-self: center; margin-left: 4px; font-style: italic; }

    .docente-selector-wrap { display: flex; flex-direction: column; gap: 8px; margin-bottom: 20px; }
    .selector-label { font-size: 11px; letter-spacing: .08em; text-transform: uppercase; color: #757575; }
    .docente-pills { display: flex; gap: 8px; flex-wrap: wrap; }
    .pill-button { padding: 7px 14px; border-radius: 20px; border: 1px solid #e0e0e0; background: white; cursor: pointer; transition: .15s; display: flex; align-items: center; gap: 6px; font-size: 13px; }
    .pill-button.active { background: #1976d2; color: white; border-color: #1976d2; }
    .count-badge { background: rgba(0,0,0,.12); border-radius: 10px; padding: 1px 6px; font-size: 10px; font-weight: 600; }
    .pill-button.active .count-badge { background: rgba(255,255,255,.25); }

    .select-hint { display: flex; align-items: center; gap: 10px; color: #9e9e9e; padding: 32px; justify-content: center; font-size: 15px; }
    .select-hint mat-icon { font-size: 24px; width: 24px; height: 24px; }

    .grid-header { display: flex; align-items: baseline; gap: 12px; margin-bottom: 12px; }
    .docente-nombre { font-size: 18px; font-weight: 600; color: #1565c0; }
    .sesiones-count { font-size: 13px; color: #757575; }

    .no-sesiones { display: flex; align-items: center; gap: 10px; color: #9e9e9e; padding: 32px; justify-content: center; font-size: 14px; border: 1px dashed #e0e0e0; border-radius: 8px; }
    .no-sesiones mat-icon { font-size: 24px; width: 24px; height: 24px; }

    .empty-state { text-align: center; padding: 64px 32px; color: #757575; }
    .empty-state mat-icon { font-size: 64px; width: 64px; height: 64px; margin-bottom: 16px; }
    .empty-state p { font-size: 16px; margin-bottom: 16px; }
  `]
})
export class HorarioDocenteComponent implements OnInit {
  state    = inject(StateService);
  catalogo = inject(CatalogoService);
  snackBar = inject(MatSnackBar);

  cargando      = signal(false);
  activeWeek    = signal<'A' | 'B'>('A');
  activeDocente = signal<Docente | null>(null);

  /** Todas las sesiones del docente (ambas semanas) — se pasan al grid para que filtre por activeWeek. */
  todasSesionesDocente = computed(() => {
    const doc = this.activeDocente();
    if (!doc) return [];
    return this.state.sesiones().filter(s => s.docenteId === doc.id);
  });

  /** Sesiones del docente visibles en la semana activa — solo para el contador del header. */
  sesionesDocente = computed(() => {
    const week = this.activeWeek();
    return this.todasSesionesDocente().filter(s => {
      if (s.semana) return s.semana === week;
      if (s.alternancia === 'SinAlternancia') return true;
      return week === 'A' ? s.alternancia === 'TipoA' : s.alternancia === 'TipoB';
    });
  });

  ngOnInit() {
    if (this.state.docentes().length === 0) {
      this.recargar();
    }
  }

  recargar() {
    this.cargando.set(true);
    this.catalogo.cargarTodo().subscribe({
      next: () => {
        this.cargando.set(false);
        if (!this.activeDocente()) {
          const primero = this.state.docentes().find(d =>
            this.state.sesiones().some(s => s.docenteId === d.id)
          ) ?? this.state.docentes()[0] ?? null;
          this.activeDocente.set(primero);
        }
      },
      error: () => {
        this.cargando.set(false);
        this.snackBar.open('No se pudo conectar con la API.', 'Cerrar', { duration: 5000, panelClass: ['snack-error'] });
      }
    });
  }

  contarSesiones(docenteId: string): number {
    return this.state.sesiones().filter(s => s.docenteId === docenteId).length;
  }
}
