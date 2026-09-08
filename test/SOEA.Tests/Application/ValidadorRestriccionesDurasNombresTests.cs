using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SOEA.Application.Features.Horario;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.ValueObjects;
using Xunit;

namespace SOEA.Tests.Application
{
    /// <summary>
    /// G2/G7 (bug reportado "alertas muestran id de sesiones, debe ser nombre" / "nombres de
    /// grupos que se cruzan, está saliendo ID en vez de nombres"): ValidadorRestriccionesDuras
    /// interpolaba GUIDs directamente en ~20 mensajes de conflicto, que el frontend pinta tal
    /// cual en el panel de logs. Este archivo fija el criterio mecánico: ningún mensaje que
    /// llega al usuario contiene un GUID, con y sin el catálogo de nombres disponible.
    /// </summary>
    public class ValidadorRestriccionesDurasNombresTests
    {
        private static readonly Regex Guid_ = new(
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            RegexOptions.Compiled);

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

        private static Sesion CrearSesion(Guid asignaturaId, Guid grupoId, decimal duracion) =>
            new(Guid.NewGuid(), asignaturaId, null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, duracion, false, false);

        [Fact]
        public void SinCatalogoDeNombres_MensajeDegradaATextoLegible_NuncaAUnGuid()
        {
            var (bloques, indice) = CrearGrilla(5);
            var asigId = Guid.NewGuid();
            var grupoId = Guid.NewGuid();
            var s1 = CrearSesion(asigId, grupoId, 2m); // ocupa bloques 0-1
            var s2 = CrearSesion(asigId, grupoId, 1m); // empieza en bloque 1 → solapa
            var sesiones = new Dictionary<Guid, Sesion> { [s1.Id] = s1, [s2.Id] = s2 };
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s1.Id, SemanaAcademica.A, bloques[0].Id, null, Modalidad.Virtual),
                new AsignacionSemanal(Guid.NewGuid(), s2.Id, SemanaAcademica.A, bloques[1].Id, null, Modalidad.Virtual),
            };

            // Sin contexto: no hay de dónde sacar nombres. El bug era mostrar el GUID crudo en
            // ese caso — el arreglo es degradar a texto legible, no reventar ni exponer el Id.
            var conflictos = ValidadorRestriccionesDuras.Validar(asignaciones, sesiones, indice);

            Assert.NotEmpty(conflictos);
            Assert.All(conflictos, c => Assert.False(Guid_.IsMatch(c), $"mensaje con GUID crudo: {c}"));
            Assert.Contains(conflictos, c => c.Contains("sin nombre"));
        }

        [Fact]
        public void ConCatalogoDeNombres_MensajeUsaElNombreReal()
        {
            var (bloques, indice) = CrearGrilla(5);
            var asigId = Guid.NewGuid();
            var grupoId = Guid.NewGuid();
            var s1 = CrearSesion(asigId, grupoId, 2m);
            var s2 = CrearSesion(asigId, grupoId, 1m);
            var sesiones = new Dictionary<Guid, Sesion> { [s1.Id] = s1, [s2.Id] = s2 };
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s1.Id, SemanaAcademica.A, bloques[0].Id, null, Modalidad.Virtual),
                new AsignacionSemanal(Guid.NewGuid(), s2.Id, SemanaAcademica.A, bloques[1].Id, null, Modalidad.Virtual),
            };
            var ctx = new ContextoValidacion(
                bloques,
                new Dictionary<Guid, (TimeOnly?, TimeOnly?)>(),
                new Dictionary<Guid, DisponibilidadSemanal>(),
                new Dictionary<Guid, int>(),
                new Dictionary<Guid, Espacio>(),
                NombrePorAsignatura: new Dictionary<Guid, string> { [asigId] = "Cálculo I" },
                NombrePorGrupo: new Dictionary<Guid, string> { [grupoId] = "G1" });

            var conflictos = ValidadorRestriccionesDuras.Validar(asignaciones, sesiones, indice, ctx);

            Assert.Contains(conflictos, c => c.Contains("Cálculo I") && c.Contains("G1"));
            Assert.All(conflictos, c => Assert.False(Guid_.IsMatch(c), $"mensaje con GUID crudo: {c}"));
        }

        [Fact]
        public void ConCatalogoDeNombres_MensajeIdentificaSesion1YSesion2ConDiaYHora()
        {
            var (bloques, indice) = CrearGrilla(5); // bloques desde 07:00, lunes
            var asigId = Guid.NewGuid();
            var grupoId = Guid.NewGuid();
            // Misma asignatura y grupo en ambas sesiones: Describir() por sí solo las deja
            // indistinguibles — el mensaje debe diferenciarlas por horario, no solo por nombre.
            var s1 = CrearSesion(asigId, grupoId, 2m); // bloque 0 → 07:00–09:00
            var s2 = CrearSesion(asigId, grupoId, 1m); // bloque 1 → 08:00–09:00, solapa con s1
            var sesiones = new Dictionary<Guid, Sesion> { [s1.Id] = s1, [s2.Id] = s2 };
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s1.Id, SemanaAcademica.A, bloques[0].Id, null, Modalidad.Virtual),
                new AsignacionSemanal(Guid.NewGuid(), s2.Id, SemanaAcademica.A, bloques[1].Id, null, Modalidad.Virtual),
            };
            var ctx = new ContextoValidacion(
                bloques,
                new Dictionary<Guid, (TimeOnly?, TimeOnly?)>(),
                new Dictionary<Guid, DisponibilidadSemanal>(),
                new Dictionary<Guid, int>(),
                new Dictionary<Guid, Espacio>(),
                NombrePorAsignatura: new Dictionary<Guid, string> { [asigId] = "Cálculo I" },
                NombrePorGrupo: new Dictionary<Guid, string> { [grupoId] = "G1" });

            var conflictos = ValidadorRestriccionesDuras.Validar(asignaciones, sesiones, indice, ctx);

            var msg = Assert.Single(conflictos, c => c.StartsWith("HC-C01"));
            Assert.Contains("Sesión 1", msg);
            Assert.Contains("Sesión 2", msg);
            Assert.Contains("lunes", msg, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("07:00", msg);
            Assert.Contains("08:00", msg);
            Assert.Contains("09:00", msg);
            // Sugerencia adaptada al tipo de error (HC-C01: mover una de las dos sesiones).
            Assert.Contains("Mueva una de las dos sesiones", msg);
        }

        [Fact]
        public void VariasReglasALaVez_NingunMensajeExponeUnGuid()
        {
            // Combina HC-C01 (solape de cohorte), HC-VH (fuera de ventana) y HC-CAP (aforo
            // insuficiente) en una sola corrida — el criterio mecánico del bug: ni una sola de
            // las ~20 interpolaciones originales debía sobrevivir.
            var (bloques, indice) = CrearGrilla(5); // bloques desde 07:00
            var asigId = Guid.NewGuid();
            var grupoId = Guid.NewGuid();
            var espacioId = Guid.NewGuid();

            var s1 = new Sesion(Guid.NewGuid(), asigId, null, Guid.NewGuid(), espacioId, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, 2m, false, false);
            var s2 = new Sesion(Guid.NewGuid(), asigId, null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Virtual, 1m, false, false);
            var sesiones = new Dictionary<Guid, Sesion> { [s1.Id] = s1, [s2.Id] = s2 };

            var asignaciones = new[]
            {
                // s1: presencial, bloque 0 (07:00), fuera de la ventana [09:00, 12:00] → HC-VH.
                // Aforo del grupo (40) supera la capacidad del espacio (20) → HC-CAP.
                new AsignacionSemanal(Guid.NewGuid(), s1.Id, SemanaAcademica.A, bloques[0].Id, espacioId, Modalidad.Presencial),
                // s2: mismo grupo/semana, empieza en bloque 1 → solapa con s1 (HC-C01).
                new AsignacionSemanal(Guid.NewGuid(), s2.Id, SemanaAcademica.A, bloques[1].Id, null, Modalidad.Virtual),
            };

            var espacio = new Espacio(espacioId, "Salón desconocido", SOEA.Domain.Enums.TipoEspacio.Salon, 20);
            var ctx = new ContextoValidacion(
                bloques,
                new Dictionary<Guid, (TimeOnly?, TimeOnly?)> { [asigId] = (new TimeOnly(9, 0), new TimeOnly(12, 0)) },
                new Dictionary<Guid, DisponibilidadSemanal>(),
                new Dictionary<Guid, int> { [grupoId] = 40 },
                new Dictionary<Guid, Espacio> { [espacioId] = espacio },
                NombrePorAsignatura: new Dictionary<Guid, string> { [asigId] = "Bioquímica" },
                NombrePorGrupo: new Dictionary<Guid, string> { [grupoId] = "G2" });

            var conflictos = ValidadorRestriccionesDuras.Validar(asignaciones, sesiones, indice, ctx);

            Assert.NotEmpty(conflictos);
            Assert.Contains(conflictos, c => c.StartsWith("HC-C01"));
            Assert.Contains(conflictos, c => c.StartsWith("HC-VH"));
            Assert.Contains(conflictos, c => c.StartsWith("HC-CAP"));
            Assert.All(conflictos, c => Assert.False(Guid_.IsMatch(c), $"mensaje con GUID crudo: {c}"));
        }
    }
}
