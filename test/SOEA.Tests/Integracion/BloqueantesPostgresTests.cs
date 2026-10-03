using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OfficeOpenXml;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Services;
using SOEA.Infrastructure.Data.Seeding;

namespace SOEA.Tests.Integracion
{
    /// <summary>
    /// Bloqueantes P0 de docs/PLAN_ENTREGA_Auditoria.md, contra PostgreSQL real y el API completo.
    /// Los cinco pasaban desapercibidos con InMemory/fakes.
    /// </summary>
    [Collection("Postgres")]
    public class BloqueantesPostgresTests
    {
        private readonly ApiPostgresFixture _api;

        public BloqueantesPostgresTests(ApiPostgresFixture api) => _api = api;

        [PostgresFact]
        public async Task P0_1_M14_SaneaDatosHuerfanos_EnVezDeTumbarElArranque()
        {
            var cadena = PostgresPruebas.NuevaBd();
            try
            {
                await using var db = PostgresPruebas.Contexto(cadena);
                var migrador = db.GetService<IMigrator>();
                await migrador.MigrateAsync("20260910003502_M10_UnificarCatalogoBloques");
                await BloqueTiempoSeeder.SeedAsync(db);

                var grilla = GrillaInstitucional.GenerarBloques();
                var lunes8 = grilla.First(b => b.Dia == DiaDeSemana.Lunes && b.HoraInicio == new TimeOnly(8, 0));
                var martes10 = grilla.First(b => b.Dia == DiaDeSemana.Martes && b.HoraInicio == new TimeOnly(10, 0));

                var fac = new Facultad(Guid.NewGuid(), "FAC");
                var prog = new Programa(Guid.NewGuid(), "PROG", fac.Id);
                var asig = new Asignatura(Guid.NewGuid(), "ASIG", "A1", 2, 1, 0, prog.Id);
                var grupo = new Grupo(Guid.NewGuid(), "G1", asig.Id, 30);

                Sesion NuevaSesion(Guid asignaturaId, Guid? espacioId) => new(Guid.NewGuid(), asignaturaId, null, lunes8.Id,
                    espacioId, grupo.Id, TipoAlternancia.SinAlternancia, Modalidad.Presencial, 2m, false, false, TipoFlujo.AulaVirtual);
                // Viva: aula inexistente y el bloque de Fase 1 (lunes) distinto al de su asignación (martes).
                var viva = NuevaSesion(asig.Id, espacioId: Guid.NewGuid());
                var asignacionViva = new AsignacionSemanal(Guid.NewGuid(), viva.Id, SemanaAcademica.A, martes10.Id, null, Modalidad.Presencial);
                var fantasma = NuevaSesion(asig.Id, espacioId: null);       // fila del import: fuera de todo horario
                var rota = NuevaSesion(Guid.NewGuid(), espacioId: null);    // asignatura inexistente
                var horario = new SOEA.Domain.Entities.Horario(Guid.NewGuid(), "2026-1", new List<Guid> { viva.Id, rota.Id });

                db.AddRange(fac, prog, asig, viva, fantasma, rota, horario);
                await db.SaveChangesAsync();
                await InsertarGrupoLegadoAsync(db, grupo, prog.Id, facultadId: Guid.NewGuid()); // facultad huérfana
                await InsertarAsignacionSinHorarioAsync(db, asignacionViva); // esquema M10: aún sin horario_id (M16)
                db.ChangeTracker.Clear();

                await migrador.MigrateAsync(); // M14: antes 23503 FK_Grupos_Facultades_facultad_id

                Assert.Equal(asig.Id, (await db.Set<Grupo>().AsNoTracking().SingleAsync()).AsignaturaId);
                var sesion = Assert.Single(await db.Set<Sesion>().AsNoTracking().ToListAsync());
                Assert.Equal(viva.Id, sesion.Id);
                Assert.Null(sesion.EspacioId);
                Assert.Equal(martes10.Id, sesion.BloqueTiempoId);
            }
            finally
            {
                await PostgresPruebas.BorrarAsync(cadena);
            }
        }

        [PostgresFact]
        public async Task P0_2_ImportarExcelConEspacio_Responde200_YNoPersisteSesionesInvisibles()
        {
            var sufijo = Sufijo();
            ExcelPackage.License.SetNonCommercialPersonal("SOEA");
            using var paquete = new ExcelPackage();
            var hoja = paquete.Workbook.Worksheets.Add("Horario");
            object[] cabecera = { "Facultad", "Programa", "Asignatura", "Grupo", "Docente", "Reales [h]", "Espacio", "Dia", "Hora", "Final" };
            object[] fila = { $"FAC {sufijo}", $"PROG {sufijo}", $"ASIG {sufijo}", 1, $"DOCENTE {sufijo}", 2, $"LAB {sufijo}", "Lunes", "08:00", "10:00" };
            for (int i = 0; i < cabecera.Length; i++)
            {
                hoja.Cells[1, i + 1].Value = cabecera[i];
                hoja.Cells[2, i + 1].Value = fila[i];
            }

            using var form = new MultipartFormDataContent
            {
                { new ByteArrayContent(paquete.GetAsByteArray()), "archivo", "horario.xlsx" }
            };
            var r = await ImportarAsync(form);
            var cuerpo = await r.Content.ReadAsStringAsync();

            Assert.True(r.StatusCode == HttpStatusCode.OK, cuerpo); // antes: 409 por el EspacioId temporal del lector
            await using var db = _api.Db();
            Assert.True(await db.Set<Espacio>().AnyAsync(e => e.Nombre.ToUpper() == $"LAB {sufijo}".ToUpper()));
            var asig = await db.Set<Asignatura>().SingleAsync(a => a.Nombre.ToUpper() == $"ASIG {sufijo}".ToUpper());
            Assert.False(await db.Set<Sesion>().AnyAsync(s => s.AsignaturaId == asig.Id));
        }

        [PostgresFact]
        public async Task P0_3_GenerarConSesionFija_Responde200_YLaFijaOcupaElLugarDeUnaSesionDelGrupo()
        {
            var c = await SembrarAsync();
            var r = await GenerarAsync(c, $"IT-{c.Sufijo}", new object[]
            {
                new
                {
                    asignaturaId = c.AsignaturaId, grupoId = c.GrupoId, espacioId = c.EspacioId,
                    dia = "lunes", horaInicio = "08:00", duracionHoras = 2, tipoFlujo = "AulaVirtual", @virtual = false
                }
            });
            var cuerpo = await r.Content.ReadAsStringAsync();

            Assert.True(r.StatusCode == HttpStatusCode.OK, cuerpo); // antes: 409, GrupoId sintético contra la FK
            var sesiones = JsonDocument.Parse(cuerpo).RootElement.GetProperty("sesiones").EnumerateArray().ToList();
            Assert.Equal(2, sesiones.Count); // la asignatura pide 2 por semana: la fija no se suma a ellas
            Assert.Contains(sesiones, s => s.GetProperty("dia").GetString() == "lunes" &&
                                           s.GetProperty("horaInicio").GetString() == "08:00" &&
                                           s.GetProperty("grupoId").GetString() == c.GrupoId.ToString());
        }

        [PostgresFact]
        public async Task P0_4_SesionManualConSolapeParcialEnLaMismaAula_Responde409()
        {
            var c = await SembrarAsync();
            var otro = await SembrarAsync();
            var (horarioId, sesiones) = await GenerarOkAsync(c, $"IT-{c.Sufijo}");
            var ocupada = sesiones.First(s => s.GetProperty("dia").GetString() != "sabado" &&
                                              TimeOnly.Parse(s.GetProperty("horaInicio").GetString()!).Hour <= 17);
            var unaHoraDespues = TimeOnly.Parse(ocupada.GetProperty("horaInicio").GetString()!).AddHours(1);

            var r = await CrearSesionManualAsync(horarioId, otro, ocupada.GetProperty("espacioId").GetString()!,
                ocupada.GetProperty("dia").GetString()!, unaHoraDespues.ToString("HH:mm"));
            var cuerpo = await r.Content.ReadAsStringAsync();

            // Antes: 201 y dos clases a la vez en la misma aula (el chequeo leía el bloque de Fase 1).
            Assert.True(r.StatusCode == HttpStatusCode.Conflict, cuerpo);
            Assert.Contains("HC-S01", cuerpo);
        }

        [PostgresFact]
        public async Task P0_5_SesionManualCreada_ApareceEnElHorarioVigente()
        {
            var c = await SembrarAsync();
            var otro = await SembrarAsync();
            var semestre = $"IT-{c.Sufijo}";
            var (horarioId, _) = await GenerarOkAsync(c, semestre);

            var r = await CrearSesionManualAsync(horarioId, otro, otro.EspacioId.ToString(), "sabado", "07:00");
            var cuerpo = await r.Content.ReadAsStringAsync();
            Assert.True(r.StatusCode == HttpStatusCode.Created, cuerpo);
            var creadaId = JsonDocument.Parse(cuerpo).RootElement[0].GetProperty("id").GetString();

            // Antes: 201, pero la sesión no pertenecía a ningún horario y desaparecía al recargar.
            var actual = await _api.Client.GetFromJsonAsync<JsonElement>($"/api/horario/actual?semestre={semestre}");
            Assert.Contains(actual.GetProperty("sesiones").EnumerateArray(), s => s.GetProperty("id").GetString() == creadaId);
        }

        // ── NEW-2 (auditoría pre-producción 2026-09-28) ──────────────────────────

        [PostgresFact]
        public async Task NEW2_BorrarEspacioUsadoSoloPorAsignaciones_PurgaLaSesion_YNoDejaHuerfanas()
        {
            var c = await SembrarAsync();
            await GenerarOkAsync(c, $"IT-{c.Sufijo}");
            await using (var previa = _api.Db())
                Assert.True(await previa.Set<AsignacionSemanal>().AnyAsync(a => a.EspacioId == c.EspacioId)); // el aula está en uso

            var r = await _api.Client.DeleteAsync($"/api/espacios/{c.EspacioId}");
            Assert.True(r.StatusCode == HttpStatusCode.NoContent, await r.Content.ReadAsStringAsync());

            // Antes: 204 y las AsignacionesSemanales seguían apuntando al aula borrada (la cascada solo
            // miraba Sesion.EspacioId, que el pipeline no rellena).
            await using var db = _api.Db();
            Assert.False(await db.Set<Espacio>().AnyAsync(e => e.Id == c.EspacioId));
            Assert.False(await db.Set<AsignacionSemanal>().AnyAsync(a => a.EspacioId == c.EspacioId));
            Assert.False(await db.Set<Sesion>().AnyAsync(s => s.AsignaturaId == c.AsignaturaId));
        }

        [PostgresFact]
        public async Task NEW2_M15_SaneaAsignacionesConAulaInexistente_YLaFkImpideVolverAGenerarlas()
        {
            var cadena = PostgresPruebas.NuevaBd();
            try
            {
                await using var db = PostgresPruebas.Contexto(cadena);
                var migrador = db.GetService<IMigrator>();
                await migrador.MigrateAsync("20260912153501_M14_ClavesAjenasSesionesYGrupos");
                await BloqueTiempoSeeder.SeedAsync(db);

                var grilla = GrillaInstitucional.GenerarBloques();
                var lunes8 = grilla.First(b => b.Dia == DiaDeSemana.Lunes && b.HoraInicio == new TimeOnly(8, 0));
                var martes10 = grilla.First(b => b.Dia == DiaDeSemana.Martes && b.HoraInicio == new TimeOnly(10, 0));

                var fac = new Facultad(Guid.NewGuid(), "FAC");
                var prog = new Programa(Guid.NewGuid(), "PROG", fac.Id);
                var asig = new Asignatura(Guid.NewGuid(), "ASIG", "A1", 2, 1, 0, prog.Id);
                var grupo = new Grupo(Guid.NewGuid(), "G1", asig.Id, 30);
                var aula = new Espacio(Guid.NewGuid(), "AULA", TipoEspacio.Salon, 40);

                Sesion NuevaSesion() => new(Guid.NewGuid(), asig.Id, null, lunes8.Id, null, grupo.Id,
                    TipoAlternancia.SinAlternancia, Modalidad.Presencial, 2m, false, false, TipoFlujo.AulaVirtual);
                var viva = NuevaSesion();
                var rota = NuevaSesion();   // su aula se borró antes de existir la FK
                db.AddRange(fac, prog, asig, aula);
                await db.SaveChangesAsync();
                await InsertarGrupoLegadoAsync(db, grupo, prog.Id);
                db.AddRange(viva, rota);
                await db.SaveChangesAsync();
                // Esquema M14: aún sin horario_id (M16), así que se siembran sin pasar por el modelo actual.
                await InsertarAsignacionSinHorarioAsync(db, new AsignacionSemanal(Guid.NewGuid(), viva.Id, SemanaAcademica.A, lunes8.Id, aula.Id, Modalidad.Presencial));
                await InsertarAsignacionSinHorarioAsync(db, new AsignacionSemanal(Guid.NewGuid(), rota.Id, SemanaAcademica.A, martes10.Id, Guid.NewGuid(), Modalidad.Presencial));
                await InsertarAsignacionSinHorarioAsync(db, new AsignacionSemanal(Guid.NewGuid(), rota.Id, SemanaAcademica.B, martes10.Id, null, Modalidad.Virtual));
                db.ChangeTracker.Clear();

                await migrador.MigrateAsync(); // M15: sin el saneamiento fallaría con 23503

                Assert.Equal(viva.Id, Assert.Single(await db.Set<Sesion>().AsNoTracking().ToListAsync()).Id);
                Assert.Equal(aula.Id, Assert.Single(await db.Set<AsignacionSemanal>().AsNoTracking().ToListAsync()).EspacioId);

                db.Add(new AsignacionSemanal(Guid.NewGuid(), viva.Id, SemanaAcademica.B, lunes8.Id, Guid.NewGuid(), Modalidad.Presencial));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
            finally
            {
                await PostgresPruebas.BorrarAsync(cadena);
            }
        }

        // ── NEW-3 (auditoría pre-producción 2026-09-28) ──────────────────────────

        [PostgresFact]
        public async Task NEW3_GenerarOtroSemestreConLasMismasAulas_Responde200_YCadaSemestreConservaSuHorario()
        {
            var c = await SembrarAsync();
            var sem1 = $"IT-{c.Sufijo}-1";
            var sem2 = $"IT-{c.Sufijo}-2";
            await GenerarOkAsync(c, sem1);

            var r = await GenerarAsync(c, sem2);
            var cuerpo = await r.Content.ReadAsStringAsync();
            Assert.True(r.StatusCode == HttpStatusCode.OK, cuerpo); // antes: 409 (23505 ux_asignacion_semanal_espacio_conflicto)

            foreach (var semestre in new[] { sem1, sem2 })
            {
                var actual = await _api.Client.GetFromJsonAsync<JsonElement>($"/api/horario/actual?semestre={semestre}");
                Assert.Equal(2, actual.GetProperty("sesiones").GetArrayLength());
            }
            // Regenerar el mismo semestre sigue funcionando (sus filas anteriores se limpian antes de insertar).
            var otra = await GenerarAsync(c, sem2);
            Assert.True(otra.StatusCode == HttpStatusCode.OK, await otra.Content.ReadAsStringAsync());
        }

        [PostgresFact]
        public async Task NEW3_TodaAsignacionPersistidaLlevaElHorarioDeSuCorrida()
        {
            var c = await SembrarAsync();
            var (horarioId, sesiones) = await GenerarOkAsync(c, $"IT-{c.Sufijo}");
            var otro = await SembrarAsync();
            var r = await CrearSesionManualAsync(horarioId, otro, otro.EspacioId.ToString(), "sabado", "07:00");
            Assert.True(r.StatusCode == HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
            var manualId = Guid.Parse(JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement[0].GetProperty("id").GetString()!);

            await using var db = _api.Db();
            var ids = sesiones.Select(s => Guid.Parse(s.GetProperty("id").GetString()!)).Append(manualId).ToList();
            var filas = await db.Set<AsignacionSemanal>().Where(a => ids.Contains(a.SesionId)).ToListAsync();
            Assert.NotEmpty(filas);
            Assert.All(filas, a => Assert.Equal(Guid.Parse(horarioId), a.HorarioId));
        }

        [PostgresFact]
        public async Task NEW3_M16_RellenaElHorarioDeLasFilasExistentes_YElIndiceSeAcotaPorHorario()
        {
            var cadena = PostgresPruebas.NuevaBd();
            try
            {
                await using var db = PostgresPruebas.Contexto(cadena);
                var migrador = db.GetService<IMigrator>();
                await migrador.MigrateAsync("20260928214745_M15_FkAsignacionEspacio");
                await BloqueTiempoSeeder.SeedAsync(db);

                var lunes8 = GrillaInstitucional.GenerarBloques().First(b => b.Dia == DiaDeSemana.Lunes && b.HoraInicio == new TimeOnly(8, 0));
                var fac = new Facultad(Guid.NewGuid(), "FAC");
                var prog = new Programa(Guid.NewGuid(), "PROG", fac.Id);
                var asig = new Asignatura(Guid.NewGuid(), "ASIG", "A1", 2, 1, 0, prog.Id);
                var grupo = new Grupo(Guid.NewGuid(), "G1", asig.Id, 30);
                var aula = new Espacio(Guid.NewGuid(), "AULA", TipoEspacio.Salon, 40);
                Sesion NuevaSesion() => new(Guid.NewGuid(), asig.Id, null, lunes8.Id, null, grupo.Id,
                    TipoAlternancia.SinAlternancia, Modalidad.Presencial, 2m, false, false, TipoFlujo.AulaVirtual);
                var s1 = NuevaSesion();
                var h1 = new SOEA.Domain.Entities.Horario(Guid.NewGuid(), "2026-1", new List<Guid> { s1.Id });
                db.AddRange(fac, prog, asig, aula);
                await db.SaveChangesAsync();
                await InsertarGrupoLegadoAsync(db, grupo, prog.Id);
                db.AddRange(s1, h1);
                await db.SaveChangesAsync();
                await InsertarAsignacionSinHorarioAsync(db, new AsignacionSemanal(Guid.NewGuid(), s1.Id, SemanaAcademica.A, lunes8.Id, aula.Id, Modalidad.Presencial));
                db.ChangeTracker.Clear();

                await migrador.MigrateAsync(); // M16

                Assert.Equal(h1.Id, (await db.Set<AsignacionSemanal>().AsNoTracking().SingleAsync()).HorarioId);

                // Mismo (aula, semana, bloque) en OTRO horario: ahora válido. En el mismo horario: sigue prohibido.
                var s2 = NuevaSesion(); var s3 = NuevaSesion();
                var h2 = new SOEA.Domain.Entities.Horario(Guid.NewGuid(), "2026-2", new List<Guid> { s2.Id, s3.Id });
                db.AddRange(s2, s3, h2);
                await db.SaveChangesAsync();
                AsignacionSemanal EnH2(Sesion s) { var a = new AsignacionSemanal(Guid.NewGuid(), s.Id, SemanaAcademica.A, lunes8.Id, aula.Id, Modalidad.Presencial); a.AsignarHorario(h2.Id); return a; }
                db.Add(EnH2(s2));
                await db.SaveChangesAsync();
                db.Add(EnH2(s3));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
            finally
            {
                await PostgresPruebas.BorrarAsync(cadena);
            }
        }

        /// <summary>Inserta un Grupo con el esquema anterior a M19, que aún tenía programa_id (NOT NULL) y
        /// facultad_id — columnas que el modelo actual ya no mapea.</summary>
        private static Task InsertarGrupoLegadoAsync(DbContext db, Grupo g, Guid programaId, Guid? facultadId = null) =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO "Grupos" (id, nombre, programa_id, asignatura_id, facultad_id, estudiantes_inscritos, alternancia) VALUES ({g.Id}, {g.Nombre}, {programaId}, {g.AsignaturaId}, {facultadId}, {g.EstudiantesInscritos}, {g.Alternancia.ToString()})""");

        /// <summary>Inserta una AsignacionSemanal sin pasar por el modelo actual, para sembrar esquemas anteriores
        /// a la columna <c>horario_id</c> (M16).</summary>
        private static Task InsertarAsignacionSinHorarioAsync(DbContext db, AsignacionSemanal a) =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO "AsignacionesSemanales" (id, sesion_id, semana, bloque_tiempo_id, espacio_id, modalidad) VALUES ({a.Id}, {a.SesionId}, {a.Semana.ToString()}, {a.BloqueTiempoId}, {a.EspacioId}, {a.Modalidad.ToString()})""");

        // ── DB-6 (auditoría pre-producción 2026-09-28) ───────────────────────────

        [PostgresFact]
        public async Task DB6_BorrarFacultadConProgramas_Responde409ConMensajeClaro_YSinProgramasSeBorra()
        {
            var c = await SembrarAsync();

            var r = await _api.Client.DeleteAsync($"/api/facultades/{c.FacultadId}");
            var cuerpo = await r.Content.ReadAsStringAsync();
            Assert.True(r.StatusCode == HttpStatusCode.Conflict, cuerpo); // antes: 204 y el programa quedaba huérfano
            Assert.Contains("programa(s)", cuerpo);

            var creada = await _api.Client.PostAsJsonAsync("/api/facultades", new { nombre = $"VACIA {c.Sufijo}" });
            var id = JsonDocument.Parse(await creada.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();
            var borrada = await _api.Client.DeleteAsync($"/api/facultades/{id}");
            Assert.Equal(HttpStatusCode.NoContent, borrada.StatusCode);
        }

        [PostgresFact]
        public async Task DB6_BorrarProgramaConAsignaturas_Responde409ConMensajeClaro()
        {
            var c = await SembrarAsync();

            var r = await _api.Client.DeleteAsync($"/api/programas/{c.ProgramaId}");
            var cuerpo = await r.Content.ReadAsStringAsync();

            Assert.True(r.StatusCode == HttpStatusCode.Conflict, cuerpo); // antes: 204 (asignaturas huérfanas) o 409 genérico
            Assert.Contains("asignatura(s)", cuerpo);
        }

        // El nombre se comparaba con ILIKE sin escapar: "LAB_1" chocaba con "LABX1" ('_' es comodín).
        [PostgresFact]
        public async Task NombreConGuionBajo_NoChocaComoComodin_YElDuplicadoRealSiResponde409()
        {
            var s = Sufijo();
            var existente = await _api.Client.PostAsJsonAsync("/api/espacios", new { nombre = $"LABX{s}", tipo = "Salón", capacidad = 30 });
            Assert.Equal(HttpStatusCode.Created, existente.StatusCode);

            var conComodin = await _api.Client.PostAsJsonAsync("/api/espacios", new { nombre = $"LAB_{s}", tipo = "Salón", capacidad = 30 });
            Assert.True(conComodin.StatusCode == HttpStatusCode.Created, await conComodin.Content.ReadAsStringAsync());

            var duplicado = await _api.Client.PostAsJsonAsync("/api/espacios", new { nombre = $"labx{s}", tipo = "Salón", capacidad = 30 });
            Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);   // sin distinguir mayúsculas, como antes
        }

        [PostgresFact]
        public async Task DB6_CrearAsignaturaConProgramaInexistente_Responde400ConMensajeClaro()
        {
            var r = await _api.Client.PostAsJsonAsync("/api/asignaturas", new
            {
                nombre = $"HUERFANA {Sufijo()}", codigo = $"H{Sufijo()}",
                sesionesTeoriaPresencialSemana = 1, horasTeoriaPresencial = 2, programaId = Guid.NewGuid()
            });
            var cuerpo = await r.Content.ReadAsStringAsync();

            Assert.True(r.StatusCode == HttpStatusCode.BadRequest, cuerpo); // antes: 201
            Assert.Contains("programa indicado no existe", cuerpo);
        }

        [PostgresFact]
        public async Task CuerpoNoJson_Responde415EnEspanol_YVentanaHorariaIlegible_Responde400()
        {
            var r415 = await _api.Client.PostAsync("/api/grupos", new StringContent("{}", System.Text.Encoding.UTF8, "text/plain"));
            var cuerpo415 = await r415.Content.ReadAsStringAsync();
            Assert.True(r415.StatusCode == HttpStatusCode.UnsupportedMediaType, cuerpo415);
            Assert.Contains("Formato de datos no admitido", cuerpo415);

            // Antes una hora ilegible se guardaba como vacía y la asignatura perdía su ventana sin aviso.
            var c = await SembrarAsync();
            var r400 = await _api.Client.PostAsJsonAsync("/api/asignaturas", new
            {
                nombre = $"VENTANA {Sufijo()}", codigo = $"V{Sufijo()}",
                sesionesTeoriaPresencialSemana = 1, horasTeoriaPresencial = 2, programaId = c.ProgramaId,
                horaInicioMin = "25:99"
            });
            var cuerpo400 = await r400.Content.ReadAsStringAsync();
            Assert.True(r400.StatusCode == HttpStatusCode.BadRequest, cuerpo400);
            Assert.Contains("ventana horaria", cuerpo400);
        }

        [PostgresFact]
        public async Task DB6_M17_SaneaAsignacionesHuerfanas_YLasFkImpidenCrearlasYCascadean()
        {
            var cadena = PostgresPruebas.NuevaBd();
            try
            {
                await using var db = PostgresPruebas.Contexto(cadena);
                var migrador = db.GetService<IMigrator>();
                await migrador.MigrateAsync("20260928221900_M16_AsignacionPorHorario");
                await BloqueTiempoSeeder.SeedAsync(db);

                var lunes8 = GrillaInstitucional.GenerarBloques().First(b => b.Dia == DiaDeSemana.Lunes && b.HoraInicio == new TimeOnly(8, 0));
                var fac = new Facultad(Guid.NewGuid(), "FAC");
                var prog = new Programa(Guid.NewGuid(), "PROG", fac.Id);
                var asig = new Asignatura(Guid.NewGuid(), "ASIG", "A1", 2, 1, 0, prog.Id);
                var grupo = new Grupo(Guid.NewGuid(), "G1", asig.Id, 30);
                Sesion NuevaSesion() => new(Guid.NewGuid(), asig.Id, null, lunes8.Id, null, grupo.Id,
                    TipoAlternancia.SinAlternancia, Modalidad.Presencial, 2m, false, false, TipoFlujo.AulaVirtual);
                var viva = NuevaSesion();
                var sinBloque = NuevaSesion();
                db.AddRange(fac, prog, asig);
                await db.SaveChangesAsync();
                await InsertarGrupoLegadoAsync(db, grupo, prog.Id);
                db.AddRange(viva, sinBloque,
                    new AsignacionSemanal(Guid.NewGuid(), viva.Id, SemanaAcademica.A, lunes8.Id, null, Modalidad.Virtual),
                    new AsignacionSemanal(Guid.NewGuid(), sinBloque.Id, SemanaAcademica.A, Guid.NewGuid(), null, Modalidad.Virtual),   // bloque inexistente
                    new AsignacionSemanal(Guid.NewGuid(), Guid.NewGuid(), SemanaAcademica.A, lunes8.Id, null, Modalidad.Virtual));       // sin sesión
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();

                await migrador.MigrateAsync(); // M17: sin el saneamiento fallaría con 23503

                Assert.Equal(viva.Id, Assert.Single(await db.Set<Sesion>().AsNoTracking().ToListAsync()).Id);
                Assert.Equal(viva.Id, Assert.Single(await db.Set<AsignacionSemanal>().AsNoTracking().ToListAsync()).SesionId);

                // Las FK nuevas rechazan referencias inexistentes...
                async Task Rechaza(params object[] entidades)
                {
                    db.ChangeTracker.Clear();
                    db.AddRange(entidades);
                    await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                    db.ChangeTracker.Clear();
                }
                await Rechaza(new AsignacionSemanal(Guid.NewGuid(), Guid.NewGuid(), SemanaAcademica.B, lunes8.Id, null, Modalidad.Virtual));
                await Rechaza(new AsignacionSemanal(Guid.NewGuid(), viva.Id, SemanaAcademica.B, Guid.NewGuid(), null, Modalidad.Virtual));
                await Rechaza(new Programa(Guid.NewGuid(), "SIN FACULTAD", Guid.NewGuid()));
                await Rechaza(new Asignatura(Guid.NewGuid(), "SIN PROGRAMA", "X1", 2, 1, 0, Guid.NewGuid()));

                // ...y borrar una sesión arrastra sus asignaciones (Cascade).
                await db.Set<Sesion>().Where(s => s.Id == viva.Id).ExecuteDeleteAsync();
                Assert.Empty(await db.Set<AsignacionSemanal>().AsNoTracking().ToListAsync());
            }
            finally
            {
                await PostgresPruebas.BorrarAsync(cadena);
            }
        }

        // ── Jerarquía Sesión → Grupo → Asignatura → Programa → Facultad (M19) ────

        [PostgresFact]
        public async Task M19_SaneaSesionesYGruposFueraDeLaJerarquia_YLaFkCompuestaLaHaceCumplir()
        {
            var cadena = PostgresPruebas.NuevaBd();
            try
            {
                await using var db = PostgresPruebas.Contexto(cadena);
                var migrador = db.GetService<IMigrator>();
                await migrador.MigrateAsync("20260929185029_M18_BloqueSabado13");
                await BloqueTiempoSeeder.SeedAsync(db);

                var lunes8 = GrillaInstitucional.GenerarBloques().First(b => b.Dia == DiaDeSemana.Lunes && b.HoraInicio == new TimeOnly(8, 0));
                var fac = new Facultad(Guid.NewGuid(), "FAC");
                var prog = new Programa(Guid.NewGuid(), "PROG", fac.Id);
                var asigA = new Asignatura(Guid.NewGuid(), "ASIG A", "A1", 2, 1, 0, prog.Id);
                var asigB = new Asignatura(Guid.NewGuid(), "ASIG B", "B1", 2, 1, 0, prog.Id);
                var grupoA = new Grupo(Guid.NewGuid(), "GA", asigA.Id, 30);
                var grupoSinAsig = new Grupo(Guid.NewGuid(), "G0", asigB.Id, 30); // se le quita la asignatura abajo
                db.AddRange(fac, prog, asigA, asigB);
                await db.SaveChangesAsync();
                await InsertarGrupoLegadoAsync(db, grupoA, prog.Id);
                await InsertarGrupoLegadoAsync(db, grupoSinAsig, prog.Id);

                Sesion NuevaSesion(Guid asig, Guid grupo) => new(Guid.NewGuid(), asig, null, lunes8.Id, null, grupo,
                    TipoAlternancia.SinAlternancia, Modalidad.Virtual, 2m, false, false, TipoFlujo.AulaVirtual);
                var ok = NuevaSesion(asigA.Id, grupoA.Id);
                var sinGrupo = NuevaSesion(asigA.Id, grupoA.Id);            // grupo_id NULL abajo: asigA tiene un solo grupo
                var otraAsignatura = NuevaSesion(asigB.Id, grupoA.Id);      // contradice a su grupo
                var deGrupoSinAsig = NuevaSesion(asigB.Id, grupoSinAsig.Id);
                var horario = new SOEA.Domain.Entities.Horario(Guid.NewGuid(), "2026-1",
                    new List<Guid> { ok.Id, sinGrupo.Id, otraAsignatura.Id, deGrupoSinAsig.Id });
                db.AddRange(ok, sinGrupo, otraAsignatura, deGrupoSinAsig, horario);
                await db.SaveChangesAsync();
                await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "Sesiones" SET grupo_id = NULL WHERE id = {sinGrupo.Id}""");
                await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "Grupos" SET asignatura_id = NULL WHERE id = {grupoSinAsig.Id}""");
                db.ChangeTracker.Clear();

                await migrador.MigrateAsync(); // M19: sin el saneamiento, el NOT NULL / la FK compuesta fallarían

                Assert.Equal(grupoA.Id, Assert.Single(await db.Set<Grupo>().AsNoTracking().ToListAsync()).Id);
                var sesiones = await db.Set<Sesion>().AsNoTracking().OrderBy(s => s.Id).ToListAsync();
                Assert.Equal(new[] { ok.Id, sinGrupo.Id }.OrderBy(i => i), sesiones.Select(s => s.Id));
                Assert.All(sesiones, s => Assert.Equal(grupoA.Id, s.GrupoId));
                Assert.Equal(new[] { ok.Id, sinGrupo.Id },
                    (await db.Set<SOEA.Domain.Entities.Horario>().AsNoTracking().SingleAsync()).SesioneIds);

                // La FK compuesta rechaza una sesión de otra asignatura que la de su grupo...
                db.Add(NuevaSesion(asigB.Id, grupoA.Id));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                db.ChangeTracker.Clear();

                // ...y si el grupo cambia de asignatura, sus sesiones cambian con él (ON UPDATE CASCADE).
                var grupo = await db.Set<Grupo>().SingleAsync();
                grupo.ActualizarAsignatura(asigB.Id);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                Assert.All(await db.Set<Sesion>().AsNoTracking().ToListAsync(), s => Assert.Equal(asigB.Id, s.AsignaturaId));
            }
            finally
            {
                await PostgresPruebas.BorrarAsync(cadena);
            }
        }

        // ── NEW-6 (auditoría pre-producción 2026-09-28) ──────────────────────────

        [PostgresFact]
        public async Task NEW6_ImportarFilaSinDocente_CreaElGrupoSinDocente_YLoCuenta()
        {
            var sufijo = Sufijo();
            object[] Fila(string asignatura, string docente) =>
                new object[] { $"FAC {sufijo}", $"PROG {sufijo}", $"{asignatura} {sufijo}", 1, docente, 2, $"AULA {sufijo}", "Lunes", "08:00", "10:00" };
            using var paquete = ExcelHorario(Fila("CON DOCENTE", $"DOCENTE {sufijo}"), Fila("SIN DOCENTE", ""));

            using var form = new MultipartFormDataContent { { new ByteArrayContent(paquete.GetAsByteArray()), "archivo", "horario.xlsx" } };
            var r = await ImportarAsync(form);
            var cuerpo = await r.Content.ReadAsStringAsync();
            Assert.True(r.StatusCode == HttpStatusCode.OK, cuerpo);

            var stats = JsonDocument.Parse(cuerpo).RootElement;
            Assert.Equal(2, stats.GetProperty("gruposCreados").GetInt32());     // antes: 1 (la fila sin docente se descartaba)
            Assert.Equal(1, stats.GetProperty("gruposSinDocente").GetInt32());  // antes: 0

            await using var db = _api.Db();
            var asig = await db.Set<Asignatura>().SingleAsync(a => a.Nombre.ToUpper() == $"SIN DOCENTE {sufijo}".ToUpper());
            Assert.Null((await db.Set<Grupo>().SingleAsync(g => g.AsignaturaId == asig.Id)).DocenteId);
        }

        /// <summary>Import en dos pasos (revisar → importar las filas tal cual), como hace la UI.</summary>
        private async Task<HttpResponseMessage> ImportarAsync(MultipartFormDataContent form)
        {
            var revision = await _api.Client.PostAsync("/api/import/excel/revisar", form);
            if (!revision.IsSuccessStatusCode) return revision;
            var filas = JsonDocument.Parse(await revision.Content.ReadAsStringAsync()).RootElement.GetProperty("filas").GetRawText();
            return await _api.Client.PostAsync("/api/import/filas", new StringContent(filas, System.Text.Encoding.UTF8, "application/json"));
        }

        private static ExcelPackage ExcelHorario(params object[][] filas)
        {
            ExcelPackage.License.SetNonCommercialPersonal("SOEA");
            var paquete = new ExcelPackage();
            var hoja = paquete.Workbook.Worksheets.Add("Horario");
            object[] cabecera = { "Facultad", "Programa", "Asignatura", "Grupo", "Docente", "Reales [h]", "Espacio", "Dia", "Hora", "Final" };
            for (int c = 0; c < cabecera.Length; c++) hoja.Cells[1, c + 1].Value = cabecera[c];
            for (int f = 0; f < filas.Length; f++)
                for (int c = 0; c < cabecera.Length; c++) hoja.Cells[f + 2, c + 1].Value = filas[f][c];
            return paquete;
        }

        // ── L-3 (auditoría pre-producción 2026-09-28) ────────────────────────────

        [PostgresFact]
        public async Task L3_ImportarExcel_GuardaLasClavesDeDiaEnMinusculaYSinTilde()
        {
            var sufijo = Sufijo();
            ExcelPackage.License.SetNonCommercialPersonal("SOEA");
            using var paquete = new ExcelPackage();
            var hoja = paquete.Workbook.Worksheets.Add("Horario");
            object[] cabecera = { "Facultad", "Programa", "Asignatura", "Grupo", "Docente", "Reales [h]", "Espacio", "Dia", "Hora", "Final" };
            object[][] filas =
            {
                new object[] { $"FAC {sufijo}", $"PROG {sufijo}", $"ASIG {sufijo}", 1, $"DOCENTE {sufijo}", 2, $"AULA {sufijo}", "Miércoles", "08:00", "10:00" },
                new object[] { $"FAC {sufijo}", $"PROG {sufijo}", $"ASIG {sufijo}", 1, $"DOCENTE {sufijo}", 2, $"AULA {sufijo}", "Sábado", "07:00", "09:00" },
            };
            for (int c = 0; c < cabecera.Length; c++) hoja.Cells[1, c + 1].Value = cabecera[c];
            for (int f = 0; f < filas.Length; f++)
                for (int c = 0; c < cabecera.Length; c++) hoja.Cells[f + 2, c + 1].Value = filas[f][c];

            using var form = new MultipartFormDataContent { { new ByteArrayContent(paquete.GetAsByteArray()), "archivo", "horario.xlsx" } };
            var r = await ImportarAsync(form);
            Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());

            await using var db = _api.Db();
            var asig = await db.Set<Asignatura>().SingleAsync(a => a.Nombre.ToUpper() == $"ASIG {sufijo}".ToUpper());
            var grupo = await db.Set<Grupo>().SingleAsync(g => g.AsignaturaId == asig.Id);
            var claves = JsonDocument.Parse(grupo.DisponibilidadUiJson!).RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToList();
            Assert.Equal(new[] { "miercoles", "sabado" }, claves); // antes: "Miercoles", "Sábado"
        }

        // ── L-2 (auditoría pre-producción 2026-09-28) ────────────────────────────

        [PostgresFact]
        public async Task L2_AsignarDocenteFueraDeSuDisponibilidad_DevuelveAdvertencia()
        {
            var (sesionId, advertencias) = await AsignarDocenteConDisponibilidadAsync(disponibleEnLaSesion: false);

            Assert.Contains(advertencias, a => a.Contains("disponibilidad")); // antes: [] siempre (FindAsync sin Include)
        }

        [PostgresFact]
        public async Task L2_AsignarDocenteDisponibleEnLaFranja_NoAdvierte()
        {
            var (_, advertencias) = await AsignarDocenteConDisponibilidadAsync(disponibleEnLaSesion: true);

            Assert.DoesNotContain(advertencias, a => a.Contains("disponibilidad"));
        }

        /// <summary>Genera un horario, crea un docente con bloques disponibles (los de la primera sesión o
        /// los de otro día) y se lo asigna por el API.</summary>
        private async Task<(string sesionId, List<string> advertencias)> AsignarDocenteConDisponibilidadAsync(bool disponibleEnLaSesion)
        {
            var c = await SembrarAsync();
            var (_, sesiones) = await GenerarOkAsync(c, $"IT-{c.Sufijo}");
            var sesionId = sesiones[0].GetProperty("id").GetString()!;

            var docenteId = Guid.NewGuid();
            await using (var db = _api.Db())
            {
                var sesion = await db.Set<Sesion>().SingleAsync(s => s.Id == Guid.Parse(sesionId));
                var bloqueSesion = await db.Set<BloqueTiempo>().SingleAsync(b => b.Id == sesion.BloqueTiempoId);
                var todos = await db.Set<BloqueTiempo>().ToListAsync();
                var docente = new Docente(docenteId, $"DOC {c.Sufijo}", "", $"{docenteId:N}@soea.test", 40m,
                    new List<FranjaHoraria> { FranjaHoraria.Matutino });
                var horas = (int)Math.Ceiling(sesion.DuracionHoras);
                foreach (var b in todos.Where(b => disponibleEnLaSesion
                             ? b.Dia == bloqueSesion.Dia && b.HoraInicio >= bloqueSesion.HoraInicio && b.HoraInicio < bloqueSesion.HoraInicio.AddHours(horas)
                             : b.Dia != bloqueSesion.Dia && b.HoraInicio == new TimeOnly(8, 0)))
                    docente.AgregarBloqueDisponibilidad(b);
                Assert.NotEmpty(docente.BloquesDisponibles);
                db.Add(docente);
                await db.SaveChangesAsync();
            }

            using var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/sesiones/{sesionId}/docente")
            {
                Content = JsonContent.Create(new { docenteId })
            };
            var r = await _api.Client.SendAsync(req);
            var cuerpo = await r.Content.ReadAsStringAsync();
            Assert.True(r.StatusCode == HttpStatusCode.OK, cuerpo);
            var advertencias = JsonDocument.Parse(cuerpo).RootElement.GetProperty("advertencias")
                .EnumerateArray().Select(e => e.GetString()!).ToList();
            return (sesionId, advertencias);
        }

        // ── SEC-2 (auditoría pre-producción 2026-09-28) ──────────────────────────

        [PostgresFact]
        public async Task SEC2_ConfiguracionDelGaSinTope_Responde400_EnVezDeOcuparLaCpu()
        {
            var c = await SembrarAsync();
            var r = await GenerarAsync(c, $"IT-{c.Sufijo}",
                configuracion: new { maxGeneraciones = 2_000_000_000, umbralConvergencia = 2_000_000_000 });
            var cuerpo = await r.Content.ReadAsStringAsync();

            Assert.True(r.StatusCode == HttpStatusCode.BadRequest, cuerpo); // antes: sin respuesta mientras el cliente esperara
            Assert.Contains("MaxGeneraciones", cuerpo);
        }

        [PostgresFact]
        public async Task SEC2_SegundaGeneracionSimultanea_Responde429_YLaPrimeraTerminaBien()
        {
            var c = await SembrarAsync();
            var otro = await SembrarAsync();

            // Se retiene un candado sobre Horarios para que la primera generación quede en vuelo (bloqueada
            // al persistir) sin depender de tiempos; se libera al final.
            await using var db = _api.Db();
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE \"Horarios\" IN ACCESS EXCLUSIVE MODE");

            var primera = GenerarAsync(c, $"IT-{c.Sufijo}");
            for (var i = 0; i < 200; i++)
            {
                var esperando = await db.Database.SqlQueryRaw<int>(
                    "SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE wait_event_type = 'Lock'").SingleAsync();
                if (esperando > 0) break;
                await Task.Delay(50);
            }
            Assert.False(primera.IsCompleted, "la primera generación debía seguir en vuelo");

            var segunda = await GenerarAsync(otro, $"IT-{otro.Sufijo}");
            var cuerpo = await segunda.Content.ReadAsStringAsync();
            Assert.True(segunda.StatusCode == HttpStatusCode.TooManyRequests, cuerpo); // antes: 200 y luego un 409 genérico
            Assert.Contains("generándose", cuerpo);

            await tx.RollbackAsync();
            Assert.Equal(HttpStatusCode.OK, (await primera).StatusCode);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private sealed record Catalogo(Guid AsignaturaId, Guid GrupoId, Guid EspacioId, string Sufijo,
            Guid FacultadId = default, Guid ProgramaId = default);

        private static string Sufijo() => Guid.NewGuid().ToString("N")[..8];

        /// <summary>Asignatura de teoría presencial (2 sesiones de 2 h), su grupo de 30 y un salón de 40.</summary>
        private async Task<Catalogo> SembrarAsync()
        {
            var sufijo = Sufijo();
            var fac = new Facultad(Guid.NewGuid(), $"FAC {sufijo}");
            var prog = new Programa(Guid.NewGuid(), $"PROG {sufijo}", fac.Id);
            var asig = new Asignatura(Guid.NewGuid(), $"ASIG {sufijo}", $"C{sufijo}", 2, 2, 0, prog.Id);
            var grupo = new Grupo(Guid.NewGuid(), "G1", asig.Id, 30);
            var espacio = new Espacio(Guid.NewGuid(), $"SALON {sufijo}", TipoEspacio.Salon, 40);

            await using var db = _api.Db();
            db.AddRange(fac, prog, asig, grupo, espacio);
            await db.SaveChangesAsync();
            return new Catalogo(asig.Id, grupo.Id, espacio.Id, sufijo, fac.Id, prog.Id);
        }

        private Task<HttpResponseMessage> GenerarAsync(Catalogo c, string semestre, object[]? fijas = null, object? configuracion = null) =>
            _api.Client.PostAsJsonAsync("/api/horario/generar", new
            {
                semestre,
                asignaturas = new[] { new { id = c.AsignaturaId, nombre = "ASIG", sesionesTeoriaPresencialSemana = 2, horasTeoriaPresencial = 2 } },
                espacios = new[] { new { id = c.EspacioId, nombre = "SALON", capacidad = 40, tipo = "Salon" } },
                grupos = new[] { new { id = c.GrupoId, nombre = "G1", asignaturaId = c.AsignaturaId, estudiantesInscritos = 30 } },
                sesionesFijas = fijas,
                configuracion
            });

        private async Task<(string horarioId, List<JsonElement> sesiones)> GenerarOkAsync(Catalogo c, string semestre)
        {
            var r = await GenerarAsync(c, semestre);
            var cuerpo = await r.Content.ReadAsStringAsync();
            Assert.True(r.StatusCode == HttpStatusCode.OK, cuerpo);
            var raiz = JsonDocument.Parse(cuerpo).RootElement;
            return (raiz.GetProperty("horarioId").GetString()!, raiz.GetProperty("sesiones").EnumerateArray().ToList());
        }

        private Task<HttpResponseMessage> CrearSesionManualAsync(string horarioId, Catalogo c, string espacioId, string dia, string horaInicio) =>
            _api.Client.PostAsJsonAsync("/api/horario/sesion-manual", new
            {
                horarioId, asignaturaId = c.AsignaturaId, grupoId = c.GrupoId, espacioId, dia, horaInicio,
                duracionHoras = 2, alternancia = "SinAlternancia", tipoFlujo = "AulaVirtual", esVirtual = false
            });
    }
}
