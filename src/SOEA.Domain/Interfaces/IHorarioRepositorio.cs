using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    public interface IHorarioRepositorio
    {
        Task<Horario?> GetByIdAsync(Guid id);
        Task<Horario?> GetBySemestreAsync(string semestre);
        /// <summary>PERF4 auditoría: todas las corridas de un semestre (consulta acotada en BD),
        /// para limpiar sesiones/asignaciones superadas sin cargar la tabla completa.</summary>
        Task<List<Horario>> GetAllBySemestreAsync(string semestre);
        /// <summary>
        /// Todas las corridas de generación, incluidas las superadas por una regeneración
        /// posterior (nunca se borran — G4 auditoría). Consumidores que necesiten acotar por la
        /// corrida vigente deben filtrar sobre <see cref="Horario.SesioneIds"/> ellos mismos.
        /// </summary>
        Task<List<Horario>> GetAllAsync();
        Task AddAsync(Horario horario);
        Task UpdateAsync(Horario horario);
    }
}
