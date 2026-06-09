using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Data.Context;

namespace SOEA.Infrastructure.Data
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly SOEABdContext _context;
        private IDbContextTransaction? _tx;

        public UnitOfWork(SOEABdContext context)
        {
            _context = context;
        }

        public async Task BeginTransactionAsync()
            => _tx = await _context.Database.BeginTransactionAsync();

        public async Task SaveAsync()
            => await _context.SaveChangesAsync();

        public async Task CommitAsync()
        {
            if (_tx is null) throw new InvalidOperationException("No hay transacción activa.");
            await _tx.CommitAsync();
        }

        public async Task RollbackAsync()
        {
            if (_tx is not null) await _tx.RollbackAsync();
        }

        public void Track<T>(T entity) where T : EntidadBase
            => _context.Set<T>().Add(entity);

        public void Dispose()
            => _tx?.Dispose();
    }
}
