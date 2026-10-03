namespace SOEA.API.Controllers
{
    /// <summary>Filas del archivo + sus incoherencias (POST import/excel/revisar y import/filas/revisar).</summary>
    public class RevisionImportDto
    {
        public List<SOEA.Domain.Interfaces.FilaCurriculum> Filas { get; set; } = new();
        public List<SOEA.Domain.Interfaces.IncoherenciaFila> Incoherencias { get; set; } = new();
        /// <summary>Avisos del archivo entero (p. ej. columnas de cabecera no reconocidas).</summary>
        public List<string> Avisos { get; set; } = new();
    }

    /// <summary>Resumen del resultado de POST /api/import/filas.</summary>
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
