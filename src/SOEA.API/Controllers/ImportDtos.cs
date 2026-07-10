namespace SOEA.API.Controllers
{
    // DTOs para los endpoints de importación (ImportController)
    public record FacultadDto(string Id, string Nombre);
    public record ProgramaDto(string Id, string Nombre, string FacultadId);

    public class CurriculumExcelDto
    {
        public List<FacultadDto>? Facultades { get; set; }
        public List<ProgramaDto>? Programas { get; set; }
        public List<AsignaturaDto>? Asignaturas { get; set; }
        public List<DocenteImportDto>? Docentes { get; set; }
        public List<SesionImportDto>? SesionesPredefinidas { get; set; }
        public List<EspacioImportDto>? Espacios { get; set; }
    }

    public class DocenteImportDto
    {
        public string? Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Cedula { get; set; } = string.Empty;
        public double MaxHoras { get; set; } = 40;
        public object? Disponibilidad { get; set; }
    }

    public class EspacioImportDto
    {
        public string Nombre { get; set; } = string.Empty;
        public string Tipo { get; set; } = "Salón";
        public int Capacidad { get; set; } = 30;
        public string? Edificio { get; set; }
        public int? Piso { get; set; }
    }

    public class SesionImportDto
    {
        public Guid AsignaturaId { get; set; }
        public Guid DocenteId { get; set; }
        public Guid BloqueTiempoId { get; set; }
        public Guid? EspacioId { get; set; }
        public Guid? GrupoId { get; set; }
        public string Alternancia { get; set; } = "SinAlternancia";
        public decimal DuracionHoras { get; set; } = 2;
    }

    public class AsignaturaDto
    {
        public string? Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string? Codigo { get; set; }
        public string? DocenteId { get; set; }
        public int HorasPorSesion { get; set; }
        public int SesionesPorSemana { get; set; }
        public int SesionesLaboratorioSemestre { get; set; }
        public string ProgramaId { get; set; } = string.Empty;
        public string Alternancia { get; set; } = "SinAlternancia";
        public int? GrupoNumero { get; set; }
        /// <summary>Obligatoria | Optativa | Electiva. Null o no reconocido → Obligatoria (conservador).</summary>
        public string? Categoria { get; set; }
    }

    public class MappingDto
    {
        public string TempId { get; set; } = string.Empty;
        public string NewId { get; set; } = string.Empty;
    }

    public class ImportSummaryDto
    {
        public int Facultades { get; set; }
        public int Programas { get; set; }
        public int Asignaturas { get; set; }
        public int Grupos { get; set; }
        public int Docentes { get; set; }
    }

    public class ImportResultDto
    {
        public List<MappingDto> Facultades { get; set; } = new();
        public List<MappingDto> Programas { get; set; } = new();
        public List<MappingDto> Asignaturas { get; set; } = new();
        public List<MappingDto> Grupos { get; set; } = new();
        public List<MappingDto> Docentes { get; set; } = new();
        public ImportSummaryDto Summary { get; set; } = new();
    }

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
        public int SesionesPersistidas { get; set; }
        public int AsignaturasSinDocente { get; set; }
        public List<string> Advertencias { get; set; } = new();
    }
}
