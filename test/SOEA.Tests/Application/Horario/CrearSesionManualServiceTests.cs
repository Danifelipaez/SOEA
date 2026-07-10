using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Cubre la creación manual de sesión para los 3 tipos (laboratorio con alternancia,
    /// teoría virtual fija, teoría presencial). TeoriaVirtual_AmbasSemanasVirtualesSinEspacio
    /// es guarda de regresión del fix de ModalidadSemanal: antes del desglose por tipo, el
    /// switch local duplicado en CrearAsignaciones habría dejado ambas semanas en Presencial
    /// con espacio no nulo para una sesión que debía ser virtual en ambas.
    /// </summary>
    public class CrearSesionManualServiceTests
    {
        private static CrearSesionManualService Crear(Guid asignaturaId, Guid? espacioFijo = null) =>
            new(new FakeBloques(), new FakeAsignaturas(asignaturaId, espacioFijo), new FakeSesiones(), new FakeAsignaciones());

        [Fact]
        public async Task Laboratorio_ConAlternanciaTipoA_GeneraPresencialEnAYVirtualEnB()
        {
            var asigId = Guid.NewGuid();
            var espacioId = Guid.NewGuid();
            var svc = Crear(asigId);

            var resultado = await svc.EjecutarAsync(new CrearSesionManualRequest
            {
                AsignaturaId = asigId,
                DocenteId = Guid.NewGuid(),
                EspacioId = espacioId,
                Dia = "lunes",
                HoraInicio = "07:00",
                DuracionHoras = 2m,
                Alternancia = "TipoA",
                TipoFlujo = "Laboratorio"
            });

            Assert.Equal(2, resultado.Count);
            var a = resultado.First(r => r.Semana == "A");
            var b = resultado.First(r => r.Semana == "B");
            Assert.False(a.Virtual);
            Assert.Equal(espacioId.ToString(), a.EspacioId);
            Assert.True(b.Virtual);
            Assert.Null(b.EspacioId);
        }

        [Fact]
        public async Task TeoriaVirtual_AmbasSemanasVirtualesSinEspacio()
        {
            var asigId = Guid.NewGuid();
            var svc = Crear(asigId);

            var resultado = await svc.EjecutarAsync(new CrearSesionManualRequest
            {
                AsignaturaId = asigId,
                DocenteId = Guid.NewGuid(),
                EspacioId = null,
                Dia = "martes",
                HoraInicio = "08:00",
                DuracionHoras = 2m,
                TipoFlujo = "AulaVirtual",
                EsVirtual = true
            });

            Assert.Equal(2, resultado.Count);
            Assert.All(resultado, r =>
            {
                Assert.True(r.Virtual);
                Assert.Null(r.EspacioId);
            });
        }

        [Fact]
        public async Task TeoriaPresencial_IgnoraAlternanciaDelRequest()
        {
            var asigId = Guid.NewGuid();
            var espacioId = Guid.NewGuid();
            var svc = Crear(asigId);

            var resultado = await svc.EjecutarAsync(new CrearSesionManualRequest
            {
                AsignaturaId = asigId,
                DocenteId = Guid.NewGuid(),
                EspacioId = espacioId,
                Dia = "miercoles",
                HoraInicio = "09:00",
                DuracionHoras = 2m,
                Alternancia = "TipoA", // decisión: teoría nunca alterna — se ignora
                TipoFlujo = "AulaVirtual",
                EsVirtual = false
            });

            Assert.Equal(2, resultado.Count);
            Assert.All(resultado, r =>
            {
                Assert.Equal("SinAlternancia", r.Alternancia);
                Assert.False(r.Virtual);
                Assert.Equal(espacioId.ToString(), r.EspacioId);
            });
        }

        // ── Fakes ────────────────────────────────────────────────────────────────

        private sealed class FakeBloques : IBloqueTiempoRepositorio
        {
            public Task<BloqueTiempo?> FindByDiaHoraAsync(DiaDeSemana dia, TimeOnly horaInicio) =>
                Task.FromResult<BloqueTiempo?>(new BloqueTiempo(Guid.NewGuid(), dia, horaInicio, horaInicio.AddHours(1)));
            public Task<bool> ExisteAlgunoAsync() => Task.FromResult(true);
            public Task AddAsync(BloqueTiempo entity) => Task.CompletedTask;
            public Task<BloqueTiempo?> GetByIdAsync(Guid id) => Task.FromResult<BloqueTiempo?>(null);
            public Task<List<BloqueTiempo>> GetAllAsync() => Task.FromResult(new List<BloqueTiempo>());
            public Task UpdateAsync(BloqueTiempo entity) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) => Task.CompletedTask;
        }

        private sealed class FakeAsignaturas : IAsignaturaRepositorio
        {
            private readonly Asignatura _asig;

            public FakeAsignaturas(Guid id, Guid? espacioFijo)
            {
                _asig = new Asignatura(id, "Test", "T-1",
                    sesionesTeoriaPresencialSemana: 2, horasTeoriaPresencial: 2,
                    sesionesTeoriaVirtualSemana: 0, horasTeoriaVirtual: 2,
                    sesionesLaboratorioSemana: 0, horasLaboratorio: 2,
                    sesionesLaboratorioSemestre: 0, programaId: Guid.NewGuid());
                if (espacioFijo.HasValue) _asig.AsignarEspacioFijo(espacioFijo);
            }

            public Task<Asignatura?> GetByIdAsync(Guid id) => Task.FromResult(id == _asig.Id ? _asig : null);
            public Task<Asignatura?> GetByCodigoAsync(string codigo) => Task.FromResult<Asignatura?>(null);
            public Task<Asignatura?> GetByCodigoYProgramaAsync(string codigo, Guid programaId) => Task.FromResult<Asignatura?>(null);
            public Task<Asignatura?> GetByNombreYProgramaAsync(string nombre, Guid programaId) => Task.FromResult<Asignatura?>(null);
            public Task<List<Asignatura>> GetAllAsync() => Task.FromResult(new List<Asignatura> { _asig });
            public Task AddAsync(Asignatura e) => Task.CompletedTask;
            public Task UpdateAsync(Asignatura e) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) => Task.CompletedTask;
        }

        private sealed class FakeSesiones : ISesionRepositorio
        {
            private readonly List<Sesion> _store = new();
            public Task AddAsync(Sesion e) { _store.Add(e); return Task.CompletedTask; }
            public Task AddRangeAsync(IEnumerable<Sesion> sesiones) { _store.AddRange(sesiones); return Task.CompletedTask; }
            public Task<bool> ExisteAsync(Guid asignaturaId, Guid docenteId, Guid bloqueTiempoId) => Task.FromResult(false);
            public Task<Sesion?> GetByIdAsync(Guid id) => Task.FromResult(_store.FirstOrDefault(s => s.Id == id));
            public Task<List<Sesion>> GetAllAsync() => Task.FromResult(_store.ToList());
            public Task UpdateAsync(Sesion e) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) => Task.CompletedTask;
        }

        private sealed class FakeAsignaciones : IAsignacionSemanalRepositorio
        {
            private readonly List<AsignacionSemanal> _store = new();
            public Task AddAsync(AsignacionSemanal e) { _store.Add(e); return Task.CompletedTask; }
            public Task AddRangeAsync(IEnumerable<AsignacionSemanal> asignaciones) { _store.AddRange(asignaciones); return Task.CompletedTask; }
            public Task<List<AsignacionSemanal>> GetBySesionIdsAsync(IEnumerable<Guid> sesionIds) =>
                Task.FromResult(_store.Where(a => sesionIds.Contains(a.SesionId)).ToList());
            public Task<AsignacionSemanal?> GetByIdAsync(Guid id) => Task.FromResult(_store.FirstOrDefault(a => a.Id == id));
            public Task<List<AsignacionSemanal>> GetAllAsync() => Task.FromResult(_store.ToList());
            public Task UpdateAsync(AsignacionSemanal e) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) => Task.CompletedTask;
        }
    }
}
