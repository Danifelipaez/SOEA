using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SOEA.Application.Features.Horario;
using SOEA.Application.Features.Horario.Requests;
using SOEA.Domain.Interfaces;
using SOEA.Engine.ConstraintProg;
using SOEA.Engine.Genetic;
using SOEA.Engine.GraphColoring;
using SOEA.Tests.Fakes;
using Xunit;

namespace SOEA.Tests.Application.Horario
{
    /// <summary>
    /// POST /api/horario/choques-aulas: el aviso de choques de aulas que el coordinador ve ANTES de
    /// generar. Caso de producción (2026-10-28): miércoles 08:00–09:00 con tres clases y dos salones.
    /// </summary>
    public class DetectarChoquesAulasServiceTests
    {
        private static GenerarHorarioService Servicio() => new(
            new AgendadorColoracionGrafo(new ConstructorGrafoConflictos(), NullLogger<AgendadorColoracionGrafo>.Instance),
            new MotorConstraintProgramming(NullLogger<MotorConstraintProgramming>.Instance),
            new MotorGenetico(NullLogger<MotorGenetico>.Instance, new AsignadorEspaciosExactoCpSat(NullLogger<AsignadorEspaciosExactoCpSat>.Instance)),
            new FakeHorarioRepo(), new FakeSesionRepo(), new FakeAsignacionRepo(), new FakeGrupoRepo(),
            new FakeCriterioCesionRepo(), new FakeUnitOfWork());

        private static string Miercoles(string desde, string hasta) =>
            $"{{\"miercoles\":{{\"noDisponible\":false,\"tipo\":\"Franja específica\",\"desde\":\"{desde}\",\"hasta\":\"{hasta}\"}}}}";

        /// <summary>FUNDAMENTOS 06–09 (3 h), BIOQUIMICA y QUIMICA GENERAL 08–10 (2 h), con 2 salones.</summary>
        private static GenerarHorarioRequest CasoProduccion(string hastaBioquimica = "10:00")
        {
            var request = new GenerarHorarioRequest
            {
                Semestre = "2026-2",
                Espacios =
                {
                    new EspacioDto { Id = Guid.NewGuid().ToString(), Nombre = "QUI", Tipo = "salon", Capacidad = 30 },
                    new EspacioDto { Id = Guid.NewGuid().ToString(), Nombre = "BIO", Tipo = "salon", Capacidad = 30 }
                }
            };
            foreach (var (nombre, horas, desde, hasta) in new[]
                     {
                         ("FUNDAMENTOS QUIMICA", 3, "06:00", "09:00"),
                         ("BIOQUIMICA", 2, "08:00", hastaBioquimica),
                         ("QUIMICA GENERAL", 2, "08:00", "10:00")
                     })
            {
                var asig = Guid.NewGuid().ToString();
                request.Asignaturas.Add(new AsignaturaDto
                    { Id = asig, Nombre = nombre, SesionesTeoriaPresencialSemana = 1, HorasTeoriaPresencial = horas });
                request.Grupos.Add(new GrupoDto
                {
                    Id = Guid.NewGuid().ToString(), Nombre = $"{nombre} - Grupo 1", AsignaturaId = asig,
                    EstudiantesInscritos = 30, DisponibilidadUiJson = Miercoles(desde, hasta)
                });
            }
            return request;
        }

        [Fact]
        public void TresClasesObligadasConDosSalones_DevuelveLaFranjaYLosGrupos()
        {
            var request = CasoProduccion();

            var choque = Assert.Single(Servicio().DetectarChoquesAulas(request));

            Assert.Equal("miercoles", choque.Dia);
            Assert.Equal(("08:00", "09:00"), (choque.HoraInicio, choque.HoraFin));
            Assert.Equal((3, 2), (choque.Clases, choque.Aulas));
            Assert.Equal(request.Grupos.Select(g => g.Id).OrderBy(x => x), choque.GrupoIds.OrderBy(x => x));
        }

        [Fact]
        public void SiUnGrupoPuedeIrAOtraHora_NoHayChoque()
        {
            Assert.Empty(Servicio().DetectarChoquesAulas(CasoProduccion(hastaBioquimica: "12:00")));
        }

        [Fact]
        public void UnaClaseFijaFueraDeLaFranja_NoCuentaEnElChoque()
        {
            // La fija ocupa el lugar de la sesión de su grupo (regla 8) y va a su bloque, no a la
            // disponibilidad del grupo: el aviso debe ver lo mismo que la generación.
            var request = CasoProduccion();
            var bio = request.Grupos[1];
            request.SesionesFijas = new()
            {
                new SesionFijaDto
                {
                    AsignaturaId = bio.AsignaturaId!, GrupoId = bio.Id, Dia = "miercoles",
                    HoraInicio = "10:00", DuracionHoras = 2, TipoFlujo = "AulaVirtual"
                }
            };

            Assert.Empty(Servicio().DetectarChoquesAulas(request));
        }

        [Fact]
        public async Task LaGeneracionFallaConLosMismosChoques()
        {
            var request = CasoProduccion();

            var r = await Servicio().EjecutarAsync(request);

            Assert.False(r.EsFactible);
            Assert.Equal(nameof(MotivoInfactibilidad.Espacio), r.MotivoInfactibilidad);
            var choque = Assert.Single(r.ChoquesAulas);
            Assert.Equal(("miercoles", "08:00", "09:00", 3, 2),
                (choque.Dia, choque.HoraInicio, choque.HoraFin, choque.Clases, choque.Aulas));
            Assert.Equal(request.Grupos.Select(g => g.Id).OrderBy(x => x), choque.GrupoIds.OrderBy(x => x));
        }
    }
}
