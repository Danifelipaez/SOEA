namespace SOEA.Application.Features.Horario.Responses
{
    /// <summary>Respuesta del endpoint POST /api/horario/reacomodar (petición 13).</summary>
    public class ReacomodarHorarioResponse
    {
        public bool   EsFactible   { get; set; }
        public string? MensajeError { get; set; }
        /// <summary>Avisos no bloqueantes — p. ej. cuántas sesiones en conflicto se reubicaron.</summary>
        public List<string> Advertencias { get; set; } = new();
        /// <summary>Horario completo refrescado (todas las sesiones, no solo la editada).</summary>
        public List<SesionGeneradaDto> Sesiones { get; set; } = new();
    }
}
