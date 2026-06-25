using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Application.Features.Horario;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using Xunit;

namespace SOEA.Tests.Application
{
    /// <summary>
    /// Verifica el validador post-generación de restricciones duras (P0.3 auditoría):
    /// detecta solapes reales de cohorte (HC-C01) y de espacio (HC-S01) sobre las
    /// asignaciones finales, en lugar de confiar en un conteo hardcodeado.
    /// </summary>
    public class ValidadorRestriccionesDurasTests
    {
        // Grilla mínima de 5 bloques contiguos (un día).
        private static (List<BloqueTiempo> bloques, Dictionary<Guid, int> indice) CrearGrilla(int n)
        {
            var bloques = Enumerable.Range(0, n)
                .Select(i => new BloqueTiempo(Guid.NewGuid(), DiaDeSemana.Lunes,
                    new TimeOnly(7 + i, 0), new TimeOnly(8 + i, 0)))
                .ToList();
            var indice = new Dictionary<Guid, int>();
            for (int i = 0; i < bloques.Count; i++) indice[bloques[i].Id] = i;
            return (bloques, indice);
        }

        // CR-08: el eje de no-solapamiento es la cohorte (GrupoId); el docente queda fuera.
        private static Sesion CrearSesion(Guid grupoId, decimal duracion) =>
            new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, duracion, false, false);

        [Fact]
        public void SinSolapes_DevuelveListaVacia()
        {
            var (bloques, indice) = CrearGrilla(5);
            var cohorte = Guid.NewGuid();
            var s1 = CrearSesion(cohorte, 1m);
            var s2 = CrearSesion(cohorte, 1m);
            var sesiones = new Dictionary<Guid, Sesion> { [s1.Id] = s1, [s2.Id] = s2 };

            // s1 en bloque 0, s2 en bloque 2 → no solapan
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s1.Id, SemanaAcademica.A, bloques[0].Id, null, Modalidad.Virtual),
                new AsignacionSemanal(Guid.NewGuid(), s2.Id, SemanaAcademica.A, bloques[2].Id, null, Modalidad.Virtual),
            };

            var conflictos = ValidadorRestriccionesDuras.Validar(asignaciones, sesiones, indice);

            Assert.Empty(conflictos);
        }

        [Fact]
        public void MismaCohorte_BloquesSolapados_DetectaHCC01()
        {
            var (bloques, indice) = CrearGrilla(5);
            var cohorte = Guid.NewGuid();
            var s1 = CrearSesion(cohorte, 2m); // ocupa bloques 0-1
            var s2 = CrearSesion(cohorte, 1m); // empieza en bloque 1 → solapa con s1
            var sesiones = new Dictionary<Guid, Sesion> { [s1.Id] = s1, [s2.Id] = s2 };

            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s1.Id, SemanaAcademica.A, bloques[0].Id, null, Modalidad.Virtual),
                new AsignacionSemanal(Guid.NewGuid(), s2.Id, SemanaAcademica.A, bloques[1].Id, null, Modalidad.Virtual),
            };

            var conflictos = ValidadorRestriccionesDuras.Validar(asignaciones, sesiones, indice);

            Assert.NotEmpty(conflictos);
            Assert.Contains(conflictos, c => c.StartsWith("HC-C01"));
        }

        [Fact]
        public void MismoEspacio_Presencial_BloquesSolapados_DetectaHCS01()
        {
            var (bloques, indice) = CrearGrilla(5);
            var espacio = Guid.NewGuid();
            var s1 = CrearSesion(Guid.NewGuid(), 1m);
            var s2 = CrearSesion(Guid.NewGuid(), 1m);
            var sesiones = new Dictionary<Guid, Sesion> { [s1.Id] = s1, [s2.Id] = s2 };

            // Dos sesiones presenciales en el mismo espacio, mismo bloque, misma semana.
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s1.Id, SemanaAcademica.A, bloques[0].Id, espacio, Modalidad.Presencial),
                new AsignacionSemanal(Guid.NewGuid(), s2.Id, SemanaAcademica.A, bloques[0].Id, espacio, Modalidad.Presencial),
            };

            var conflictos = ValidadorRestriccionesDuras.Validar(asignaciones, sesiones, indice);

            Assert.Contains(conflictos, c => c.StartsWith("HC-S01"));
        }

        [Fact]
        public void MismoEspacio_DistintaSemana_NoEsConflicto()
        {
            var (bloques, indice) = CrearGrilla(5);
            var espacio = Guid.NewGuid();
            var s1 = CrearSesion(Guid.NewGuid(), 1m);
            var s2 = CrearSesion(Guid.NewGuid(), 1m);
            var sesiones = new Dictionary<Guid, Sesion> { [s1.Id] = s1, [s2.Id] = s2 };

            // Mismo espacio y bloque pero semanas distintas → el modelo bi-semanal lo permite.
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s1.Id, SemanaAcademica.A, bloques[0].Id, espacio, Modalidad.Presencial),
                new AsignacionSemanal(Guid.NewGuid(), s2.Id, SemanaAcademica.B, bloques[0].Id, espacio, Modalidad.Presencial),
            };

            var conflictos = ValidadorRestriccionesDuras.Validar(asignaciones, sesiones, indice);

            Assert.Empty(conflictos);
        }
    }
}
