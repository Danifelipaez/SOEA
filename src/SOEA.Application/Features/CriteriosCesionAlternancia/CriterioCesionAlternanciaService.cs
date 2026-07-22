using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;

namespace SOEA.Application.Features.CriteriosCesionAlternancia
{
    /// <summary>
    /// Lista ordenada y activable de criterios de cesión a alternancia por saturación de espacio
    /// (fallback presencial-first). Catálogo fijo de 2 filas de sistema (Electiva / Elegible) — no
    /// admite crear ni eliminar filas, solo reordenar y activar/desactivar.
    /// </summary>
    public class CriterioCesionAlternanciaService
    {
        private readonly ICriterioCesionAlternanciaRepositorio _repo;

        public CriterioCesionAlternanciaService(ICriterioCesionAlternanciaRepositorio repo) => _repo = repo;

        public async Task<List<CriterioCesionAlternanciaDto>> ListarAsync()
        {
            var list = await _repo.GetAllAsync();
            return list.OrderBy(c => c.Orden).Select(MapToDto).ToList();
        }

        /// <summary>
        /// Actualiza orden y/o estado activo de un criterio. Si el nuevo orden ya lo ocupa otro
        /// criterio, se intercambian (swap) — así el orden siempre queda consistente sin duplicados.
        /// </summary>
        public async Task<List<CriterioCesionAlternanciaDto>> ActualizarAsync(Guid id, int? orden, bool? activo)
        {
            var existing = await _repo.GetByIdAsync(id)
                ?? throw new InvalidOperationException($"Criterio de cesión con ID {id} no encontrado.");

            if (activo.HasValue)
                existing.EstablecerActivo(activo.Value);

            if (orden.HasValue && orden.Value != existing.Orden)
            {
                var todos = await _repo.GetAllAsync();
                var conflicto = todos.FirstOrDefault(c => c.Id != id && c.Orden == orden.Value);
                var ordenAnterior = existing.Orden;
                existing.Reordenar(orden.Value);
                if (conflicto is not null)
                {
                    conflicto.Reordenar(ordenAnterior);
                    await _repo.UpdateAsync(conflicto);
                }
            }

            await _repo.UpdateAsync(existing);
            return await ListarAsync();
        }

        private static CriterioCesionAlternanciaDto MapToDto(CriterioCesionAlternancia c) => new()
        {
            Id = c.Id,
            Criterio = c.Criterio.ToString(),
            Orden = c.Orden,
            Activo = c.Activo
        };
    }

    public class CriterioCesionAlternanciaDto
    {
        public Guid Id { get; set; }
        /// <summary>Electiva | Elegible.</summary>
        public string Criterio { get; set; } = "";
        public int Orden { get; set; }
        public bool Activo { get; set; }
    }
}
