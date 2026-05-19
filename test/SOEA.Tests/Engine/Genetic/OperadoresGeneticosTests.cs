using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Engine.Genetic;

namespace SOEA.Tests.Engine.Genetic
{
    public class OperadoresGeneticosTests
    {
        private static Sesion CrearSesion(Guid docenteId, Modalidad modalidad = Modalidad.Presencial) =>
            new(Guid.NewGuid(), Guid.NewGuid(), docenteId, Guid.NewGuid(), null, null,
                TipoAlternancia.SinAlternancia, modalidad, 2m, false, false);

        // HC-I01: Reparar must resolve same-docente same-block conflicts
        [Fact]
        public void Reparar_ConflictoDocente_EliminaSolapamiento()
        {
            var docenteId = Guid.NewGuid();
            var sesiones = new List<Sesion>
            {
                CrearSesion(docenteId),
                CrearSesion(docenteId)
            };

            // Both sessions assigned to block 0 — docente conflict
            var cromosoma = new CromosomaHorario(
                sesiones.Select(s => s.Id).ToArray(),
                new[] { 0, 0 },
                new[] { 0, 1 });

            var operadores = new OperadoresGeneticos(sesiones, maxBloques: 10, maxEspacios: 3, rng: new Random(42));
            operadores.Reparar(cromosoma);

            Assert.NotEqual(cromosoma.BloqueIndices[0], cromosoma.BloqueIndices[1]);
        }

        // HC-S01: Reparar must resolve same-space same-block conflicts for presencial sessions
        [Fact]
        public void Reparar_ConflictoEspacio_EliminaSolapamiento()
        {
            var sesiones = new List<Sesion>
            {
                CrearSesion(Guid.NewGuid(), Modalidad.Presencial),
                CrearSesion(Guid.NewGuid(), Modalidad.Presencial)
            };

            // Both sessions in space 0, block 0 — room conflict
            var cromosoma = new CromosomaHorario(
                sesiones.Select(s => s.Id).ToArray(),
                new[] { 0, 0 },
                new[] { 0, 0 });

            var operadores = new OperadoresGeneticos(sesiones, maxBloques: 10, maxEspacios: 5, rng: new Random(42));
            operadores.Reparar(cromosoma);

            var sameBlockAndSpace =
                cromosoma.BloqueIndices[0] == cromosoma.BloqueIndices[1] &&
                cromosoma.EspacioIndices[0] == cromosoma.EspacioIndices[1];

            Assert.False(sameBlockAndSpace, "Room conflict was not repaired: same block AND same space remain.");
        }

        // Virtual sessions must be skipped in HC-S01 space-conflict repair
        [Fact]
        public void Reparar_SesionVirtual_NoAfectaEspacio()
        {
            var sesiones = new List<Sesion>
            {
                CrearSesion(Guid.NewGuid(), Modalidad.Virtual),
                CrearSesion(Guid.NewGuid(), Modalidad.Virtual)
            };

            var cromosoma = new CromosomaHorario(
                sesiones.Select(s => s.Id).ToArray(),
                new[] { 0, 0 },
                new[] { 0, 0 });

            var operadores = new OperadoresGeneticos(sesiones, maxBloques: 10, maxEspacios: 5, rng: new Random(42));

            // Should not throw; virtual sessions are exempt from space-conflict repair
            operadores.Reparar(cromosoma);
        }
    }
}
