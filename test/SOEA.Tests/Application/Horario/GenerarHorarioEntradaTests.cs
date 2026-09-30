using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SOEA.Application.Features.Horario;
using SOEA.Application.Features.Horario.Requests;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Engine.ConstraintProg;
using SOEA.Engine.Genetic;
using SOEA.Engine.GraphColoring;
using SOEA.Tests.Fakes;
using Xunit;

namespace SOEA.Tests.Application.Horario
{
    /// <summary>
    /// Auditoría de escala (2026-09-29): entradas que el pipeline trataba mal — asignaturas sin grupos
    /// omitidas en silencio, Ids repetidos que tumbaban con un 400 en inglés de framework.
    /// </summary>
    public class GenerarHorarioEntradaTests
    {
        private static GenerarHorarioService Servicio() => new(
            new AgendadorColoracionGrafo(new ConstructorGrafoConflictos(), NullLogger<AgendadorColoracionGrafo>.Instance),
            new MotorConstraintProgramming(NullLogger<MotorConstraintProgramming>.Instance),
            new MotorGenetico(NullLogger<MotorGenetico>.Instance, new AsignadorEspaciosExactoCpSat(NullLogger<AsignadorEspaciosExactoCpSat>.Instance)),
            new FakeHorarioRepo(), new FakeSesionRepo(), new FakeAsignacionRepo(), new FakeGrupoRepo(),
            new FakeCriterioCesionRepo(), new FakeUnitOfWork());

        private static AsignaturaDto Asig(string id, string nombre) =>
            new() { Id = id, Nombre = nombre, SesionesTeoriaPresencialSemana = 1, HorasTeoriaPresencial = 2 };

        private static EspacioDto Salon(string id) => new() { Id = id, Nombre = "Salón 1", Tipo = "salon", Capacidad = 40 };

        [Fact]
        public async Task SinNingunGrupo_ResponderDatosNombrandoLasAsignaturasSinGrupos()
        {
            var request = new GenerarHorarioRequest
            {
                Asignaturas = { Asig(Guid.NewGuid().ToString(), "Física"), Asig(Guid.NewGuid().ToString(), "Cálculo") },
                Espacios = { Salon(Guid.NewGuid().ToString()) }
            };

            var r = await Servicio().EjecutarAsync(request);

            Assert.False(r.EsFactible);
            Assert.Equal(nameof(MotivoInfactibilidad.Datos), r.MotivoInfactibilidad);
            Assert.Contains("2 asignatura(s) no tienen grupos", r.MensajeError);
            Assert.Contains("Física", r.MensajeError);
        }

        [Fact]
        public async Task AsignaturaSinGrupos_SeAvisaPorNombre_YElRestoSeGenera()
        {
            var conGrupo = Guid.NewGuid().ToString();
            var request = new GenerarHorarioRequest
            {
                Asignaturas = { Asig(conGrupo, "Física"), Asig(Guid.NewGuid().ToString(), "Huérfana") },
                Espacios = { Salon(Guid.NewGuid().ToString()) },
                Grupos = { new GrupoDto { Id = Guid.NewGuid().ToString(), Nombre = "G1", AsignaturaId = conGrupo, EstudiantesInscritos = 20 } }
            };

            var r = await Servicio().EjecutarAsync(request);

            Assert.True(r.EsFactible, r.MensajeError);
            Assert.Contains(r.Logs, l => l.StartsWith("[WARN]") && l.Contains("'Huérfana' no tiene grupos"));
            Assert.DoesNotContain(r.Logs, l => l.Contains("'Física' no tiene grupos"));
        }

        [Fact]
        public async Task IdsRepetidosDeAsignaturaYEspacio_NoTumbanLaGeneracion_YSeAvisa()
        {
            var asigId = Guid.NewGuid().ToString();
            var espId = Guid.NewGuid().ToString();
            var request = new GenerarHorarioRequest
            {
                Asignaturas = { Asig(asigId, "Física"), Asig(asigId, "Física (copia)") },
                Espacios = { Salon(espId), Salon(espId) },
                Grupos = { new GrupoDto { Id = Guid.NewGuid().ToString(), Nombre = "G1", AsignaturaId = asigId, EstudiantesInscritos = 20 } }
            };

            var r = await Servicio().EjecutarAsync(request);   // antes: ArgumentException "An item with the same key…"

            Assert.True(r.EsFactible, r.MensajeError);
            Assert.Contains(r.Logs, l => l.Contains("Asignatura 'Física (copia)'") && l.Contains("repetido"));
            Assert.Contains(r.Logs, l => l.Contains("Espacio 'Salón 1'") && l.Contains("repetido"));
        }
    }
}
