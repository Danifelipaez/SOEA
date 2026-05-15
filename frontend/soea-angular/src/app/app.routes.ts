import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'horario', pathMatch: 'full' },
  { path: 'ingesta', loadComponent: () => import('./features/ingesta/ingesta.component').then(m => m.IngestaComponent) },
  { path: 'horario', loadComponent: () => import('./features/horario/horario.component').then(m => m.HorarioComponent) },
  { path: 'dashboard-admin', loadComponent: () => import('./features/dashboard-admin/dashboard-admin.component').then(m => m.DashboardAdminComponent) },
  { path: 'dashboard-developer', loadComponent: () => import('./features/dashboard-developer/dashboard-developer.component').then(m => m.DashboardDeveloperComponent) }
];
