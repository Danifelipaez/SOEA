# Especificación de salida JSON

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
  "periodo": "2025-1",
  "generadoEn": "2025-03-01T10:30:00Z",
  "estado": "Publicado",
  "resumen": {
    "totalSesiones": 180,
    "violacionesDuras": 0,
    "puntajeRestriccionesBlandas": 12.5
  },
  "sesiones": [ ... ]
}
```

---

## Objeto Sesion

```json
{
  "id_sesion": "a1b2c3d4-...",
  "id_grupo": "c1c2c3d4-...",
  "id_docente": "d1d2c3d4-...",
  "id_espacio": "e1e2c3d4-...",
  "dia_semana": "Monday",
  "hora_inicio": "07:00",
  "hora_fin": "09:00",
  "modalidad": "Presencial",
  "semana_num": 3,
  "es_alternancia_virtual": false,
  "creado_por_id_admin": "f1f2f3f4-...",
  "grupo": {
    "id_grupo": "c1c2c3d4-...",
    "prog_academico": "Ingeniería de Sistemas",
    "cohorte": "Sem 3",
    "num_estudiantes": 28,
    "asignatura": {
      "id_asignatura": "b1b2c3d4-...",
      "nombre": "Algoritmos y Programación",
      "tipo_de_clase": "Teorica"
    }
  },
  "docente": {
    "id_usuario": "d1d2c3d4-...",
    "nombre": "Dra. Ana López",
    "email": "ana.lopez@university.edu.co",
    "tipo_vinculacion": "Planta"
  },
  "espacio": {
    "id_espacio": "e1e2c3d4-...",
    "nombre": "Aula 204",
    "bloque": "B",
    "tipo": "Aula",
    "capacidad": 35,
    "equipamiento": "Proyector",
    "es_virtual": false
  }
}
```

> `id_espacio` y `espacio` serán `null` cuando `modalidad = "Virtual"`.

---

## Descripción de campos

| Campo | Tipo | Descripción |
|---|---|---|
| `horarioId` | string (UUID) | Identificador único de esta versión del horario |
| `periodo` | string | Por ejemplo, "2025-1" o "2025-2" |
| `generadoEn` | string (ISO 8601) | Marca temporal UTC de generación |
| `estado` | string | `Borrador`, `Publicado` o `Archivado` |
| `resumen.totalSesiones` | int | Cantidad total de sesiones en la salida |
| `resumen.violacionesDuras` | int | Debe ser 0 en un horario publicado válido |
| `resumen.puntajeRestriccionesBlandas` | decimal | Puntuación ponderada de violaciones blandas (más bajo = mejor) |
| `sesion.modalidad` | string | `Presencial` o `Virtual` |
| `sesion.dia_semana` | string | `Monday`, `Tuesday`, `Wednesday`, `Thursday`, `Friday` |
| `sesion.hora_inicio` | string | Formato `HH:mm` (24 horas) |
| `sesion.hora_fin` | string | Formato `HH:mm` (24 horas) |
| `sesion.semana_num` | int | Número de semana del semestre |
| `sesion.es_alternancia_virtual` | bool | Indica alternancia virtual en esa semana |

---

## Ejemplo: salida válida mínima (2 sesiones)

```json
{
  "horarioId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "periodo": "2025-1",
  "generadoEn": "2025-03-01T10:30:00Z",
  "estado": "Publicado",
  "resumen": {
    "totalSesiones": 2,
    "violacionesDuras": 0,
    "puntajeRestriccionesBlandas": 0.0
  },
  "sesiones": [
    {
      "id_sesion": "a1b2c3d4-0001-0001-0001-000000000001",
      "id_grupo": "c1c2c3d4-2222-2222-2222-222222222222",
      "id_docente": "d1d2c3d4-3333-3333-3333-333333333333",
      "id_espacio": "e1e2c3d4-4444-4444-4444-444444444444",
      "dia_semana": "Monday",
      "hora_inicio": "07:00",
      "hora_fin": "09:00",
      "modalidad": "Presencial",
      "semana_num": 1,
      "es_alternancia_virtual": false,
      "creado_por_id_admin": "f1f2f3f4-5555-5555-5555-555555555555",
      "grupo": {
        "id_grupo": "c1c2c3d4-2222-2222-2222-222222222222",
        "prog_academico": "Ingeniería",
        "cohorte": "Sem 1",
        "num_estudiantes": 30,
        "asignatura": { "id_asignatura": "b1b2c3d4-1111-1111-1111-111111111111", "nombre": "Cálculo I", "tipo_de_clase": "Teorica" }
      },
      "docente": { "id_usuario": "d1d2c3d4-3333-3333-3333-333333333333", "nombre": "Prof. Carlos Ruiz", "email": "c.ruiz@uni.edu.co", "tipo_vinculacion": "Catedra" },
      "espacio": { "id_espacio": "e1e2c3d4-4444-4444-4444-444444444444", "nombre": "Aula 101", "bloque": "A", "tipo": "Aula", "capacidad": 40, "equipamiento": "Proyector", "es_virtual": false }
    },
    {
      "id_sesion": "a1b2c3d4-0001-0001-0001-000000000002",
      "id_grupo": "c1c2c3d4-2222-2222-2222-222222222223",
      "id_docente": "d1d2c3d4-3333-3333-3333-333333333334",
      "id_espacio": null,
      "dia_semana": "Tuesday",
      "hora_inicio": "09:00",
      "hora_fin": "11:00",
      "modalidad": "Virtual",
      "semana_num": 2,
      "es_alternancia_virtual": true,
      "creado_por_id_admin": "f1f2f3f4-5555-5555-5555-555555555555",
      "grupo": {
        "id_grupo": "c1c2c3d4-2222-2222-2222-222222222223",
        "prog_academico": "Ingeniería",
        "cohorte": "Sem 2",
        "num_estudiantes": 22,
        "asignatura": { "id_asignatura": "b1b2c3d4-1111-1111-1111-111111111112", "nombre": "Química Orgánica", "tipo_de_clase": "Laboratorio" }
      },
      "docente": { "id_usuario": "d1d2c3d4-3333-3333-3333-333333333334", "nombre": "Dra. María Torres", "email": "m.torres@uni.edu.co", "tipo_vinculacion": "Planta" },
      "espacio": null
    }
  ]
}
```

---

## Uso posterior

- **Aplicación frontend Angular**: consume este JSON para renderizar rejillas de horarios
- **UI de revisión del coordinador**: usa `resumen.violacionesDuras` para marcar borradores inválidos
- **Trazabilidad de auditoría**: el JSON completo se almacena en la base de datos por cada versión del horario

---

## Preguntas abiertas

- ¿El frontend necesita que `grupo`, `docente` y `espacio` sean siempre objetos completos o basta con IDs?
- ¿Se debe incluir un arreglo de violaciones con detalle por sesión?
- ¿La semana se representa con `semana_num` numérico o con fechas reales del calendario?
