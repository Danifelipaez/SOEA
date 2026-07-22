namespace SOEA.Application.Features.Horario.Requests
{
    /// <summary>
    /// Payload enviado por el frontend Angular al endpoint POST /api/horario/generar.
    /// Contiene el estado actual de la sesión de ingesta (asignaturas, docentes, espacios).
    /// </summary>
    public class GenerarHorarioRequest
    {
        /// <summary>
        /// Identificador del semestre académico. Valor fijo: "2026-1".
        /// </summary>
        public string Semestre { get; set; } = "2026-1";

        public List<AsignaturaDto> Asignaturas { get; set; } = new();
        public List<DocenteDto>    Docentes    { get; set; } = new();
        public List<EspacioDto>    Espacios    { get; set; } = new();
        /// <summary>
        /// Grupos de estudiantes con su disponibilidad horaria.
        /// HC-G01: si un grupo declara disponibilidad, CP-SAT rechaza slots fuera de esa franja.
        /// Lista vacía = sin restricción de disponibilidad de grupo.
        /// </summary>
        public List<GrupoDto>      Grupos      { get; set; } = new();

        /// <summary>
        /// Parámetros del algoritmo genético y pesos de soft constraints.
        /// Null = usar valores por defecto del motor.
        /// </summary>
        public ConfiguracionAlgoritmoDto? Configuracion { get; set; }

        /// <summary>
        /// Sesiones cuya franja (día+hora) y espacio ya están decididas (horario base).
        /// CP-SAT las añade como restricciones de igualdad — no se mueven.
        /// Null o lista vacía = no usar base.
        /// </summary>
        public List<SesionFijaDto>? SesionesFijas { get; set; }
    }

    public class SesionFijaDto
    {
        public string  AsignaturaId { get; set; } = string.Empty;
        public string  DocenteId    { get; set; } = string.Empty;
        public string? EspacioId    { get; set; }
        public string  Dia          { get; set; } = "lunes";
        public string  HoraInicio   { get; set; } = "07:00";
        public string  HoraFin      { get; set; } = "09:00";
        public decimal DuracionHoras { get; set; } = 2m;
        public string? Alternancia  { get; set; }
        public bool    Virtual      { get; set; }
        /// <summary>Laboratorio | AulaVirtual. Null/no reconocido → Laboratorio (default histórico).</summary>
        public string? TipoFlujo    { get; set; }
    }

    public class ConfiguracionAlgoritmoDto
    {
        public int    TamañoPoblacion      { get; set; } = 50;
        public int    MaxGeneraciones      { get; set; } = 200;
        public double ProbabilidadMutacion { get; set; } = 0.05;
        public double ProbabilidadCruce    { get; set; } = 0.80;
        public int    UmbralConvergencia   { get; set; } = 30;
        public int    PesoErgo             { get; set; } = 3;
        public int    PesoTiempos          { get; set; } = 2;
        // Antes "PesoAlmuerzo" (C2 auditoría: no pondera almuerzo, sino rachas de >6h — SC-09).
        // Default antes 1, en desacuerdo con el default real del motor (3) — B4 auditoría.
        public int    PesoMaxHorasSeguidas { get; set; } = 3;
        /// <summary>SC-BAL: desbalance de carga por día entre Semana A y B. Antes se descartaba en el mapeo (B4).</summary>
        public int    PesoBalanceSemanas   { get; set; } = 2;
        /// <summary>SC-PRES informativo (B2): pondera la métrica reportada, ya no afecta el ranking del GA. Antes se descartaba en el mapeo (B4).</summary>
        public int    PesoPresencialFirst  { get; set; } = 4;
        /// <summary>Semilla del RNG. Null = aleatoria (producción); fija = reproducible. Antes se descartaba en el mapeo (B4).</summary>
        public int?   Semilla              { get; set; }
    }

    public class AsignaturaDto
    {
        public string Id           { get; set; } = string.Empty;
        public string Nombre       { get; set; } = string.Empty;
        public string? DocenteId   { get; set; }
        /// <summary>Sesiones de teoría presencial por semana.</summary>
        public int SesionesTeoriaPresencialSemana { get; set; }
        /// <summary>Duración en horas de cada sesión de teoría presencial.</summary>
        public int HorasTeoriaPresencial { get; set; }
        /// <summary>
        /// Sesiones de teoría virtual por semana. Modo fijo e independiente de Alternancia:
        /// siempre se generan en modalidad virtual, sin toggle semanal.
        /// </summary>
        public int SesionesTeoriaVirtualSemana { get; set; }
        /// <summary>Duración en horas de cada sesión de teoría virtual.</summary>
        public int HorasTeoriaVirtual { get; set; }
        /// <summary>Sesiones de laboratorio por semana. Único track sujeto a Alternancia (TipoA/TipoB).</summary>
        public int SesionesLaboratorioSemana { get; set; }
        /// <summary>Duración en horas de cada sesión de laboratorio.</summary>
        public int HorasLaboratorio { get; set; }
        public string? ProgramaId  { get; set; }
        /// <summary>TipoA | TipoB | SinAlternancia — solo aplica al track de laboratorio.</summary>
        public string? Alternancia    { get; set; }
        /// <summary>
        /// Espacio físico fijo para esta asignatura (HC-S05).
        /// Cuando está presente, CP-SAT solo asigna sesiones presenciales a este espacio.
        /// </summary>
        public string? EspacioFijoId  { get; set; }
        /// <summary>
        /// Categoría de la asignatura para priorizar la asignación presencial (SC-PRES).
        /// Obligatoria tiene máxima prioridad; Electiva se puede virtualizar primero cuando
        /// la capacidad de espacios está saturada.
        /// Null o valor no reconocido → Obligatoria (conservador).
        /// </summary>
        public string? Categoria { get; set; }
        /// <summary>
        /// Ventana horaria de la asignatura (hard constraint HC-VH, la fija Secretaría Académica).
        /// Formato "HH:mm". Ninguna sesión de la asignatura puede asignarse fuera de [Min, Max].
        /// Null = sin restricción de ventana.
        /// </summary>
        public string? HoraInicioMin { get; set; }
        public string? HoraFinMax    { get; set; }
        /// <summary>
        /// Candidata a ceder a alternancia si el algoritmo agota el espacio físico disponible
        /// (cesión por saturación de espacio). Ver <c>Asignatura.EsCandidataAlternancia</c>.
        /// </summary>
        public bool EsCandidataAlternancia { get; set; }
    }

    public class DocenteDto
    {
        public string Id     { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        /// <summary>Maximo de horas semanales contratadas.</summary>
        public decimal? MaxHoras { get; set; }
        /// <summary>
        /// Disponibilidad por día: clave = nombre del día (lunes, martes, …).
        /// Valor = objeto con campos: noDisponible (bool), tipo, franjaGeneral, desde, hasta.
        /// </summary>
        public Dictionary<string, DisponibilidadDiaDto> Disponibilidad { get; set; } = new();
    }

    public class DisponibilidadDiaDto
    {
        public bool    NoDisponible { get; set; }
        public string? Tipo         { get; set; }
        public string? FranjaGeneral { get; set; }
        public string? Desde        { get; set; }
        public string? Hasta        { get; set; }
    }

    public class EspacioDto
    {
        public string Id         { get; set; } = string.Empty;
        public string Nombre     { get; set; } = string.Empty;
        public int    Capacidad  { get; set; }
        public string? Tipo      { get; set; }
    }

    public class GrupoDto
    {
        public string  Id                  { get; set; } = string.Empty;
        public string  Nombre              { get; set; } = string.Empty;
        public string? Codigo              { get; set; }
        public string? AsignaturaId        { get; set; }
        public string? FacultadId          { get; set; }
        public int     EstudiantesInscritos { get; set; } = 1;
        /// <summary>
        /// Disponibilidad horaria del grupo. Valores válidos: "Matutino", "Vespertino".
        /// Lista vacía = sin restricción de franja (el grupo puede tener clase a cualquier hora).
        /// </summary>
        public List<string> Disponibilidad { get; set; } = new();
        public string? DisponibilidadUiJson { get; set; }
    }
}
