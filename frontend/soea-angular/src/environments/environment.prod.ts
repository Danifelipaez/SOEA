/**
 * Configuración de entorno para producción en Azure.
 * Reemplaza la URL por la de tu App Service antes de hacer ng build --configuration production.
 * Ejemplo: 'https://soea-api.azurewebsites.net/api'
 */
export const environment = {
  production: true,
  apiBaseUrl: 'https://TU-APP-SERVICE.azurewebsites.net/api',
};
