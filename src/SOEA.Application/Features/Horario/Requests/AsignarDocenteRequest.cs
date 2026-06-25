namespace SOEA.Application.Features.Horario.Requests
{
    public class AsignarDocenteRequest
    {
        public Guid SesionId { get; set; }
        /// <summary>null para desasignar el docente de la sesión.</summary>
        public Guid? DocenteId { get; set; }
    }
}
