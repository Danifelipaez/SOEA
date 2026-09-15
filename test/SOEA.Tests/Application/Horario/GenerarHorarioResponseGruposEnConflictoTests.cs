using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SOEA.Application.Features.Horario;
using SOEA.Application.Features.Horario.Requests;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Engine.ConstraintProg;
using SOEA.Engine.Genetic;
using SOEA.Engine.GraphColoring;

namespace SOEA.Tests.Application.Horario
{
    /// <summary>
    /// El campo estructurado ResultadoFactibilidad.GruposResponsablesIds (ver
    /// SweepGruposResponsablesIdsTests, a nivel de motor) no servía de nada si
    /// GenerarHorarioService no lo copiaba a la respuesta HTTP — confirma que sí llega hasta
    /// GenerarHorarioResponse.GruposEnConflicto, lo único que el frontend puede consumir.
    /// Fase 2 se reemplaza por un fake que devuelve directamente el resultado infactible con
    /// Ids — así la prueba cubre solo el mapeo del servicio, no la lógica del solver (ya cubierta
    /// a nivel de motor).
    /// </summary>
    public class GenerarHorarioResponseGruposEnConflictoTests
    {
        private sealed class FakeHorarioRepo : IHorarioRepositorio
        {
            public Task<SOEA.Domain.Entities.Horario?> GetByIdAsync(Guid id) => Task.FromResult<SOEA.Domain.Entities.Horario?>(null);
            public Task<SOEA.Domain.Entities.Horario?> GetBySemestreAsync(string semestre) => Task.FromResult<SOEA.Domain.Entities.Horario?>(null);
            public Task<List<SOEA.Domain.Entities.Horario>> GetAllAsync() => Task.FromResult(new List<SOEA.Domain.Entities.Horario>());
            public Task<List<SOEA.Domain.Entities.Horario>> GetAllBySemestreAsync(string semestre) => Task.FromResult(new List<SOEA.Domain.Entities.Horario>());
            public Task AddAsync(SOEA.Domain.Entities.Horario horario) => Task.CompletedTask;
            public Task UpdateAsync(SOEA.Domain.Entities.Horario horario) => Task.CompletedTask;
        }

        private sealed class FakeSesionRepo : ISesionRepositorio
        {
            public Task AddAsync(Sesion entity) => Task.CompletedTask;
            public Task AddRangeAsync(IEnumerable<Sesion> sesiones) => Task.CompletedTask;
            public Task<Sesion?> GetByIdAsync(Guid id) => Task.FromResult<Sesion?>(null);
            public Task<List<Sesion>> GetAllAsync() => Task.FromResult(new List<Sesion>());
            public Task UpdateAsync(Sesion entity) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) => Task.CompletedTask;
            public Task DeleteRangeAsync(IEnumerable<Guid> ids) => Task.CompletedTask;
            public Task<List<Sesion>> GetByIdsAsync(IEnumerable<Guid> ids) => Task.FromResult(new List<Sesion>());
            public Task<bool> ExisteAsync(Guid asignaturaId, Guid? docenteId, Guid bloqueTiempoId) => Task.FromResult(false);
            public Task<List<Guid>> GetIdsByGrupoIdAsync(Guid grupoId) => Task.FromResult(new List<Guid>());
            public Task<List<Guid>> GetIdsByAsignaturaIdAsync(Guid asignaturaId) => Task.FromResult(new List<Guid>());
            public Task<List<Guid>> GetIdsByEspacioIdAsync(Guid espacioId) => Task.FromResult(new List<Guid>());
        }

        private sealed class FakeAsignacionRepo : IAsignacionSemanalRepositorio
        {
            public Task AddAsync(AsignacionSemanal entity) => Task.CompletedTask;
            public Task AddRangeAsync(IEnumerable<AsignacionSemanal> asignaciones) => Task.CompletedTask;
            public Task<AsignacionSemanal?> GetByIdAsync(Guid id) => Task.FromResult<AsignacionSemanal?>(null);
            public Task<List<AsignacionSemanal>> GetAllAsync() => Task.FromResult(new List<AsignacionSemanal>());
            public Task UpdateAsync(AsignacionSemanal entity) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) => Task.CompletedTask;
            public Task DeleteBySesionIdsAsync(IEnumerable<Guid> sesionIds) => Task.CompletedTask;
            public Task<List<AsignacionSemanal>> GetBySesionIdsAsync(IEnumerable<Guid> sesionIds) =>
                Task.FromResult(new List<AsignacionSemanal>());
        }

        private sealed class FakeCriterioCesionRepo : ICriterioCesionAlternanciaRepositorio
        {
            public Task AddAsync(CriterioCesionAlternancia entity) => Task.CompletedTask;
            public Task<CriterioCesionAlternancia?> GetByIdAsync(Guid id) => Task.FromResult<CriterioCesionAlternancia?>(null);
            public Task<List<CriterioCesionAlternancia>> GetAllAsync() => Task.FromResult(new List<CriterioCesionAlternancia>());
            public Task UpdateAsync(CriterioCesionAlternancia entity) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) => Task.CompletedTask;
        }

        private sealed class FakeGrupoRepo : IGrupoRepositorio
        {
            public Task AddAsync(Grupo entity) => Task.CompletedTask;
            public Task<Grupo?> GetByIdAsync(Guid id) => Task.FromResult<Grupo?>(null);
            public Task<List<Grupo>> GetAllAsync() => Task.FromResult(new List<Grupo>());
            public Task UpdateAsync(Grupo entity) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) => Task.CompletedTask;
            public Task<Grupo?> GetByNombreYProgramaAsync(string nombre, Guid programaId) => Task.FromResult<Grupo?>(null);
            public Task<Grupo?> GetByCodigoAsync(string codigo) => Task.FromResult<Grupo?>(null);
            public Task<IEnumerable<Grupo>> GetByAsignaturaIdAsync(Guid asignaturaId) => Task.FromResult<IEnumerable<Grupo>>(new List<Grupo>());
            public Task<IEnumerable<Grupo>> GetByDocenteIdAsync(Guid docenteId) => Task.FromResult<IEnumerable<Grupo>>(new List<Grupo>());
        }

        private sealed class FakeUow : IUnitOfWork
        {
            public Task BeginTransactionAsync() => Task.CompletedTask;
            public Task SaveAsync() => Task.CompletedTask;
            public Task CommitAsync() => Task.CompletedTask;
            public Task RollbackAsync() => Task.CompletedTask;
            public void Track<T>(T entity) where T : EntidadBase { }
            public void Dispose() { }
        }

        /// <summary>Fase 2 falsa: siempre infactible, con Ids de grupos responsables fijos.</summary>
        private sealed class FakeMotorInfactibleConGrupos : IMotorConstraintProgramming
        {
            private readonly IReadOnlyList<Guid> _responsables;
            public FakeMotorInfactibleConGrupos(IReadOnlyList<Guid> responsables) => _responsables = responsables;

            public Task<ResultadoFactibilidad> ResolverFactibilidadAsync(
                IEnumerable<Sesion> sesiones, IEnumerable<BloqueTiempo> bloques, IEnumerable<Espacio> espacios,
                IEnumerable<Grupo>? grupos = null, IEnumerable<Guid>? sesionesFijasIds = null,
                IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura = null,
                CancellationToken ct = default) =>
                Task.FromResult(new ResultadoFactibilidad(
                    false, Array.Empty<AsignacionSemanal>(),
                    "El modelo no tiene solución factible. Diagnóstico adicional: al excluir cualquiera de los grupos en conflicto el modelo pasa a ser factible.",
                    MotivoInfactibilidad.Otro, _responsables));
        }

        private static GenerarHorarioService CrearServicio(IReadOnlyList<Guid> responsables)
        {
            var fase1 = new AgendadorColoracionGrafo(
                new ConstructorGrafoConflictos(), NullLogger<AgendadorColoracionGrafo>.Instance);
            var fase2 = new FakeMotorInfactibleConGrupos(responsables);
            var fase3 = new MotorGenetico(NullLogger<MotorGenetico>.Instance,
                new AsignadorEspaciosExactoCpSat(NullLogger<AsignadorEspaciosExactoCpSat>.Instance));
            return new GenerarHorarioService(
                fase1, fase2, fase3, new FakeHorarioRepo(), new FakeSesionRepo(),
                new FakeAsignacionRepo(), new FakeGrupoRepo(), new FakeCriterioCesionRepo(), new FakeUow());
        }

        [Fact]
        public async Task Fase2InfactibleConGruposResponsables_ResponseExponeGruposEnConflicto()
        {
            var asigId = Guid.NewGuid().ToString();
            var grupoAId = Guid.NewGuid();
            var grupoBId = Guid.NewGuid();
            var request = new GenerarHorarioRequest
            {
                Semestre = "2026-1",
                Asignaturas = new List<AsignaturaDto>
                {
                    new() { Id = asigId, Nombre = "Física", SesionesTeoriaPresencialSemana = 1, HorasTeoriaPresencial = 2 }
                },
                Espacios = new List<EspacioDto>(),
                Grupos = new List<GrupoDto>
                {
                    new() { Id = grupoAId.ToString(), Nombre = "Grupo A", AsignaturaId = asigId, EstudiantesInscritos = 20 },
                    new() { Id = grupoBId.ToString(), Nombre = "Grupo B", AsignaturaId = asigId, EstudiantesInscritos = 20 }
                },
                Docentes = new List<DocenteDto>()
            };

            var r = await CrearServicio(new[] { grupoAId, grupoBId }).EjecutarAsync(request);

            Assert.False(r.EsFactible);
            Assert.Equal(nameof(MotivoInfactibilidad.Otro), r.MotivoInfactibilidad);
            Assert.NotNull(r.GruposEnConflicto);
            Assert.Contains(grupoAId.ToString(), r.GruposEnConflicto);
            Assert.Contains(grupoBId.ToString(), r.GruposEnConflicto);
        }

        [Fact]
        public async Task Fase2InfactibleSinGruposResponsables_GruposEnConflictoVacio()
        {
            var asigId = Guid.NewGuid().ToString();
            var request = new GenerarHorarioRequest
            {
                Semestre = "2026-1",
                Asignaturas = new List<AsignaturaDto>
                {
                    new() { Id = asigId, Nombre = "Física", SesionesTeoriaPresencialSemana = 1, HorasTeoriaPresencial = 2 }
                },
                Espacios = new List<EspacioDto>(),
                Grupos = new List<GrupoDto>
                {
                    new() { Id = Guid.NewGuid().ToString(), Nombre = "Grupo A", AsignaturaId = asigId, EstudiantesInscritos = 20 }
                },
                Docentes = new List<DocenteDto>()
            };

            // Sin barrido (SweepGrupos deshabilitado o sin candidatos): GruposResponsablesIds es null.
            var r = await CrearServicio(Array.Empty<Guid>()).EjecutarAsync(request);

            Assert.False(r.EsFactible);
            Assert.NotNull(r.GruposEnConflicto);
            Assert.Empty(r.GruposEnConflicto);
        }
    }
}
