using System;
using System.Collections.Generic;

namespace SOEA.Application.Features.Import
{
    /// <summary>
    /// Resultado de <see cref="ImportarCurriculumService.EjecutarAsync"/>.
    /// Contiene contadores de entidades creadas/actualizadas y los mapas tempId→realId
    /// que el controller usa para construir la respuesta HTTP de cada endpoint.
    /// </summary>
    public class ImportarCurriculumStats
    {
        public int FacultadesCreadas    { get; set; }
        public int ProgramasCreados     { get; set; }
        public int DocentesCreados      { get; set; }
        public int DocentesActualizados { get; set; }
        public int EspaciosCreados      { get; set; }
        public int EspaciosActualizados { get; set; }
        public int AsignaturasCreadas   { get; set; }
        public int AsignaturasActualizadas { get; set; }
        public int GruposCreados        { get; set; }
        public int SesionesPersistidas  { get; set; }
        public int AsignaturasSinDocente { get; set; }
        public List<string> Advertencias { get; set; } = new();

        /// <summary>TempGuid (del CurriculumExcelResult) → RealGuid (persisted en BD).</summary>
        public Dictionary<Guid, Guid> FacultadMappings   { get; set; } = new();
        public Dictionary<Guid, Guid> ProgramaMappings   { get; set; } = new();
        public Dictionary<Guid, Guid> AsignaturaMappings { get; set; } = new();
        public Dictionary<Guid, Guid> DocenteMappings    { get; set; } = new();
        public Dictionary<Guid, Guid> GrupoMappings      { get; set; } = new();
    }
}
