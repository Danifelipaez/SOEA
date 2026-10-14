# soea-angular

SPA de SOEA: la coordinación académica carga el catálogo del semestre, genera el horario, lo ajusta a mano y revisa sus indicadores. Es de un solo operador, **sin login ni roles**.

Angular 21 (standalone, signals) + Angular Material. Pruebas con vitest.

```bash
npm install
npm start        # → http://localhost:4200 (API en http://localhost:5066/api)
npm run build    # producción usa environment.prod.ts → https://soea-api.azurewebsites.net/api
npm test
```

| Ruta | Paso | Componente |
|---|---|---|
| `/catalogo` | 1. Preparar catálogo | `features/ingesta` |
| `/horario` | 2–3. Generar y ajustar | `features/horario` |
| `/revisar` | 4. Revisar indicadores | `features/dashboard-admin` |
| `/publicar` | 5. Publicar (bloqueado) | `features/publicar` |

Estado global en `core/state.service.ts`; carga del catálogo en `core/catalogo.service.ts`; llamadas a la API en `core/persistencia.service.ts` y `core/horario-api.service.ts`.

Documentación completa (recorrido, estado, reglas reflejadas en el cliente, importación de Excel): [`docs/frontend.md`](../../docs/frontend.md).
