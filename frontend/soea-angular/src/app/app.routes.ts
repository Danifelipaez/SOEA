import { Routes } from '@angular/router';

// IA por flujo (journey de 5 pasos) — ver docs/MAPEO_FLUJOS_FRONTEND.md.
// horario-docente queda fuera de alcance (descartada); dashboard-admin/developer y
// tipos-alternancia/configuracion-alternancia se disolvieron dentro de este journey.
export const routes: Routes = [
  { path: '', redirectTo: 'catalogo', pathMatch: 'full' },
  { path: 'catalogo', loadComponent: () => import('./features/ingesta/ingesta.component').then(m => m.IngestaComponent) },
  { path: 'horario', loadComponent: () => import('./features/horario/horario.component').then(m => m.HorarioComponent) },
  { path: 'revisar', loadComponent: () => import('./features/dashboard-admin/dashboard-admin.component').then(m => m.DashboardAdminComponent) },
  { path: 'publicar', loadComponent: () => import('./features/publicar/publicar.component').then(m => m.PublicarComponent) },
];
