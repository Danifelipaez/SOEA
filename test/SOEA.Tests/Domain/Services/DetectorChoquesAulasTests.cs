using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Services;

namespace SOEA.Tests.Domain.Services
{
    /// <summary>
    /// Choques de aulas que la disponibilidad hace inevitables. Caso de producción: cada grupo del
    /// Excel trae una franja exacta, y tres clases quedaban obligadas a la misma hora con dos aulas;
    /// el generador solo decía "no alcanzan las aulas" sin nombrar la hora ni las clases.
    /// </summary>
    public class DetectorChoquesAulasTests
    {
        private static Sesion Pres(decimal duracion = 2m) =>
            new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, Guid.NewGuid(),
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, duracion, false, false,
                TipoFlujo.AulaVirtual);

        private static IReadOnlyList<ChoqueAulas> Detectar(
            IEnumerable<(Sesion Sesion, int[] Starts, int[] Aulas)> datos)
        {
            var lista = datos.ToList();
            return DetectorChoquesAulas.Detectar(
                lista.Select(d => d.Sesion),
                lista.ToDictionary(d => d.Sesion.Id, d => d.Starts),
                lista.ToDictionary(d => d.Sesion.Id, d => d.Aulas.ToList()));
        }

        [Fact]
        public void TresClasesObligadasALaMismaHora_ConDosAulas_EsChoque()
        {
            // 06–09 (3 h), 08–10 y 08–10: coinciden de 08 a 09, como el miércoles en producción.
            var larga = Pres(3m); var a = Pres(); var b = Pres();
            var choques = Detectar(new[]
            {
                (larga, new[] { 0 }, new[] { 0, 1 }),
                (a,     new[] { 2 }, new[] { 0, 1 }),
                (b,     new[] { 2 }, new[] { 0, 1 })
            });

            var choque = Assert.Single(choques);
            Assert.Equal(2, choque.BloqueInicio);
            Assert.Equal(3, choque.BloqueFin);
            Assert.Equal(2, choque.Aulas);
            Assert.Equal(new[] { larga.Id, a.Id, b.Id }.OrderBy(x => x), choque.SesionIds);
        }

        [Fact]
        public void BloquesSeguidosConLasMismasClases_SonUnaSolaFranja()
        {
            var x = Pres(); var y = Pres(); var z = Pres();
            var choque = Assert.Single(Detectar(new[]
            {
                (x, new[] { 4 }, new[] { 0, 1 }), (y, new[] { 4 }, new[] { 0, 1 }), (z, new[] { 4 }, new[] { 0, 1 })
            }));
            Assert.Equal((4, 6), (choque.BloqueInicio, choque.BloqueFin));
        }

        [Fact]
        public void ConAulasSuficientes_NoHayChoque()
        {
            Assert.Empty(Detectar(new[]
            {
                (Pres(), new[] { 2 }, new[] { 0, 1, 2 }),
                (Pres(), new[] { 2 }, new[] { 0, 1, 2 }),
                (Pres(), new[] { 2 }, new[] { 0, 1, 2 })
            }));
        }

        [Fact]
        public void ClaseQueAunPuedeMoverse_NoCuenta()
        {
            // La tercera puede empezar en 2 o en 6: ninguna hora le es obligatoria.
            Assert.Empty(Detectar(new[]
            {
                (Pres(), new[] { 2 }, new[] { 0, 1 }),
                (Pres(), new[] { 2 }, new[] { 0, 1 }),
                (Pres(), new[] { 2, 6 }, new[] { 0, 1 })
            }));
        }

        [Fact]
        public void DosClasesConLaMismaAulaFija_EsChoqueAunqueHayaOtras()
        {
            var x = Pres(); var y = Pres();
            var choque = Assert.Single(Detectar(new[]
            {
                (x, new[] { 0 }, new[] { 0 }), (y, new[] { 0 }, new[] { 0 }), (Pres(), new[] { 0 }, new[] { 1, 2 })
            }));
            Assert.Equal(1, choque.Aulas);
            Assert.Equal(new[] { x.Id, y.Id }.OrderBy(i => i), choque.SesionIds);
        }

        [Fact]
        public void ParejaDeAlternancia_OcupaUnaSemanaCadaUna_NoEsChoque()
        {
            var a = Pres(); var b = Pres(); var c = Pres();
            var pareja = Guid.NewGuid();
            a.AplicarAlternancia(TipoAlternancia.TipoA, cedidaPorSaturacion: true, parejaAlternanciaId: pareja);
            b.AplicarAlternancia(TipoAlternancia.TipoB, cedidaPorSaturacion: true, parejaAlternanciaId: pareja);

            Assert.Empty(Detectar(new[]
            {
                (a, new[] { 2 }, new[] { 0, 1 }), (b, new[] { 2 }, new[] { 0, 1 }), (c, new[] { 2 }, new[] { 0, 1 })
            }));
        }

        [Fact]
        public void ChoqueEnLasDosSemanas_SeInformaUnaVez()
        {
            Assert.Single(Detectar(new[]
            {
                (Pres(), new[] { 0 }, new[] { 0 }), (Pres(), new[] { 0 }, new[] { 0 })
            }));
        }
    }
}
