using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;

namespace SOEA.Tests.Fakes
{
    /// <summary>
    /// Dobles de prueba en memoria para los repositorios de dominio.
    ///
    /// Antes cada archivo de prueba escribía los suyos: 12 copias de FakeGrupoRepo, 10 de
    /// FakeSesionRepo, 8 de FakeAsignacionRepo… ~1.400 líneas de la misma plantilla, y cada
    /// método nuevo en una interfaz obligaba a tocarlas todas. Aquí vive una sola versión.
    ///
    /// La siembra se pasa por constructor (params), así que una prueba se sigue leyendo igual
    /// de explícita: new FakeSesionRepo(sesionA, sesionB).
    ///
    /// ponytail: sin verificación de llamadas ni framework de mocking — la suite comprueba
    /// estado final (qué quedó en el store), no interacciones. <see cref="Eliminados"/> cubre
    /// el único caso en que hace falta saber que un borrado ocurrió.
    /// </summary>
    public class FakeRepositorio<T> : IRepositorio<T> where T : EntidadBase
    {
        protected readonly Dictionary<Guid, T> Store = new();

        /// <summary>Ids borrados, en orden. Para aserciones del tipo "el guard no dejó fugas".</summary>
        public List<Guid> Eliminados { get; } = new();

        public FakeRepositorio(params T[] entidades)
        {
            foreach (var e in entidades) Store[e.Id] = e;
        }

        public IReadOnlyCollection<T> Todos => Store.Values.ToList();

        public Task AddAsync(T entity) { Store[entity.Id] = entity; return Task.CompletedTask; }
        public Task<T?> GetByIdAsync(Guid id) => Task.FromResult(Store.GetValueOrDefault(id));
        public Task<List<T>> GetAllAsync() => Task.FromResult(Store.Values.ToList());
        public Task UpdateAsync(T entity) { Store[entity.Id] = entity; return Task.CompletedTask; }

        public Task DeleteAsync(Guid id)
        {
            Store.Remove(id);
            Eliminados.Add(id);
            return Task.CompletedTask;
        }

        // PERF3 auditoría: borrado en lote (una sola "sentencia" en vez de un DeleteAsync por id) —
        // ver ISesionRepositorio.DeleteRangeAsync.
        public Task DeleteRangeAsync(IEnumerable<Guid> ids)
        {
            foreach (var id in ids) { Store.Remove(id); Eliminados.Add(id); }
            return Task.CompletedTask;
        }

        // PERF4 auditoría: lectura en lote — ver ISesionRepositorio.GetByIdsAsync.
        public Task<List<T>> GetByIdsAsync(IEnumerable<Guid> ids)
        {
            var set = ids.ToHashSet();
            return Task.FromResult(Store.Values.Where(t => set.Contains(t.Id)).ToList());
        }
    }

    public sealed class FakeAsignaturaRepo : FakeRepositorio<Asignatura>, IAsignaturaRepositorio
    {
        public FakeAsignaturaRepo(params Asignatura[] asignaturas) : base(asignaturas) { }

        public Task<Asignatura?> GetByCodigoAsync(string codigo) =>
            Task.FromResult(Store.Values.FirstOrDefault(a => a.Codigo == codigo));

        public Task<Asignatura?> GetByCodigoYProgramaAsync(string codigo, Guid programaId) =>
            Task.FromResult(Store.Values.FirstOrDefault(a => a.Codigo == codigo && a.ProgramaId == programaId));

        public Task<Asignatura?> GetByNombreYProgramaAsync(string nombre, Guid programaId) =>
            Task.FromResult(Store.Values.FirstOrDefault(a =>
                a.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase) && a.ProgramaId == programaId));
    }

    public sealed class FakeGrupoRepo : FakeRepositorio<Grupo>, IGrupoRepositorio
    {
        public FakeGrupoRepo(params Grupo[] grupos) : base(grupos) { }

        public Task<Grupo?> GetByNombreYProgramaAsync(string nombre, Guid programaId) =>
            Task.FromResult(Store.Values.FirstOrDefault(g =>
                g.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase) && g.ProgramaId == programaId));

        public Task<Grupo?> GetByCodigoAsync(string codigo) =>
            Task.FromResult(Store.Values.FirstOrDefault(g => g.Codigo == codigo));

        public Task<IEnumerable<Grupo>> GetByAsignaturaIdAsync(Guid asignaturaId) =>
            Task.FromResult(Store.Values.Where(g => g.AsignaturaId == asignaturaId).AsEnumerable());

        public Task<IEnumerable<Grupo>> GetByDocenteIdAsync(Guid docenteId) =>
            Task.FromResult(Store.Values.Where(g => g.DocenteId == docenteId).AsEnumerable());
    }

    public sealed class FakeSesionRepo : FakeRepositorio<Sesion>, ISesionRepositorio
    {
        public FakeSesionRepo(params Sesion[] sesiones) : base(sesiones) { }

        public Task AddRangeAsync(IEnumerable<Sesion> sesiones)
        {
            foreach (var s in sesiones) Store[s.Id] = s;
            return Task.CompletedTask;
        }

        public Task<bool> ExisteAsync(Guid asignaturaId, Guid? docenteId, Guid bloqueTiempoId) =>
            Task.FromResult(Store.Values.Any(s =>
                s.AsignaturaId == asignaturaId && s.DocenteId == docenteId && s.BloqueTiempoId == bloqueTiempoId));

        public Task<List<Guid>> GetIdsByGrupoIdAsync(Guid grupoId) =>
            Task.FromResult(Store.Values.Where(s => s.GrupoId == grupoId).Select(s => s.Id).ToList());

        public Task<List<Guid>> GetIdsByAsignaturaIdAsync(Guid asignaturaId) =>
            Task.FromResult(Store.Values.Where(s => s.AsignaturaId == asignaturaId).Select(s => s.Id).ToList());

        public Task<List<Guid>> GetIdsByEspacioIdAsync(Guid espacioId) =>
            Task.FromResult(Store.Values.Where(s => s.EspacioId == espacioId).Select(s => s.Id).ToList());
    }

    public sealed class FakeAsignacionRepo : FakeRepositorio<AsignacionSemanal>, IAsignacionSemanalRepositorio
    {
        public FakeAsignacionRepo(params AsignacionSemanal[] asignaciones) : base(asignaciones) { }

        public Task AddRangeAsync(IEnumerable<AsignacionSemanal> asignaciones)
        {
            foreach (var a in asignaciones) Store[a.Id] = a;
            return Task.CompletedTask;
        }

        public Task<List<AsignacionSemanal>> GetBySesionIdsAsync(IEnumerable<Guid> sesionIds)
        {
            var set = sesionIds.ToHashSet();
            return Task.FromResult(Store.Values.Where(a => set.Contains(a.SesionId)).ToList());
        }

        // PERF3 auditoría: borrado en lote por sesionId — ver IAsignacionSemanalRepositorio.DeleteBySesionIdsAsync.
        public Task DeleteBySesionIdsAsync(IEnumerable<Guid> sesionIds)
        {
            var set = sesionIds.ToHashSet();
            foreach (var id in Store.Values.Where(a => set.Contains(a.SesionId)).Select(a => a.Id).ToList())
            {
                Store.Remove(id);
                Eliminados.Add(id);
            }
            return Task.CompletedTask;
        }
    }

    public sealed class FakeDocenteRepo : FakeRepositorio<Docente>, IDocenteRepositorio
    {
        public FakeDocenteRepo(params Docente[] docentes) : base(docentes) { }

        public Task<Docente?> GetByCedulaAsync(string cedula) =>
            Task.FromResult(Store.Values.FirstOrDefault(d => d.CedulaIdentidad == cedula));

        public Task<Docente?> GetByNombreAsync(string nombre) =>
            Task.FromResult(Store.Values.FirstOrDefault(d =>
                d.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase)));
    }

    public sealed class FakeEspacioRepo : FakeRepositorio<Espacio>, IEspacioRepositorio
    {
        public FakeEspacioRepo(params Espacio[] espacios) : base(espacios) { }

        public Task<Espacio?> GetByNombreAsync(string nombre) =>
            Task.FromResult(Store.Values.FirstOrDefault(e =>
                e.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase)));
    }

    public sealed class FakeBloqueRepo : FakeRepositorio<BloqueTiempo>, IBloqueTiempoRepositorio
    {
        public FakeBloqueRepo(params BloqueTiempo[] bloques) : base(bloques) { }

        public Task<BloqueTiempo?> FindByDiaHoraAsync(DiaDeSemana dia, TimeOnly horaInicio) =>
            Task.FromResult(Store.Values.FirstOrDefault(b => b.Dia == dia && b.HoraInicio == horaInicio));

        public Task<bool> ExisteAlgunoAsync() => Task.FromResult(Store.Count > 0);
    }

    public sealed class FakeCriterioCesionRepo
        : FakeRepositorio<CriterioCesionAlternancia>, ICriterioCesionAlternanciaRepositorio
    {
        public FakeCriterioCesionRepo(params CriterioCesionAlternancia[] criterios) : base(criterios) { }
    }

    public sealed class FakeFacultadRepo : FakeRepositorio<Facultad>, IFacultadRepositorio
    {
        public FakeFacultadRepo(params Facultad[] facultades) : base(facultades) { }

        public Task<Facultad?> GetByNombreAsync(string nombre) =>
            Task.FromResult(Store.Values.FirstOrDefault(f =>
                f.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase)));
    }

    public sealed class FakeProgramaRepo : FakeRepositorio<Programa>, IProgramaRepositorio
    {
        public FakeProgramaRepo(params Programa[] programas) : base(programas) { }

        public Task<Programa?> GetByNombreYFacultadAsync(string nombre, Guid facultadId) =>
            Task.FromResult(Store.Values.FirstOrDefault(p =>
                p.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase) && p.FacultadId == facultadId));
    }

    /// <summary>
    /// <see cref="IHorarioRepositorio"/> no hereda de <see cref="IRepositorio{T}"/> (no expone
    /// borrado: un Horario es la bitácora de una corrida y nunca se elimina), así que este fake
    /// no reutiliza <see cref="FakeRepositorio{T}"/>.
    /// </summary>
    public sealed class FakeHorarioRepo : IHorarioRepositorio
    {
        private readonly Dictionary<Guid, Horario> _store = new();

        public FakeHorarioRepo(params Horario[] horarios)
        {
            foreach (var h in horarios) _store[h.Id] = h;
        }

        public Task<Horario?> GetByIdAsync(Guid id) => Task.FromResult(_store.GetValueOrDefault(id));

        public Task<Horario?> GetBySemestreAsync(string semestre) =>
            Task.FromResult(_store.Values.LastOrDefault(h => h.Semestre == semestre));

        public Task<List<Horario>> GetAllAsync() => Task.FromResult(_store.Values.ToList());
        public Task<List<Horario>> GetAllBySemestreAsync(string semestre) =>
            Task.FromResult(_store.Values.Where(h => h.Semestre == semestre).ToList());
        public Task AddAsync(Horario horario) { _store[horario.Id] = horario; return Task.CompletedTask; }
        public Task UpdateAsync(Horario horario) { _store[horario.Id] = horario; return Task.CompletedTask; }
    }

    /// <summary>
    /// Transacción en memoria: registra si hubo commit o rollback, para que una prueba pueda
    /// comprobar que un fallo a mitad de camino no dejó escrituras a medias.
    /// </summary>
    public sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public List<EntidadBase> Rastreadas { get; } = new();

        public Task BeginTransactionAsync() => Task.CompletedTask;
        public Task SaveAsync() => Task.CompletedTask;
        public Task CommitAsync() { Commits++; return Task.CompletedTask; }
        public Task RollbackAsync() { Rollbacks++; return Task.CompletedTask; }
        public void Track<T>(T entity) where T : EntidadBase => Rastreadas.Add(entity);
        public void AttachUnchanged<T>(T entity) where T : EntidadBase => Rastreadas.Add(entity);
        public void Dispose() { }
    }
}
