namespace SOEA.Application.Features.Horario.Responses
{
    /// <summary>
    /// Resultado de asignar (o desasignar) un docente a una sesión ya generada.
    /// Las advertencias son soft — la asignación se persistió igual.
    /// </summary>
    public class AsignarDocenteResponse
    {
        public string SesionId { get; set; } = string.Empty;
        /// <summary>null cuando se desasignó el docente.</summary>
        public string? DocenteId { get; set; }
        public List<string> Advertencias { get; set; } = new();
    }
}
