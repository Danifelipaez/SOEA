using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SOEA.Application.Features.Horario;
using SOEA.Application.Features.Horario.Requests;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using Xunit;

namespace SOEA.Tests.Application.Horario
{
    /// <summary>
    /// El loop de Etapa 2 en GenerarHorarioService.EjecutarAsync (reintenta Fase 1/2 mientras
    /// CederSiguienteCandidatoLab siga cediendo pares de laboratorio) no tiene un tope explícito
    /// de iteraciones — termina matemáticamente porque cada iteración consume candidatos
    /// (PuedeCederLab exige &gt;1 sesión por (asignatura, grupo), así que ceder deja a ambos grupos
    /// de la pareja sin más candidatos). En producción cada iteración es un solve CP-SAT completo
    /// (hasta 120s); aquí Fase 2 se fakea siempre infactible por Espacio para forzar el peor caso
    /// (agotar TODOS los candidatos, nunca resolver por factibilidad real) con un dataset de 30
    /// grupos — y confirmar que el loop termina en un tiempo acotado, no se cuelga.
    /// </summary>
    public class CederSiguienteCandidatoLabLoopEstresTests
    {
        private sealed class FakeHorarioRepo : IHorarioRepositorio
        {
            public Task<SOEA.Domain.Entities.Horario?> GetByIdAsync(Guid id) => Task.FromResult<SOEA.Domain.Entities.Horario?>(null);
            public Task<SOEA.Domain.Entities.Horario?> GetBySemestreAsync(string semestre) => Task.FromResult<SOEA.Domain.Entities.Horario?>(null);
            public Task<List<SOEA.Domain.Entities.Horario>> GetAllAsync() => Task.FromResult(new List<SOEA.Domain.Entities.Horario>());
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
            public Task<bool> ExisteAsync(Guid asignaturaId, Guid docenteId, Guid bloqueTiempoId) => Task.FromResult(false);
        }

        private sealed class FakeAsignacionRepo : IAsignacionSemanalRepositorio
        {
            public Task AddAsync(AsignacionSemanal entity) => Task.CompletedTask;
            public Task AddRangeAsync(IEnumerable<AsignacionSemanal> asignaciones) => Task.CompletedTask;
            public Task<AsignacionSemanal?> GetByIdAsync(Guid id) => Task.FromResult<AsignacionSemanal?>(null);
            public Task<List<AsignacionSemanal>> GetAllAsync() => Task.FromResult(new List<AsignacionSemanal>());
            public Task UpdateAsync(AsignacionSemanal entity) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) => Task.CompletedTask;
            public Task<List<AsignacionSemanal>> GetBySesionIdsAsync(IEnumerable<Guid> sesionIds) =>
                Task.FromResult(new List<AsignacionSemanal>());
        }

        private sealed class FakeCriterioCesionRepo : ICriterioCesionAlternanciaRepositorio
        {
            private readonly List<CriterioCesionAlternancia> _criterios = new()
            {
                new CriterioCesionAlternancia(Guid.NewGuid(), CriterioElegibilidadAlternancia.Electiva, 1)
            };
            public Task AddAsync(CriterioCesionAlternancia entity) => Task.CompletedTask;
            public Task<CriterioCesionAlternancia?> GetByIdAsync(Guid id) => Task.FromResult<CriterioCesionAlternancia?>(null);
            public Task<List<CriterioCesionAlternancia>> GetAllAsync() => Task.FromResult(_criterios);
            public Task UpdateAsync(CriterioCesionAlternancia entity) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) => Task.CompletedTask;
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

        /// <summary>Fase 1 falsa: identidad — no hace falta colorear de verdad para este test.</summary>
        private sealed class FakeMotorColoracion : IMotorColoracionGrafo
        {
            public Task<IEnumerable<Sesion>> AsignarBloquesDeTiempoAsync(
                IEnumerable<Sesion> sesiones, IEnumerable<BloqueTiempo> bloquesDisponibles,
                IEnumerable<Grupo>? grupos = null,
                IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura = null,
                CancellationToken ct = default) =>
                Task.FromResult(sesiones);
        }

        /// <summary>Fase 2 falsa: SIEMPRE infactible por Espacio — el peor caso para el loop de
        /// Etapa 2, que solo se detiene cuando CederSiguienteCandidatoLab agota los candidatos.</summary>
        private sealed class FakeMotorSiempreInfactiblePorEspacio : IMotorConstraintProgramming
        {
            public int Llamadas;
            public Task<ResultadoFactibilidad> ResolverFactibilidadAsync(
                IEnumerable<Sesion> sesiones, IEnumerable<BloqueTiempo> bloques, IEnumerable<Espacio> espacios,
                IEnumerable<Grupo>? grupos = null, IEnumerable<Guid>? sesionesFijasIds = null,
                IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura = null,
                CancellationToken ct = default)
            {
                Interlocked.Increment(ref Llamadas);
                return Task.FromResult(new ResultadoFactibilidad(
                    false, Array.Empty<AsignacionSemanal>(), "saturación de espacio", MotivoInfactibilidad.Espacio));
            }
        }

        /// <summary>Fase 3: no debería invocarse nunca (el pipeline aborta en Fase 2 infactible
        /// antes de llegar aquí) — lanza si por error llega a ejecutarse.</summary>
        private sealed class FakeMotorGeneticoNoDeberiaLlamarse : IMotorGenetico
        {
            public Task<ResultadoOptimizacion> OptimizarAsync(
                IEnumerable<Sesion> sesiones, IEnumerable<AsignacionSemanal> asignacionesFase2,
                IEnumerable<BloqueTiempo> bloques, IEnumerable<Espacio> espacios, IEnumerable<Docente> docentes,
                IEnumerable<Grupo>? grupos = null, ConfiguracionOptimizacion? config = null,
                IReadOnlyDictionary<Guid, (int sesionesSemana, CategoriaAsignatura categoria)>? infoAsignatura = null,
                IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura = null,
                IReadOnlySet<Guid>? sesionesFijasIds = null,
                IReadOnlyList<Guid>? sesionesCedidasParaRevertir = null,
                CancellationToken ct = default) =>
                throw new InvalidOperationException("Fase 3 no debería invocarse: Fase 2 nunca es factible en este test.");
        }

        [Fact]
        public async Task LoopDeCesionDeLabs_ConMuchosPares_TerminaEnTiempoAcotado()
        {
            const int totalGrupos = 30; // 30 asignaturas×grupos con 2 sesiones de lab cada uno = 60 sesiones
            var asignaturas = new List<AsignaturaDto>();
            var grupos = new List<GrupoDto>();
            var espacios = new List<EspacioDto> { new() { Id = Guid.NewGuid().ToString(), Nombre = "Lab", Capacidad = 30, Tipo = "Laboratorio" } };

            for (int i = 0; i < totalGrupos; i++)
            {
                var asigId = Guid.NewGuid().ToString();
                asignaturas.Add(new AsignaturaDto
                {
                    Id = asigId,
                    Nombre = $"Asig{i}",
                    SesionesLaboratorioSemana = 2,
                    HorasLaboratorio = 2,
                    Categoria = "Electiva" // elegible para CederSiguienteCandidatoLab
                });
                grupos.Add(new GrupoDto
                {
                    Id = Guid.NewGuid().ToString(),
                    Nombre = $"Grupo{i}",
                    AsignaturaId = asigId,
                    EstudiantesInscritos = 20
                });
            }

            var request = new GenerarHorarioRequest
            {
                Semestre = "2026-1",
                Asignaturas = asignaturas,
                Espacios = espacios,
                Grupos = grupos,
                Docentes = new List<DocenteDto>()
            };

            var fase2 = new FakeMotorSiempreInfactiblePorEspacio();
            var service = new GenerarHorarioService(
                new FakeMotorColoracion(), fase2, new FakeMotorGeneticoNoDeberiaLlamarse(),
                new FakeHorarioRepo(), new FakeSesionRepo(), new FakeAsignacionRepo(),
                new FakeGrupoRepo(), new FakeCriterioCesionRepo(), new FakeUow());

            var cronometro = Stopwatch.StartNew();
            var r = await service.EjecutarAsync(request);
            cronometro.Stop();

            // El pipeline nunca logra ser factible (Fase 2 siempre infactible por Espacio), pero
            // el loop SÍ debe terminar — no colgarse reintentando indefinidamente.
            Assert.False(r.EsFactible);
            Assert.True(cronometro.ElapsedMilliseconds < 5000,
                $"El loop de cesión de labs tardó {cronometro.ElapsedMilliseconds}ms — sugiere que no está acotando iteraciones.");
            // Cada iteración cede exactamente 1 par (2 sesiones) de 2 grupos distintos; con 30
            // grupos de 2 sesiones cada uno, como máximo 15 pares antes de agotar candidatos.
            // +1 porque Fase 2 se llama una vez más antes de que el loop detecte "sin más candidatos".
            Assert.True(fase2.Llamadas <= totalGrupos / 2 + 1,
                $"Fase 2 se invocó {fase2.Llamadas} veces — más de lo que los pares de laboratorio disponibles permiten.");
        }
    }
}
