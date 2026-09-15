namespace SOEA.Application.Features.Horario.Requests
{
    /// <summary>
    /// Payload para crear una sesión manualmente sin re-ejecutar el modelo.
    /// La validación de hard constraints se realiza en el servicio antes de persistir.
    /// </summary>
    public class CrearSesionManualRequest
    {
        /// <summary>Horario vigente al que se agrega la sesión (el que devolvieron /generar o /actual).</summary>
        public Guid    HorarioId     { get; set; }
        public Guid    AsignaturaId  { get; set; }
        /// <summary>
        /// DOC1 auditoría: nullable — CR-02/CR-08 permiten sesiones sin docente
        /// (presencial-first: el docente se asigna después de generar). Antes era Guid no-nullable:
        /// al omitirlo, el binder deserializaba Guid.Empty y se persistía como si fuera un docente
        /// real (ahora Sesion rechaza Guid.Empty en el constructor).
        /// </summary>
        public Guid?   DocenteId     { get; set; }
        /// <summary>null para la fila virtual de sesiones con alternancia.</summary>
        public Guid?   EspacioId     { get; set; }
        /// <summary>
        /// Grupo/cohorte de la sesión. R2 auditoría: el diálogo de creación manual del frontend ya
        /// obliga a elegir un grupo, pero el dato se descartaba (el request no lo traía) — sin él,
        /// HC-SEP se evaluaba por (asignatura, tipo) sobre TODAS las cohortes (rechazaba la sesión
        /// del lunes del grupo B porque el grupo A ya tenía una el lunes) y HC-S05 (aula fija del
        /// grupo) no se podía aplicar en absoluto.
        /// </summary>
        public Guid?   GrupoId       { get; set; }
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
