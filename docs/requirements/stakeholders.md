# Partes interesadas

## Propósito
Identificar quién usa SOEA, qué rol desempeña y qué le corresponde validar.
Copilot usa este documento al generar lógica de autorización basada en roles, vistas de UI y control de acceso.

## Alcance
Todos los actores humanos que interactúan directamente con el sistema o cuyas restricciones debe respetar.

---

## Roles

### Admin
- **Quién**: Personal de la oficina de programación o administrador de TI
- **Responsibilities**:
  - Cargar archivos Excel (asignaturas, grupos, espacios, disponibilidad de docentes)
  - Configurar parámetros del sistema (fechas del semestre, alcance del piloto)
  - Ejecutar el pipeline de optimización
  - Administrar cuentas de usuario
- **Valida**: Integridad de datos, configuración del sistema, publicación final del horario

### Coordinador académico
- **Quién**: Coordinador de facultad o director académico por programa
- **Responsibilities**:
  - Revisar los horarios generados para su programa
  - Marcar violaciones de restricciones o excepciones de reglas de negocio
  - Aprobar o solicitar una nueva optimización
- **Valida**: Corrección del horario para los programas asignados

### Docente
- **Quién**: Profesor universitario o catedrático
- **Responsibilities**:
  - Ver su horario personal de docencia
  - Reportar conflictos de disponibilidad
- **Valida**: Sus propias asignaciones de sesión

### Estudiante
- **Quién**: Estudiante de pregrado matriculado
- **Responsibilities**:
  - Ver el horario de su grupo de estudiantes
- **Valida**: No aplica (rol de solo lectura)

---

## Partes interesadas indirectas

| Parte interesada | Interés |
|---|---|
| Dirección universitaria | Uso eficiente de los espacios físicos |
| Departamento de TI | Despliegue del sistema y seguridad de los datos |
| Organismo de acreditación | Cumplimiento del horario con la normativa académica |

---

## Preguntas abiertas

- ¿La institución necesita un rol de "Jefe de Departamento" separado del Coordinador?
- ¿Los docentes deberían poder bloquear disponibilidad desde la UI o solo mediante Excel cargado?
