/**
 * Semestre con el que arranca la aplicación (L-8 auditoría 2026-09-28: el literal "2026-1" estaba repetido
 * en cuatro sitios más un "Ing. Sistemas" fijo en la barra del recorrido). La fuente de verdad en ejecución es
 * `StateService.semestre`; esta constante solo la inicializa. El backend ya admite varios semestres a la vez
 * (NEW-3), pero la UI aún no ofrece elegirlo: hace falta decidir cómo se escoge.
 */
export const SEMESTRE_POR_DEFECTO = '2026-1';
