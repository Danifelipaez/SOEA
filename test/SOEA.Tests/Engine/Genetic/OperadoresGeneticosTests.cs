using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Services;
using SOEA.Engine.Genetic;

namespace SOEA.Tests.Engine.Genetic
{
    public class OperadoresGeneticosTests
    {
        private static Sesion CrearSesion(Guid docenteId, decimal duracion = 2m, Modalidad modalidad = Modalidad.Presencial) =>
            new(Guid.NewGuid(), Guid.NewGuid(), docenteId, Guid.NewGuid(), null, null,
                TipoAlternancia.SinAlternancia, modalidad, duracion, false, false);

        private static List<BloqueTiempo> CrearGrilla(int bloquesPorDia)
        {
            var bloques = new List<BloqueTiempo>();
            foreach (var dia in new[] { DiaDeSemana.Lunes, DiaDeSemana.Martes })
            {
                for (int h = 0; h < bloquesPorDia; h++)
                    bloques.Add(new BloqueTiempo(Guid.NewGuid(), dia, new TimeOnly(7 + h, 0), new TimeOnly(8 + h, 0)));
            }
            return bloques;
        }

        // HC-I01: Reparar debe eliminar solapamiento de SPANS (no sólo igualdad de bloque) del mismo docente
        [Fact]
        public void Reparar_ConflictoDocente_EliminaSolapamientoDeSpans()
        {
            var docenteId = Guid.NewGuid();
            var sesiones = new List<Sesion>
            {
                CrearSesion(docenteId, 2m),
                CrearSesion(docenteId, 2m)
            };
            var bloques = CrearGrilla(bloquesPorDia: 10);

            // Ambas en bloque 0 con duración 2 — solapan en bloques 0 y 1
            var cromosoma = new CromosomaHorario(
                sesiones.Select(s => s.Id).ToArray(),
                new[] { 0, 0 },
                new[] { 0, 1 });

            var operadores = new OperadoresGeneticos(sesiones, bloques, maxEspacios: 3, rng: new Random(42));
            operadores.Reparar(cromosoma);

            var diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);
            var solapa = BloquesPlanner.Solapan(
                cromosoma.BloqueIndices[0], 2,
                cromosoma.BloqueIndices[1], 2,
                diaPorIdx);

            Assert.False(solapa, "El conflicto de docente no se reparó: los spans siguen solapando.");
        }

        // HC-S01: Reparar debe eliminar solapamiento de SPANS en el mismo espacio
        [Fact]
        public void Reparar_ConflictoEspacio_EliminaSolapamientoDeSpans()
        {
            var sesiones = new List<Sesion>
            {
                CrearSesion(Guid.NewGuid(), 2m, Modalidad.Presencial),
                CrearSesion(Guid.NewGuid(), 2m, Modalidad.Presencial)
            };
            var bloques = CrearGrilla(bloquesPorDia: 10);

            // Mismo espacio 0, mismo bloque 0 — solapan
            var cromosoma = new CromosomaHorario(
                sesiones.Select(s => s.Id).ToArray(),
                new[] { 0, 0 },
                new[] { 0, 0 });

            var operadores = new OperadoresGeneticos(sesiones, bloques, maxEspacios: 5, rng: new Random(42));
            operadores.Reparar(cromosoma);

            var diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);
            var mismoEspacio = cromosoma.EspacioIndices[0] == cromosoma.EspacioIndices[1];
            var solapanEnTiempo = BloquesPlanner.Solapan(
                cromosoma.BloqueIndices[0], 2,
                cromosoma.BloqueIndices[1], 2,
                diaPorIdx);

            Assert.False(mismoEspacio && solapanEnTiempo,
                "Conflicto de espacio sin reparar: mismo espacio y spans solapan.");
        }

        // Sesiones virtuales no participan de la reparación de espacio
        [Fact]
        public void Reparar_SesionVirtual_NoAfectaEspacio()
        {
            var sesiones = new List<Sesion>
            {
                CrearSesion(Guid.NewGuid(), 2m, Modalidad.Virtual),
                CrearSesion(Guid.NewGuid(), 2m, Modalidad.Virtual)
            };
            var bloques = CrearGrilla(bloquesPorDia: 10);

            var cromosoma = new CromosomaHorario(
                sesiones.Select(s => s.Id).ToArray(),
                new[] { 0, 0 },
                new[] { 0, 0 });

            var operadores = new OperadoresGeneticos(sesiones, bloques, maxEspacios: 5, rng: new Random(42));
            operadores.Reparar(cromosoma);
            // No debe lanzar; las virtuales se exceptúan de HC-S01.
        }

        // Mutar respeta "no cruzar día" para la duración de la sesión
        [Fact]
        public void Mutar_NoGeneraStartQueCruceDia()
        {
            var sesion = CrearSesion(Guid.NewGuid(), 2m);
            var sesiones = new List<Sesion> { sesion };
            var bloques = CrearGrilla(bloquesPorDia: 5); // Lunes 7-12, Martes 7-12 → idx 0-4 lunes, 5-9 martes
            var diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);
            var rangos = BloquesPlanner.RangosPorDia(bloques);

            var cromosoma = new CromosomaHorario(new[] { sesion.Id }, new[] { 0 }, new[] { 0 });
            var operadores = new OperadoresGeneticos(sesiones, bloques, maxEspacios: 2, rng: new Random(42));

            // Mutación forzada (probabilidad 1.0) muchas veces; ningún start resultante puede cruzar día.
            for (int i = 0; i < 200; i++)
            {
                operadores.Mutar(cromosoma, probabilidadPorGen: 1.0);
                Assert.True(
                    BloquesPlanner.CabeEnDia(cromosoma.BloqueIndices[0], 2, rangos, diaPorIdx),
                    $"Mutación generó start {cromosoma.BloqueIndices[0]} que cruza día con duración 2.");
            }
        }
    }
}
