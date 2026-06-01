/**
 * Configuración de entorno para producción.
 * apiBaseUrl relativo: asume que la API se sirve tras el mismo host/reverse-proxy.
 * Ajustar a la URL absoluta del backend si se despliega en otro dominio.
 */
export const environment = {
  production: true,
  apiBaseUrl: '/api',
};
