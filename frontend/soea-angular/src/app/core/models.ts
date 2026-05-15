export interface Espacio {
  id: string;
  nombre: string;
  capacidad: number;
  tipo: 'Laboratorio' | 'Salón';
}

export interface Docente {
  id: string;
  nombre: string;
  cedula: string;
  maxHoras: number;
  disponibilidad: any; // e.g. { lunes: { tipo: 'Franja general', valor: 'Matutino (6:00–12:00)' }, ... }
}

export interface Asignatura {
  id: string;
  codigo: string;
  nombre: string;
  tipo: 'Tipo A' | 'Tipo B';
  prioridad: 1 | 2 | 3;
  duracion: number; // 2 o 3 horas
  docenteId: string;
  espacioFijoId?: string;
}

export interface Sesion {
  id: string;
  asignaturaId: string;
  dia: string;
  horaInicio: string; // "06:00"
  duracion: number;
  espacioId?: string;
  virtual: boolean;
}
