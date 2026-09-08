using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.ValueObjects;
using SOEA.Engine.ConstraintProg;

namespace SOEA.Tests.Engine.ConstraintProg
{
    /// <summary>
    /// `EjecutarSweepDiagnostico` (MotorConstraintProgramming.cs:647-688) envuelve el loop por
    /// grupo candidato en `catch (Exception ex) when (ex is not OperationCanceledException)` —
    /// la intención del filtro es que una cancelación real SIEMPRE se propague hacia el llamador
    /// (nunca se trague como "diagnóstico omitido"), a diferencia de cualquier otra excepción.
    /// Estos tests confirman que esa intención se cumple en los dos puntos donde puede ocurrir
    /// una cancelación: antes de arrancar el solve principal, y durante el barrido en sí.
    /// </summary>
    public class SweepGruposCancelacionTests
    {
        private static List<BloqueTiempo> CrearBloques(int count) =>
            Enumerable.Range(0, count)
                .Select(i => new BloqueTiempo(
                    Guid.NewGuid(), DiaDeSemana.Lunes,
                    new TimeOnly(7 + i, 0), new TimeOnly(8 + i, 0)))
                .ToList();

        private static Grupo GrupoConEspacioFijo(Guid espacioFijoId, int estudiantes = 20)
        {
            var grupo = new Grupo(Guid.NewGuid(), $"Grupo-{Guid.NewGuid():N}".Substring(0, 12), Guid.NewGuid(), estudiantes);
            grupo.ActualizarRequisitosEspacio(new List<RequisitoEspacio>
            {
                new(TipoSesion.TeoriaPresencial, espacioFijoId, TipoEspacio.Salon, 1)
            });
            return grupo;
        }

        private static Sesion CrearSesionPresencial(Guid grupoId, decimal duracion = 1m) =>
            new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, duracion, false, false,
                tipoFlujo: TipoFlujo.AulaVirtual);

        [Fact]
        public async Task CancelacionAntesDeArrancar_PropagaOperationCanceled()
        {
            var bloques = CrearBloques(1);
            var espacioFijo = new Espacio(Guid.NewGuid(), "Salón fijo", TipoEspacio.Salon, 30);
            var grupoA = GrupoConEspacioFijo(espacioFijo.Id);
            var sesionA = CrearSesionPresencial(grupoA.Id, 1m);

            var motor = new MotorConstraintProgramming(
                NullLogger<MotorConstraintProgramming>.Instance,
                new CpSatOptions { SweepGrupos = true });

            var ctCancelado = new CancellationToken(canceled: true);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                motor.ResolverFactibilidadAsync(
                    new[] { sesionA }, bloques, new[] { espacioFijo }, new[] { grupoA },
                    ct: ctCancelado));
        }

        [Fact]
        public void CancelacionDuranteElBarrido_PropagaOperationCanceled_NoSeTraga()
        {
            // EjecutarSweepDiagnostico es privado: se invoca por reflexión con un token YA
            // cancelado para forzar determinísticamente el punto exacto que se quiere probar
            // (la cancelación ocurre dentro del loop de re-solves del barrido, no en el solve
            // principal) — un CancellationTokenSource con temporizador real sería sensible a
            // timing entre máquinas/CI. Se sacrifica "caja negra" por un resultado reproducible.
            var bloques = CrearBloques(1);
            var espacioFijo = new Espacio(Guid.NewGuid(), "Salón fijo", TipoEspacio.Salon, 30);
            var otroSalon = new Espacio(Guid.NewGuid(), "Salón libre", TipoEspacio.Salon, 30);
            var grupoA = GrupoConEspacioFijo(espacioFijo.Id);
            var grupoB = GrupoConEspacioFijo(espacioFijo.Id);
            var sesionA = CrearSesionPresencial(grupoA.Id, 1m);
            var sesionB = CrearSesionPresencial(grupoB.Id, 1m);

            var motor = new MotorConstraintProgramming(
                NullLogger<MotorConstraintProgramming>.Instance,
                new CpSatOptions { SweepGrupos = true });

            var metodo = typeof(MotorConstraintProgramming).GetMethod(
                "EjecutarSweepDiagnostico", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(metodo);

            var args = new object[]
            {
                new List<Sesion> { sesionA, sesionB },
                bloques,
                new List<Espacio> { espacioFijo, otroSalon },
                new List<Grupo> { grupoA, grupoB },
                new HashSet<Guid>(),
                new Dictionary<Guid, (TimeOnly? min, TimeOnly? max)>(),
                new CancellationToken(canceled: true),
            };

            var ex = Assert.Throws<TargetInvocationException>(() => metodo!.Invoke(motor, args));
            Assert.IsAssignableFrom<OperationCanceledException>(ex.InnerException);
        }
    }
}
