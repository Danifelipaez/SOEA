using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;

namespace SOEA.Application.Features.TiposAlternancia
{
    /// <summary>
    /// CRUD del catálogo de tipos de alternancia configurables (Inc. C). Los tipos de sistema
    /// (TipoA, TipoB, SinAlternancia) no se eliminan y conservan su patrón base.
    /// </summary>
    public class TipoAlternanciaConfigService
    {
        private readonly ITipoAlternanciaConfigRepositorio _repo;

        public TipoAlternanciaConfigService(ITipoAlternanciaConfigRepositorio repo) => _repo = repo;

        public async Task<List<TipoAlternanciaConfigDto>> GetAllAsync()
        {
            var list = await _repo.GetAllAsync();
            return list.OrderByDescending(t => t.EsSistema).ThenBy(t => t.Nombre).Select(MapToDto).ToList();
        }

        public async Task<TipoAlternanciaConfigDto> CreateAsync(TipoAlternanciaConfigDto dto)
        {
            var id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id;
            var entidad = new TipoAlternanciaConfig(
                id, dto.Nombre, ParsePatron(dto.PatronBase), dto.SemanasPresenciales, dto.Color, esSistema: false);
            await _repo.AddAsync(entidad);
            return MapToDto(entidad);
        }

        public async Task<TipoAlternanciaConfigDto?> UpdateAsync(Guid id, TipoAlternanciaConfigDto dto)
        {
            var existing = await _repo.GetByIdAsync(id);
            if (existing is null) return null;

            existing.ActualizarDatos(dto.Nombre, ParsePatron(dto.PatronBase), dto.SemanasPresenciales, dto.Color, dto.Activo);
            await _repo.UpdateAsync(existing);
            return MapToDto(existing);
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var existing = await _repo.GetByIdAsync(id);
            if (existing is null) return false;
            if (existing.EsSistema)
                throw new InvalidOperationException("Los tipos de sistema no se pueden eliminar.");
            await _repo.DeleteAsync(id);
            return true;
        }

        private static TipoAlternanciaConfigDto MapToDto(TipoAlternanciaConfig t) => new()
        {
            Id = t.Id,
            Nombre = t.Nombre,
            PatronBase = t.PatronBase.ToString(),
            SemanasPresenciales = t.SemanasPresenciales,
            Color = t.Color,
            EsSistema = t.EsSistema,
            Activo = t.Activo
        };

        private static PatronBaseAlternancia ParsePatron(string? patron) =>
            Enum.TryParse<PatronBaseAlternancia>(patron, ignoreCase: true, out var p)
                ? p : PatronBaseAlternancia.SinAlternancia;
    }

    public class TipoAlternanciaConfigDto
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = "";
        /// <summary>PresencialEnSemanaA | PresencialEnSemanaB | SinAlternancia.</summary>
        public string PatronBase { get; set; } = "SinAlternancia";
        public int SemanasPresenciales { get; set; }
        public string Color { get; set; } = "#607d8b";
        public bool EsSistema { get; set; }
        public bool Activo { get; set; } = true;
    }
}
