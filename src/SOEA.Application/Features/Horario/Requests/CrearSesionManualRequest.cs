namespace SOEA.Application.Features.Horario.Requests
{
    /// <summary>
    /// Payload para crear una sesión manualmente sin re-ejecutar el modelo.
    /// La validación de hard constraints se realiza en el servicio antes de persistir.
    /// </summary>
    public class CrearSesionManualRequest
    {
        public Guid    AsignaturaId  { get; set; }
        public Guid    DocenteId     { get; set; }
        /// <summary>null para la fila virtual de sesiones con alternancia.</summary>
        public Guid?   EspacioId     { get; set; }
        /// <summary>Día en minúsculas: "lunes", "martes", "miercoles", "jueves", "viernes", "sabado".</summary>
        public string  Dia           { get; set; } = string.Empty;
        /// <summary>Hora de inicio en formato "HH:mm".</summary>
        public string  HoraInicio    { get; set; } = string.Empty;
        public decimal DuracionHoras { get; set; }
        /// <summary>TipoA | TipoB | SinAlternancia. Solo aplica si TipoFlujo=Laboratorio (único track que alterna).</summary>
        public string  Alternancia   { get; set; } = "SinAlternancia";
        /// <summary>Laboratorio | AulaVirtual. Determina TipoFlujo (HC-S03).</summary>
        public string  TipoFlujo     { get; set; } = "AulaVirtual";
        /// <summary>Solo aplica si TipoFlujo=AulaVirtual: true=teoría virtual fija, false=teoría presencial. Ignorado si Laboratorio.</summary>
        public bool    EsVirtual     { get; set; }
    }
}
