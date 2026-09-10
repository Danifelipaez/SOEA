using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SOEA.Application.Features.Import;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Data;
using SOEA.Infrastructure.Data.Context;
using SOEA.Infrastructure.Data.Repositories;
using Xunit;

namespace SOEA.Tests.Application
{
    /// <summary>
    /// Regresión (auditoría de limpieza, hallazgo 1.6): dos filas del Excel que resuelven a la
    /// MISMA entidad (nombre o código repetido — el caso normal: una facultad dicta varias
    /// asignaturas, un docente varias materias) creaban dos registros en vez de uno.
    ///
    /// Usa un DbContext real (proveedor InMemory) con los repositorios REALES de
    /// SOEA.Infrastructure.Data, no fakes de mano — es exactamente lo que reproduce el bug: un
    /// Add() de EF Core no es visible para una consulta LINQ posterior hasta el próximo
    /// SaveChangesAsync, en NINGÚN proveedor (incluido InMemory). Los fakes de
    /// ImportarCurriculumServiceTests.cs puentean deliberadamente lo "trackeado" con las
    /// consultas de cada repo fake, así que no pueden reproducir este bug — de ahí este archivo
    /// aparte.
    ///
    /// Cubre las dos variantes del fix con las entidades cuyo lookup NO usa EF.Functions.ILike
    /// (Npgsql-específico, sin traducción en el proveedor InMemory de pruebas):
    ///   - Docentes: el diccionario en memoria (docentesNormDict) nunca se actualizaba al crear.
    ///   - Asignaturas (por código+programa, GetByCodigoYProgramaAsync usa "==" plano): el SaveAsync
    ///     se movía al final de CADA iteración en vez de una sola vez tras el bucle completo.
    /// Facultades/Programas/Espacios/Grupos comparten el mismo fix (mismo patrón, ver el
    /// comentario en ImportarCurriculumService.cs), pero su lookup por nombre sí usa ILike y por
    /// eso no se prueban aquí contra InMemory.
    /// </summary>
    public class ImportarCurriculumServiceDuplicacionTests
    {
        private static (ImportarCurriculumService svc, SOEABdContext db) Crear()
        {
            // InMemory no soporta transacciones reales (BeginTransactionAsync es un no-op que
            // solo advierte) — el servicio bajo prueba sí las usa, así que se silencia el aviso.
            var options = new DbContextOptionsBuilder<SOEABdContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var db = new SOEABdContext(options);
            db.Database.EnsureCreated();

            var uow = new UnitOfWork(db);
            var svc = new ImportarCurriculumService(
                uow,
                new FacultadRepositorio(db),
                new ProgramaRepositorio(db),
                new DocenteRepositorio(db),
                new EspacioRepositorio(db),
                new AsignaturaRepository(db),
                new GrupoRepositorio(db),
                new SesionRepositorio(db),
                new BloqueTiempoRepositorio(db));
            return (svc, db);
        }

        [Fact]
        public async Task DosFilasConElMismoDocente_CreanUnSoloDocente()
        {
            var (svc, db) = Crear();

            var resultado = new CurriculumExcelResult(
                facultades: new List<Facultad>(),
                programas: new List<Programa>(),
                asignaturas: new List<Asignatura>(),
                docentes: new List<Docente>
                {
                    // Dos filas del Excel: mismo docente dictando dos asignaturas distintas.
                    new(Guid.NewGuid(), "Ana Pérez", "", "", 20m, new List<FranjaHoraria> { FranjaHoraria.Matutino }),
                    new(Guid.NewGuid(), "Ana Pérez", "", "", 20m, new List<FranjaHoraria> { FranjaHoraria.Matutino })
                },
                sesionesPredefinidas: new List<Sesion>(),
                espacios: new List<Espacio>(),
                grupos: new List<Grupo>());

            var stats = await svc.EjecutarAsync(resultado);

            Assert.Equal(1, await db.Docentes.CountAsync());
            Assert.Equal(1, stats.DocentesCreados);
        }

        [Fact]
        public async Task DosFilasConLaMismaAsignatura_CreanUnaSolaAsignatura()
        {
            var (svc, db) = Crear();
            var programaId = Guid.NewGuid(); // sin Programa real: el lookup es por Código, no requiere que exista.

            var resultado = new CurriculumExcelResult(
                facultades: new List<Facultad>(),
                programas: new List<Programa>(),
                asignaturas: new List<Asignatura>
                {
                    // Dos filas del Excel para la misma asignatura (p. ej. una fila por sesión semanal).
                    new(Guid.NewGuid(), "Cálculo I", "CALC-1", 2, 2, 0, programaId),
                    new(Guid.NewGuid(), "Cálculo I", "CALC-1", 2, 2, 0, programaId)
                },
                docentes: new List<Docente>(),
                sesionesPredefinidas: new List<Sesion>(),
                espacios: new List<Espacio>(),
                grupos: new List<Grupo>());

            var stats = await svc.EjecutarAsync(resultado);

            Assert.Equal(1, await db.Asignaturas.CountAsync());
            Assert.Equal(1, stats.AsignaturasCreadas);
        }
    }
}
