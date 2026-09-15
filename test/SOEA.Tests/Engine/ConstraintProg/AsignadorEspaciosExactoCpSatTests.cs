using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.ValueObjects;
using SOEA.Engine.ConstraintProg;
using Xunit;

namespace SOEA.Tests.Engine.ConstraintProg
{
    /// <summary>
    /// Fase 3 (M2 del análisis de espacios): reemplaza el coloreo greedy anterior
    /// (AsignadorEspacios, eliminado) por una asignación exacta vía CP-SAT. Estos tests migran la
    /// cobertura original de Fase 1 y flipean el contraejemplo de M2 — antes documentaba el
    /// sub-óptimo del greedy (Null), ahora prueba que la asignación exacta SÍ lo resuelve.
    /// </summary>
    public class AsignadorEspaciosExactoCpSatTests
    {
        private static readonly AsignadorEspaciosExactoCpSat Asignador =
            new(NullLogger<AsignadorEspaciosExactoCpSat>.Instance);

        private static Sesion CrearSesionPresencial(Guid grupoId) =>
            new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m, false, false,
                tipoFlujo: TipoFlujo.AulaVirtual); // TeoriaPresencial (tipoFlujo != Laboratorio)

        private static readonly DiaDeSemana[] CuatroBloquesUnDia =
            { DiaDeSemana.Lunes, DiaDeSemana.Lunes, DiaDeSemana.Lunes, DiaDeSemana.Lunes };

        [Fact]
        public void DosSesionesNoSolapadas_UnEspacio_AsignaAmbas()
        {
            var salon = new Espacio(Guid.NewGuid(), "Salón", TipoEspacio.Salon, 30);
            var s1 = CrearSesionPresencial(Guid.NewGuid());
            var s2 = CrearSesionPresencial(Guid.NewGuid());
            var starts = new[] { 0, 1 }; // bloques 0 y 1: no se solapan (duración 1h cada una)

            var resultado = Asignador.Asignar(
                new List<Sesion> { s1, s2 }, starts, new[] { 1, 1 },
                new[] { salon }, CuatroBloquesUnDia);

            Assert.NotNull(resultado);
            Assert.Equal(salon.Id, resultado![s1.Id]);
            Assert.Equal(salon.Id, resultado![s2.Id]);
        }

        [Fact]
        public void DosSesionesSolapadas_UnSoloEspacio_DevuelveNull()
        {
            var salon = new Espacio(Guid.NewGuid(), "Salón", TipoEspacio.Salon, 30);
            var s1 = CrearSesionPresencial(Guid.NewGuid());
            var s2 = CrearSesionPresencial(Guid.NewGuid());
            var starts = new[] { 0, 0 }; // mismo bloque: solapan, sólo hay un espacio

            var resultado = Asignador.Asignar(
                new List<Sesion> { s1, s2 }, starts, new[] { 1, 1 },
                new[] { salon }, CuatroBloquesUnDia);

            Assert.Null(resultado);
        }

        [Fact]
        public void RespetaRequisitoDeGrupo_EspacioFijo()
        {
            var fijo = new Espacio(Guid.NewGuid(), "Fijo", TipoEspacio.Salon, 30);
            var otro = new Espacio(Guid.NewGuid(), "Otro", TipoEspacio.Salon, 30);
            var grupoId = Guid.NewGuid();
            var s = CrearSesionPresencial(grupoId);
            var requisitos = new Dictionary<Guid, List<RequisitoEspacio>>
            {
                [grupoId] = new() { new RequisitoEspacio(TipoSesion.TeoriaPresencial, fijo.Id, TipoEspacio.Salon, 1) }
            };

            var resultado = Asignador.Asignar(
                new List<Sesion> { s }, new[] { 0 }, new[] { 1 },
                new[] { otro, fijo }, CuatroBloquesUnDia, requisitosPorGrupo: requisitos);

            Assert.NotNull(resultado);
            Assert.Equal(fijo.Id, resultado![s.Id]);
        }

        [Fact]
        public void AforoInsuficiente_DevuelveNull()
        {
            var pequeno = new Espacio(Guid.NewGuid(), "Chico", TipoEspacio.Salon, 10);
            var grupoId = Guid.NewGuid();
            var s = CrearSesionPresencial(grupoId);
            var estudiantes = new Dictionary<Guid, int> { [grupoId] = 30 };

            var resultado = Asignador.Asignar(
                new List<Sesion> { s }, new[] { 0 }, new[] { 1 },
                new[] { pequeno }, CuatroBloquesUnDia, estudiantesPorGrupo: estudiantes);

            Assert.Null(resultado);
        }

        // ── M2 del análisis (flip): el greedy anterior por hora de inicio no era óptimo cuando
        // las "máquinas" (espacios) no son idénticas — cada sesión trae su propio conjunto de
        // candidatos según su requisito de grupo. Contraejemplo mínimo: S1 admite {A,B} (sin
        // requisito), S2 admite sólo {A} (requisito de espacio fijo), ambas solapadas. El greedy
        // le daba A a S1 (primer libre) y descartaba TODA la asignación aunque (S1→B, S2→A) sí es
        // factible. La asignación exacta SÍ la encuentra.
        [Fact]
        public void ContraejemploM2_AsignacionExacta_EncuentraLaSolucionQueElGreedyPerdia()
        {
            var a = new Espacio(Guid.NewGuid(), "A", TipoEspacio.Salon, 30);
            var b = new Espacio(Guid.NewGuid(), "B", TipoEspacio.Salon, 30);
            var grupoLibre = Guid.NewGuid();  // sin requisito → candidatos {A, B}
            var grupoFijo  = Guid.NewGuid();  // requisito de espacio fijo = A → candidatos {A}

            var s1 = CrearSesionPresencial(grupoLibre); // empieza primero (start 0)
            var s2 = CrearSesionPresencial(grupoFijo);  // empieza después (start 1), solapada con s1

            var requisitos = new Dictionary<Guid, List<RequisitoEspacio>>
            {
                [grupoFijo] = new() { new RequisitoEspacio(TipoSesion.TeoriaPresencial, a.Id, TipoEspacio.Salon, 1) }
            };
            var starts = new[] { 0, 1 };
            var duraciones = new[] { 2, 2 }; // [0,2) y [1,3) se solapan

            var resultado = Asignador.Asignar(
                new List<Sesion> { s1, s2 }, starts, duraciones,
                new[] { a, b }, CuatroBloquesUnDia, requisitosPorGrupo: requisitos);

            Assert.NotNull(resultado);
            Assert.Equal(a.Id, resultado![s2.Id]); // s2 solo admite A
            Assert.Equal(b.Id, resultado[s1.Id]);  // s1 cede a B, libre
        }

        // ── HC-ALT: una pareja de alternancia comparte el MISMO aula; que no colisionen lo
        // garantiza el reparto por semana (una ocupa el aula en A, la otra en B).
        [Fact]
        public void ParejaDeAlternancia_ComparteElMismoEspacio()
        {
            var e1 = new Espacio(Guid.NewGuid(), "E1", TipoEspacio.Salon, 30);
            var e2 = new Espacio(Guid.NewGuid(), "E2", TipoEspacio.Salon, 30);
            var patron = Guid.NewGuid();

            var s1 = new Sesion(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, Guid.NewGuid(),
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m, false, false, tipoFlujo: TipoFlujo.AulaVirtual);
            var s2 = new Sesion(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, Guid.NewGuid(),
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m, false, false, tipoFlujo: TipoFlujo.AulaVirtual);
            s1.AplicarAlternancia(TipoAlternancia.TipoA, Guid.NewGuid(), cedidaPorSaturacion: true, parejaAlternanciaId: patron);
            s2.AplicarAlternancia(TipoAlternancia.TipoB, Guid.NewGuid(), cedidaPorSaturacion: true, parejaAlternanciaId: patron);

            // Mismo bloque para las dos: s1 ocupa el aula en la semana A, s2 en la B.
            var starts = new[] { 0, 0 };

            var resultado = Asignador.Asignar(
                new List<Sesion> { s1, s2 }, starts, new[] { 1, 1 },
                new[] { e1, e2 }, CuatroBloquesUnDia);

            Assert.NotNull(resultado);
            Assert.Equal(resultado![s1.Id], resultado[s2.Id]);
        }

        // Una sesión que NO alterna ocupa su aula en las DOS semanas, así que no puede compartirla
        // con nadie — ni siquiera con un TipoB. Es el caso que el índice único de BD ya no ve.
        [Fact]
        public void NoPareada_BloqueaSuAulaEnAmbasSemanas()
        {
            var unico = new Espacio(Guid.NewGuid(), "Único", TipoEspacio.Salon, 30);

            var fija = CrearSesionPresencial(Guid.NewGuid());
            var tipoB = CrearSesionPresencial(Guid.NewGuid());
            tipoB.AplicarAlternancia(TipoAlternancia.TipoB, Guid.NewGuid());

            var resultado = Asignador.Asignar(
                new List<Sesion> { fija, tipoB }, new[] { 0, 0 }, new[] { 1, 1 },
                new[] { unico }, CuatroBloquesUnDia);

            Assert.Null(resultado);
        }
    }
}
