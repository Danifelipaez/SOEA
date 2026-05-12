using System;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    public interface IScheduleRepository
    {
        Task<Horario?> GetByIdAsync(Guid id);
        Task<Horario?> GetBySemestreAsync(string semestre);
        Task AddAsync(Horario horario);
        Task UpdateAsync(Horario horario);
    }
}
