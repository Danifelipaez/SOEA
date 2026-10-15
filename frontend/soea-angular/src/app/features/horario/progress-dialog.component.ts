import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDialogModule } from '@angular/material/dialog';

// ═══ Diálogo de progreso (HF-3 · B — generando) ═══
// FE15 auditoría: antes mostraba "Fase 1/3 · GraphColoring" → "2/3 · CP-SAT" → "3/3 · Algoritmo
// genético" avanzando con setTimeout a los 2s y 10s fijos, sin relación con el progreso real del
// backend (que puede tardar segundos o, en un caso patológico, mucho más — ver PERF1). Le mostraba
// al coordinador (no técnico) jerga de motor y una barra que mentía sobre cuánto faltaba. Sin
// progreso real que reportar (el backend no transmite eventos de avance), es más honesto un
// indicador indeterminado que no finge saber en qué fase va.
@Component({
  selector: 'app-progress-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule],
  template: `
    <div class="popbd" style="align-items:center;text-align:center;gap:13px;padding:26px 22px">
      <div class="spinner"></div>
      <div class="opt">GENERANDO EL HORARIO…</div>
      <div class="prog"><i></i></div>
      <div class="text-muted" style="font-size:12px">Puede tardar hasta 2 min. No cierres la ventana.</div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .spinner { width: 44px; height: 44px; border: 3px solid var(--color-accent-200); border-top-color: var(--color-accent); border-radius: 50%; animation: soea-spin .9s linear infinite; }
    @keyframes soea-spin { to { transform: rotate(360deg); } }
    .opt { font: 600 15px var(--font-heading); letter-spacing: .06em; color: var(--color-accent); }
    .prog { width: 100%; height: 12px; border: 1px solid var(--color-neutral-700); position: relative; overflow: hidden; }
    .prog > i { position: absolute; inset: 0 auto 0 0; width: 30%; background: var(--color-accent); animation: soea-prog 2.4s ease-in-out infinite; }
    @keyframes soea-prog { 0% { width: 12%; } 50% { width: 70%; } 100% { width: 92%; } }
  `]
})
export class ProgressDialogComponent {}
