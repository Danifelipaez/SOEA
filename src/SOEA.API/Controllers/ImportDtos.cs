namespace SOEA.API.Controllers
{
    /// <summary>Resumen del resultado de POST /api/import/excel.</summary>
    public class ImportExcelStatsDto
    {
        public int FacultadesCreadas { get; set; }
        public int ProgramasCreados { get; set; }
        public int DocentesCreados { get; set; }
        public int DocentesActualizados { get; set; }
        public int EspaciosCreados { get; set; }
        public int EspaciosActualizados { get; set; }
        public int AsignaturasCreadas { get; set; }
        public int AsignaturasActualizadas { get; set; }
        public int GruposCreados { get; set; }
        public int GruposSinDocente { get; set; }
        public List<string> Advertencias { get; set; } = new();
    }
}
