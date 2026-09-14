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
        // dbName null = base de datos InMemory nueva (nombre aleatorio). Pasar el mismo nombre que
        // un Crear() anterior simula una SEGUNDA request contra los mismos datos con un DbContext
        // NUEVO — exactamente como en producción (DbContext scoped por request) — sin arrastrar el
        // change tracker de la primera llamada, que es lo que rompería una reimportación real: EF
        // lanza "already being tracked" si se reutiliza el mismo DbContext para dos EjecutarAsync.
        private static (ImportarCurriculumService svc, SOEABdContext db) Crear(string? dbName = null)
        {
            // InMemory no soporta transacciones reales (BeginTransactionAsync es un no-op que
            // solo advierte) — el servicio bajo prueba sí las usa, así que se silencia el aviso.
            var options = new DbContextOptionsBuilder<SOEABdContext>()
                .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
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
        public async Task Reimportar_ConDocenteExistente_ActualizaLaFilaEnBD()
        {
            // IMP1 auditoría: existe (de _docentes.GetAllAsync(), AsNoTracking) se mutaba sin
            // decírselo al DbContext — la respuesta reportaba "1 docente actualizado" y la fila en
            // BD quedaba intacta. Solo un DbContext REAL (no un fake en memoria) puede reproducir
            // esto: un fake que guarda en un Dictionary no distingue tracked de detached.
            var dbName = Guid.NewGuid().ToString();
            var (svc1, _) = Crear(dbName);

            var primero = new CurriculumExcelResult(
                facultades: new List<Facultad>(), programas: new List<Programa>(),
                asignaturas: new List<Asignatura>(),
                docentes: new List<Docente> { new(Guid.NewGuid(), "Juan Pérez", "", "", 20m, new List<FranjaHoraria> { FranjaHoraria.Matutino }) },
                sesionesPredefinidas: new List<Sesion>(), espacios: new List<Espacio>(), grupos: new List<Grupo>());
            await svc1.EjecutarAsync(primero);

            // Segunda "request": DbContext NUEVO contra la MISMA base de datos InMemory — así
            // reproduce la reimportación real (un docente ya persistido, no uno recién creado en
            // el mismo request), en vez de un cliente lanzando "already being tracked".
            var (svc2, db2) = Crear(dbName);
            var segundo = new CurriculumExcelResult(
                facultades: new List<Facultad>(), programas: new List<Programa>(),
                asignaturas: new List<Asignatura>(),
                docentes: new List<Docente> { new(Guid.NewGuid(), "Juan Pérez", "Gómez", "", 30m, new List<FranjaHoraria> { FranjaHoraria.Matutino }) },
                sesionesPredefinidas: new List<Sesion>(), espacios: new List<Espacio>(), grupos: new List<Grupo>());
            var stats = await svc2.EjecutarAsync(segundo);

            Assert.Equal(1, await db2.Docentes.CountAsync());
            Assert.Equal(1, stats.DocentesActualizados);
            var docenteEnBd = await db2.Docentes.AsNoTracking().SingleAsync();
            Assert.Equal("Gómez", docenteEnBd.Apellido);
            Assert.Equal(30m, docenteEnBd.MaximoHorasSemanales);
        }

        [Fact]
        public async Task Grupo_ConFacultadIdTemporal_SeRemapeaAlIdRealDeLaFacultad()
        {
            // M14 auditoría (descubierto al investigar la migración de FK de saneamiento):
            // progRealId/asigRealId/docRealId se remapean del id TEMPORAL (asignado durante el
            // mapeo DTO→entidad) al id REAL creado al persistir, pero g.FacultadId no pasaba por
            // el mismo remapeo — se persistía el id temporal, que nunca corresponde a ninguna fila
            // real de Facultades. En la BD local esto dejó el 100% de los Grupos con facultad_id
            // huérfano.
            var (svc, db) = Crear();
            var facTempId = Guid.NewGuid(); // id "temporal" tal como lo asigna el mapeo DTO→entidad
            var programaId = Guid.NewGuid();

            var resultado = new CurriculumExcelResult(
                facultades: new List<Facultad> { new(facTempId, "INGENIERIA") },
                programas: new List<Programa>(),
                asignaturas: new List<Asignatura>(),
                docentes: new List<Docente>(),
                sesionesPredefinidas: new List<Sesion>(),
                espacios: new List<Espacio>(),
                grupos: new List<Grupo> { new(Guid.NewGuid(), "G1", programaId, 30, facultadId: facTempId) });
            await svc.EjecutarAsync(resultado);

            var facultadReal = await db.Facultades.AsNoTracking().SingleAsync();
            var grupoPersistido = await db.Grupos.AsNoTracking().SingleAsync();

            Assert.Equal(facultadReal.Id, grupoPersistido.FacultadId);
            Assert.NotEqual(facTempId, grupoPersistido.FacultadId); // el id temporal nunca debe sobrevivir
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
