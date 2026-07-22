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
            <span class="jbadge">{{ step.done ? '✓' : step.badge }}</span>
            <span class="jname">{{ step.label }}</span>
          </a>
          @if (!last) { <span class="jsep">›</span> }
        }
      </div>
      <div class="ctx">2026-1 · Ing. Sistemas</div>
    </nav>
  `,
  styles: [`
    .journey-bar { display: flex; align-items: center; gap: 24px; height: 57px; padding: 0 22px;
      background: var(--color-bg); border-bottom: 1px solid var(--color-divider);
      position: fixed; top: 0; left: 0; right: 0; z-index: 1000; }
    .brand { font: 600 21px var(--font-heading); letter-spacing: 0.02em; color: var(--color-text); }
    .steps { display: flex; align-items: center; gap: 18px; }
    .step { display: flex; align-items: center; gap: 9px; text-decoration: none; }
    .step.disabled { opacity: .55; cursor: default; pointer-events: none; }

    /* badge base = paso siguiente (pendiente): borde sólido */
    .jbadge { border: 1.5px solid var(--color-divider); color: var(--color-neutral-600); }
    .jname  { font: 600 15px var(--font-heading); letter-spacing: 0.01em; color: var(--color-neutral-600); }

    .step.done .jbadge   { background: var(--color-accent-800); color: #fff; border-color: var(--color-accent-800); }
    .step.done .jname    { color: var(--color-text); }
    .step.active .jbadge { background: var(--color-accent); color: #fff; border-color: var(--color-accent); }
    .step.active .jname  { color: var(--color-accent); border-bottom: 2px solid var(--color-accent); padding-bottom: 3px; }
    .step.disabled .jbadge { border-style: dashed; border-color: var(--color-neutral-400); color: var(--color-neutral-500); }
    .step.disabled .jname  { color: var(--color-neutral-500); }

    .ctx { margin-left: auto; font-size: 13px; color: var(--color-neutral-700); }
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
