using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Application.Features.Horario;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.ValueObjects;
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

        // Grilla de un bloque de 1h por día, lunes..viernes (HC-SEP necesita días distintos).
        private static (List<BloqueTiempo> bloques, Dictionary<Guid, int> indice) CrearGrillaMultiDia()
        {
            var bloques = new[] { DiaDeSemana.Lunes, DiaDeSemana.Martes, DiaDeSemana.Miercoles, DiaDeSemana.Jueves, DiaDeSemana.Viernes }
                .Select(dia => new BloqueTiempo(Guid.NewGuid(), dia, new TimeOnly(7, 0), new TimeOnly(8, 0)))
                .ToList();
            var indice = new Dictionary<Guid, int>();
            for (int i = 0; i < bloques.Count; i++) indice[bloques[i].Id] = i;
            return (bloques, indice);
        }

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
            Dictionary<Guid, DisponibilidadSemanal>? disponibilidad = null,
            Dictionary<Guid, int>? estudiantes = null,
            IEnumerable<Espacio>? espacios = null,
            HashSet<Guid>? fijas = null,
            Dictionary<Guid, List<RequisitoEspacio>>? requisitosPorGrupo = null) =>
            new(bloques,
                ventanas ?? new Dictionary<Guid, (TimeOnly?, TimeOnly?)>(),
                disponibilidad ?? new Dictionary<Guid, DisponibilidadSemanal>(),
                estudiantes ?? new Dictionary<Guid, int>(),
                (espacios ?? Enumerable.Empty<Espacio>()).ToDictionary(e => e.Id),
                fijas,
                requisitosPorGrupo);

        // "Franja específica" con ventana exacta — evita el ambiguo límite de las 13:00 de la
        // etiqueta "Matutino" (ver DisponibilidadSemanalTests) para que los tests sean inequívocos.
        private static DisponibilidadSemanal DisponibilidadLunes(string desde, string hasta) =>
            DisponibilidadSemanal.Desde(new Dictionary<string, DisponibilidadSemanal.DiaEntradaCruda>
            {
                ["lunes"] = new(false, "Franja específica", null, desde, hasta)
            });

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
            var (bloques, indice) = CrearGrilla(5); // bloques 07:00..12:00 (lunes)
            var grupoId = Guid.NewGuid();
            var s = CrearSesionCompleta(Guid.NewGuid(), grupoId, 1m);
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s.Id, SemanaAcademica.A, bloques[0].Id, null, Modalidad.Virtual)
            };
            var ctx = Contexto(bloques, disponibilidad: new()
            {
                [grupoId] = DisponibilidadLunes("13:00", "18:00") // grupo solo tarde
            });

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignaciones, new Dictionary<Guid, Sesion> { [s.Id] = s }, indice, ctx);

            Assert.Contains(conflictos, c => c.StartsWith("HC-G01"));
        }

        // ── M8: reglas que antes eran degradaciones silenciosas ──────────────────────

        [Fact]
        public void HCS04_PresencialSinEspacio_Detecta()
        {
            var (bloques, indice) = CrearGrilla(5);
            var s = CrearSesionCompleta(Guid.NewGuid(), Guid.NewGuid(), 1m);
            var asignaciones = new[]
            {
                // Presencial pero sin EspacioId: antes pasaba el validador limpia.
                new AsignacionSemanal(Guid.NewGuid(), s.Id, SemanaAcademica.A, bloques[0].Id, null, Modalidad.Presencial)
            };
            var ctx = Contexto(bloques);

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignaciones, new Dictionary<Guid, Sesion> { [s.Id] = s }, indice, ctx);

            Assert.Contains(conflictos, c => c.StartsWith("HC-S04"));
        }

        [Fact]
        public void Datos_EspacioDesconocido_Detecta()
        {
            var (bloques, indice) = CrearGrilla(5);
            var s = CrearSesionCompleta(Guid.NewGuid(), Guid.NewGuid(), 1m);
            var espacioDesconocido = Guid.NewGuid(); // no está en ctx.EspacioPorId
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s.Id, SemanaAcademica.A, bloques[0].Id, espacioDesconocido, Modalidad.Presencial)
            };
            var ctx = Contexto(bloques); // sin espacios registrados

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignaciones, new Dictionary<Guid, Sesion> { [s.Id] = s }, indice, ctx);

            Assert.Contains(conflictos, c => c.StartsWith("DATOS"));
            // El continue tras reportar DATOS evita ruido: no debe además fallar HC-S03/HC-CAP/HC-S05
            // por un espacio del que no tenemos datos.
            Assert.DoesNotContain(conflictos, c => c.StartsWith("HC-S03") || c.StartsWith("HC-CAP") || c.StartsWith("HC-S05"));
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

        // ── HC-S03/HC-S05 vía Grupo.RequisitosEspacio (M9: nunca probado end-to-end) ─

        [Fact]
        public void HCS03_RequisitoDeGrupoPermiteTipoDistintoAlDefault_SinConflicto()
        {
            var (bloques, indice) = CrearGrilla(5);
            var grupoId = Guid.NewGuid();
            // Sesión de teoría presencial (default: excluye laboratorio); el requisito del
            // grupo la reautoriza explícitamente para un laboratorio (CumpleTipo, rama :37).
            var s = CrearSesionCompleta(Guid.NewGuid(), grupoId, 1m);
            var lab = new Espacio(Guid.NewGuid(), "Lab", TipoEspacio.Laboratorio, 30);
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s.Id, SemanaAcademica.A, bloques[0].Id, lab.Id, Modalidad.Presencial)
            };
            var requisitos = new Dictionary<Guid, List<RequisitoEspacio>>
            {
                [grupoId] = new() { new RequisitoEspacio(TipoSesion.TeoriaPresencial, null, TipoEspacio.Laboratorio, 1) }
            };
            var ctx = Contexto(bloques, espacios: new[] { lab }, requisitosPorGrupo: requisitos);

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignaciones, new Dictionary<Guid, Sesion> { [s.Id] = s }, indice, ctx);

            Assert.Empty(conflictos);
        }

        [Fact]
        public void HCS03_RequisitoDeGrupoConTipoDistinto_Detecta()
        {
            var (bloques, indice) = CrearGrilla(5);
            var grupoId = Guid.NewGuid();
            var s = CrearSesionCompleta(Guid.NewGuid(), grupoId, 1m);
            var salon = new Espacio(Guid.NewGuid(), "Salón", TipoEspacio.Salon, 30);
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s.Id, SemanaAcademica.A, bloques[0].Id, salon.Id, Modalidad.Presencial)
            };
            // El grupo exige auditorio para su teoría presencial; se asignó a un salón.
            var requisitos = new Dictionary<Guid, List<RequisitoEspacio>>
            {
                [grupoId] = new() { new RequisitoEspacio(TipoSesion.TeoriaPresencial, null, TipoEspacio.Auditorio, 1) }
            };
            var ctx = Contexto(bloques, espacios: new[] { salon }, requisitosPorGrupo: requisitos);

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignaciones, new Dictionary<Guid, Sesion> { [s.Id] = s }, indice, ctx);

            Assert.Contains(conflictos, c => c.StartsWith("HC-S03"));
        }

        [Fact]
        public void HCS03_RequisitoDeGrupoConEspacioFijo_AsignadoAOtroEspacio_Detecta()
        {
            // Un EspacioId fijo declarado en Grupo.RequisitosEspacio (no en Sesion.EspacioId) se
            // hace cumplir vía CumpleTipo (HC-S03) — y desde M7, TAMBIÉN vía el chequeo dedicado de
            // HC-S05 sobre el requisito del grupo (ver HCS05_RequisitoDeGrupo_SinEspacioIdEnSesion_Detecta).
            // Ambas etiquetas describen la misma violación real desde ángulos distintos.
            var (bloques, indice) = CrearGrilla(5);
            var grupoId = Guid.NewGuid();
            var fijo = new Espacio(Guid.NewGuid(), "Fijo del grupo", TipoEspacio.Salon, 30);
            var otro = new Espacio(Guid.NewGuid(), "Otro", TipoEspacio.Salon, 30);
            var s = CrearSesionCompleta(Guid.NewGuid(), grupoId, 1m); // sin EspacioId a nivel de Sesion
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s.Id, SemanaAcademica.A, bloques[0].Id, otro.Id, Modalidad.Presencial)
            };
            var requisitos = new Dictionary<Guid, List<RequisitoEspacio>>
            {
                [grupoId] = new() { new RequisitoEspacio(TipoSesion.TeoriaPresencial, fijo.Id, TipoEspacio.Salon, 1) }
            };
            var ctx = Contexto(bloques, espacios: new[] { fijo, otro }, requisitosPorGrupo: requisitos);

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignaciones, new Dictionary<Guid, Sesion> { [s.Id] = s }, indice, ctx);

            Assert.Contains(conflictos, c => c.StartsWith("HC-S03"));
            Assert.Contains(conflictos, c => c.StartsWith("HC-S05")); // M7
        }

        // M7: el chequeo de HC-S05 ahora también consulta el requisito de espacio del GRUPO, no
        // sólo Sesion.EspacioId — cubre sesiones cuyo EspacioId nunca se copió del requisito
        // (p. ej. creadas por una vía distinta a MapearSesionesIniciales).
        [Fact]
        public void HCS05_RequisitoDeGrupo_SinEspacioIdEnSesion_Detecta()
        {
            var (bloques, indice) = CrearGrilla(5);
            var grupoId = Guid.NewGuid();
            var fijo = new Espacio(Guid.NewGuid(), "Fijo del grupo", TipoEspacio.Salon, 30);
            var otro = new Espacio(Guid.NewGuid(), "Otro", TipoEspacio.Salon, 30);
            var s = CrearSesionCompleta(Guid.NewGuid(), grupoId, 1m); // sin EspacioId a nivel de Sesion
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s.Id, SemanaAcademica.A, bloques[0].Id, otro.Id, Modalidad.Presencial)
            };
            var requisitos = new Dictionary<Guid, List<RequisitoEspacio>>
            {
                [grupoId] = new() { new RequisitoEspacio(TipoSesion.TeoriaPresencial, fijo.Id, null, 1) }
            };
            var ctx = Contexto(bloques, espacios: new[] { fijo, otro }, requisitosPorGrupo: requisitos);

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignaciones, new Dictionary<Guid, Sesion> { [s.Id] = s }, indice, ctx);

            Assert.Contains(conflictos, c => c.StartsWith("HC-S05"));
        }

        [Fact]
        public void HCS05_ConEspacioIdPropioYaCorrecto_NoDuplicaViaRequisitoDelGrupo()
        {
            // Cuando Sesion.EspacioId ya está presente y la asignación lo respeta, el chequeo por
            // requisito de grupo no debe disparar un HC-S05 adicional/redundante (el grupo de este
            // test no declara EspacioId en su requisito, así que tampoco interactúa con HC-S03).
            var (bloques, indice) = CrearGrilla(5);
            var grupoId = Guid.NewGuid();
            var espacioDeLaSesion = new Espacio(Guid.NewGuid(), "De la sesión", TipoEspacio.Salon, 30);
            var s = CrearSesionCompleta(Guid.NewGuid(), grupoId, 1m, espacioFijo: espacioDeLaSesion.Id);
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s.Id, SemanaAcademica.A, bloques[0].Id, espacioDeLaSesion.Id, Modalidad.Presencial)
            };
            var ctx = Contexto(bloques, espacios: new[] { espacioDeLaSesion });

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignaciones, new Dictionary<Guid, Sesion> { [s.Id] = s }, indice, ctx);

            Assert.Empty(conflictos);
        }

        [Fact]
        public void HCS03_RequisitoDeGrupoConEspacioFijo_MismoEspacio_SinConflicto()
        {
            var (bloques, indice) = CrearGrilla(5);
            var grupoId = Guid.NewGuid();
            var fijo = new Espacio(Guid.NewGuid(), "Fijo del grupo", TipoEspacio.Salon, 30);
            var s = CrearSesionCompleta(Guid.NewGuid(), grupoId, 1m);
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s.Id, SemanaAcademica.A, bloques[0].Id, fijo.Id, Modalidad.Presencial)
            };
            var requisitos = new Dictionary<Guid, List<RequisitoEspacio>>
            {
                [grupoId] = new() { new RequisitoEspacio(TipoSesion.TeoriaPresencial, fijo.Id, TipoEspacio.Salon, 1) }
            };
            var ctx = Contexto(bloques, espacios: new[] { fijo }, requisitosPorGrupo: requisitos);

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignaciones, new Dictionary<Guid, Sesion> { [s.Id] = s }, indice, ctx);

            Assert.Empty(conflictos);
        }

        // ── HC-ALT: alternancia atómica por espacio — nunca probada en el validador (sólo en
        // CP-SAT). Dos sesiones que comparten ParejaAlternanciaId deben tener tipos opuestos,
        // coincidir de bloque por semana, y compartir el mismo espacio entre sus semanas
        // presenciales.

        private static Sesion CrearSesionAlternancia(Guid patron, Guid pareja, TipoAlternancia alternancia) =>
            new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, Guid.NewGuid(),
                alternancia, Modalidad.Presencial, 1m, false, false,
                tipoFlujo: TipoFlujo.AulaVirtual, patronAlternanciaId: patron, parejaAlternanciaId: pareja);

        [Fact]
        public void HCALT_TiposNoOpuestos_Detecta()
        {
            var (bloques, indice) = CrearGrilla(3);
            var pareja = Guid.NewGuid();
            // Ambas TipoA: no son opuestas (debería ser TipoA/TipoB).
            var s1 = CrearSesionAlternancia(TipoAlternanciaConfig.IdTipoA, pareja, TipoAlternancia.TipoA);
            var s2 = CrearSesionAlternancia(TipoAlternanciaConfig.IdTipoA, pareja, TipoAlternancia.TipoA);
            var sesiones = new Dictionary<Guid, Sesion> { [s1.Id] = s1, [s2.Id] = s2 };
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s1.Id, SemanaAcademica.A, bloques[0].Id, null, Modalidad.Virtual),
                new AsignacionSemanal(Guid.NewGuid(), s2.Id, SemanaAcademica.A, bloques[0].Id, null, Modalidad.Virtual),
            };

            var conflictos = ValidadorRestriccionesDuras.Validar(asignaciones, sesiones, indice);

            Assert.Contains(conflictos, c => c.StartsWith("HC-ALT") && c.Contains("tipos opuestos"));
        }

        [Fact]
        public void HCALT_BloqueDistintoEnSemana_Detecta()
        {
            var (bloques, indice) = CrearGrilla(3);
            var pareja = Guid.NewGuid();
            var s1 = CrearSesionAlternancia(TipoAlternanciaConfig.IdTipoA, pareja, TipoAlternancia.TipoA);
            var s2 = CrearSesionAlternancia(TipoAlternanciaConfig.IdTipoB, pareja, TipoAlternancia.TipoB);
            var sesiones = new Dictionary<Guid, Sesion> { [s1.Id] = s1, [s2.Id] = s2 };
            // Misma semana A, bloques distintos: la pareja debe coincidir de franja.
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s1.Id, SemanaAcademica.A, bloques[0].Id, null, Modalidad.Virtual),
                new AsignacionSemanal(Guid.NewGuid(), s2.Id, SemanaAcademica.A, bloques[1].Id, null, Modalidad.Virtual),
            };

            var conflictos = ValidadorRestriccionesDuras.Validar(asignaciones, sesiones, indice);

            Assert.Contains(conflictos, c => c.StartsWith("HC-ALT") && c.Contains("no coincide de bloque"));
        }

        [Fact]
        public void HCALT_EspacioDistintoEntreSemanasPresenciales_Detecta()
        {
            var (bloques, indice) = CrearGrilla(3);
            var pareja = Guid.NewGuid();
            var s1 = CrearSesionAlternancia(TipoAlternanciaConfig.IdTipoA, pareja, TipoAlternancia.TipoA);
            var s2 = CrearSesionAlternancia(TipoAlternanciaConfig.IdTipoB, pareja, TipoAlternancia.TipoB);
            var sesiones = new Dictionary<Guid, Sesion> { [s1.Id] = s1, [s2.Id] = s2 };
            var salonA = Guid.NewGuid();
            var salonB = Guid.NewGuid();
            var asignaciones = new[]
            {
                // s1 presencial en semana A (salonA); s2 virtual en A.
                new AsignacionSemanal(Guid.NewGuid(), s1.Id, SemanaAcademica.A, bloques[0].Id, salonA, Modalidad.Presencial),
                new AsignacionSemanal(Guid.NewGuid(), s2.Id, SemanaAcademica.A, bloques[0].Id, null, Modalidad.Virtual),
                // s1 virtual en semana B; s2 presencial en B pero en un salón DISTINTO.
                new AsignacionSemanal(Guid.NewGuid(), s1.Id, SemanaAcademica.B, bloques[0].Id, null, Modalidad.Virtual),
                new AsignacionSemanal(Guid.NewGuid(), s2.Id, SemanaAcademica.B, bloques[0].Id, salonB, Modalidad.Presencial),
            };

            var conflictos = ValidadorRestriccionesDuras.Validar(asignaciones, sesiones, indice);

            Assert.Contains(conflictos, c => c.StartsWith("HC-ALT") && c.Contains("mismo espacio"));
        }

        [Fact]
        public void HCALT_ParejaValida_SinConflictos()
        {
            var (bloques, indice) = CrearGrilla(3);
            var pareja = Guid.NewGuid();
            var s1 = CrearSesionAlternancia(TipoAlternanciaConfig.IdTipoA, pareja, TipoAlternancia.TipoA);
            var s2 = CrearSesionAlternancia(TipoAlternanciaConfig.IdTipoB, pareja, TipoAlternancia.TipoB);
            var sesiones = new Dictionary<Guid, Sesion> { [s1.Id] = s1, [s2.Id] = s2 };
            var salon = Guid.NewGuid();
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s1.Id, SemanaAcademica.A, bloques[0].Id, salon, Modalidad.Presencial),
                new AsignacionSemanal(Guid.NewGuid(), s2.Id, SemanaAcademica.A, bloques[0].Id, null, Modalidad.Virtual),
                new AsignacionSemanal(Guid.NewGuid(), s1.Id, SemanaAcademica.B, bloques[0].Id, null, Modalidad.Virtual),
                new AsignacionSemanal(Guid.NewGuid(), s2.Id, SemanaAcademica.B, bloques[0].Id, salon, Modalidad.Presencial),
            };

            var conflictos = ValidadorRestriccionesDuras.Validar(asignaciones, sesiones, indice);

            Assert.Empty(conflictos);
        }

        // ── HC-SEP: separación mínima de días — nunca probada en el validador (sólo en CP-SAT).

        [Fact]
        public void HCSEP_SesionesSinSeparacionMinima_Detecta()
        {
            var (bloques, indice) = CrearGrillaMultiDia(); // lunes..viernes
            var grupoId = Guid.NewGuid();
            var asigId = Guid.NewGuid();
            var s1 = CrearSesionCompleta(asigId, grupoId, 1m);
            var s2 = CrearSesionCompleta(asigId, grupoId, 1m);
            var sesiones = new Dictionary<Guid, Sesion> { [s1.Id] = s1, [s2.Id] = s2 };
            // Lunes y martes: diferencia de 1 día, por debajo del mínimo de 2.
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s1.Id, SemanaAcademica.A, bloques[0].Id, null, Modalidad.Virtual),
                new AsignacionSemanal(Guid.NewGuid(), s2.Id, SemanaAcademica.A, bloques[1].Id, null, Modalidad.Virtual),
            };
            var ctx = Contexto(bloques);

            var conflictos = ValidadorRestriccionesDuras.Validar(asignaciones, sesiones, indice, ctx);

            Assert.Contains(conflictos, c => c.StartsWith("HC-SEP"));
        }

        [Fact]
        public void HCSEP_ConSeparacionSuficiente_SinConflictos()
        {
            var (bloques, indice) = CrearGrillaMultiDia();
            var grupoId = Guid.NewGuid();
            var asigId = Guid.NewGuid();
            var s1 = CrearSesionCompleta(asigId, grupoId, 1m);
            var s2 = CrearSesionCompleta(asigId, grupoId, 1m);
            var sesiones = new Dictionary<Guid, Sesion> { [s1.Id] = s1, [s2.Id] = s2 };
            // Lunes y miércoles: diferencia de 2 días, cumple el mínimo.
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s1.Id, SemanaAcademica.A, bloques[0].Id, null, Modalidad.Virtual),
                new AsignacionSemanal(Guid.NewGuid(), s2.Id, SemanaAcademica.A, bloques[2].Id, null, Modalidad.Virtual),
            };
            var ctx = Contexto(bloques);

            var conflictos = ValidadorRestriccionesDuras.Validar(asignaciones, sesiones, indice, ctx);

            Assert.Empty(conflictos);
        }

        // ── HC-BASE (regla 8): una sesión del horario base no puede moverse de su bloque
        // pre-asignado — nunca probada en el validador (sólo la exención de HC-VH lo era).

        [Fact]
        public void HCBASE_SesionFijaMovidaDeSuBloque_Detecta()
        {
            var (bloques, indice) = CrearGrilla(5);
            var s = CrearSesionCompleta(Guid.NewGuid(), Guid.NewGuid(), 1m);
            s.AsignarBloqueTiempo(bloques[0].Id); // bloque del horario base
            var asignaciones = new[]
            {
                // Se persistió en el bloque 1, no en el bloque 0 que trae fijado.
                new AsignacionSemanal(Guid.NewGuid(), s.Id, SemanaAcademica.A, bloques[1].Id, null, Modalidad.Virtual)
            };
            var ctx = Contexto(bloques, fijas: new HashSet<Guid> { s.Id });

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignaciones, new Dictionary<Guid, Sesion> { [s.Id] = s }, indice, ctx);

            Assert.Contains(conflictos, c => c.StartsWith("HC-BASE"));
        }

        [Fact]
        public void HCBASE_SesionFijaEnSuBloque_SinConflictos()
        {
            var (bloques, indice) = CrearGrilla(5);
            var s = CrearSesionCompleta(Guid.NewGuid(), Guid.NewGuid(), 1m);
            s.AsignarBloqueTiempo(bloques[0].Id);
            var asignaciones = new[]
            {
                new AsignacionSemanal(Guid.NewGuid(), s.Id, SemanaAcademica.A, bloques[0].Id, null, Modalidad.Virtual)
            };
            var ctx = Contexto(bloques, fijas: new HashSet<Guid> { s.Id });

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignaciones, new Dictionary<Guid, Sesion> { [s.Id] = s }, indice, ctx);

            Assert.Empty(conflictos);
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
                disponibilidad: new() { [grupoId] = DisponibilidadLunes("06:00", "12:00") },
                estudiantes: new() { [grupoId] = 20 },
                espacios: new[] { lab });

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignaciones, new Dictionary<Guid, Sesion> { [s.Id] = s }, indice, ctx);

            Assert.Empty(conflictos);
        }
    }
}
