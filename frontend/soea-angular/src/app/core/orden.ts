/** Orden alfabético en español: sin distinguir mayúsculas/tildes y con números naturales ("Grupo 2" < "Grupo 10"). */
export const porTexto = (a: string, b: string): number =>
  a.localeCompare(b, 'es', { numeric: true, sensitivity: 'base' });

export const porNombre = (a: { nombre: string }, b: { nombre: string }): number => porTexto(a.nombre, b.nombre);

/** Copia ordenada con {@link porNombre}. */
export const ordenar = <T extends { nombre: string }>(lista: readonly T[]): T[] => [...lista].sort(porNombre);
