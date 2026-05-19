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

        private static Sesion CrearSesion(Guid docenteId, Guid? espacioId, TipoAlternancia alt) =>
            new(Guid.NewGuid(), Guid.NewGuid(), docenteId, Guid.NewGuid(), espacioId, null,
                alt, Modalidad.Presencial, 2m, false, false);

        // ALT-01: TipoA and TipoB sharing the same room must NOT conflict in the graph
        [Fact]
        public void TipoA_y_TipoB_MismoEspacio_NoTienenConflicto()
        {
            var espacioId = Guid.NewGuid();
            var docente1 = Guid.NewGuid();
            var docente2 = Guid.NewGuid();

            var sesionA = CrearSesion(docente1, espacioId, TipoAlternancia.TipoA);
            var sesionB = CrearSesion(docente2, espacioId, TipoAlternancia.TipoB);

            var grafo = Constructor.Construir(new List<Sesion> { sesionA, sesionB });

            Assert.DoesNotContain(sesionB.Id, grafo[sesionA.Id]);
            Assert.DoesNotContain(sesionA.Id, grafo[sesionB.Id]);
        }

        // SinAlternancia sessions sharing the same room must conflict
        [Fact]
        public void SinAlternancia_MismoEspacio_TienenConflicto()
        {
            var espacioId = Guid.NewGuid();
            var sesion1 = CrearSesion(Guid.NewGuid(), espacioId, TipoAlternancia.SinAlternancia);
            var sesion2 = CrearSesion(Guid.NewGuid(), espacioId, TipoAlternancia.SinAlternancia);

            var grafo = Constructor.Construir(new List<Sesion> { sesion1, sesion2 });

            Assert.Contains(sesion2.Id, grafo[sesion1.Id]);
            Assert.Contains(sesion1.Id, grafo[sesion2.Id]);
        }

        // Same docente always conflicts regardless of room
        [Fact]
        public void MismoDocente_SinImportarEspacio_TienenConflicto()
        {
            var docenteId = Guid.NewGuid();
            var sesion1 = CrearSesion(docenteId, Guid.NewGuid(), TipoAlternancia.SinAlternancia);
            var sesion2 = CrearSesion(docenteId, Guid.NewGuid(), TipoAlternancia.SinAlternancia);

            var grafo = Constructor.Construir(new List<Sesion> { sesion1, sesion2 });

            Assert.Contains(sesion2.Id, grafo[sesion1.Id]);
        }
    }
}
