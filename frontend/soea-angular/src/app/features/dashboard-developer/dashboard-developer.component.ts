import { Component, inject, signal, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTabsModule } from '@angular/material/tabs';
import { MatButtonModule } from '@angular/material/button';
import { ReactiveFormsModule, FormBuilder, Validators, FormGroup } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSliderModule } from '@angular/material/slider';
import { MatSnackBar } from '@angular/material/snack-bar';

@Component({
  selector: 'app-dashboard-developer',
  standalone: true,
  imports: [CommonModule, MatTabsModule, MatButtonModule, ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatSliderModule],
  template: `
    <div class="dashboard-container">
      <h1 class="page-title text-primary">Dashboard Developer</h1>
      
      <mat-tab-group class="dev-tabs">
        <mat-tab label="Logs del Algoritmo">
          <div class="tab-content">
            <div class="logs-actions">
              <button mat-stroked-button (click)="limpiarLogs()">Limpiar logs</button>
              <button mat-stroked-button (click)="exportarLogs()">Exportar logs (.txt)</button>
              <span class="run-info">Última ejecución: 2024-05-15 10:30 (Duración: 12.4s)</span>
            </div>
            <div class="logs-panel">
              <pre *ngFor="let log of logs()"><span [class.log-error]="log.includes('cuello de botella') || log.includes('Infactibilidad')">{{log}}</span></pre>
            </div>
          </div>
        </mat-tab>
        
        <mat-tab label="Configuración del Sistema">
          <div class="tab-content">
            <form [formGroup]="configForm" class="config-form" (ngSubmit)="guardarConfig()">
              
              <div class="config-section">
                <h3 class="section-subtitle">Parámetros del Algoritmo Genético</h3>
                <div class="form-grid">
                  <mat-form-field appearance="outline">
                    <mat-label>Tamaño de la población</mat-label>
                    <input matInput type="number" formControlName="pobSize" required>
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>Tasa de mutación (0-1)</mat-label>
                    <input matInput type="number" step="0.01" formControlName="mutRate" required>
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>Tasa de cruce (0-1)</mat-label>
                    <input matInput type="number" step="0.01" formControlName="crossRate" required>
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>Número máximo de generaciones</mat-label>
                    <input matInput type="number" formControlName="maxGen" required>
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>Umbral de convergencia</mat-label>
                    <input matInput type="number" step="0.001" formControlName="convThresh" required>
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>Elite preservation</mat-label>
                    <input matInput type="number" formControlName="elite" required>
                  </mat-form-field>
                </div>
                <div class="warning-msg" *ngIf="mutCrossWarning()">
                  ⚠️ Advertencia: Tasa de mutación + Tasa de cruce > 1.0
                </div>
              </div>

              <div class="config-section">
                <h3 class="section-subtitle">Límites Operativos</h3>
                <div class="form-grid">
                  <mat-form-field appearance="outline">
                    <mat-label>Hora máxima fin de clase</mat-label>
                    <input matInput type="time" formControlName="maxHoraFin" required>
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>Hora máxima fin sábados</mat-label>
                    <input matInput type="time" formControlName="maxHoraSab" required>
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>Intervalo mínimo entre sesiones (días dist.)</mat-label>
                    <input matInput type="number" formControlName="minIntervalo" required>
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>Máx. horas consecutivas sin penalización</mat-label>
                    <input matInput type="number" formControlName="maxHorasCons" required>
                  </mat-form-field>
                </div>
              </div>

              <div class="config-section">
                <h3 class="section-subtitle">Pesos de Soft Constraints (0 a 10)</h3>
                <div class="sliders-col">
                  <div class="slider-row">
                    <span class="slider-label">Ergonomía docente</span>
                    <mat-slider min="0" max="10" step="1" discrete>
                      <input matSliderThumb formControlName="pesoErgo">
                    </mat-slider>
                  </div>
                  <div class="slider-row">
                    <span class="slider-label">Minimización tiempos muertos</span>
                    <mat-slider min="0" max="10" step="1" discrete>
                      <input matSliderThumb formControlName="pesoTiempos">
                    </mat-slider>
                  </div>
                  <div class="slider-row">
                    <span class="slider-label">Priorización pedagógica</span>
                    <mat-slider min="0" max="10" step="1" discrete>
                      <input matSliderThumb formControlName="pesoPrio">
                    </mat-slider>
                  </div>
                  <div class="slider-row">
                    <span class="slider-label">Estabilidad de aula</span>
                    <mat-slider min="0" max="10" step="1" discrete>
                      <input matSliderThumb formControlName="pesoEstab">
                    </mat-slider>
                  </div>
                  <div class="slider-row">
                    <span class="slider-label">Franja almuerzo libre</span>
                    <mat-slider min="0" max="10" step="1" discrete>
                      <input matSliderThumb formControlName="pesoAlm">
                    </mat-slider>
                  </div>
                </div>
              </div>

              <div class="form-actions">
                <button mat-button type="button" (click)="restaurarValores()">Restaurar valores por defecto</button>
                <button mat-flat-button color="primary" class="primary-button" type="submit" [disabled]="configForm.invalid">Guardar configuración</button>
              </div>

            </form>
          </div>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: [`
    .dashboard-container { padding: 16px; background: white; border-radius: 8px; border: 1px solid #e0e0e0; min-height: 500px; }
    .page-title { margin-bottom: 24px; font-weight: 500; font-size: 24px; }
    .dev-tabs { margin-top: 16px; }
    .tab-content { padding: 24px 0; }
    
    .logs-actions { display: flex; gap: 16px; align-items: center; margin-bottom: 16px; }
    .run-info { margin-left: auto; color: #616161; font-size: 14px; }
    .logs-panel { background: #1e1e1e; color: #d4d4d4; padding: 16px; border-radius: 8px; height: 400px; overflow-y: auto; font-family: monospace; }
    .logs-panel pre { margin: 0; padding: 2px 0; }
    .log-error { color: #f44336; font-weight: bold; }
    
    .config-section { margin-bottom: 32px; padding: 24px; border: 1px solid #e0e0e0; border-radius: 8px; }
    .section-subtitle { margin-top: 0; margin-bottom: 24px; font-weight: 500; color: #424242; }
    .form-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(250px, 1fr)); gap: 16px; }
    .warning-msg { color: #dc3545; font-weight: 500; margin-top: 8px; }
    
    .sliders-col { display: flex; flex-direction: column; gap: 16px; max-width: 600px; }
    .slider-row { display: flex; align-items: center; gap: 24px; }
    .slider-label { width: 250px; font-weight: 500; }
    mat-slider { flex: 1; margin: 0; }
    
    .form-actions { display: flex; justify-content: flex-end; gap: 16px; margin-top: 24px; }
  `]
})
export class DashboardDeveloperComponent implements OnDestroy {
  fb = inject(FormBuilder);
  snackBar = inject(MatSnackBar);

  logs = signal<string[]>([
    '[INFO] Iniciando pipeline de optimización...',
    '[INFO] Fase 1: Pre-procesamiento (coloración de grafos) iniciada.',
    '[INFO] Fase 1 completada en 120ms.',
    '[INFO] Fase 2: Viabilidad (CP-SAT) iniciada.',
    '[WARN] Infactibilidad encontrada: Conflicto de espacio en franja 10:00-12:00 Lunes.',
    '[INFO] Resolviendo infactibilidad mediante relajación de prioridades.',
    '[INFO] Fase 2 completada en 2300ms.',
    '[INFO] Fase 3: Algoritmo genético iniciado. Población: 100, Generaciones: 500.',
    '[INFO] Gen 1 | Fitness: 0.45',
    '[INFO] Gen 50 | Fitness: 0.72 | Posible cuello de botella: Mutación lenta.',
    '[INFO] Gen 500 | Fitness: 0.94',
    '[INFO] Fase 3 completada en 8400ms.'
  ]);

  configForm: FormGroup;
  valChangeSub: any;

  constructor() {
    this.configForm = this.fb.group({
      pobSize: [100, Validators.required],
      mutRate: [0.05, Validators.required],
      crossRate: [0.80, Validators.required],
      maxGen: [500, Validators.required],
      convThresh: [0.001, Validators.required],
      elite: [10, Validators.required],
      maxHoraFin: ['21:30', Validators.required],
      maxHoraSab: ['14:00', Validators.required],
      minIntervalo: [0, Validators.required],
      maxHorasCons: [4, Validators.required],
      pesoErgo: [5],
      pesoTiempos: [5],
      pesoPrio: [5],
      pesoEstab: [5],
      pesoAlm: [5]
    });

    this.valChangeSub = this.configForm.valueChanges.subscribe(() => {
      // Re-evaluate signals if needed, or we just compute on demand
    });
  }

  mutCrossWarning(): boolean {
    const m = this.configForm.get('mutRate')?.value || 0;
    const c = this.configForm.get('crossRate')?.value || 0;
    return (m + c) > 1.0;
  }

  limpiarLogs() {
    this.logs.set([]);
  }

  exportarLogs() {
    const blob = new Blob([this.logs().join('\n')], { type: 'text/plain' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'soea_logs.txt';
    a.click();
    window.URL.revokeObjectURL(url);
  }

  restaurarValores() {
    this.configForm.patchValue({
      pobSize: 100,
      mutRate: 0.05,
      crossRate: 0.80,
      maxGen: 500,
      convThresh: 0.001,
      elite: 10,
      maxHoraFin: '21:30',
      maxHoraSab: '14:00',
      minIntervalo: 0,
      maxHorasCons: 4,
      pesoErgo: 5,
      pesoTiempos: 5,
      pesoPrio: 5,
      pesoEstab: 5,
      pesoAlm: 5
    });
  }

  guardarConfig() {
    if (this.configForm.valid) {
      this.snackBar.open('Configuración guardada correctamente', 'Cerrar', { duration: 3000 });
    }
  }

  ngOnDestroy() {
    if (this.valChangeSub) this.valChangeSub.unsubscribe();
  }
}
