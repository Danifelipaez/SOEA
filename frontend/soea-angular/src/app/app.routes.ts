import { Routes } from '@angular/router';

// IA por flujo (journey de 5 pasos) — ver docs/MAPEO_FLUJOS_FRONTEND.md.
// horario-docente queda fuera de alcance (descartada); dashboard-admin/developer y
// tipos-alternancia/configuracion-alternancia se disolvieron dentro de este journey.
const BASE = 'SOEA';
export const routes: Routes = [
  { path: '', redirectTo: 'catalogo', pathMatch: 'full' },
  { path: 'catalogo', title: `Catálogo · ${BASE}`, loadComponent: () => import('./features/ingesta/ingesta.component').then(m => m.IngestaComponent) },
  { path: 'horario', title: `Horario · ${BASE}`, loadComponent: () => import('./features/horario/horario.component').then(m => m.HorarioComponent) },
  { path: 'revisar', title: `Revisar · ${BASE}`, loadComponent: () => import('./features/dashboard-admin/dashboard-admin.component').then(m => m.DashboardAdminComponent) },
  { path: 'publicar', title: `Publicar · ${BASE}`, loadComponent: () => import('./features/publicar/publicar.component').then(m => m.PublicarComponent) },
  // Sin ruta comodín antes: una URL mal escrita mostraba una página en blanco sin ninguna pista.
  { path: '**', redirectTo: 'catalogo' },
];
