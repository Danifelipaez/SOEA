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
                var grupo = new Grupo(Guid.NewGuid(), "G1", prog.Id, 30, asignaturaId: asig.Id, facultadId: Guid.NewGuid());

                Sesion NuevaSesion(Guid asignaturaId, Guid? espacioId) => new(Guid.NewGuid(), asignaturaId, null, lunes8.Id,
                    espacioId, grupo.Id, TipoAlternancia.SinAlternancia, Modalidad.Presencial, 2m, false, false, TipoFlujo.AulaVirtual);
                // Viva: aula inexistente y el bloque de Fase 1 (lunes) distinto al de su asignación (martes).
                var viva = NuevaSesion(asig.Id, espacioId: Guid.NewGuid());
                var asignacionViva = new AsignacionSemanal(Guid.NewGuid(), viva.Id, SemanaAcademica.A, martes10.Id, null, Modalidad.Presencial);
                var fantasma = NuevaSesion(asig.Id, espacioId: null);       // fila del import: fuera de todo horario
                var rota = NuevaSesion(Guid.NewGuid(), espacioId: null);    // asignatura inexistente
                var horario = new SOEA.Domain.Entities.Horario(Guid.NewGuid(), "2026-1", new List<Guid> { viva.Id, rota.Id });

                db.AddRange(fac, prog, asig, grupo, viva, asignacionViva, fantasma, rota, horario);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();

                await migrador.MigrateAsync(); // M14: antes 23503 FK_Grupos_Facultades_facultad_id

                Assert.Null((await db.Set<Grupo>().AsNoTracking().SingleAsync()).FacultadId);
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
            var r = await _api.Client.PostAsync("/api/import/excel", form);
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

        // ── Helpers ──────────────────────────────────────────────────────────────

        private sealed record Catalogo(Guid AsignaturaId, Guid GrupoId, Guid EspacioId, string Sufijo);

        private static string Sufijo() => Guid.NewGuid().ToString("N")[..8];

        /// <summary>Asignatura de teoría presencial (2 sesiones de 2 h), su grupo de 30 y un salón de 40.</summary>
        private async Task<Catalogo> SembrarAsync()
        {
            var sufijo = Sufijo();
            var fac = new Facultad(Guid.NewGuid(), $"FAC {sufijo}");
            var prog = new Programa(Guid.NewGuid(), $"PROG {sufijo}", fac.Id);
            var asig = new Asignatura(Guid.NewGuid(), $"ASIG {sufijo}", $"C{sufijo}", 2, 2, 0, prog.Id);
            var grupo = new Grupo(Guid.NewGuid(), "G1", prog.Id, 30, asignaturaId: asig.Id, facultadId: fac.Id);
            var espacio = new Espacio(Guid.NewGuid(), $"SALON {sufijo}", TipoEspacio.Salon, 40);

            await using var db = _api.Db();
            db.AddRange(fac, prog, asig, grupo, espacio);
            await db.SaveChangesAsync();
            return new Catalogo(asig.Id, grupo.Id, espacio.Id, sufijo);
        }

        private Task<HttpResponseMessage> GenerarAsync(Catalogo c, string semestre, object[]? fijas = null) =>
            _api.Client.PostAsJsonAsync("/api/horario/generar", new
            {
                semestre,
                asignaturas = new[] { new { id = c.AsignaturaId, nombre = "ASIG", sesionesTeoriaPresencialSemana = 2, horasTeoriaPresencial = 2 } },
                espacios = new[] { new { id = c.EspacioId, nombre = "SALON", capacidad = 40, tipo = "Salon" } },
                grupos = new[] { new { id = c.GrupoId, nombre = "G1", asignaturaId = c.AsignaturaId, estudiantesInscritos = 30 } },
                sesionesFijas = fijas
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
