export interface Facultad {
  id: string;
  nombre: string;
}

export interface Programa {
  id: string;
  nombre: string;
  facultadId: string;
}

export interface Espacio {
  id: string;
  nombre: string;
  capacidad: number;
  tipo: 'Laboratorio' | 'Salón' | 'Auditorio';
  edificio?: string;
  piso?: number;
}

export interface Docente {
  id: string;
  nombre: string;
  cedula: string;
  maxHoras: number;
  disponibilidad: any; // { lunes: { noDisponible, tipo, franjaGeneral, desde, hasta }, ... }
}

/**
 * Asignatura académica de la malla curricular.
 * Una misma asignatura (mismo código) puede aparecer en distintos programas/facultades,
 * por lo que la identidad real es (codigo, programaId).
 */
export interface Asignatura {
  id: string;
  codigo: string;
  nombre: string;
  /** TipoA = 8 sesiones de lab (semanas impares presencial), TipoB = más de 8 (flexible), SinAlternancia */
  alternancia: 'TipoA' | 'TipoB' | 'SinAlternancia';
  /** Número de grupo dentro de la asignatura (1..N). Opcional; usado en importaciones para diferenciar repeticiones */
  grupoNumero?: number;
  horasPorSesion: number;    // 2 o 3 horas
  sesionesPorSemana: number;
  sesionesLaboratorioSemestre: number;
  programaId: string;
  docenteId?: string;        // Docente asignado (puede quedar vacío para que el algoritmo decida)
  espacioFijoId?: string;    // Espacio requerido (opcional)
}

/** Parámetros del algoritmo genético y pesos de soft constraints configurados por el developer. */
export interface ConfiguracionAlgoritmo {
  pobSize:    number;  // TamañoPoblacion
  mutRate:    number;  // ProbabilidadMutacion
  crossRate:  number;  // ProbabilidadCruce
  maxGen:     number;  // MaxGeneraciones
  pesoErgo:   number;  // SC-01: horario compacto
  pesoTiempos: number; // SC-06: tiempos muertos
  pesoAlm:    number;  // SC-09: concentración diaria
}

export const CONFIGURACION_DEFECTO: ConfiguracionAlgoritmo = {
  pobSize: 50, mutRate: 0.05, crossRate: 0.80, maxGen: 200,
  pesoErgo: 3, pesoTiempos: 2, pesoAlm: 1,
};

export interface Sesion {
  id: string;
  asignaturaId: string;
  docenteId: string;
  dia: string;           // 'lunes' | 'martes' | ...
  horaInicio: string;    // "07:00"
  horaFin: string;       // "09:00"
  /** Duración real de la sesión en horas (input fijo desde el backend). */
  duracionHoras: number;
  espacioId?: string;    // null si virtual
  virtual: boolean;
  alternancia: 'TipoA' | 'TipoB' | 'SinAlternancia';
}
