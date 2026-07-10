using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Application.Features.Import;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using Xunit;

namespace SOEA.Tests.Application
{
    /// <summary>
    /// Verifica el flujo de persistencia de ImportarCurriculumService usando fakes en memoria.
    /// Cubre: creación de entidades nuevas, reuso de existentes (upsert) y generación de stats.
    /// </summary>
    public class ImportarCurriculumServiceTests
    {
        // ── Helpers de construcción ──────────────────────────────────────────────

        private static Facultad Facultad(Guid id, string nombre) => new(id, nombre);
        private static Programa Programa(Guid id, string nombre, Guid facId) => new(id, nombre, facId);

        private static Docente Docente(Guid id, string nombre) =>
            new(id, nombre, "", $"{id}@soea.local", 40m,
                new List<FranjaHoraria> { FranjaHoraria.Matutino });

        private static Asignatura Asignatura(Guid id, string nombre, string codigo, Guid progId) =>
            new(id, nombre, codigo, 2, 1, 8, progId);

        private static Grupo Grupo(Guid id, string nombre, Guid progId) =>
            new(id, nombre, progId, 1, 30);

        // ── Construcción del servicio ────────────────────────────────────────────

        private static ImportarCurriculumService Crear(
            FakeFacultadRepo facRepo,
            FakeProgramaRepo progRepo,
            FakeDocenteRepo docRepo,
            FakeEspacioRepo espRepo,
            FakeAsignaturaRepo asigRepo,
            FakeGrupoRepo grupoRepo,
            FakeSesionRepo sesionRepo,
            FakeBloqueTiempoRepo bloqueRepo,
            FakeUnitOfWork uow)
            => new(uow, facRepo, progRepo, docRepo, espRepo, asigRepo, grupoRepo, sesionRepo, bloqueRepo);

        // ── Tests ────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Facultad_Nueva_SeRegistraEnStats()
        {
            var uow      = new FakeUnitOfWork();
            var facRepo  = new FakeFacultadRepo(uow);
            var service  = Crear(facRepo, new FakeProgramaRepo(uow), new FakeDocenteRepo(uow),
                new FakeEspacioRepo(uow), new FakeAsignaturaRepo(uow), new FakeGrupoRepo(uow),
                new FakeSesionRepo(), new FakeBloqueTiempoRepo(), uow);

            var facId    = Guid.NewGuid();
            var resultado = Resultado(facultades: new[] { Facultad(facId, "Facultad Ciencias") });

            var stats = await service.EjecutarAsync(resultado);

            Assert.Equal(1, stats.FacultadesCreadas);
            Assert.True(stats.FacultadMappings.ContainsKey(facId));
        }

        [Fact]
        public async Task Facultad_Existente_NoDuplica()
        {
            var facId   = Guid.NewGuid();
            var uow     = new FakeUnitOfWork();
            var facRepo = new FakeFacultadRepo(uow, Facultad(facId, "Ciencias Básicas"));
            var service = Crear(facRepo, new FakeProgramaRepo(uow), new FakeDocenteRepo(uow),
                new FakeEspacioRepo(uow), new FakeAsignaturaRepo(uow), new FakeGrupoRepo(uow),
                new FakeSesionRepo(), new FakeBloqueTiempoRepo(), uow);

            var tempFacId = Guid.NewGuid();
            var resultado = Resultado(facultades: new[] { Facultad(tempFacId, "Ciencias Básicas") });

            var stats = await service.EjecutarAsync(resultado);

            Assert.Equal(0, stats.FacultadesCreadas);
            // El mapa señala al ID real existente
            Assert.Equal(facId, stats.FacultadMappings[tempFacId]);
        }

        [Fact]
        public async Task Asignatura_Nueva_ContadaEnStats()
        {
            var facId  = Guid.NewGuid();
            var progId = Guid.NewGuid();
            var uow    = new FakeUnitOfWork();
            var facRepo  = new FakeFacultadRepo(uow,  Facultad(facId, "Ingeniería"));
            var progRepo = new FakeProgramaRepo(uow,  Programa(progId, "Química Industrial", facId));

            var service = Crear(facRepo, progRepo, new FakeDocenteRepo(uow),
                new FakeEspacioRepo(uow), new FakeAsignaturaRepo(uow), new FakeGrupoRepo(uow),
                new FakeSesionRepo(), new FakeBloqueTiempoRepo(), uow);

            var asigTempId = Guid.NewGuid();
            var facTempId  = Guid.NewGuid();
            var progTempId = Guid.NewGuid();

            var resultado = Resultado(
                facultades:  new[] { Facultad(facTempId, "Ingeniería") },
                programas:   new[] { Programa(progTempId, "Química Industrial", facTempId) },
                asignaturas: new[] { Asignatura(asigTempId, "Análisis Químico", "QUI101", progTempId) });

            var stats = await service.EjecutarAsync(resultado);

            Assert.Equal(1, stats.AsignaturasCreadas);
            Assert.Equal(0, stats.AsignaturasActualizadas);
            Assert.True(stats.AsignaturaMappings.ContainsKey(asigTempId));
        }

        [Fact]
        public async Task Asignatura_Existente_PorNombre_ActualizaEnLugarDeCrear()
        {
            var facId    = Guid.NewGuid();
            var progId   = Guid.NewGuid();
            var asigId   = Guid.NewGuid();
            var uow      = new FakeUnitOfWork();
            var facRepo  = new FakeFacultadRepo(uow,  Facultad(facId, "Ciencias"));
            var progRepo = new FakeProgramaRepo(uow,  Programa(progId, "Biología", facId));
            var asigRepo = new FakeAsignaturaRepo(uow, Asignatura(asigId, "Bioquímica", "BIO201", progId));

            var service = Crear(facRepo, progRepo, new FakeDocenteRepo(uow),
                new FakeEspacioRepo(uow), asigRepo, new FakeGrupoRepo(uow),
                new FakeSesionRepo(), new FakeBloqueTiempoRepo(), uow);

            var facTempId  = Guid.NewGuid();
            var progTempId = Guid.NewGuid();
            var asigTempId = Guid.NewGuid();

            var resultado = Resultado(
                facultades:  new[] { Facultad(facTempId, "Ciencias") },
                programas:   new[] { Programa(progTempId, "Biología", facTempId) },
                asignaturas: new[] { Asignatura(asigTempId, "Bioquímica", "BIO201", progTempId) });

            var stats = await service.EjecutarAsync(resultado);

            Assert.Equal(0, stats.AsignaturasCreadas);
            Assert.Equal(1, stats.AsignaturasActualizadas);
            // El mapa apunta al ID real ya existente
            Assert.Equal(asigId, stats.AsignaturaMappings[asigTempId]);
        }

        [Fact]
        public async Task Asignatura_Existente_ActualizaDuracionYNombre()
        {
            var facId    = Guid.NewGuid();
            var progId   = Guid.NewGuid();
            var asigId   = Guid.NewGuid();
            var uow      = new FakeUnitOfWork();
            var existente = Asignatura(asigId, "Bioquímica", "BIO201", progId); // 2h, 1 ses/sem, 8 lab
            var facRepo  = new FakeFacultadRepo(uow,  Facultad(facId, "Ciencias"));
            var progRepo = new FakeProgramaRepo(uow,  Programa(progId, "Biología", facId));
            var asigRepo = new FakeAsignaturaRepo(uow, existente);

            var service = Crear(facRepo, progRepo, new FakeDocenteRepo(uow),
                new FakeEspacioRepo(uow), asigRepo, new FakeGrupoRepo(uow),
                new FakeSesionRepo(), new FakeBloqueTiempoRepo(), uow);

            var facTempId  = Guid.NewGuid();
            var progTempId = Guid.NewGuid();
            var resultado = Resultado(
                facultades:  new[] { Facultad(facTempId, "Ciencias") },
                programas:   new[] { Programa(progTempId, "Biología", facTempId) },
                asignaturas: new[] { new Asignatura(Guid.NewGuid(), "Bioquímica Avanzada", "BIO201", 3, 2, 11, progTempId) });

            var stats = await service.EjecutarAsync(resultado);

            Assert.Equal(1, stats.AsignaturasActualizadas);
            Assert.Equal("Bioquímica Avanzada", existente.Nombre);
            Assert.Equal(3, existente.HorasPorSesion);
            Assert.Equal(2, existente.SesionesPorSemana);
            Assert.Equal(11, existente.SesionesLaboratorioSemestre);
        }

        [Fact]
        public async Task Asignatura_Existente_SinAlternanciaEntrante_NoPisaTipoEstablecido()
        {
            var facId    = Guid.NewGuid();
            var progId   = Guid.NewGuid();
            var uow      = new FakeUnitOfWork();
            var existente = Asignatura(Guid.NewGuid(), "Bioquímica", "BIO201", progId);
            existente.EstablecerAlternancia(TipoAlternancia.TipoB); // override manual previo
            var facRepo  = new FakeFacultadRepo(uow,  Facultad(facId, "Ciencias"));
            var progRepo = new FakeProgramaRepo(uow,  Programa(progId, "Biología", facId));
            var asigRepo = new FakeAsignaturaRepo(uow, existente);

            var service = Crear(facRepo, progRepo, new FakeDocenteRepo(uow),
                new FakeEspacioRepo(uow), asigRepo, new FakeGrupoRepo(uow),
                new FakeSesionRepo(), new FakeBloqueTiempoRepo(), uow);

            var facTempId  = Guid.NewGuid();
            var progTempId = Guid.NewGuid();
            // Entrante con 0 sesiones de lab ⇒ SinAlternancia: NO debe pisar el TipoB establecido.
            var resultado = Resultado(
                facultades:  new[] { Facultad(facTempId, "Ciencias") },
                programas:   new[] { Programa(progTempId, "Biología", facTempId) },
                asignaturas: new[] { new Asignatura(Guid.NewGuid(), "Bioquímica", "BIO201", 2, 1, 0, progTempId) });

            await service.EjecutarAsync(resultado);

            Assert.Equal(TipoAlternancia.TipoB, existente.Alternancia);
        }

        [Fact]
        public async Task Espacio_Existente_ActualizaCapacidadYTipo()
        {
            var uow      = new FakeUnitOfWork();
            var existente = new Espacio(Guid.NewGuid(), "Lab Química 1", TipoEspacio.Salon, 20);
            var espRepo  = new FakeEspacioRepo(uow, existente);

            var service = Crear(new FakeFacultadRepo(uow), new FakeProgramaRepo(uow),
                new FakeDocenteRepo(uow), espRepo, new FakeAsignaturaRepo(uow),
                new FakeGrupoRepo(uow), new FakeSesionRepo(), new FakeBloqueTiempoRepo(), uow);

            var resultado = Resultado(
                espacios: new[] { new Espacio(Guid.NewGuid(), "Lab Química 1", TipoEspacio.Laboratorio, 35, "Bloque A", 2) });

            var stats = await service.EjecutarAsync(resultado);

            Assert.Equal(0, stats.EspaciosCreados);
            Assert.Equal(1, stats.EspaciosActualizados);
            Assert.Equal(35, existente.Capacidad);
            Assert.Equal(TipoEspacio.Laboratorio, existente.Tipo);
            Assert.Equal("Bloque A", existente.Edificio);
        }

        [Fact]
        public async Task Docente_Existente_ActualizaMaxHoras()
        {
            var uow      = new FakeUnitOfWork();
            var existente = Docente(Guid.NewGuid(), "Juan Pérez"); // 40 h
            var docRepo  = new FakeDocenteRepo(uow, existente);

            var service = Crear(new FakeFacultadRepo(uow), new FakeProgramaRepo(uow),
                docRepo, new FakeEspacioRepo(uow), new FakeAsignaturaRepo(uow),
                new FakeGrupoRepo(uow), new FakeSesionRepo(), new FakeBloqueTiempoRepo(), uow);

            var entrante = new Docente(Guid.NewGuid(), "Juan Pérez", "", "juan.perez@unimag.edu.co", 20m,
                new List<FranjaHoraria> { FranjaHoraria.Matutino });
            var resultado = Resultado(docentes: new[] { entrante });

            var stats = await service.EjecutarAsync(resultado);

            Assert.Equal(1, stats.DocentesActualizados);
            Assert.Equal(20m, existente.MaximoHorasSemanales);
            Assert.Equal("juan.perez@unimag.edu.co", existente.Correo);
        }

        [Fact]
        public async Task EstadisticasVacias_CuandoResultadoEsVacio()
        {
            var uow     = new FakeUnitOfWork();
            var service = Crear(new FakeFacultadRepo(uow), new FakeProgramaRepo(uow),
                new FakeDocenteRepo(uow), new FakeEspacioRepo(uow), new FakeAsignaturaRepo(uow),
                new FakeGrupoRepo(uow), new FakeSesionRepo(), new FakeBloqueTiempoRepo(), uow);

            var stats = await service.EjecutarAsync(Resultado());

            Assert.Equal(0, stats.FacultadesCreadas);
            Assert.Equal(0, stats.AsignaturasCreadas);
            Assert.Equal(0, stats.SesionesPersistidas);
        }

        // ── Resultado vacío helper ───────────────────────────────────────────────

        private static CurriculumExcelResult Resultado(
            IReadOnlyList<Facultad>?   facultades  = null,
            IReadOnlyList<Programa>?   programas   = null,
            IReadOnlyList<Asignatura>? asignaturas = null,
            IReadOnlyList<Docente>?    docentes    = null,
            IReadOnlyList<Sesion>?     sesiones    = null,
            IReadOnlyList<Espacio>?    espacios    = null,
            IReadOnlyList<Grupo>?      grupos      = null)
            => new(
                facultades  ?? Array.Empty<Facultad>(),
                programas   ?? Array.Empty<Programa>(),
                asignaturas ?? Array.Empty<Asignatura>(),
                docentes    ?? Array.Empty<Docente>(),
                sesiones    ?? Array.Empty<Sesion>(),
                espacios    ?? Array.Empty<Espacio>(),
                grupos      ?? Array.Empty<Grupo>());

        // ── FakeUnitOfWork ───────────────────────────────────────────────────────

        private sealed class FakeUnitOfWork : IUnitOfWork
        {
            private readonly Dictionary<Type, Dictionary<Guid, EntidadBase>> _tracked = new();

            public Task BeginTransactionAsync() => Task.CompletedTask;
            public Task SaveAsync()             => Task.CompletedTask;
            public Task CommitAsync()           => Task.CompletedTask;
            public Task RollbackAsync()         => Task.CompletedTask;
            public void Dispose() { }

            public void Track<T>(T entity) where T : EntidadBase
            {
                var t = typeof(T);
                if (!_tracked.ContainsKey(t)) _tracked[t] = new();
                _tracked[t][entity.Id] = entity;
            }

            public T? Find<T>(Guid id) where T : EntidadBase
                => _tracked.TryGetValue(typeof(T), out var store)
                    ? store.TryGetValue(id, out var e) ? (T)e : null
                    : null;

            public IEnumerable<T> All<T>() where T : EntidadBase
                => _tracked.TryGetValue(typeof(T), out var store)
                    ? store.Values.Cast<T>()
                    : Enumerable.Empty<T>();
        }

        // ── Repos fake ───────────────────────────────────────────────────────────

        private sealed class FakeFacultadRepo : IFacultadRepositorio
        {
            private readonly FakeUnitOfWork _uow;
            private readonly Dictionary<Guid, Facultad> _store = new();

            public FakeFacultadRepo(FakeUnitOfWork uow, params Facultad[] seed)
            {
                _uow = uow;
                foreach (var f in seed) _store[f.Id] = f;
            }

            public Task<Facultad?> GetByNombreAsync(string nombre)
            {
                var result = _store.Values.FirstOrDefault(x =>
                    x.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase))
                    ?? _uow.All<Facultad>().FirstOrDefault(x =>
                        x.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase));
                return Task.FromResult(result);
            }

            public Task<Facultad?> GetByIdAsync(Guid id)
                => Task.FromResult(_store.GetValueOrDefault(id) ?? _uow.Find<Facultad>(id));
            public Task<List<Facultad>> GetAllAsync()
                => Task.FromResult(_store.Values.Concat(_uow.All<Facultad>()).DistinctBy(x => x.Id).ToList());
            public Task AddAsync(Facultad e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task UpdateAsync(Facultad e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }
        }

        private sealed class FakeProgramaRepo : IProgramaRepositorio
        {
            private readonly FakeUnitOfWork _uow;
            private readonly Dictionary<Guid, Programa> _store = new();

            public FakeProgramaRepo(FakeUnitOfWork uow, params Programa[] seed)
            {
                _uow = uow;
                foreach (var p in seed) _store[p.Id] = p;
            }

            public Task<Programa?> GetByNombreYFacultadAsync(string nombre, Guid facultadId)
            {
                var result = _store.Values.FirstOrDefault(x =>
                    x.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase) && x.FacultadId == facultadId)
                    ?? _uow.All<Programa>().FirstOrDefault(x =>
                        x.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase) && x.FacultadId == facultadId);
                return Task.FromResult(result);
            }

            public Task<Programa?> GetByIdAsync(Guid id)
                => Task.FromResult(_store.GetValueOrDefault(id) ?? _uow.Find<Programa>(id));
            public Task<List<Programa>> GetAllAsync()
                => Task.FromResult(_store.Values.Concat(_uow.All<Programa>()).DistinctBy(x => x.Id).ToList());
            public Task AddAsync(Programa e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task UpdateAsync(Programa e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }
        }

        private sealed class FakeDocenteRepo : IDocenteRepositorio
        {
            private readonly FakeUnitOfWork _uow;
            private readonly Dictionary<Guid, Docente> _store = new();

            public FakeDocenteRepo(FakeUnitOfWork uow, params Docente[] seed)
            {
                _uow = uow;
                foreach (var d in seed) _store[d.Id] = d;
            }

            public Task<Docente?> GetByCedulaAsync(string cedula) =>
                Task.FromResult(_store.Values.FirstOrDefault(d => d.CedulaIdentidad == cedula));
            public Task<Docente?> GetByNombreAsync(string nombre) =>
                Task.FromResult(_store.Values.FirstOrDefault(d =>
                    d.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase)));
            public Task<Docente?> GetByIdAsync(Guid id) =>
                Task.FromResult(_store.GetValueOrDefault(id) ?? _uow.Find<Docente>(id));
            public Task<List<Docente>> GetAllAsync() =>
                Task.FromResult(_store.Values.Concat(_uow.All<Docente>()).DistinctBy(x => x.Id).ToList());
            public Task AddAsync(Docente e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task UpdateAsync(Docente e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }
        }

        private sealed class FakeEspacioRepo : IEspacioRepositorio
        {
            private readonly FakeUnitOfWork _uow;
            private readonly Dictionary<Guid, Espacio> _store = new();

            public FakeEspacioRepo(FakeUnitOfWork uow, params Espacio[] seed)
            {
                _uow = uow;
                foreach (var e in seed) _store[e.Id] = e;
            }

            public Task<Espacio?> GetByNombreAsync(string nombre)
            {
                var result = _store.Values.FirstOrDefault(x =>
                    x.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase))
                    ?? _uow.All<Espacio>().FirstOrDefault(x =>
                        x.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase));
                return Task.FromResult(result);
            }

            public Task<Espacio?> GetByIdAsync(Guid id) =>
                Task.FromResult(_store.GetValueOrDefault(id) ?? _uow.Find<Espacio>(id));
            public Task<List<Espacio>> GetAllAsync() =>
                Task.FromResult(_store.Values.Concat(_uow.All<Espacio>()).DistinctBy(x => x.Id).ToList());
            public Task AddAsync(Espacio e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task UpdateAsync(Espacio e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }
        }

        private sealed class FakeAsignaturaRepo : IAsignaturaRepositorio
        {
            private readonly FakeUnitOfWork _uow;
            private readonly Dictionary<Guid, Asignatura> _store = new();

            public FakeAsignaturaRepo(FakeUnitOfWork uow, params Asignatura[] seed)
            {
                _uow = uow;
                foreach (var a in seed) _store[a.Id] = a;
            }

            public Task<Asignatura?> GetByCodigoAsync(string codigo) =>
                Task.FromResult(_store.Values.FirstOrDefault(a => a.Codigo == codigo));
            public Task<Asignatura?> GetByCodigoYProgramaAsync(string codigo, Guid programaId) =>
                Task.FromResult(_store.Values.FirstOrDefault(a => a.Codigo == codigo && a.ProgramaId == programaId));
            public Task<Asignatura?> GetByNombreYProgramaAsync(string nombre, Guid programaId)
            {
                var result = _store.Values.FirstOrDefault(a =>
                    a.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase) && a.ProgramaId == programaId);
                return Task.FromResult(result);
            }
            public Task<Asignatura?> GetByIdAsync(Guid id) =>
                Task.FromResult(_store.GetValueOrDefault(id) ?? _uow.Find<Asignatura>(id));
            public Task<List<Asignatura>> GetAllAsync() =>
                Task.FromResult(_store.Values.Concat(_uow.All<Asignatura>()).DistinctBy(x => x.Id).ToList());
            public Task AddAsync(Asignatura e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task UpdateAsync(Asignatura e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }
        }

        private sealed class FakeGrupoRepo : IGrupoRepositorio
        {
            private readonly FakeUnitOfWork _uow;
            private readonly Dictionary<Guid, Grupo> _store = new();

            public FakeGrupoRepo(FakeUnitOfWork uow, params Grupo[] seed)
            {
                _uow = uow;
                foreach (var g in seed) _store[g.Id] = g;
            }

            public Task<Grupo?> GetByNombreYProgramaAsync(string nombre, Guid programaId)
            {
                var result = _store.Values.FirstOrDefault(x =>
                    x.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase) && x.ProgramaId == programaId)
                    ?? _uow.All<Grupo>().FirstOrDefault(x =>
                        x.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase) && x.ProgramaId == programaId);
                return Task.FromResult(result);
            }

            public Task<Grupo?> GetByIdAsync(Guid id) =>
                Task.FromResult(_store.GetValueOrDefault(id) ?? _uow.Find<Grupo>(id));
            public Task<List<Grupo>> GetAllAsync() =>
                Task.FromResult(_store.Values.Concat(_uow.All<Grupo>()).DistinctBy(x => x.Id).ToList());
            public Task AddAsync(Grupo e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task UpdateAsync(Grupo e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }

            public Task<Grupo?> GetByCodigoAsync(string codigo)
            {
                var result = _store.Values.FirstOrDefault(x =>
                    x.Codigo != null && x.Codigo.Equals(codigo, StringComparison.OrdinalIgnoreCase));
                return Task.FromResult(result);
            }

            public Task<IEnumerable<Grupo>> GetByAsignaturaIdAsync(Guid asignaturaId) =>
                Task.FromResult<IEnumerable<Grupo>>(_store.Values.Where(x => x.AsignaturaId == asignaturaId).ToList());
        }

        private sealed class FakeSesionRepo : ISesionRepositorio
        {
            private readonly Dictionary<Guid, Sesion> _store = new();

            public Task AddRangeAsync(IEnumerable<Sesion> sesiones)
            {
                foreach (var s in sesiones) _store[s.Id] = s;
                return Task.CompletedTask;
            }

            public Task<bool> ExisteAsync(Guid asignaturaId, Guid docenteId, Guid bloqueTiempoId) =>
                Task.FromResult(_store.Values.Any(x =>
                    x.AsignaturaId   == asignaturaId  &&
                    x.DocenteId      == docenteId     &&
                    x.BloqueTiempoId == bloqueTiempoId));

            public Task<Sesion?> GetByIdAsync(Guid id) => Task.FromResult(_store.GetValueOrDefault(id));
            public Task<List<Sesion>> GetAllAsync() => Task.FromResult(_store.Values.ToList());
            public Task AddAsync(Sesion e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task UpdateAsync(Sesion e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }
        }

        private sealed class FakeBloqueTiempoRepo : IBloqueTiempoRepositorio
        {
            private readonly Dictionary<Guid, BloqueTiempo> _store = new();

            public FakeBloqueTiempoRepo(params BloqueTiempo[] seed)
            {
                foreach (var b in seed) _store[b.Id] = b;
            }

            public Task<BloqueTiempo?> FindByDiaHoraAsync(DiaDeSemana dia, TimeOnly horaInicio) =>
                Task.FromResult(_store.Values.FirstOrDefault(b => b.Dia == dia && b.HoraInicio == horaInicio));
            public Task<bool> ExisteAlgunoAsync() => Task.FromResult(_store.Count > 0);
            public Task<BloqueTiempo?> GetByIdAsync(Guid id) => Task.FromResult(_store.GetValueOrDefault(id));
            public Task<List<BloqueTiempo>> GetAllAsync() => Task.FromResult(_store.Values.ToList());
            public Task AddAsync(BloqueTiempo e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task UpdateAsync(BloqueTiempo e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }
        }
    }
}
