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

/** Requisito de espacio de un grupo por tipo de sesión (HC-S03/HC-S05). Reemplaza a
 *  Asignatura.espacioFijoId — ahora vive por grupo y por tipo de sesión. */
export interface RequisitoEspacio {
  tipoSesion: 'TeoriaPresencial' | 'TeoriaVirtual' | 'Laboratorio';
  /** Espacio concreto exigido. Ausente = cualquier espacio de tipoEspacio. */
  espacioId?: string;
  tipoEspacio: 'Salon' | 'Laboratorio' | 'Auditorio';
  sesiones: number;
}

export interface Grupo {
  id: string;
  /** Asignatura a la que pertenece el grupo. Requerido en creación — invariante de dominio. */
  asignaturaId: string;
  nombre: string;
  estudiantesInscritos: number;
  programaId: string;
  facultadId?: string;
  /** Docente que dicta la asignatura para este grupo (Fase 2: el docente vive en el grupo). */
  docenteId?: string;
  codigo?: string;
  disponibilidadUiJson?: string; // JSON crudo por día que envía/recibe la API
  requisitosEspacio?: RequisitoEspacio[];
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
  /** Sesiones de teoría presencial por semana. */
  sesionesTeoriaPresencialSemana: number;
  horasTeoriaPresencial: number;
  /** Sesiones de teoría virtual por semana. Modo fijo, independiente de alternancia (sin pareja presencial). */
  sesionesTeoriaVirtualSemana: number;
  horasTeoriaVirtual: number;
  /** Sesiones de laboratorio por semana. Único track sujeto a alternancia (TipoA/TipoB). */
  sesionesLaboratorioSemana: number;
  horasLaboratorio: number;
  /** Total semestral de sesiones de laboratorio — distinto del conteo semanal; solo alimenta la inferencia de alternancia. */
  sesionesLaboratorioSemestre: number;
  programaId: string;
  // Fase 2: el docente ya no vive en la asignatura, sino en el Grupo (Grupo.docenteId).
  // El requisito de espacio tampoco vive aquí: ver Grupo.requisitosEspacio.
  /** Candidata a ceder a alternancia si el algoritmo agota el espacio físico (cesión por saturación de espacio). */
  esCandidataAlternancia?: boolean;
  /** Ventana horaria HC-VH (hard constraint, la fija Secretaría Académica). Formato "HH:mm".
   *  Sin UI propia todavía — llega por import de Excel u otra vía; ausente = sin restricción. */
  horaInicioMin?: string;
  horaFinMax?: string;
}

/** Parámetros del algoritmo genético y pesos de soft constraints configurados por el developer. */
export interface ConfiguracionAlgoritmo {
  pobSize:    number;  // TamañoPoblacion
  mutRate:    number;  // ProbabilidadMutacion
  crossRate:  number;  // ProbabilidadCruce
  maxGen:     number;  // MaxGeneraciones
  pesoErgo:   number;  // SC-01: horario compacto
  pesoTiempos: number; // SC-06: tiempos muertos
  pesoAlm:    number;  // SC-09: concentración diaria (backend: PesoMaxHorasSeguidas)
  /** SC-BAL: desbalance de carga por día entre Semana A y B. Sin UI propia todavía. */
  pesoBalanceSemanas?: number;
  /** SC-PRES informativo: pondera la métrica reportada, no afecta el ranking del GA. Sin UI propia todavía. */
  pesoPresencialFirst?: number;
  /** Semilla del RNG. Ausente = aleatoria (producción). Sin UI propia todavía. */
  semilla?: number;
}

export const CONFIGURACION_DEFECTO: ConfiguracionAlgoritmo = {
  pobSize: 50, mutRate: 0.05, crossRate: 0.80, maxGen: 200,
  pesoErgo: 3, pesoTiempos: 2, pesoAlm: 1,
  pesoBalanceSemanas: 2, pesoPresencialFirst: 4,
};

/** Fila de la lista ordenada/activable de criterios de cesión a alternancia por saturación de
 *  espacio. Catálogo fijo de 2 filas de sistema. */
export interface CriterioCesionAlternancia {
  id: string;
  /** MultiplesSesiones no otorga elegibilidad por sí solo — solo desempata el orden entre
   *  asignaturas ya elegibles por otro criterio (Electiva/Optativa/Elegible). */
  criterio: 'Electiva' | 'Optativa' | 'Elegible' | 'MultiplesSesiones';
  orden: number;
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
   *  'A' = semanas impares (TipoA presencial), 'B' = semanas pares (TipoB presencial)
   *  (convención del backend: SemanaAcademica.cs).
   *  El horario (día/franja) es idéntico en A y B; solo cambia la modalidad presencial↔virtual. */
  semana?: 'A' | 'B';
  /** Laboratorio | AulaVirtual. Distingue teoría (presencial o virtual) de laboratorio. */
  tipoFlujo?: 'Laboratorio' | 'AulaVirtual';
}

/** Vista de UI de los 3 tipos de sesión combinables por asignatura (desglose por tipo). */
export type TipoSesionUi = 'TeoriaPresencial' | 'TeoriaVirtual' | 'Laboratorio';

export function tipoSesionUiDesde(tipoFlujo: 'Laboratorio' | 'AulaVirtual' | undefined, virtual: boolean): TipoSesionUi {
  if (tipoFlujo === 'Laboratorio') return 'Laboratorio';
  return virtual ? 'TeoriaVirtual' : 'TeoriaPresencial';
}

export function tipoFlujoDesde(tipo: TipoSesionUi): 'Laboratorio' | 'AulaVirtual' {
  return tipo === 'Laboratorio' ? 'Laboratorio' : 'AulaVirtual';
}

export function esVirtualDesde(tipo: TipoSesionUi): boolean {
  return tipo === 'TeoriaVirtual';
}
