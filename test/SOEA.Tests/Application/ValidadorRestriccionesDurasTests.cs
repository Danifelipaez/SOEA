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

        // ── Reglas con ContextoValidacion (asimetría GA↔CP-SAT cerrada) ─────────────

        private static Sesion CrearSesionCompleta(
            Guid asignaturaId, Guid grupoId, decimal duracion,
            TipoFlujo tipoFlujo = TipoFlujo.AulaVirtual, Guid? espacioFijo = null) =>
            new(Guid.NewGuid(), asignaturaId, null, Guid.NewGuid(), espacioFijo, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, duracion, false, false,
                tipoFlujo: tipoFlujo);

        private static ContextoValidacion Contexto(
            List<BloqueTiempo> bloques,
            Dictionary<Guid, (TimeOnly?, TimeOnly?)>? ventanas = null,
            Dictionary<Guid, IReadOnlyList<FranjaHoraria>>? franjas = null,
            Dictionary<Guid, int>? estudiantes = null,
            IEnumerable<Espacio>? espacios = null,
            HashSet<Guid>? fijas = null) =>
            new(bloques,
                ventanas ?? new Dictionary<Guid, (TimeOnly?, TimeOnly?)>(),
                franjas ?? new Dictionary<Guid, IReadOnlyList<FranjaHoraria>>(),
                estudiantes ?? new Dictionary<Guid, int>(),
                (espacios ?? Enumerable.Empty<Espacio>()).ToDictionary(e => e.Id),
                fijas);

        [Fact]
        public void HCVH_SesionFueraDeVentana_Detecta()
        {
            var (bloques, indice) = CrearGrilla(5); // bloques desde 07:00
            var asigId = Guid.NewGuid();
            var s = CrearSesionCompleta(asigId, Guid.NewGuid(), 1m);
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s.Id, SemanaAcademica.A, bloques[0].Id, null, Modalidad.Virtual)
            };
            var ctx = Contexto(bloques, ventanas: new()
            {
                [asigId] = (new TimeOnly(8, 0), new TimeOnly(10, 0)) // 07:00 queda fuera
            });

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignaciones, new Dictionary<Guid, Sesion> { [s.Id] = s }, indice, ctx);

            Assert.Contains(conflictos, c => c.StartsWith("HC-VH"));
        }

        [Fact]
        public void HCVH_SesionFija_EstaExenta()
        {
            var (bloques, indice) = CrearGrilla(5);
            var asigId = Guid.NewGuid();
            var s = CrearSesionCompleta(asigId, Guid.NewGuid(), 1m);
            s.AsignarBloqueTiempo(bloques[0].Id); // como hace MapearSesionesFijas para el horario base
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s.Id, SemanaAcademica.A, bloques[0].Id, null, Modalidad.Virtual)
            };
            var ctx = Contexto(bloques,
                ventanas: new() { [asigId] = (new TimeOnly(8, 0), new TimeOnly(10, 0)) },
                fijas: new HashSet<Guid> { s.Id }); // horario base: CP-SAT tampoco le aplica dominio

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignaciones, new Dictionary<Guid, Sesion> { [s.Id] = s }, indice, ctx);

            Assert.Empty(conflictos);
        }

        [Fact]
        public void HCG01_InicioFueraDeFranja_Detecta()
        {
            var (bloques, indice) = CrearGrilla(5); // 07:00–12:00 → todo Matutino
            var grupoId = Guid.NewGuid();
            var s = CrearSesionCompleta(Guid.NewGuid(), grupoId, 1m);
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s.Id, SemanaAcademica.A, bloques[0].Id, null, Modalidad.Virtual)
            };
            var ctx = Contexto(bloques, franjas: new()
            {
                [grupoId] = new List<FranjaHoraria> { FranjaHoraria.Vespertino } // grupo solo tarde
            });

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignaciones, new Dictionary<Guid, Sesion> { [s.Id] = s }, indice, ctx);

            Assert.Contains(conflictos, c => c.StartsWith("HC-G01"));
        }

        [Fact]
        public void HCCAP_AforoInsuficiente_Detecta()
        {
            var (bloques, indice) = CrearGrilla(5);
            var grupoId = Guid.NewGuid();
            var espacio = new Espacio(Guid.NewGuid(), "Salón chico", TipoEspacio.Salon, 10);
            var s = CrearSesionCompleta(Guid.NewGuid(), grupoId, 1m);
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s.Id, SemanaAcademica.A, bloques[0].Id, espacio.Id, Modalidad.Presencial)
            };
            var ctx = Contexto(bloques,
                estudiantes: new() { [grupoId] = 30 },
                espacios: new[] { espacio });

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignaciones, new Dictionary<Guid, Sesion> { [s.Id] = s }, indice, ctx);

            Assert.Contains(conflictos, c => c.StartsWith("HC-CAP"));
        }

        [Fact]
        public void HCS03_LaboratorioEnSalon_Detecta()
        {
            var (bloques, indice) = CrearGrilla(5);
            var salon = new Espacio(Guid.NewGuid(), "Salón", TipoEspacio.Salon, 30);
            var s = CrearSesionCompleta(Guid.NewGuid(), Guid.NewGuid(), 1m, tipoFlujo: TipoFlujo.Laboratorio);
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s.Id, SemanaAcademica.A, bloques[0].Id, salon.Id, Modalidad.Presencial)
            };
            var ctx = Contexto(bloques, espacios: new[] { salon });

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignaciones, new Dictionary<Guid, Sesion> { [s.Id] = s }, indice, ctx);

            Assert.Contains(conflictos, c => c.StartsWith("HC-S03"));
        }

        [Fact]
        public void HCS05_EspacioDistintoAlFijo_Detecta()
        {
            var (bloques, indice) = CrearGrilla(5);
            var fijo = new Espacio(Guid.NewGuid(), "Fijo", TipoEspacio.Salon, 30);
            var otro = new Espacio(Guid.NewGuid(), "Otro", TipoEspacio.Salon, 30);
            var s = CrearSesionCompleta(Guid.NewGuid(), Guid.NewGuid(), 1m, espacioFijo: fijo.Id);
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s.Id, SemanaAcademica.A, bloques[0].Id, otro.Id, Modalidad.Presencial)
            };
            var ctx = Contexto(bloques, espacios: new[] { fijo, otro });

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignaciones, new Dictionary<Guid, Sesion> { [s.Id] = s }, indice, ctx);

            Assert.Contains(conflictos, c => c.StartsWith("HC-S05"));
        }

        [Fact]
        public void ContextoCompleto_AsignacionValida_SinConflictos()
        {
            var (bloques, indice) = CrearGrilla(5); // 07:00..12:00
            var asigId  = Guid.NewGuid();
            var grupoId = Guid.NewGuid();
            var lab = new Espacio(Guid.NewGuid(), "Lab", TipoEspacio.Laboratorio, 30);
            var s = CrearSesionCompleta(asigId, grupoId, 1m, tipoFlujo: TipoFlujo.Laboratorio, espacioFijo: lab.Id);
            var asignaciones = new[]
            {
                // 08:00 (bloque 1): dentro de ventana, franja Matutino, lab correcto, aforo OK.
                new AsignacionSemanal(Guid.NewGuid(), s.Id, SemanaAcademica.A, bloques[1].Id, lab.Id, Modalidad.Presencial)
            };
            var ctx = Contexto(bloques,
                ventanas: new() { [asigId] = (new TimeOnly(8, 0), new TimeOnly(10, 0)) },
                franjas: new() { [grupoId] = new List<FranjaHoraria> { FranjaHoraria.Matutino } },
                estudiantes: new() { [grupoId] = 20 },
                espacios: new[] { lab });

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignaciones, new Dictionary<Guid, Sesion> { [s.Id] = s }, indice, ctx);

            Assert.Empty(conflictos);
        }
    }
}
