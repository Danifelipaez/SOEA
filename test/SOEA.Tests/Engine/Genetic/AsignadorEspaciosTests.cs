using System;
using System.Collections.Generic;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.ValueObjects;
using SOEA.Engine.Genetic;
using Xunit;

namespace SOEA.Tests.Engine.Genetic
{
    /// <summary>
    /// AsignadorEspacios es internal (InternalsVisibleTo habilitado en SOEA.Engine.Genetic.csproj
    /// para este proyecto): hasta ahora sólo se probaba indirectamente vía MotorGeneticoTests
    /// (M9 del análisis de espacios). Estos tests aíslan su coloreo greedy de la complejidad del GA.
    /// </summary>
    public class AsignadorEspaciosTests
    {
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

            var resultado = AsignadorEspacios.Asignar(
                new List<Sesion> { s1, s2 }, starts, starts, new[] { 1, 1 },
                new[] { salon }, CuatroBloquesUnDia);

            Assert.NotNull(resultado);
            Assert.Equal(salon.Id, resultado![(s1.Id, SemanaAcademica.A)]);
            Assert.Equal(salon.Id, resultado[(s2.Id, SemanaAcademica.A)]);
        }

        [Fact]
        public void DosSesionesSolapadas_UnSoloEspacio_DevuelveNull()
        {
            var salon = new Espacio(Guid.NewGuid(), "Salón", TipoEspacio.Salon, 30);
            var s1 = CrearSesionPresencial(Guid.NewGuid());
            var s2 = CrearSesionPresencial(Guid.NewGuid());
            var starts = new[] { 0, 0 }; // mismo bloque: solapan, sólo hay un espacio

            var resultado = AsignadorEspacios.Asignar(
                new List<Sesion> { s1, s2 }, starts, starts, new[] { 1, 1 },
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

            var resultado = AsignadorEspacios.Asignar(
                new List<Sesion> { s }, new[] { 0 }, new[] { 0 }, new[] { 1 },
                new[] { otro, fijo }, CuatroBloquesUnDia, requisitosPorGrupo: requisitos);

            Assert.NotNull(resultado);
            Assert.Equal(fijo.Id, resultado![(s.Id, SemanaAcademica.A)]);
        }

        [Fact]
        public void AforoInsuficiente_DevuelveNull()
        {
            var pequeno = new Espacio(Guid.NewGuid(), "Chico", TipoEspacio.Salon, 10);
            var grupoId = Guid.NewGuid();
            var s = CrearSesionPresencial(grupoId);
            var estudiantes = new Dictionary<Guid, int> { [grupoId] = 30 };

            var resultado = AsignadorEspacios.Asignar(
                new List<Sesion> { s }, new[] { 0 }, new[] { 0 }, new[] { 1 },
                new[] { pequeno }, CuatroBloquesUnDia, estudiantesPorGrupo: estudiantes);

            Assert.Null(resultado);
        }

        // ── M2 del análisis: el greedy por hora de inicio NO es óptimo cuando las "máquinas"
        // (espacios) no son idénticas — cada sesión tiene su propio conjunto de candidatos según
        // su requisito de grupo. Contraejemplo mínimo: S1 admite {A,B} (sin requisito), S2 admite
        // sólo {A} (requisito de espacio fijo), ambas solapadas y S1 empieza primero. El greedy le
        // da A a S1 (primer libre), deja a S2 sin candidato y descarta TODA la asignación — aunque
        // (S1→B, S2→A) sí es factible. Documenta el comportamiento ACTUAL (sub-óptimo); la Fase 3
        // del plan lo reemplaza por una asignación exacta y este assert deberá invertirse a NotNull.
        [Fact]
        public void ContraejemploM2_GreedySubOptimo_PierdeUnaAsignacionFactible()
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

            var resultado = AsignadorEspacios.Asignar(
                new List<Sesion> { s1, s2 }, starts, starts, duraciones,
                new[] { a, b }, CuatroBloquesUnDia, requisitosPorGrupo: requisitos);

            Assert.Null(resultado); // sub-óptimo: existe (s1→B, s2→A) pero el greedy no la encuentra
        }
    }
}
