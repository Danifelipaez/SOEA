# Despliegue

## Propósito
Describir cómo se despliega SOEA, qué infraestructura requiere y en qué se diferencian los entornos.
Copilot usa esto al generar archivos de configuración, montajes de Docker o código específico por entorno.

## Alcance
Configuraciones de despliegue para desarrollo, staging y producción.

---

## Modelo de despliegue

SOEA se despliega como un **monolito de un solo proceso** con los siguientes componentes:

| Componente | Unidad de despliegue |
|---|---|
| API .NET | Un solo proceso ASP.NET Core |
| Base de datos | Instancia de SQL Server o PostgreSQL |
| Frontend | SPA Angular (archivos estáticos servidos por la API o por un CDN/reverse proxy) |

---

## Matriz de entornos

| Entorno | Base de datos | Frontend | Notas |
|---|---|---|---|
| Desarrollo | SQL Server local / SQLite | `ng serve` (servidor de desarrollo Angular) | Estación de trabajo del desarrollador |
| Staging | SQL Server / PostgreSQL | SPA Angular compilada | Pruebas de integración |
| Producción | PostgreSQL (recomendado) | Servida mediante Nginx o Azure Static Web Apps | Despliegue piloto |

---

## Configuración

Los ajustes específicos por entorno se almacenan en:
- `src/SOEA.API/appsettings.json` — configuración base
- `src/SOEA.API/appsettings.Development.json` — sobrescrituras de desarrollo
- Variables de entorno — secretos y sobrescrituras de producción (nunca se suben al control de código)

Claves de configuración requeridas:
- `ConnectionStrings:DefaultConnection` — cadena de conexión a la base de datos
- `Jwt:Secret` — clave de firma JWT
- `Jwt:Issuer` / `Jwt:Audience` — metadatos del token
- `OptimizationEngine:TimeoutSeconds` — tiempo máximo del solucionador (predeterminado: 600)

---

## Docker (futuro)

Se planean un `Dockerfile` y un `docker-compose.yml` para el despliegue en staging.
El archivo compose definirá:
- Servicio `soea-api` (ASP.NET Core)
- Servicio `soea-db` (PostgreSQL)
- Montaje de volumen para archivos de entrada Excel

---

## Preguntas abiertas

- ¿Azure, Linux local o un servidor universitario es el host objetivo de producción?
- ¿Docker está disponible en el servidor de producción objetivo?
- ¿La app Angular debe servirse desde la API ASP.NET Core (embebida) o por separado?
