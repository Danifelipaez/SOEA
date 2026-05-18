# Especificación de salida JSON
**Última actualización:** 2026-05-16

## Propósito
Definir la estructura exacta del documento JSON que SOEA produce como salida de horario.
Copilot usa esto al generar código de serialización, modelos de respuesta de API y tipos
de enlace de datos del frontend.

## Alcance
La salida JSON canónica del pipeline de optimización (FR-05 en `docs/requirements/SRS.md`).

---

## Estructura de nivel superior

```json
{
  "horarioId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "semestreLabel": "2025-1",
  "generadoEn": "2025-03-01T10:30:00Z",
  "estado": "Publicado",
  "resumen": {
    "totalSesiones": 180,
    "violacionesRestriccionesDuras": 0,
    "puntajeFitness": 12.5
  },
  "sesiones": [ ... ]
}
```

---

## Objeto Sesión

```json
{
  "sesionId": "a1b2c3d4-...",
  "asignatura": {
    "id": "...",
    "nombre": "Algoritmos y Programación",
    "codigo": "ICSW-301"
  },
  "grupo": {
    "id": "...",
    "nombre": "Systems Engineering — Sem 3",
    "alternancia": "TipoA",
    "estudiantesInscritos": 28
  },
  "docente": {
    "id": "...",
    "nombreCompleto": "Dra. Ana López",
    "correo": "ana.lopez@university.edu.co"
  },
  "espacio": {
    "id": "...",
    "nombre": "Aula 204",
    "tipo": "Salon",
    "capacidad": 35
  },
  "bloqueTiempo": {
    "diaDeSemana": "Lunes",
    "horaInicio": "07:00",
    "horaFin": "09:00"
  },
  "modalidad": "Presencial",
  "horasDuracion": 2.0,
  "esBloque": false,
  "esBloqueDividido": false
}
```

---

## Descripción de campos

| Campo | Tipo | Descripción |
|---|---|---|
| `horarioId` | string (UUID) | Identificador único de esta versión del horario |
| `semestreLabel` | string | Por ejemplo, "2025-1" o "2025-2" |
| `generadoEn` | string (ISO 8601) | Marca temporal UTC de generación |
| `estado` | string | `Borrador`, `Publicado` o `Archivado` |
| `resumen.totalSesiones` | int | Cantidad total de sesiones en la salida |
| `resumen.violacionesRestriccionesDuras` | int | Debe ser 0 en un horario publicado válido |
| `resumen.puntajeFitness` | decimal | Puntuación ponderada de violaciones blandas (más bajo = mejor) |
| `sesion.espacio` | object or null | Null cuando la modalidad es "Virtual" |
| `sesion.modalidad` | string | `Presencial` o `Virtual` |
| `sesion.bloqueTiempo.diaDeSemana` | string | `Lunes`, `Martes`, `Miercoles`, `Jueves`, `Viernes` |
| `sesion.bloqueTiempo.horaInicio` | string | Formato `HH:mm` (24 horas) |
| `sesion.bloqueTiempo.horaFin` | string | Formato `HH:mm` (24 horas) |

---

## Ejemplo: salida válida mínima (2 sesiones)

```json
{
  "horarioId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "semestreLabel": "2025-1",
  "generadoEn": "2025-03-01T10:30:00Z",
  "estado": "Publicado",
  "resumen": {
    "totalSesiones": 2,
    "violacionesRestriccionesDuras": 0,
    "puntajeFitness": 0.0
  },
  "sesiones": [
    {
      "sesionId": "a1b2c3d4-0001-0001-0001-000000000001",
      "asignatura": { "id": "b1b2c3d4-1111-1111-1111-111111111111", "nombre": "Cálculo I", "codigo": "MAT-101" },
      "grupo": { "id": "c1c2c3d4-2222-2222-2222-222222222222", "nombre": "Engineering — Sem 1", "alternancia": "TipoA", "estudiantesInscritos": 30 },
      "docente": { "id": "d1d2d3d4-3333-3333-3333-333333333333", "nombreCompleto": "Prof. Carlos Ruiz", "correo": "c.ruiz@uni.edu.co" },
      "espacio": { "id": "e1e2e3e4-4444-4444-4444-444444444444", "nombre": "Aula 101", "tipo": "Salon", "capacidad": 40 },
      "bloqueTiempo": { "diaDeSemana": "Lunes", "horaInicio": "07:00", "horaFin": "09:00" },
      "modalidad": "Presencial",
      "horasDuracion": 2.0,
      "esBloque": false,
      "esBloqueDividido": false
    },
    {
      "sesionId": "a1b2c3d4-0001-0001-0001-000000000002",
      "asignatura": { "id": "b1b2c3d4-1111-1111-1111-111111111112", "nombre": "Química Orgánica", "codigo": "QUI-201" },
      "grupo": { "id": "c1c2c3d4-2222-2222-2222-222222222223", "nombre": "Chemical Eng — Sem 2", "alternancia": "TipoB", "estudiantesInscritos": 22 },
      "docente": { "id": "d1d2d3d4-3333-3333-3333-333333333334", "nombreCompleto": "Dra. María Torres", "correo": "m.torres@uni.edu.co" },
      "espacio": null,
      "bloqueTiempo": { "diaDeSemana": "Martes", "horaInicio": "09:00", "horaFin": "11:00" },
      "modalidad": "Virtual",
      "horasDuracion": 2.0,
      "esBloque": false,
      "esBloqueDividido": false
    }
  ]
}
```

---

## Uso posterior

- **Aplicación frontend Angular**: consume este JSON para renderizar rejillas de horarios
- **UI de revisión del coordinador**: usa `resumen.violacionesRestriccionesDuras` para marcar borradores inválidos
- **Trazabilidad de auditoría**: el JSON completo se almacena en la base de datos por cada versión del horario

---

## Preguntas abiertas

- ¿La salida debe incluir un arreglo `violations` que liste cada violación individual de restricción?
- ¿Los espacios de tiempo deben usar notación de duración ISO 8601 o las cadenas HH:mm anteriores?
- ¿Hace falta un endpoint de salida filtrado por cohorte o por docente?
