/**
 * Configuración de entorno por defecto (desarrollo).
 * En build de producción se reemplaza por environment.prod.ts vía fileReplacements
 * en angular.json (P1.5 auditoría).
 */
export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5066/api',
};
