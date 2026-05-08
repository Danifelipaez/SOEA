# Criterios de aceptación

## Propósito
Definir las condiciones que deben cumplirse para considerar que SOEA está listo para el despliegue piloto
y, más adelante, para el despliegue institucional. Los coordinadores y propietarios del proyecto usan estos criterios para
validar el sistema antes de su aprobación.

## Alcance
Criterios de aceptación del piloto y criterios de preparación posteriores al piloto.

---

## Criterios de aceptación del piloto

Un horario generado por SOEA para el conjunto de datos del piloto se acepta cuando TODAS las siguientes condiciones son verdaderas:

### Calidad del horario

| # | Criterio | Cómo verificar |
|---|---|---|
| AC-01 | Cero violaciones de restricciones duras en el horario generado | `summary.hardConstraintViolations = 0` en la salida JSON |
| AC-02 | La puntuación de aptitud de restricciones blandas está dentro del 20% del valor base documentado del piloto | Comparar con el horario de referencia congelado para el conjunto de datos del piloto |
| AC-03 | Cada sesión del conjunto de datos del piloto aparece exactamente una vez en la salida | Conteo de sesiones en salida = conteo de sesiones en entrada |
| AC-04 | Se respetan todas las reglas de disponibilidad de docentes | Cruzar la salida con el archivo de disponibilidad de docentes |
| AC-05 | Ningún espacio físico excede su capacidad | Verificar `enrolledStudents ≤ space.capacity` para todas las sesiones presenciales |
| AC-06 | Las reglas de alternancia se aplican correctamente (Tipo A y B compartiendo un espacio) | Revisión manual puntual por parte del coordinador |

### Rendimiento del sistema

| # | Criterio | Cómo verificar |
|---|---|---|
| AC-07 | El pipeline de optimización termina en menos de 10 minutos para los datos del piloto | Medir el tiempo transcurrido en los logs |
| AC-08 | La API devuelve una respuesta JSON válida (coincide con `json-output-spec.md`) | Prueba de validación de esquema |

### Usabilidad

| # | Criterio | Cómo verificar |
|---|---|---|
| AC-09 | Al menos 2 coordinadores académicos han revisado el horario y lo han aprobado | Registro de aprobación del coordinador |
| AC-10 | El horario puede exportarse y leerse sin herramientas adicionales | Abrir el archivo JSON en un visor estándar |

---

## Criterios de preparación posteriores al piloto

Antes del despliegue a nivel institucional:

| # | Criterio |
|---|---|
| AC-P01 | El sistema maneja el conjunto de datos completo del semestre (todos los programas) dentro de los objetivos de rendimiento |
| AC-P02 | El control de acceso basado en roles está verificado para los cuatro roles (Administrador, Coordinador, Docente, Estudiante) |
| AC-P03 | La ingesta de datos acepta archivos Excel institucionales reales sin errores |
| AC-P04 | El sistema está desplegado en un entorno de pruebas y probado por TI |
| AC-P05 | Existe una guía de usuario o documento de incorporación para los coordinadores |

---

## Preguntas abiertas

- ¿Quién es la autoridad nominal de aprobación para AC-09?
- ¿Cuál es el horario de referencia congelado y la puntuación base de aptitud para el conjunto de datos del piloto (AC-02)?
