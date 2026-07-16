import { Component, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NavigationEnd, Router, RouterModule } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map, startWith } from 'rxjs';

interface JourneyStep {
  path: string;
  label: string;
  badge: string;
  disabled?: boolean;
}

const STEPS: JourneyStep[] = [
  { path: '/catalogo', label: 'Catálogo', badge: '1' },
  { path: '/horario', label: 'Horario', badge: '2·3' },
  { path: '/revisar', label: 'Revisar', badge: '4' },
  { path: '/publicar', label: 'Publicar', badge: '5', disabled: true },
];

/** Barra de navegación por flujo (journey), reemplaza el navbar plano anterior — ver docs/MAPEO_FLUJOS_FRONTEND.md */
@Component({
  selector: 'app-journey-bar',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <nav class="journey-bar">
      <span class="brand">SOEA</span>
      <div class="steps">
        @for (step of steps(); track step.path; let last = $last) {
          <a class="step"
             [class.active]="step.active"
             [class.done]="step.done"
             [class.disabled]="step.disabled"
             [routerLink]="step.disabled ? null : step.path">
            <span class="badge">{{ step.done ? '✓' : step.badge }}</span>
            <span class="label">{{ step.label }}</span>
          </a>
          @if (!last) { <span class="sep">›</span> }
        }
      </div>
    </nav>
  `,
  styles: [`
    .journey-bar { display: flex; align-items: center; gap: 24px; height: 64px; padding: 0 24px;
      background: #fff; border-bottom: 1px solid #e0e0e0; position: fixed; top: 0; left: 0; right: 0; z-index: 1000; }
    .brand { font-size: 21px; font-weight: 600; letter-spacing: 0.02em; color: #212121; }
    .steps { display: flex; align-items: center; gap: 18px; }
    .step { display: flex; align-items: center; gap: 9px; text-decoration: none; color: #757575; }
    .step.disabled { opacity: .55; cursor: default; pointer-events: none; }
    .badge { width: 23px; height: 23px; border-radius: 50%; display: grid; place-items: center;
      font-size: 11px; font-weight: 600; flex: none; border: 1.5px solid #d0d0d0; color: #757575; }
    .step.done .badge { background: #1565c0; color: #fff; border-color: #1565c0; }
    .step.active .badge { background: #007bff; color: #fff; border-color: #007bff; }
    .step.disabled .badge { border-style: dashed; }
    .label { font-size: 15px; font-weight: 600; }
    .step.active .label { color: #007bff; border-bottom: 2px solid #007bff; padding-bottom: 3px; }
    .sep { color: #bdbdbd; font-size: 15px; }
  `]
})
export class JourneyBarComponent {
  private router = inject(Router);

  private currentUrl = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map(e => e.urlAfterRedirects),
      startWith(this.router.url)
    ),
    { initialValue: this.router.url }
  );

  steps = computed(() => {
    const url = this.currentUrl();
    const activeIdx = STEPS.findIndex(s => url.startsWith(s.path));
    return STEPS.map((s, i) => ({
      ...s,
      active: i === activeIdx,
      done: activeIdx >= 0 && i < activeIdx,
    }));
  });
}
