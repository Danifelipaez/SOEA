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
        public int    PesoAlmuerzo         { get; set; } = 1;
    }

    public class AsignaturaDto
    {
        public string Id           { get; set; } = string.Empty;
        public string Nombre       { get; set; } = string.Empty;
        public string? DocenteId   { get; set; }
        public int?    Creditos     { get; set; }
        /// <summary>Horas semanales totales a asignar.</summary>
        public decimal? HorasSemanales { get; set; }
        /// <summary>Duración de cada sesión en horas (2 o 3). Null = no enviado (usar fallback).</summary>
        public int? HorasPorSesion  { get; set; }
        /// <summary>Número de sesiones por semana. Null = no enviado (derivar de HorasSemanales).</summary>
        public int? SesionesPorSemana { get; set; }
        public string? ProgramaId  { get; set; }
        public string? Alternancia    { get; set; }
        public bool   EsVirtual      { get; set; }
        /// <summary>
        /// Espacio físico fijo para esta asignatura (HC-S05).
        /// Cuando está presente, CP-SAT solo asigna sesiones presenciales a este espacio.
        /// </summary>
        public string? EspacioFijoId  { get; set; }
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
}
