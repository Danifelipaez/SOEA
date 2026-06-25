using System;
using System.Collections.Generic;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Engine.GraphColoring;

namespace SOEA.Tests.Engine.GraphColoring
{
    public class ConstructorGrafoConflictosTests
    {
        private static readonly ConstructorGrafoConflictos Constructor = new();

        // CR-08: el eje de conflicto es la cohorte (GrupoId), no el docente (fuera del pipeline).
        private static Sesion CrearSesion(Guid grupoId, Guid? espacioId, TipoAlternancia alt) =>
            new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), espacioId, grupoId,
                alt, Modalidad.Presencial, 2m, false, false);

        // ALT-01: TipoA y TipoB de DISTINTAS cohortes compartiendo aula no entran en conflicto.
        [Fact]
        public void TipoA_y_TipoB_DistintaCohorte_MismoEspacio_NoTienenConflicto()
        {
            var espacioId = Guid.NewGuid();
            var cohorte1 = Guid.NewGuid();
            var cohorte2 = Guid.NewGuid();

            var sesionA = CrearSesion(cohorte1, espacioId, TipoAlternancia.TipoA);
            var sesionB = CrearSesion(cohorte2, espacioId, TipoAlternancia.TipoB);

            var grafo = Constructor.Construir(new List<Sesion> { sesionA, sesionB });

            Assert.DoesNotContain(sesionB.Id, grafo[sesionA.Id]);
            Assert.DoesNotContain(sesionA.Id, grafo[sesionB.Id]);
        }

        // SinAlternancia de distintas cohortes compartiendo aula sí entran en conflicto (espacio).
        [Fact]
        public void SinAlternancia_DistintaCohorte_MismoEspacio_TienenConflicto()
        {
            var espacioId = Guid.NewGuid();
            var sesion1 = CrearSesion(Guid.NewGuid(), espacioId, TipoAlternancia.SinAlternancia);
            var sesion2 = CrearSesion(Guid.NewGuid(), espacioId, TipoAlternancia.SinAlternancia);

            var grafo = Constructor.Construir(new List<Sesion> { sesion1, sesion2 });

            Assert.Contains(sesion2.Id, grafo[sesion1.Id]);
            Assert.Contains(sesion1.Id, grafo[sesion2.Id]);
        }

        // CR-08: la misma cohorte siempre entra en conflicto, sin importar el aula
        // (un grupo no puede estar en dos sesiones a la vez).
        [Fact]
        public void MismaCohorte_SinImportarEspacio_TienenConflicto()
        {
            var cohorteId = Guid.NewGuid();
            var sesion1 = CrearSesion(cohorteId, Guid.NewGuid(), TipoAlternancia.SinAlternancia);
            var sesion2 = CrearSesion(cohorteId, Guid.NewGuid(), TipoAlternancia.SinAlternancia);

            var grafo = Constructor.Construir(new List<Sesion> { sesion1, sesion2 });

            Assert.Contains(sesion2.Id, grafo[sesion1.Id]);
        }
    }
}
