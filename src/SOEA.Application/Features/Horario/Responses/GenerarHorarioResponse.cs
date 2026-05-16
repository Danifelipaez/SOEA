namespace SOEA.Application.Features.Horario.Responses
{
    /// <summary>
    /// Respuesta del endpoint POST /api/horario/generar.
    /// El frontend Angular la usa para poblar el StateService con las sesiones generadas.
    /// </summary>
    public class GenerarHorarioResponse
    {
        public Guid   HorarioId      { get; set; }
        public string Semestre       { get; set; } = string.Empty;
        public bool   EsFactible     { get; set; }
        public decimal PuntajeFitness { get; set; }
        public int    Generaciones   { get; set; }
        public string? MensajeError  { get; set; }
        public List<string> Logs     { get; set; } = new();
        public List<SesionGeneradaDto> Sesiones { get; set; } = new();
    }

    /// <summary>
    /// Representación plana de una sesión para consumo del frontend.
    /// Mapea 1-a-1 con el modelo Sesion del StateService Angular.
    /// </summary>
    public class SesionGeneradaDto
    {
        public string  Id           { get; set; } = string.Empty;
        public string  AsignaturaId { get; set; } = string.Empty;
        public string  DocenteId    { get; set; } = string.Empty;
        public string? EspacioId    { get; set; }
        /// <summary>Nombre del día: lunes, martes, miercoles, jueves, viernes, sabado.</summary>
        public string  Dia          { get; set; } = string.Empty;
        public string  HoraInicio   { get; set; } = string.Empty;   // "HH:mm"
        public string  HoraFin      { get; set; } = string.Empty;   // "HH:mm"
        /// <summary>TipoA, TipoB o SinAlternancia.</summary>
        public string  Alternancia  { get; set; } = "SinAlternancia";
        public bool    Virtual      { get; set; }
    }
}
