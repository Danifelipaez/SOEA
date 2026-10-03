/** Orden alfabético en español: sin distinguir mayúsculas/tildes y con números naturales ("Grupo 2" < "Grupo 10"). */
export const porNombre = (a: { nombre: string }, b: { nombre: string }): number =>
  a.nombre.localeCompare(b.nombre, 'es', { numeric: true, sensitivity: 'base' });
