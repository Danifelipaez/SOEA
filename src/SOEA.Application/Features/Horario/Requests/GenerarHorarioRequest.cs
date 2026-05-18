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
    }

    public class AsignaturaDto
    {
        public string Id           { get; set; } = string.Empty;
        public string Nombre       { get; set; } = string.Empty;
        public string? DocenteId   { get; set; }
        public int    Creditos     { get; set; }
        /// <summary>Horas semanales totales a asignar.</summary>
        public decimal HorasSemanales { get; set; }
        /// <summary>Duración de cada sesión en horas (2 o 3). 0 = no enviado (usar fallback).</summary>
        public int HorasPorSesion  { get; set; }
        /// <summary>Número de sesiones por semana. 0 = no enviado (derivar de HorasSemanales).</summary>
        public int SesionesPorSemana { get; set; }
        public string? ProgramaId  { get; set; }
        public string? Alternancia { get; set; }
        public bool   EsVirtual    { get; set; }
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
