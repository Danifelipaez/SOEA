import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BaseChartDirective } from 'ng2-charts';
import { MatTableModule } from '@angular/material/table';
import { ChartConfiguration, ChartData, ChartType } from 'chart.js';
import { StateService } from '../../core/state.service';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'app-dashboard-admin',
  standalone: true,
  imports: [CommonModule, BaseChartDirective, MatTableModule, RouterModule],
  template: `
    <div class="dashboard-container">
      <h1 class="page-title text-primary">Dashboard Admin</h1>

      <div class="cards-row">
        <div class="metric-card">
          <div class="metric-title">% Ocupación total</div>
          <div class="metric-value">84%</div>
        </div>
        <div class="metric-card">
          <div class="metric-title">Total sesiones presenciales</div>
          <div class="metric-value">124</div>
        </div>
        <div class="metric-card">
          <div class="metric-title">Total sesiones virtuales</div>
          <div class="metric-value">32</div>
        </div>
        <div class="metric-card">
          <div class="metric-title">Total franjas ociosas</div>
          <div class="metric-value">45</div>
        </div>
      </div>

      <div class="chart-row">
        <div class="card-box flex-1">
          <h2 class="section-title">Ocupación por espacio</h2>
          <div class="chart-container">
            <canvas baseChart
              [data]="barChartData"
              [options]="barChartOptions"
              [type]="'bar'">
            </canvas>
          </div>
        </div>
      </div>

      <div class="tables-row">
        <div class="card-box flex-1">
          <h2 class="section-title">Ocupación por docente</h2>
          <table mat-table [dataSource]="docentesData" class="mat-elevation-z0 border-table" matSort>
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
          <div class="space-selector mb-16">
            <button class="pill-button active">Laboratorio A</button>
            <button class="pill-button">Laboratorio B</button>
            <button class="pill-button">Salón 101</button>
          </div>
          <table class="mini-matrix">
            <tbody>
              <tr *ngFor="let f of [1,2,3,4,5,6,7,8]">
                <td *ngFor="let d of [1,2,3,4,5,6]" [class.ociosa]="isOciosa(f,d)" [class.ocupada]="!isOciosa(f,d)"></td>
              </tr>
            </tbody>
          </table>
          <div class="legend">
            <span class="legend-item"><span class="box ociosa"></span> Ociosa</span>
            <span class="legend-item"><span class="box ocupada"></span> Ocupada</span>
          </div>
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
    .mini-matrix td { height: 30px; border: 1px solid white; }
    .ociosa { background-color: #fff59d; } /* amarillo */
    .ocupada { background-color: #bbdefb; } /* azul claro */
    .legend { display: flex; gap: 16px; margin-top: 16px; font-size: 12px; }
    .legend-item { display: flex; align-items: center; gap: 4px; }
    .legend-item .box { width: 16px; height: 16px; border-radius: 2px; }
  `]
})
export class DashboardAdminComponent {
  
  barChartOptions: ChartConfiguration['options'] = {
    responsive: true,
    maintainAspectRatio: false,
    indexAxis: 'y',
    scales: { x: { min: 0, max: 100 } },
    plugins: {
      legend: { display: false }
    }
  };

  barChartData: ChartData<'bar'> = {
    labels: ['Laboratorio A', 'Laboratorio B', 'Salón 101'],
    datasets: [
      {
        data: [85, 95, 70],
        backgroundColor: (ctx) => {
          const val = ctx.raw as number;
          return val > 92 ? '#dc3545' : '#007bff';
        }
      }
    ]
  };

  docentesData = [
    { docente: 'Juan Perez', horasAsignadas: 30, maxHoras: 40, porcentaje: 75, estado: 'Normal', estadoClass: 'status-normal' },
    { docente: 'Maria Gomez', horasAsignadas: 38, maxHoras: 40, porcentaje: 95, estado: 'Alerta', estadoClass: 'status-alerta' },
    { docente: 'Carlos Ruiz', horasAsignadas: 40, maxHoras: 40, porcentaje: 100, estado: 'Límite', estadoClass: 'status-limite' }
  ];

  isOciosa(f: number, d: number) {
    return (f + d) % 3 === 0; // random pattern for demo
  }
}
