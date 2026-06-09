using System;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    /// <summary>
    /// Abstrae la gestión de transacciones y el seguimiento de entidades nuevas.
    /// Todas las repos del servicio de importación comparten el mismo DbContext (scoped),
    /// por lo que participan en la misma transacción abierta aquí.
    /// </summary>
    public interface IUnitOfWork : IDisposable
    {
        Task BeginTransactionAsync();
        /// <summary>Persiste todos los cambios pendientes sin confirmar la transacción.</summary>
        Task SaveAsync();
        Task CommitAsync();
        Task RollbackAsync();
        /// <summary>Registra la entidad en el change tracker sin guardar en BD todavía.</summary>
        void Track<T>(T entity) where T : EntidadBase;
    }
}
