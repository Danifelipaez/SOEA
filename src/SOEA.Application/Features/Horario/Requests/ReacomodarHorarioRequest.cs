namespace SOEA.Application.Features.Horario.Requests
{
    /// <summary>
    /// Mueve una sesión ya generada a un nuevo (día, hora, espacio) sin regenerar el horario
    /// completo. Petición 13: solo la sesión editada y las que ahora chocan con ella se recalculan;
    /// el resto conserva exactamente su asignación previa.
    /// </summary>
    public class ReacomodarHorarioRequest
    {
        public Guid   HorarioId       { get; set; }
        public Guid   SesionEditadaId { get; set; }
        public string Dia             { get; set; } = "lunes";
        public string HoraInicio      { get; set; } = "07:00";
        /// <summary>Nuevo espacio físico. Vacío/null = no cambiar el espacio actual de la sesión.</summary>
        public string? EspacioId      { get; set; }
    }
}
