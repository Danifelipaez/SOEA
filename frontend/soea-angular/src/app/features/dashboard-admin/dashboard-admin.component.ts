import { Component, inject, computed, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BaseChartDirective } from 'ng2-charts';
import { MatTableModule } from '@angular/material/table';
import { ChartConfiguration, ChartData } from 'chart.js';
import { StateService } from '../../core/state.service';
import { CatalogoService } from '../../core/catalogo.service';
import { RouterModule } from '@angular/router';
import { Espacio } from '../../core/models';

@Component({
  selector: 'app-dashboard-admin',
  standalone: true,
  imports: [CommonModule, BaseChartDirective, MatTableModule, RouterModule],
  template: `
    <div class="dashboard-container">
      <h1 class="page-title text-primary">Dashboard Admin</h1>

      <!-- Métricas del horario -->
      <div class="cards-row">
        <div class="metric-card">
          <div class="metric-title">% Ocupación total</div>
          <div class="metric-value">{{ ocupacionPct() }}%</div>
        </div>
        <div class="metric-card">
          <div class="metric-title">Total sesiones presenciales</div>
          <div class="metric-value">{{ totalPresenciales() }}</div>
        </div>
        <div class="metric-card">
          <div class="metric-title">Total sesiones virtuales</div>
          <div class="metric-value">{{ totalVirtuales() }}</div>
        </div>
        <div class="metric-card">
          <div class="metric-title">Total franjas ociosas</div>
          <div class="metric-value">{{ franjasOciosas() }}</div>
        </div>
      </div>

      <!-- Catálogo cargado -->
      <div class="section-title-row"><h2 class="section-title">Catálogo cargado</h2></div>
      <div class="cards-row cards-row-sm">
        <div class="metric-card metric-card-sm">
          <div class="metric-title">Asignaturas</div>
          <div class="metric-value metric-value-sm">{{ state.asignaturas().length }}</div>
        </div>
        <div class="metric-card metric-card-sm">
          <div class="metric-title">Docentes</div>
          <div class="metric-value metric-value-sm">{{ state.docentes().length }}</div>
        </div>
        <div class="metric-card metric-card-sm">
          <div class="metric-title">Espacios</div>
          <div class="metric-value metric-value-sm">{{ state.espacios().length }}</div>
        </div>
        <div class="metric-card metric-card-sm">
          <div class="metric-title">Programas</div>
          <div class="metric-value metric-value-sm">{{ state.programas().length }}</div>
        </div>
        <div class="metric-card metric-card-sm">
          <div class="metric-title">Facultades</div>
          <div class="metric-value metric-value-sm">{{ state.facultades().length }}</div>
        </div>
      </div>

      <div class="chart-row">
        <div class="card-box flex-1">
          <h2 class="section-title">Ocupación por espacio</h2>
          <div class="chart-container">
            <canvas baseChart
              [data]="barChartData()"
              [options]="barChartOptions"
              [type]="'bar'">
            </canvas>
          </div>
        </div>
      </div>

      <div class="tables-row">
        <div class="card-box flex-1">
          <h2 class="section-title">Ocupación por docente</h2>
          <table mat-table [dataSource]="docentesData()" class="mat-elevation-z0 border-table">
            <ng-container matColumnDef="docente">
              <th mat-header-cell *matHeaderCellDef> Docente </th>
              <td mat-cell *matCellDef="let element"> {{element.docente}} </td>
            </ng-container>
            <ng-container matColumnDef="horasAsignadas">
              <th mat-header-cell *matHeaderCellDef> Horas asig. </th>
              <td mat-cell *matCellDef="let element"> {{element.horasAsignadas}} </td>
            </ng-container>
            <ng-container matColumnDef="maxHoras">
              <th mat-header-cell *matHeaderCellDef> Máx. horas </th>
              <td mat-cell *matCellDef="let element"> {{element.maxHoras}} </td>
            </ng-container>
            <ng-container matColumnDef="porcentaje">
              <th mat-header-cell *matHeaderCellDef> % Carga </th>
              <td mat-cell *matCellDef="let element"> {{element.porcentaje}}% </td>
            </ng-container>
            <ng-container matColumnDef="estado">
              <th mat-header-cell *matHeaderCellDef> Estado </th>
              <td mat-cell *matCellDef="let element">
                <span class="status-badge" [ngClass]="element.estadoClass">{{element.estado}}</span>
              </td>
            </ng-container>
            <tr mat-header-row *matHeaderRowDef="['docente', 'horasAsignadas', 'maxHoras', 'porcentaje', 'estado']"></tr>
            <tr mat-row *matRowDef="let row; columns: ['docente', 'horasAsignadas', 'maxHoras', 'porcentaje', 'estado'];"></tr>
          </table>
        </div>
      </div>

      <div class="mini-matrix-row">
        <div class="card-box flex-1">
          <h2 class="section-title">Mapa de franjas ociosas</h2>
          @if (state.espacios().length === 0) {
            <p class="no-data-hint">Sin espacios cargados. Ve a <a routerLink="/ingesta">Ingesta</a> para cargar datos.</p>
          } @else {
            <div class="space-selector mb-16">
              @for (esp of state.espacios(); track esp.id) {
                <button class="pill-button"
                        [class.active]="matrixSpace()?.id === esp.id"
                        (click)="matrixSpace.set(esp)">
                  {{ esp.nombre }}
                </button>
              }
            </div>
            <div class="matrix-legend-row">
              <span class="dia-label" *ngFor="let d of diasLabel">{{ d }}</span>
            </div>
            <table class="mini-matrix">
              <tbody>
                <tr *ngFor="let f of franjasMiniIdx">
                  <td *ngFor="let d of diasIdx"
                      [class.ociosa]="isOciosaMini(f, d)"
                      [class.ocupada]="!isOciosaMini(f, d)"
                      [title]="franjaLabel(f) + ' · ' + diasLabel[d]">
                  </td>
                </tr>
              </tbody>
            </table>
            <div class="legend">
              <span class="legend-item"><span class="box ociosa"></span> Ociosa</span>
              <span class="legend-item"><span class="box ocupada"></span> Ocupada</span>
            </div>
          }
        </div>
      </div>

    </div>
  `,
  styles: [`
    .dashboard-container { padding: 16px; display: flex; flex-direction: column; gap: 24px; }
    .page-title { margin: 0; font-weight: 500; font-size: 24px; }
    .cards-row { display: flex; gap: 24px; }
    .metric-card { flex: 1; background: white; padding: 24px; border-radius: 8px; border: 1px solid #e0e0e0; display: flex; flex-direction: column; gap: 8px; }
    .metric-title { color: #616161; font-size: 14px; font-weight: 500; }
    .metric-value { font-size: 32px; font-weight: bold; color: #007bff; }
    
    .chart-row, .tables-row, .mini-matrix-row { display: flex; }
    .card-box { background: white; padding: 24px; border-radius: 8px; border: 1px solid #e0e0e0; }
    .flex-1 { flex: 1; }
    .section-title { margin: 0 0 16px 0; font-size: 18px; font-weight: 500; }
    .chart-container { height: 300px; }
    
    .border-table { border: 1px solid #e0e0e0; border-bottom: 0; width: 100%; }
    .status-badge { padding: 4px 8px; border-radius: 12px; font-size: 12px; font-weight: 500; }
    .status-normal { background: #e8f5e9; color: #2e7d32; }
    .status-alerta { background: #fff8e1; color: #f57f17; }
    .status-limite { background: #ffebee; color: #c62828; }
    
    .space-selector { display: flex; gap: 8px; overflow-x: auto; }
    .pill-button { padding: 6px 12px; border-radius: 16px; border: 1px solid #e0e0e0; background: white; cursor: pointer; transition: 0.2s; font-size: 12px; }
    .pill-button.active { background: #007bff; color: white; border-color: #007bff; }
    .mb-16 { margin-bottom: 16px; }
    
    .mini-matrix { width: 100%; border-collapse: collapse; }
    .mini-matrix td { height: 28px; border: 1px solid white; cursor: default; }
    .ociosa { background-color: #fff59d; }
    .ocupada { background-color: #bbdefb; }
    .legend { display: flex; gap: 16px; margin-top: 16px; font-size: 12px; }
    .legend-item { display: flex; align-items: center; gap: 4px; }
    .legend-item .box { width: 16px; height: 16px; border-radius: 2px; }
    .matrix-legend-row { display: flex; gap: 1px; margin-bottom: 4px; }
    .dia-label { flex: 1; text-align: center; font-size: 10px; font-weight: 600; color: #757575; }
    .no-data-hint { color: #9e9e9e; font-size: 14px; }
    .section-title-row { margin-bottom: 8px; }
    .cards-row-sm { gap: 12px; }
    .metric-card-sm { padding: 16px; }
    .metric-value-sm { font-size: 24px; }
  `]
})
export class DashboardAdminComponent implements OnInit {
  state    = inject(StateService);
  catalogo = inject(CatalogoService);

  matrixSpace = signal<Espacio | null>(null);

  ngOnInit() {
    if (this.state.espacios().length === 0) {
      this.catalogo.cargarTodo().subscribe({
        next: () => {
          this.matrixSpace.set(this.state.espacios()[0] ?? null);
        }
      });
    } else {
      this.matrixSpace.set(this.state.espacios()[0] ?? null);
    }
  }

  readonly diasLabel = ['Lun', 'Mar', 'Mié', 'Jue', 'Vie', 'Sáb'];
  readonly diasIdx   = [0, 1, 2, 3, 4, 5];
  readonly diasNombre = ['lunes','martes','miercoles','jueves','viernes','sabado'];
  /** Franjas de 07:00 a 20:00 (14 franjas de 1 h). */
  readonly franjasMiniIdx = Array.from({ length: 14 }, (_, i) => i);

  franjaLabel(idx: number): string {
    return `${String(7 + idx).padStart(2, '0')}:00`;
  }

  /** Devuelve true si la franja está libre (sin sesiones presenciales) para el espacio activo. */
  isOciosaMini(franjaIdx: number, diaIdx: number): boolean {
    const esp = this.matrixSpace() ?? this.state.espacios()[0];
    if (!esp || this.state.sesiones().length === 0) return true;
    const hora = this.franjaLabel(franjaIdx);
    const dia  = this.diasNombre[diaIdx];
    return !this.state.sesiones().some(s =>
      s.espacioId === esp.id && s.dia === dia && s.horaInicio === hora && !s.virtual
    );
  }

  // ── Metrics ──────────────────────────────────────────────────────────────────

  totalPresenciales = computed(() =>
    this.state.sesiones().filter(s => !s.virtual).length);

  totalVirtuales = computed(() =>
    this.state.sesiones().filter(s => s.virtual).length);

  /** Total slots available across all spaces × all day hours (07-20 = 13h × 6 days). */
  private totalSlots = computed(() => this.state.espacios().length * 13 * 6);

  ocupacionPct = computed(() => {
    const slots = this.totalSlots();
    if (slots === 0) return 0;
    return Math.round((this.state.sesiones().filter(s => !s.virtual).length / slots) * 100);
  });

  /** Idle 1-hour slots = total slots minus occupied presencial sessions. */
  franjasOciosas = computed(() =>
    Math.max(0, this.totalSlots() - this.totalPresenciales()));

  // ── Docentes table ────────────────────────────────────────────────────────────

  docentesData = computed(() => {
    const sesiones = this.state.sesiones();
    return this.state.docentes().map(d => {
      const sesDoc = sesiones.filter(s => s.docenteId === d.id);
      // Estimate hours: count sessions (each ~2h) or use horaFin-horaInicio when available
      const horasAsignadas = sesDoc.reduce((acc, s) => {
        const [hI, mI] = s.horaInicio.split(':').map(Number);
        const [hF, mF] = s.horaFin.split(':').map(Number);
        return acc + ((hF * 60 + mF) - (hI * 60 + mI)) / 60;
      }, 0);
      const maxHoras = d.maxHoras || 40;
      const porcentaje = maxHoras > 0 ? Math.round((horasAsignadas / maxHoras) * 100) : 0;
      const estado = porcentaje >= 100 ? 'Límite' : porcentaje >= 85 ? 'Alerta' : 'Normal';
      const estadoClass = porcentaje >= 100 ? 'status-limite' : porcentaje >= 85 ? 'status-alerta' : 'status-normal';
      return { docente: d.nombre, horasAsignadas: Math.round(horasAsignadas * 10) / 10, maxHoras, porcentaje, estado, estadoClass };
    });
  });

  // ── Bar chart ─────────────────────────────────────────────────────────────────

  barChartOptions: ChartConfiguration['options'] = {
    responsive: true,
    maintainAspectRatio: false,
    indexAxis: 'y',
    scales: { x: { min: 0, max: 100 } },
    plugins: { legend: { display: false } }
  };

  barChartData = computed<ChartData<'bar'>>(() => {
    const espacios = this.state.espacios();
    const sesiones = this.state.sesiones().filter(s => !s.virtual);
    const slotsPerSpace = 13 * 6;
    const labels = espacios.map(e => e.nombre);
    const data = espacios.map(e => {
      const occ = sesiones.filter(s => s.espacioId === e.id).length;
      return slotsPerSpace > 0 ? Math.round((occ / slotsPerSpace) * 100) : 0;
    });
    return {
      labels,
      datasets: [{
        data,
        backgroundColor: data.map(v => v > 92 ? '#dc3545' : '#007bff')
      }]
    };
  });

}
