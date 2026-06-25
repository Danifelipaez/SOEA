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

export interface Grupo {
  id: string;
  /** Asignatura a la que pertenece el grupo. Requerido en creación — invariante de dominio. */
  asignaturaId: string;
  nombre: string;
  estudiantesInscritos: number;
  semestre: number;
  programaId: string;
  facultadId?: string;
  codigo?: string;
  disponibilidadUiJson?: string; // JSON crudo por día que envía/recibe la API
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
  /** TipoA = presencial en semanas A (pares), virtual en B; TipoB = presencial en B (impares), virtual en A; SinAlternancia */
  alternancia: 'TipoA' | 'TipoB' | 'SinAlternancia';
  /** Prioridad de presencialidad: Obligatoria > Optativa > Electiva (CR-05) */
  categoria?: 'Obligatoria' | 'Optativa' | 'Electiva';
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

/**
 * Tipo de alternancia configurable (catálogo editable, Inc. C). Cada tipo mapea a un patrón base
 * sobre el modelo de 2 semanas (A/B). Los de sistema (TipoA/TipoB/SinAlternancia) no se eliminan.
 */
export interface TipoAlternanciaConfig {
  id: string;
  nombre: string;
  /** 'PresencialEnSemanaA' | 'PresencialEnSemanaB' | 'SinAlternancia' */
  patronBase: 'PresencialEnSemanaA' | 'PresencialEnSemanaB' | 'SinAlternancia';
  semanasPresenciales: number;
  color: string;
  esSistema: boolean;
  activo: boolean;
}

/** Horario base: snapshot nombrado de sesiones con franjas ya decididas.
 *  Se usa como restricciones de entrada en la siguiente generación. */
export interface HorarioBase {
  id: string;
  nombre: string;
  creadoEn: string;  // ISO date string
  sesiones: Sesion[];
}

export interface Sesion {
  id: string;
  asignaturaId: string;
  docenteId?: string;
  dia: string;           // 'lunes' | 'martes' | ...
  horaInicio: string;    // "07:00"
  horaFin: string;       // "09:00"
  /** Duración real de la sesión en horas (input fijo desde el backend). */
  duracionHoras: number;
  espacioId?: string;    // null si virtual
  /** Lab de origen de la sesión: el espacio donde es presencial. Presente incluso cuando
   *  la fila es virtual (espacioId=null), para poder filtrar la sesión a su lab en la matriz. */
  espacioIdHogar?: string;
  virtual: boolean;
  alternancia: 'TipoA' | 'TipoB' | 'SinAlternancia';
  /** Semana del ciclo de alternancia. Presente desde el modelo bi-semanal (Incremento 1).
   *  'A' = semanas pares (TipoA presencial), 'B' = semanas impares (TipoB presencial).
   *  El horario (día/franja) es idéntico en A y B; solo cambia la modalidad presencial↔virtual. */
  semana?: 'A' | 'B';
}
