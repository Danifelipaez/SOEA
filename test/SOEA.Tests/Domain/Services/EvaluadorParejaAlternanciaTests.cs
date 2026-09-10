using System;
using System.Collections.Generic;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Services;
using SOEA.Domain.ValueObjects;

namespace SOEA.Tests.Domain.Services
{
    /// <summary>
    /// Reglas de emparejamiento de alternancia. Las dos compuertas nuevas (franja común y aula
    /// común) son las que impiden que una pareja vuelva el modelo infactible por un motivo distinto
    /// de Espacio, que es el fallo que rompería el bucle de cesión reactiva.
    /// </summary>
    public class EvaluadorParejaAlternanciaTests
    {
        private static readonly int[] Todo = { 0, 1, 2, 3 };

        private static Sesion Sesion(
            Guid? asignaturaId = null,
            Guid? grupoId = null,
            decimal duracion = 2m,
            TipoFlujo flujo = TipoFlujo.AulaVirtual) =>
            new(Guid.NewGuid(), asignaturaId ?? Guid.NewGuid(), null, Guid.NewGuid(), null,
                grupoId ?? Guid.NewGuid(), TipoAlternancia.SinAlternancia, Modalidad.Presencial,
                duracion, false, false, flujo);

        private static MotivoRechazoPareja Evaluar(
            Sesion a, Sesion b,
            IReadOnlyDictionary<Guid, Grupo>? grupos = null,
            int[]? dominioA = null, int[]? dominioB = null,
            int[]? aulasA = null, int[]? aulasB = null) =>
            EvaluadorParejaAlternancia.Evaluar(
                a, b, grupos ?? new Dictionary<Guid, Grupo>(),
                dominioA ?? Todo, dominioB ?? Todo, aulasA ?? Todo, aulasB ?? Todo);

        [Fact]
        public void CandidatasCompatibles_SeAceptan()
        {
            Assert.Equal(MotivoRechazoPareja.Ninguno, Evaluar(Sesion(), Sesion()));
        }

        [Fact]
        public void MismaAsignatura_SeRechaza()
        {
            var asig = Guid.NewGuid();
            Assert.Equal(MotivoRechazoPareja.MismaAsignatura,
                         Evaluar(Sesion(asignaturaId: asig), Sesion(asignaturaId: asig)));
        }

        [Fact]
        public void MismoGrupo_SeRechaza()
        {
            // HC-C01 es independiente de la semana y HC-ALT fuerza el mismo bloque: la cohorte
            // tendría dos sesiones a la misma hora en las dos semanas.
            var grupo = Guid.NewGuid();
            Assert.Equal(MotivoRechazoPareja.MismoGrupo,
                         Evaluar(Sesion(grupoId: grupo), Sesion(grupoId: grupo)));
        }

        [Fact]
        public void DuracionDistinta_SeRechaza()
        {
            Assert.Equal(MotivoRechazoPareja.DuracionDistinta,
                         Evaluar(Sesion(duracion: 2m), Sesion(duracion: 3m)));
        }

        [Fact]
        public void SinFranjaComun_SeRechaza()
        {
            // Bioquímica solo puede martes temprano, Química Orgánica solo martes tarde.
            Assert.Equal(MotivoRechazoPareja.SinFranjaComun,
                         Evaluar(Sesion(), Sesion(), dominioA: new[] { 0, 1 }, dominioB: new[] { 8, 9 }));
        }

        [Fact]
        public void ConFranjaParcialmenteComun_SeAcepta()
        {
            // El caso del usuario: martes 8-14 contra martes 10-16 se resuelve en 10-12.
            Assert.Equal(MotivoRechazoPareja.Ninguno,
                         Evaluar(Sesion(), Sesion(), dominioA: new[] { 1, 2, 3 }, dominioB: new[] { 3, 4, 5 }));
        }

        [Fact]
        public void SinAulaComun_SeRechaza()
        {
            // Un laboratorio y una teoría presencial tienen conjuntos de aulas disjuntos: sin esta
            // compuerta la pareja se aceptaba y CP-SAT devolvía infactible con motivo Otro.
            Assert.Equal(MotivoRechazoPareja.SinAulaComun,
                         Evaluar(Sesion(), Sesion(), aulasA: new[] { 0, 1 }, aulasB: new[] { 2, 3 }));
        }

        [Fact]
        public void UnaConRequisitoDeEspacioYLaOtraSin_SeRechaza()
        {
            var grupoConReq = new Grupo(Guid.NewGuid(), "G1", Guid.NewGuid(), 30);
            grupoConReq.ActualizarRequisitosEspacio(new List<RequisitoEspacio>
            {
                new(TipoSesion.TeoriaPresencial, Guid.NewGuid(), null, 1)
            });
            var grupoSinReq = new Grupo(Guid.NewGuid(), "G2", Guid.NewGuid(), 30);

            var a = Sesion(grupoId: grupoConReq.Id);
            var b = Sesion(grupoId: grupoSinReq.Id);

            Assert.Equal(MotivoRechazoPareja.RequisitoIncompatible, Evaluar(a, b,
                new Dictionary<Guid, Grupo> { [grupoConReq.Id] = grupoConReq, [grupoSinReq.Id] = grupoSinReq }));
        }

        [Fact]
        public void AmbasConElMismoRequisitoDeEspacio_SeAceptan()
        {
            var espacio = Guid.NewGuid();
            var g1 = new Grupo(Guid.NewGuid(), "G1", Guid.NewGuid(), 30);
            var g2 = new Grupo(Guid.NewGuid(), "G2", Guid.NewGuid(), 30);
            foreach (var g in new[] { g1, g2 })
                g.ActualizarRequisitosEspacio(new List<RequisitoEspacio>
                {
                    new(TipoSesion.Laboratorio, espacio, TipoEspacio.Laboratorio, 1)
                });

            var a = Sesion(grupoId: g1.Id, flujo: TipoFlujo.Laboratorio);
            var b = Sesion(grupoId: g2.Id, flujo: TipoFlujo.Laboratorio);

            Assert.Equal(MotivoRechazoPareja.Ninguno, Evaluar(a, b,
                new Dictionary<Guid, Grupo> { [g1.Id] = g1, [g2.Id] = g2 }));
        }

        [Fact]
        public void RequisitosConTipoDeAulaDistinto_SeRechazan()
        {
            var (a, b, grupos) = ConRequisitos(
                new RequisitoEspacio(TipoSesion.Laboratorio, null, TipoEspacio.Laboratorio, 1),
                new RequisitoEspacio(TipoSesion.Laboratorio, null, TipoEspacio.Salon, 1));

            Assert.Equal(MotivoRechazoPareja.RequisitoIncompatible, Evaluar(a, b, grupos));
        }

        [Fact]
        public void RequisitosConEspacioFijoDistinto_SeRechazan()
        {
            var (a, b, grupos) = ConRequisitos(
                new RequisitoEspacio(TipoSesion.Laboratorio, Guid.NewGuid(), TipoEspacio.Laboratorio, 1),
                new RequisitoEspacio(TipoSesion.Laboratorio, Guid.NewGuid(), TipoEspacio.Laboratorio, 1));

            Assert.Equal(MotivoRechazoPareja.RequisitoIncompatible, Evaluar(a, b, grupos));
        }

        private static (Sesion a, Sesion b, Dictionary<Guid, Grupo> grupos) ConRequisitos(
            RequisitoEspacio reqA, RequisitoEspacio reqB)
        {
            var g1 = new Grupo(Guid.NewGuid(), "G1", Guid.NewGuid(), 30);
            var g2 = new Grupo(Guid.NewGuid(), "G2", Guid.NewGuid(), 30);
            g1.ActualizarRequisitosEspacio(new List<RequisitoEspacio> { reqA });
            g2.ActualizarRequisitosEspacio(new List<RequisitoEspacio> { reqB });
            return (Sesion(grupoId: g1.Id, flujo: TipoFlujo.Laboratorio),
                    Sesion(grupoId: g2.Id, flujo: TipoFlujo.Laboratorio),
                    new Dictionary<Guid, Grupo> { [g1.Id] = g1, [g2.Id] = g2 });
        }

        [Fact]
        public void FranjasComunes_DevuelveLaInterseccionOrdenadaYSinRepetidos()
        {
            Assert.Equal(new[] { 3, 5 },
                         EvaluadorParejaAlternancia.FranjasComunes(new[] { 5, 3, 3, 1 }, new[] { 3, 5, 9 }));
        }
    }
}
