using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SOEA.Application.Features.Horario;
using SOEA.Application.Features.Horario.Requests;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;

namespace SOEA.Tests.Application.Horario
{
    /// <summary>
    /// Una sesión fija (horario base) cuyo día/hora no coincide con ningún bloque de la grilla
    /// canónica se omite en silencio (GenerarHorarioService.MapearSesionesFijas, línea ~394-400):
    /// no se agrega a `sesiones`, pero queda registrada en `response.Logs` como advertencia y
    /// cuenta en `response.SesionesFijasOmitidas`. Test de caracterización/regresión — el
    /// comportamiento (omitir con aviso, no lanzar) ya es el esperado; esto solo prueba que
    /// llega intacto hasta la respuesta HTTP final. Las 3 fases se fakean para aislar el mapeo
    /// del resto del pipeline (mismo patrón que GenerarHorarioResponseGruposEnConflictoTests).
    /// </summary>
    public class MapearSesionesFijasOmitidasTests
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

        /// <summary>Fase 1 falsa: identidad — devuelve las sesiones tal cual llegaron.</summary>
        private sealed class FakeMotorColoracion : IMotorColoracionGrafo
        {
            public Task<IEnumerable<Sesion>> AsignarBloquesDeTiempoAsync(
                IEnumerable<Sesion> sesiones, IEnumerable<BloqueTiempo> bloquesDisponibles,
                IEnumerable<Grupo>? grupos = null,
                IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura = null,
                IReadOnlySet<Guid>? sesionesFijasIds = null,
                CancellationToken ct = default) =>
                Task.FromResult(sesiones);
        }

        /// <summary>Fase 2 falsa: siempre factible, generando UNA AsignacionSemanal por sesión (su
        /// semana canónica) en el bloque ya fijado por MapearSesionesFijas.</summary>
        private sealed class FakeMotorFactible : IMotorConstraintProgramming
        {
            public Task<ResultadoFactibilidad> ResolverFactibilidadAsync(
                IEnumerable<Sesion> sesiones, IEnumerable<BloqueTiempo> bloques, IEnumerable<Espacio> espacios,
                IEnumerable<Grupo>? grupos = null, IEnumerable<Guid>? sesionesFijasIds = null,
                IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura = null,
                CancellationToken ct = default)
            {
                var asignaciones = sesiones.Select(s => new AsignacionSemanal(
                    Guid.NewGuid(), s.Id, ModalidadSemanal.SemanaCanonica(s), s.BloqueTiempoId,
                    ModalidadSemanal.ModalidadCanonica(s) == Modalidad.Presencial ? s.EspacioId : null,
                    ModalidadSemanal.ModalidadCanonica(s))).ToList();
                return Task.FromResult(new ResultadoFactibilidad(true, asignaciones, ""));
            }
        }

        /// <summary>Fase 3 falsa: identidad — devuelve las asignaciones de Fase 2 sin optimizar.</summary>
        private sealed class FakeMotorGenetico : IMotorGenetico
        {
            public Task<ResultadoOptimizacion> OptimizarAsync(
                IEnumerable<Sesion> sesiones, IEnumerable<AsignacionSemanal> asignacionesFase2,
                IEnumerable<BloqueTiempo> bloques, IEnumerable<Espacio> espacios,
                IEnumerable<Grupo>? grupos = null, ConfiguracionOptimizacion? config = null,
                IReadOnlyDictionary<Guid, (int sesionesSemana, CategoriaAsignatura categoria)>? infoAsignatura = null,
                IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura = null,
                IReadOnlySet<Guid>? sesionesFijasIds = null,
                IReadOnlyList<Guid>? sesionesCedidasParaRevertir = null,
                CancellationToken ct = default) =>
                Task.FromResult(new ResultadoOptimizacion(asignacionesFase2.ToList(), 0m, 0, UsoFallback: false));
        }

        private static GenerarHorarioService CrearServicio() => new(
            new FakeMotorColoracion(), new FakeMotorFactible(), new FakeMotorGenetico(),
            new FakeHorarioRepo(), new FakeSesionRepo(), new FakeAsignacionRepo(),
            new FakeGrupoRepo(), new FakeCriterioCesionRepo(), new FakeUow());

        private static readonly string GrupoId = Guid.NewGuid().ToString();

        /// <summary>Toda sesión fija necesita un grupo incluido en la generación (P0-3).</summary>
        private static List<GrupoDto> UnGrupo() => new() { new() { Id = GrupoId, Nombre = "G1" } };

        /// <summary>
        /// P0-3 auditoría: sin un grupo del request la fija se omite con aviso. Antes recibía un
        /// GrupoId inventado que la FK de Sesiones rechazaba al persistir (409 en toda generación con base).
        /// </summary>
        [Fact]
        public async Task SesionFijaSinGrupoDelRequest_SeOmiteConAviso()
        {
            var asigId = Guid.NewGuid().ToString();
            var request = new GenerarHorarioRequest
            {
                Semestre = "2026-1",
                Grupos = UnGrupo(),
                SesionesFijas = new List<SesionFijaDto>
                {
                    new() { GrupoId = GrupoId, AsignaturaId = asigId, Dia = "martes", HoraInicio = "07:00", Virtual = true },
                    new() { AsignaturaId = asigId, Virtual = true },
                    new() { GrupoId = Guid.NewGuid().ToString(), AsignaturaId = asigId, Dia = "miercoles", Virtual = true }
                }
            };

            var r = await CrearServicio().EjecutarAsync(request);

            Assert.True(r.EsFactible, r.MensajeError ?? string.Join("\n", r.Logs));
            Assert.Equal(2, r.SesionesFijasOmitidas);
            Assert.Equal(GrupoId, Assert.Single(r.Sesiones).GrupoId);
        }

        /// <summary>
        /// P0-3 auditoría: la fija ocupa el lugar de una sesión del mismo grupo, asignatura y tipo — el
        /// generador planifica el resto alrededor (regla 8). Antes se sumaba: 2 semanales + 1 fija = 3.
        /// </summary>
        [Fact]
        public async Task SesionFija_ReemplazaUnaSesionDelMismoGrupoAsignaturaYTipo()
        {
            var asigId = Guid.NewGuid().ToString();
            var request = new GenerarHorarioRequest
            {
                Semestre = "2026-1",
                Asignaturas = new List<AsignaturaDto>
                {
                    new() { Id = asigId, Nombre = "Cálculo I", SesionesTeoriaVirtualSemana = 2, HorasTeoriaVirtual = 2 }
                },
                Grupos = new List<GrupoDto> { new() { Id = GrupoId, Nombre = "G1", AsignaturaId = asigId } },
                SesionesFijas = new List<SesionFijaDto>
                {
                    new()
                    {
                        GrupoId = GrupoId, AsignaturaId = asigId, Dia = "lunes", HoraInicio = "07:00",
                        DuracionHoras = 2m, Virtual = true, TipoFlujo = "AulaVirtual"
                    }
                }
            };

            var r = await CrearServicio().EjecutarAsync(request);

            Assert.True(r.EsFactible, r.MensajeError ?? string.Join("\n", r.Logs));
            Assert.Equal(2, r.Sesiones.Count);
        }

        [Fact]
        public async Task SesionFijaConDiaQueNoExisteEnLaGrilla_SeOmiteConAvisoYSeCuenta()
        {
            var asigId = Guid.NewGuid().ToString();
            var request = new GenerarHorarioRequest
            {
                Semestre = "2026-1",
                Asignaturas = new List<AsignaturaDto>(),
                Espacios = new List<EspacioDto>(),
                Grupos = UnGrupo(),
                Docentes = new List<DocenteDto>(),
                SesionesFijas = new List<SesionFijaDto>
                {
                    // Válida: "lunes"/"07:00" son los valores por defecto del DTO — coinciden con
                    // un bloque real de GrillaInstitucional. Virtual=true evita tener que declarar
                    // un espacio para que la sesión sea válida.
                    new() { GrupoId = GrupoId, AsignaturaId =asigId, Virtual = true },
                    // Inválida: "domingo" no existe en la grilla canónica (Lunes..Sábado).
                    new() { GrupoId = GrupoId, AsignaturaId =asigId, Dia = "domingo", HoraInicio = "07:00", Virtual = true },
                }
            };

            var r = await CrearServicio().EjecutarAsync(request);

            Assert.True(r.EsFactible);
            Assert.Equal(1, r.SesionesFijasOmitidas);
            Assert.Contains(r.Logs, log => log.Contains("[WARN] Sesión fija omitida") && log.Contains("domingo"));
            // La válida sí llegó hasta el horario generado — una fila, que aplica a todas las
            // semanas (la sesión no alterna, así que no hay contraparte virtual que derivar).
            var fila = Assert.Single(r.Sesiones);
            Assert.Equal(string.Empty, fila.Semana);
        }

        /// <summary>
        /// Regresión (auditoría de limpieza, hallazgo 1.7): MapearSesionesFijas ignoraba
        /// SesionFijaDto.DocenteId y construía la sesión con docenteId: null, aunque el frontend sí
        /// lo manda (horario-api.service.ts). Regenerar con horario base borraba el docente de
        /// todas sus sesiones fijas en cada corrida.
        /// </summary>
        [Fact]
        public async Task SesionFijaConDocenteId_LoConservaEnLaSesionGenerada()
        {
            var asigId = Guid.NewGuid().ToString();
            var docenteId = Guid.NewGuid();
            var request = new GenerarHorarioRequest
            {
                Semestre = "2026-1",
                Asignaturas = new List<AsignaturaDto>(),
                Espacios = new List<EspacioDto>(),
                Grupos = UnGrupo(),
                Docentes = new List<DocenteDto>(),
                SesionesFijas = new List<SesionFijaDto>
                {
                    new() { GrupoId = GrupoId, AsignaturaId =asigId, DocenteId = docenteId.ToString(), Virtual = true }
                }
            };

            var r = await CrearServicio().EjecutarAsync(request);

            Assert.True(r.EsFactible, r.MensajeError ?? string.Join("\n", r.Logs));
            var fila = Assert.Single(r.Sesiones);
            Assert.Equal(docenteId.ToString(), fila.DocenteId);
        }

        /// <summary>
        /// Regresión (auditoría de limpieza, hallazgo 1.7): un AsignaturaId que no parsea como
        /// Guid caía a Guid.NewGuid() — creaba una sesión FANTASMA (sin categoría, sin ventana
        /// horaria, "asignatura sin nombre" en cualquier mensaje de conflicto) en vez de omitirse
        /// con aviso, que es justo el contrato que el propio método documenta y que el caso
        /// "día que no existe en la grilla" (arriba) sí respeta.
        /// </summary>
        [Fact]
        public async Task SesionFijaConAsignaturaIdInvalido_SeOmiteEnVezDeCrearSesionFantasma()
        {
            var asigValidaId = Guid.NewGuid().ToString();
            var request = new GenerarHorarioRequest
            {
                Semestre = "2026-1",
                Asignaturas = new List<AsignaturaDto>(),
                Espacios = new List<EspacioDto>(),
                Grupos = UnGrupo(),
                Docentes = new List<DocenteDto>(),
                SesionesFijas = new List<SesionFijaDto>
                {
                    // Válida, en un slot distinto — solo para que el Horario resultante no quede
                    // vacío (regla de dominio ajena a este bug: un Horario exige ≥1 sesión).
                    new() { GrupoId = GrupoId, AsignaturaId =asigValidaId, Dia = "martes", HoraInicio = "07:00", Virtual = true },
                    new() { GrupoId = GrupoId, AsignaturaId ="no-es-un-guid", Virtual = true }
                }
            };

            var r = await CrearServicio().EjecutarAsync(request);

            Assert.True(r.EsFactible, r.MensajeError ?? string.Join("\n", r.Logs));
            Assert.Equal(1, r.SesionesFijasOmitidas);
            var fila = Assert.Single(r.Sesiones);
            Assert.Equal(asigValidaId, fila.AsignaturaId);
        }
    }
}
