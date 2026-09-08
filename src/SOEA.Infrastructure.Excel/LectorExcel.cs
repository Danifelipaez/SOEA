using OfficeOpenXml;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SOEA.Infrastructure.Excel
{
    /// <summary>
    /// Implementación del lector de Excel para SOEA.
    /// Formato de entrada del horario existente — columnas detectadas por cabecera (row 1),
    /// no por posición fija; acepta variantes vistas en archivos reales, por ejemplo:
    ///   Facultad | Programa | Asignatura | Código (opcional) | Tipo de Espacio (opcional)
    ///   Espacio/Curso/Salón/Aula (nombre específico) | Duración/Horas/Reales [h] | Día | Hora
    ///   Docente | Grupo (opcional, número de grupo/sección real) | Final (opcional, hora de fin)
    /// Si "Grupo" viene en el archivo, se usa como número real de grupo (y como Grupo.Codigo) en
    /// vez de numerar secuencialmente. Si "Final" viene, se usa como hora de fin real de la sesión
    /// en vez de derivarla de Hora + Duración.
    /// </summary>
    public class LectorExcel : ILectorExcel
    {
        private readonly ILogger<LectorExcel> _logger;

        public LectorExcel(ILogger<LectorExcel> logger)
        {
            _logger = logger;
        }

        public async Task<CurriculumExcelResult> LeerCurriculumAsync(
            Stream excelStream,
            IReadOnlyDictionary<(DiaDeSemana Dia, TimeOnly HoraInicio), BloqueTiempo>? catalogoBloques = null)
        {
            _logger.LogInformation("Iniciando lectura del currículum desde archivo Excel.");

            using var paquete = new ExcelPackage();
            await paquete.LoadAsync(excelStream);

            var hoja = paquete.Workbook.Worksheets[0];
            var totalFilas = hoja.Dimension?.Rows ?? 0;
            if (totalFilas < 2)
            {
                _logger.LogWarning("El archivo Excel no tiene filas de datos.");
                return new CurriculumExcelResult(
                    Array.Empty<Facultad>(), Array.Empty<Programa>(), Array.Empty<Asignatura>(),
                    Array.Empty<Docente>(), Array.Empty<Sesion>(), Array.Empty<Espacio>(),
                    Array.Empty<Grupo>(), new[] { "El archivo no tiene filas de datos." });
            }

            // ── 1. Detectar columnas por cabecera (row 1) ─────────────────────────────
            var colIdx = DetectarColumnas(hoja, totalFilas);
            _logger.LogDebug("Columnas detectadas: {Cols}", string.Join(", ", colIdx.Select(kv => $"{kv.Key}={kv.Value}")));

            // Columnas mínimas requeridas
            int cFacultad   = ColReq(colIdx, "facultad",   1);
            int cPrograma   = ColReq(colIdx, "programa",   2);
            int cAsignatura = ColReq(colIdx, new[] { "asignatura", "nombre" }, 3);
            // Sin fallback posicional: la posición 4 es "Grupo" en el formato real de Rosa, no
            // "Código" — adivinar por posición aquí corrompía Asignatura.Codigo con el número de
            // grupo (colisión de unicidad codigo+programa entre asignaturas distintas del mismo
            // programa que comparten número de grupo). Sin columna "Código" reconocible, el
            // constructor de Asignatura ya genera un código dummy único — preferible a adivinar mal.
            int cCodigo     = ColOpt(colIdx, new[] { "codigo", "código" }, -1);
            int cTipoEsp    = ColOpt(colIdx, new[] { "tipo_espacio", "tipo espacio", "tipoespacio" }, 5);
            int cEspNombre  = ColOpt(colIdx, new[] { "espacio", "curso", "espacio_especifico", "espacio especifico", "salon", "salón", "aula" }, 6);
            int cDuracion   = ColOpt(colIdx, new[] { "duracion", "duración", "duracion h", "duracion [h]", "duración [h]", "horas", "reales", "reales h" }, 7);
            int cDia        = ColOpt(colIdx, new[] { "dia", "día", "day" }, 8);
            int cHora       = ColOpt(colIdx, new[] { "hora", "horario", "time" }, 9);
            int cDocente    = ColOpt(colIdx, new[] { "docente", "profesor", "teacher" }, 10);
            // Columnas opcionales sin posición histórica: sin fallback (-1 = ausente).
            int cGrupo      = ColOpt(colIdx, new[] { "grupo", "seccion", "sección" }, -1);
            int cHoraFin    = ColOpt(colIdx, new[] { "final", "hora fin", "hora final", "horafin", "fin" }, -1);

            // Discriminador de conteo: por número de grupo cuando el Excel lo trae (cada grupo
            // cuenta SUS PROPIAS filas → su propio número real de reuniones semanales), o por
            // docente si no (formato viejo). Sin esto, un docente que dicta VARIAS secciones de la
            // misma asignatura (una fila por sección, cada una reuniéndose una vez/semana) inflaba
            // sesionesPorSemana al número de secciones que dicta, no al de reuniones de cada una.
            string DiscriminadorConteo(int fila)
            {
                var txtG = Celda(hoja, fila, cGrupo);
                if (!string.IsNullOrWhiteSpace(txtG) && int.TryParse(txtG, out var gnum)) return $"N{gnum}";
                return NormalizadorTexto.Normalizar(Celda(hoja, fila, cDocente));
            }

            // ── 2. Pre-pass: contar sesiones por (asig_norm, prog_texto, discriminador) ─
            // Esto nos da el sesionesPorSemana real sin hardcodear 2.
            var conteoGrupos = new Dictionary<(string AsigNorm, string ProgTexto, string Discriminador), int>();
            for (int fila = 2; fila <= totalFilas; fila++)
            {
                var fa = Celda(hoja, fila, cFacultad);
                var pr = Celda(hoja, fila, cPrograma);
                var as_ = Celda(hoja, fila, cAsignatura);
                if (string.IsNullOrWhiteSpace(fa) || string.IsNullOrWhiteSpace(pr) || string.IsNullOrWhiteSpace(as_)) continue;

                var clave = (NormalizadorTexto.Normalizar(as_), NormalizadorTexto.Normalizar(pr), DiscriminadorConteo(fila));
                conteoGrupos[clave] = conteoGrupos.TryGetValue(clave, out int cnt) ? cnt + 1 : 1;
            }

            // ── 3. Main pass: construir entidades ─────────────────────────────────────
            var facultadesDict  = new Dictionary<string, Facultad>(StringComparer.OrdinalIgnoreCase);
            var programasDict   = new Dictionary<string, Programa>(StringComparer.OrdinalIgnoreCase);
            // Clave: (asig_norm, programaId) → Asignatura ÚNICA (el docente NO es parte de la clave;
            // la misma asignatura la dictan docentes distintos, diferenciados por Grupo).
            var asignaturasDict = new Dictionary<(string AsigNorm, Guid ProgramaId), Asignatura>();
            // Clave: docente_norm → Docente
            var docentesDict    = new Dictionary<string, Docente>(StringComparer.OrdinalIgnoreCase);
            var espaciosDict    = new Dictionary<string, Espacio>(StringComparer.OrdinalIgnoreCase);
            var sesionesPredefinidas = new List<Sesion>();
            var grupos          = new List<Grupo>();
            // Clave: (asig_norm, programaId, discriminador) → Grupo. El discriminador es el número
            // real de grupo ("N{numero}") cuando el Excel lo trae, o el docente normalizado si no
            // (formato viejo, sin columna Grupo).
            var gruposDict      = new Dictionary<(string AsigNorm, Guid ProgramaId, string Discriminador), Grupo>();
            // conteo de grupos por (asig_norm, programaId) para numerar secuencialmente
            var gruposContador  = new Dictionary<(string AsigNorm, Guid ProgramaId), int>();
            var advertencias    = new List<string>();

            for (int fila = 2; fila <= totalFilas; fila++)
            {
                var txtFacultad   = Celda(hoja, fila, cFacultad);
                var txtPrograma   = Celda(hoja, fila, cPrograma);
                var txtAsignatura = Celda(hoja, fila, cAsignatura);

                if (string.IsNullOrWhiteSpace(txtFacultad) || string.IsNullOrWhiteSpace(txtPrograma) || string.IsNullOrWhiteSpace(txtAsignatura))
                {
                    advertencias.Add($"Fila {fila}: datos incompletos (Facultad/Programa/Asignatura vacíos), omitida.");
                    _logger.LogWarning("Fila {Fila}: datos incompletos, se omite.", fila);
                    continue;
                }

                var txtCodigo        = Celda(hoja, fila, cCodigo);
                var txtTipoEspacio   = Celda(hoja, fila, cTipoEsp);
                var txtEspNombre     = Celda(hoja, fila, cEspNombre);
                var txtDuracion      = Celda(hoja, fila, cDuracion);
                var txtDia           = Celda(hoja, fila, cDia);
                var txtHora          = Celda(hoja, fila, cHora);
                var txtHoraFin       = Celda(hoja, fila, cHoraFin);
                var txtDocente       = Celda(hoja, fila, cDocente);
                var txtGrupo         = Celda(hoja, fila, cGrupo);
                var numeroGrupoExplicito = int.TryParse(txtGrupo, out var ngExp) ? ngExp : (int?)null;
                var docenteNorm      = NormalizadorTexto.Normalizar(txtDocente);
                var asignaturaNorm   = NormalizadorTexto.Normalizar(txtAsignatura);

                // Facultad
                if (!facultadesDict.TryGetValue(txtFacultad, out var facultad))
                {
                    // Verificación pre-deploy (2026-09-08): un typo real en el Excel ("INGENIERA" en
                    // vez de "INGENIERIA") no lo detecta el match case-insensitive de arriba y crea
                    // una facultad duplicada en silencio. No se fusiona automáticamente (unir dos
                    // facultades realmente distintas sería peor que el duplicado, mismo criterio que
                    // DetectorDocentesDuplicados) — solo se avisa para que Rosa lo revise.
                    var similar = facultadesDict.Keys.FirstOrDefault(existente => EsPosibleTypo(existente, txtFacultad));
                    if (similar != null)
                        advertencias.Add($"Fila {fila}: la facultad '{txtFacultad}' es muy similar a '{similar}', ya registrada — revisar si es un typo antes de tratarlas como distintas.");

                    facultad = new Facultad(Guid.NewGuid(), txtFacultad);
                    facultadesDict[txtFacultad] = facultad;
                }

                // Programa
                var clavePrograma = $"{NormalizadorTexto.Normalizar(txtFacultad)}|{NormalizadorTexto.Normalizar(txtPrograma)}";
                if (!programasDict.TryGetValue(clavePrograma, out var programa))
                {
                    programa = new Programa(Guid.NewGuid(), txtPrograma, facultad.Id);
                    programasDict[clavePrograma] = programa;
                }

                // Duración
                int duracion = int.TryParse(txtDuracion, out var d) && d > 0 ? d : 2;
                if (string.IsNullOrWhiteSpace(txtDuracion) && !string.IsNullOrWhiteSpace(txtHora) &&
                    (txtHora.Contains('-') || txtHora.Contains('–') || txtHora.Contains('—')))
                {
                    if (TryParseRangoHora(txtHora, 2, out var hIni, out var hFin))
                    {
                        var dif = (hFin - hIni).TotalHours;
                        if (dif > 0) duracion = (int)Math.Round(dif);
                    }
                }

                // SesionesPorSemana: tomado del pre-pass
                var claveConteo = (asignaturaNorm, NormalizadorTexto.Normalizar(txtPrograma), DiscriminadorConteo(fila));
                int sesionesSemana = conteoGrupos.TryGetValue(claveConteo, out int cnt2) ? cnt2 : 1;

                // Asignatura: ÚNICA por (asig_norm, programaId) — el docente ya no la diferencia.
                var claveAsig = (asignaturaNorm, programa.Id);
                if (!asignaturasDict.TryGetValue(claveAsig, out var asignatura))
                {
                    asignatura = new Asignatura(
                        id: Guid.NewGuid(),
                        nombre: txtAsignatura,
                        codigo: txtCodigo,
                        horasPorSesion: duracion,
                        sesionesPorSemana: sesionesSemana,
                        sesionesLaboratorioSemestre: 0,
                        programaId: programa.Id
                    );
                    asignaturasDict[claveAsig] = asignatura;
                }

                // Espacio
                Espacio? espacioAsignado = null;
                if (!string.IsNullOrWhiteSpace(txtEspNombre))
                {
                    var espNorm = NormalizadorTexto.Normalizar(txtEspNombre);
                    if (!espaciosDict.TryGetValue(espNorm, out var espacio))
                    {
                        var tipo = txtTipoEspacio.Contains("Laboratorio", StringComparison.OrdinalIgnoreCase)
                            ? TipoEspacio.Laboratorio : TipoEspacio.Salon;
                        espacio = new Espacio(Guid.NewGuid(), txtEspNombre, tipo, 30, "", null);
                        espaciosDict[espNorm] = espacio;
                    }
                    espacioAsignado = espacio;
                }

                // Docente
                if (!string.IsNullOrWhiteSpace(txtDocente))
                {
                    if (!docentesDict.TryGetValue(docenteNorm, out var docente))
                    {
                        docente = new Docente(
                            Guid.NewGuid(), txtDocente, "", "", 40m,
                            new List<FranjaHoraria> { FranjaHoraria.Matutino });
                        docentesDict[docenteNorm] = docente;
                    }

                    // Grupo: uno por (asignatura, programa, grupo/docente). El docente vive en el
                    // GRUPO, no en la asignatura (la misma asignatura la dictan docentes distintos).
                    // Si el Excel trae un número de grupo real, es la clave/nombre autoritativa;
                    // si no (formato viejo), se sigue numerando secuencialmente por docente.
                    var discriminadorGrupo = numeroGrupoExplicito.HasValue ? $"N{numeroGrupoExplicito}" : docenteNorm;
                    var claveGrupo = (asignaturaNorm, programa.Id, discriminadorGrupo);
                    if (!gruposDict.TryGetValue(claveGrupo, out var grupoDocente))
                    {
                        int numGrupoMostrado;
                        if (numeroGrupoExplicito.HasValue)
                        {
                            numGrupoMostrado = numeroGrupoExplicito.Value;
                        }
                        else
                        {
                            var grupoKey = (asignaturaNorm, programa.Id);
                            gruposContador.TryGetValue(grupoKey, out int numGrupoActual);
                            numGrupoActual++;
                            gruposContador[grupoKey] = numGrupoActual;
                            numGrupoMostrado = numGrupoActual;
                        }

                        var nombreGrupo = $"{txtAsignatura} - Grupo {numGrupoMostrado}";
                        grupoDocente = new Grupo(
                            Guid.NewGuid(), nombreGrupo, programa.Id, 30, asignatura.Alternancia,
                            codigo: numeroGrupoExplicito?.ToString(),
                            asignaturaId: asignatura.Id, facultadId: facultad.Id, docenteId: docente.Id);
                        grupos.Add(grupoDocente);
                        gruposDict[claveGrupo] = grupoDocente;
                    }

                    // Disponibilidad del docente: expandir rango en bloques de 1h
                    Guid bloqueIdParaSesion = Guid.Empty;
                    if (!string.IsNullOrWhiteSpace(txtDia) && !string.IsNullOrWhiteSpace(txtHora))
                    {
                        // Si hay columna "Final" con hora de fin explícita, es autoritativa sobre
                        // el rango derivado de Hora + Duración.
                        TimeOnly horaIniParaBloque, horaFinParaBloque;
                        bool horaOk;
                        if (!string.IsNullOrWhiteSpace(txtHoraFin) &&
                            TryParseHoraUnica(txtHora, out var horaIniExplicita) &&
                            TryParseHoraUnica(txtHoraFin, out var horaFinExplicita))
                        {
                            horaIniParaBloque = horaIniExplicita;
                            horaFinParaBloque = horaFinExplicita;
                            horaOk = true;
                        }
                        else
                        {
                            horaOk = TryParseRangoHora(txtHora, duracion, out horaIniParaBloque, out horaFinParaBloque);
                        }

                        if (TryParseDia(txtDia, out var diaSemana) && horaOk)
                        {
                            var horaIni = horaIniParaBloque;
                            var horaFin = horaFinParaBloque;
                            // Expandir rango en slots de 1h y agregar a disponibilidad del docente
                            var horaActual = horaIni;
                            bool esPrimerBloque = true;
                            while (horaActual < horaFin)
                            {
                                var horaNext = horaActual.AddHours(1);
                                var bloqueKey = (diaSemana, horaActual);
                                BloqueTiempo? bloque;

                                if (catalogoBloques != null && catalogoBloques.TryGetValue(bloqueKey, out var bloqueSeeded))
                                {
                                    bloque = bloqueSeeded;
                                }
                                else
                                {
                                    bloque = new BloqueTiempo(Guid.NewGuid(), diaSemana, horaActual, horaNext);
                                }

                                docente.AgregarBloqueDisponibilidad(bloque);

                                if (esPrimerBloque)
                                {
                                    bloqueIdParaSesion = bloque.Id;
                                    esPrimerBloque = false;
                                }

                                horaActual = horaNext;
                            }
                        }
                        else
                        {
                            advertencias.Add($"Fila {fila}: no se pudo parsear Día='{txtDia}' Hora='{txtHora}'. Sesión sin bloque asignado.");
                        }
                    }
                    else
                    {
                        advertencias.Add($"Fila {fila}: docente '{txtDocente}' sin Día/Hora. Sesión creada sin bloque.");
                    }

                    // Sesión predefinida (solo una por fila, referencia el bloque de inicio del slot)
                    var sesion = new Sesion(
                        id: Guid.NewGuid(),
                        asignaturaId: asignatura.Id,
                        docenteId: docente.Id,
                        bloqueId: bloqueIdParaSesion,
                        espacioId: espacioAsignado?.Id,
                        grupoId: grupoDocente.Id,
                        alternancia: asignatura.Alternancia,
                        modalidad: Modalidad.Presencial,
                        duracionHoras: duracion,
                        esBloque: false,
                        estaDividida: false
                    );
                    sesionesPredefinidas.Add(sesion);
                }
                else
                {
                    advertencias.Add($"Fila {fila}: asignatura '{txtAsignatura}' sin docente asignado.");
                }
            }

            var resultado = new CurriculumExcelResult(
                facultades:  facultadesDict.Values.ToList().AsReadOnly(),
                programas:   programasDict.Values.ToList().AsReadOnly(),
                asignaturas: asignaturasDict.Values.ToList().AsReadOnly(),
                docentes:    docentesDict.Values.ToList().AsReadOnly(),
                sesionesPredefinidas: sesionesPredefinidas.AsReadOnly(),
                espacios: espaciosDict.Values.ToList().AsReadOnly(),
                grupos: grupos.AsReadOnly(),
                advertencias: advertencias.AsReadOnly()
            );

            _logger.LogInformation(
                "Lectura finalizada. Facultades:{F} Programas:{P} Asignaturas:{A} Docentes:{D} Sesiones:{S} Espacios:{E} Advertencias:{W}.",
                resultado.Facultades.Count, resultado.Programas.Count, resultado.Asignaturas.Count,
                resultado.Docentes.Count, resultado.SesionesPredefinidas.Count,
                resultado.Espacios.Count, resultado.Advertencias.Count);

            return resultado;
        }

        // ── Helpers de detección y acceso a celdas ────────────────────────────────

        private static Dictionary<string, int> DetectarColumnas(ExcelWorksheet hoja, int totalFilas)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (totalFilas < 1) return result;

            var totalCols = hoja.Dimension?.Columns ?? 0;
            for (int col = 1; col <= totalCols; col++)
            {
                var header = hoja.Cells[1, col].Text.Trim();
                if (!string.IsNullOrWhiteSpace(header))
                {
                    var norm = NormalizadorTexto.Normalizar(header)
                                .Replace("[", "").Replace("]", "").TrimEnd();
                    result[norm] = col;
                    // También indexar sin espacios para mayor tolerancia
                    var sinEspacios = norm.Replace(" ", "_");
                    if (!result.ContainsKey(sinEspacios)) result[sinEspacios] = col;
                }
            }
            return result;
        }

        private static int ColReq(Dictionary<string, int> idx, string clave, int fallback)
            => idx.TryGetValue(clave, out int c) ? c : fallback;

        private static int ColReq(Dictionary<string, int> idx, string[] claves, int fallback)
        {
            foreach (var k in claves)
                if (idx.TryGetValue(k, out int c)) return c;
            return fallback;
        }

        private static int ColOpt(Dictionary<string, int> idx, string[] claves, int fallback)
        {
            foreach (var k in claves)
                if (idx.TryGetValue(k, out int c)) return c;
            return fallback;
        }

        private static string Celda(ExcelWorksheet hoja, int fila, int col)
            => col > 0 ? hoja.Cells[fila, col].Text.Trim() : string.Empty;


        /// <summary>
        /// Intenta construir un BloqueTiempo a partir del texto de Día y Hora leído del Excel.
        /// Acepta formatos comunes: "Lunes", "lun", "Monday"; Hora: "7:00", "07:00 AM", "7".
        /// </summary>
        private BloqueTiempo? ParsearBloqueDisponibilidad(string txtDia, string txtHora, int duracionHoras, int fila)
        {
            if (!TryParseDia(txtDia, out var dia))
            {
                _logger.LogWarning("Fila {Fila}: no se pudo parsear el día '{Dia}'.", fila, txtDia);
                return null;
            }

            if (!TryParseRangoHora(txtHora, duracionHoras, out var horaInicio, out var horaFin))
            {
                _logger.LogWarning("Fila {Fila}: no se pudo parsear la hora '{Hora}'.", fila, txtHora);
                return null;
            }

            return new BloqueTiempo(Guid.NewGuid(), dia, horaInicio, horaFin);
        }

        /// <summary>
        /// Heurística conservadora (mismo criterio que DetectorDocentesDuplicados: falso negativo es
        /// preferible a fusionar dos facultades realmente distintas): dos nombres normalizados a
        /// máximo 2 ediciones de distancia, con longitudes parecidas, son un probable typo del mismo
        /// nombre — no una facultad legítimamente distinta ("INGENIERIA" vs "INGENIERIA CIVIL" difieren
        /// en más de 2 ediciones y no deben avisar).
        /// </summary>
        private static bool EsPosibleTypo(string a, string b)
        {
            var na = NormalizadorTexto.Normalizar(a);
            var nb = NormalizadorTexto.Normalizar(b);
            if (na == nb || na.Length == 0 || nb.Length == 0) return false;
            if (Math.Abs(na.Length - nb.Length) > 2) return false;

            return DistanciaLevenshtein(na, nb) <= 2;
        }

        private static int DistanciaLevenshtein(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                {
                    var costo = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + costo);
                }

            return d[a.Length, b.Length];
        }

        private static bool TryParseDia(string texto, out DiaDeSemana dia)
        {
            dia = DiaDeSemana.Lunes;
            if (string.IsNullOrWhiteSpace(texto)) return false;

            // Normalizar: quitar tildes y pasar a minúsculas
            var normalizado = NormalizarTexto(texto);

            dia = normalizado switch
            {
                var s when s.StartsWith("lun") || s.StartsWith("mon") => DiaDeSemana.Lunes,
                var s when s.StartsWith("mar") || s.StartsWith("tue") => DiaDeSemana.Martes,
                var s when s.StartsWith("mie") || s.StartsWith("wed") => DiaDeSemana.Miercoles,
                var s when s.StartsWith("jue") || s.StartsWith("thu") => DiaDeSemana.Jueves,
                var s when s.StartsWith("vie") || s.StartsWith("fri") => DiaDeSemana.Viernes,
                var s when s.StartsWith("sab") || s.StartsWith("sat") => DiaDeSemana.Sábado,
                _ => (DiaDeSemana)(-1)
            };

            return (int)dia >= 0;
        }

        private static bool TryParseRangoHora(string texto, int duracionFallback, out TimeOnly horaInicio, out TimeOnly horaFin)
        {
            horaInicio = TimeOnly.MinValue;
            horaFin = TimeOnly.MinValue;

            if (string.IsNullOrWhiteSpace(texto)) return false;

            var partes = texto.Split(new[] { '-', '–', '—' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (partes.Length == 0) return false;

            if (!TryParseHoraUnica(partes[0], out horaInicio))
                return false;

            if (partes.Length > 1 && TryParseHoraUnica(partes[1], out var hFin))
            {
                horaFin = hFin;
            }
            else
            {
                horaFin = horaInicio.AddHours(duracionFallback);
            }

            return true;
        }

        private static bool TryParseHoraUnica(string texto, out TimeOnly hora)
        {
            hora = TimeOnly.MinValue;
            if (string.IsNullOrWhiteSpace(texto)) return false;

            // Intentar formatos comunes
            string[] formatos = { "H:mm", "HH:mm", "h:mm tt", "hh:mm tt", "H:mm:ss", "HH:mm:ss", "h:mm:ss tt", "hh:mm:ss tt" };
            if (TimeOnly.TryParseExact(texto, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out hora))
                return true;

            // Último recurso: solo número de hora (ej. "7")
            if (int.TryParse(texto, out var soloHora) && soloHora >= 0 && soloHora <= 23)
            {
                hora = new TimeOnly(soloHora, 0);
                return true;
            }

            return false;
        }

        private static string NormalizarTexto(string texto)
        {
            var normalizado = texto.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
            return new string(normalizado.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        }

        public async Task<CurriculumExcelResult> LeerAsignaturasModo2Async(Stream excelStream)
        {
            _logger.LogInformation("Iniciando lectura de Asignaturas (Modo 2).");
            var facultadesDict = new Dictionary<string, Facultad>(StringComparer.OrdinalIgnoreCase);
            var programasDict = new Dictionary<string, Programa>(StringComparer.OrdinalIgnoreCase);
            var asignaturasDict = new Dictionary<(string nombre, Guid programaId), Asignatura>();
            var docentesDict = new Dictionary<string, Docente>(StringComparer.OrdinalIgnoreCase);
            var espaciosDict = new Dictionary<string, Espacio>(StringComparer.OrdinalIgnoreCase);
            var sesionesPredefinidas = new List<Sesion>();
            var gruposDict = new Dictionary<(string nombre, Guid programaId), List<Grupo>>();
            var grupos = new List<Grupo>();

            using var paquete = new ExcelPackage();
            await paquete.LoadAsync(excelStream);

            var hoja = paquete.Workbook.Worksheets[0];
            var totalFilas = hoja.Dimension?.Rows ?? 0;

            for (int fila = 2; fila <= totalFilas; fila++)
            {
                var txtFacultad   = hoja.Cells[fila, 1].Text.Trim();
                var txtPrograma   = hoja.Cells[fila, 2].Text.Trim();
                var txtAsignatura = hoja.Cells[fila, 3].Text.Trim();
                var txtCodigo     = hoja.Cells[fila, 4].Text.Trim();
                var txtTipoEspacio= hoja.Cells[fila, 5].Text.Trim();
                var txtEspacio    = hoja.Cells[fila, 6].Text.Trim();
                var txtDuracion   = hoja.Cells[fila, 7].Text.Trim();
                var txtDocente    = hoja.Cells[fila, 8].Text.Trim();

                if (string.IsNullOrWhiteSpace(txtFacultad) || string.IsNullOrWhiteSpace(txtPrograma) || string.IsNullOrWhiteSpace(txtAsignatura))
                    continue;

                // 1. Facultad
                if (!facultadesDict.TryGetValue(txtFacultad, out var facultad))
                {
                    facultad = new Facultad(Guid.NewGuid(), txtFacultad);
                    facultadesDict[txtFacultad] = facultad;
                }

                // 2. Programa
                var clavePrograma = $"{txtFacultad}|{txtPrograma}";
                if (!programasDict.TryGetValue(clavePrograma, out var programa))
                {
                    programa = new Programa(Guid.NewGuid(), txtPrograma, facultad.Id);
                    programasDict[clavePrograma] = programa;
                }

                // 3. Asignatura
                int duracion = int.TryParse(txtDuracion, out var d) && d > 0 ? d : 2;
                var claveAsignatura = (txtAsignatura.ToUpperInvariant(), programa.Id);
                if (!asignaturasDict.ContainsKey(claveAsignatura))
                {
                    var asignatura = new Asignatura(Guid.NewGuid(), txtAsignatura, txtCodigo, duracion, 2, 0, programa.Id);
                    asignaturasDict[claveAsignatura] = asignatura;
                }

                // Crear un Grupo incremental por cada fila (aunque la asignatura sea única)
                if (!gruposDict.TryGetValue(claveAsignatura, out var listaGrupos))
                {
                    listaGrupos = new List<Grupo>();
                    gruposDict[claveAsignatura] = listaGrupos;
                }
                var asignaturaParaGrupo = asignaturasDict[claveAsignatura];
                var numeroGrupo = listaGrupos.Count + 1;
                var nombreGrupo = $"{txtAsignatura} - Grupo {numeroGrupo}";
                var nuevoGrupo = new Grupo(Guid.NewGuid(), nombreGrupo, programa.Id, 30, asignaturaParaGrupo.Alternancia,
                    asignaturaId: asignaturaParaGrupo.Id);
                listaGrupos.Add(nuevoGrupo);
                grupos.Add(nuevoGrupo);

                // 4. Docente (Solo creación básica, la disponibilidad se cargará con el Excel 3).
                //    El docente vive en el GRUPO, no en la asignatura.
                if (!string.IsNullOrWhiteSpace(txtDocente))
                {
                    if (!docentesDict.TryGetValue(txtDocente, out var docente))
                    {
                        docente = new Docente(Guid.NewGuid(), txtDocente, "", "", 40m, new List<FranjaHoraria> { FranjaHoraria.Matutino });
                        docentesDict[txtDocente] = docente;
                    }
                    nuevoGrupo.AsignarDocente(docente.Id);
                }

                // 5. Espacio
                Espacio? espacioAsignado = null;
                if (!string.IsNullOrWhiteSpace(txtEspacio))
                {
                    if (!espaciosDict.TryGetValue(txtEspacio, out var espacio))
                    {
                        var tipo = txtTipoEspacio.Contains("Laboratorio", StringComparison.OrdinalIgnoreCase) ? TipoEspacio.Laboratorio : TipoEspacio.Salon;
                        espacio = new Espacio(Guid.NewGuid(), txtEspacio, tipo, 30, "", null);
                        espaciosDict[txtEspacio] = espacio;
                    }
                    espacioAsignado = espacio;
                }

                // 6. Sesión
                if (!string.IsNullOrWhiteSpace(txtDocente) && docentesDict.TryGetValue(txtDocente, out var docenteFinal))
                {
                    var asignaturaFinal = asignaturasDict[claveAsignatura];
                    var sesion = new Sesion(
                        Guid.NewGuid(), asignaturaFinal.Id, docenteFinal.Id, Guid.Empty, espacioAsignado?.Id,
                        nuevoGrupo.Id, asignaturaFinal.Alternancia, Modalidad.Presencial, duracion, false, false);
                    sesionesPredefinidas.Add(sesion);
                }
            }

            return new CurriculumExcelResult(
                facultadesDict.Values.ToList().AsReadOnly(),
                programasDict.Values.ToList().AsReadOnly(),
                asignaturasDict.Values.ToList().AsReadOnly(),
                docentesDict.Values.ToList().AsReadOnly(),
                sesionesPredefinidas.AsReadOnly(),
                espaciosDict.Values.ToList().AsReadOnly(),
                grupos.AsReadOnly()
            );
        }

        public async Task<IEnumerable<Docente>> LeerDisponibilidadDocentesAsync(Stream excelStream, IEnumerable<Docente> docentesExistentes)
        {
            _logger.LogInformation("Iniciando lectura del Excel secundario de disponibilidad de docentes.");
            var docentes = new List<Docente>();
            var docentesDict = docentesExistentes.ToDictionary(d => d.Nombre, StringComparer.OrdinalIgnoreCase);

            using var paquete = new ExcelPackage();
            await paquete.LoadAsync(excelStream);

            var hoja = paquete.Workbook.Worksheets[0];
            var totalFilas = hoja.Dimension?.Rows ?? 0;

            for (int fila = 2; fila <= totalFilas; fila++)
            {
                var txtDocente    = hoja.Cells[fila, 1].Text.Trim();
                var correo        = hoja.Cells[fila, 2].Text.Trim();
                var txtMaxHoras   = hoja.Cells[fila, 3].Text.Trim();
                var txtDias       = hoja.Cells[fila, 4].Text.Trim();
                var txtFranjas    = hoja.Cells[fila, 5].Text.Trim();

                if (string.IsNullOrWhiteSpace(txtDocente)) continue;

                // Parsear Dias
                var diasDocente = new List<DiaDeSemana>();
                if (!string.IsNullOrWhiteSpace(txtDias))
                {
                    var partesDias = txtDias.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var pd in partesDias)
                    {
                        if (TryParseDia(pd, out var d)) diasDocente.Add(d);
                    }
                }
                if (!diasDocente.Any()) 
                {
                    // Si no especifica, asume toda la semana hábil
                    diasDocente.AddRange(new[] { DiaDeSemana.Lunes, DiaDeSemana.Martes, DiaDeSemana.Miercoles, DiaDeSemana.Jueves, DiaDeSemana.Viernes });
                }

                // Parsear Franjas
                var franjasDocente = new List<FranjaHoraria>();
                if (!string.IsNullOrWhiteSpace(txtFranjas))
                {
                    var partesFranjas = txtFranjas.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var pf in partesFranjas)
                    {
                        if (Enum.TryParse<FranjaHoraria>(pf, true, out var f)) franjasDocente.Add(f);
                    }
                }
                if (!franjasDocente.Any())
                {
                    // Si no especifica, asume Matutino
                    franjasDocente.Add(FranjaHoraria.Matutino);
                }

                decimal maxHoras = decimal.TryParse(txtMaxHoras, out var m) ? m : 40m;

                // Validación simple de correo para no reventar la entidad
                string correoFinal = "";
                if (!string.IsNullOrWhiteSpace(correo))
                {
                    try { var addr = new System.Net.Mail.MailAddress(correo); correoFinal = addr.Address == correo ? correo : ""; }
                    catch { correoFinal = ""; }
                }

                if (!docentesDict.TryGetValue(txtDocente, out var docente))
                {
                    // Si el docente no existía en la carga de asignaturas, se crea (aunque no dicte nada)
                    docente = new Docente(Guid.NewGuid(), txtDocente, "", correoFinal, maxHoras, franjasDocente);
                    docentesDict[txtDocente] = docente;
                    docentes.Add(docente);
                }
                else
                {
                    // Actualizamos correo y horas si están vacíos
                    docente.ActualizarDatos(docente.Nombre, docente.Apellido, string.IsNullOrWhiteSpace(docente.Correo) ? correoFinal : docente.Correo, maxHoras);
                    
                    // Acumulamos las franjas si hay múltiples filas para el mismo docente
                    var franjasAcumuladas = docente.Disponibilidad.Union(franjasDocente).Distinct().ToList();
                    docente.ActualizarDisponibilidad(franjasAcumuladas);

                    if (!docentes.Contains(docente)) docentes.Add(docente);
                }

                // Generar Bloques de Tiempo Ficticios para las Franjas/Días indicados para alimentar a Welsh-Powell
                // Matutino: 6 a 12 | Vespertino: 12 a 18 (Sábado solo hasta 14:00)
                foreach(var dia in diasDocente)
                {
                    var horaLimiteDia = dia == DiaDeSemana.Sábado ? new TimeOnly(14, 0) : new TimeOnly(22, 0);

                    if (franjasDocente.Contains(FranjaHoraria.Matutino))
                    {
                        docente.AgregarBloqueDisponibilidad(new BloqueTiempo(Guid.NewGuid(), dia, new TimeOnly(6,0), new TimeOnly(8,0)));
                        docente.AgregarBloqueDisponibilidad(new BloqueTiempo(Guid.NewGuid(), dia, new TimeOnly(8,0), new TimeOnly(10,0)));
                        docente.AgregarBloqueDisponibilidad(new BloqueTiempo(Guid.NewGuid(), dia, new TimeOnly(10,0), new TimeOnly(12,0)));
                    }
                    if (franjasDocente.Contains(FranjaHoraria.Vespertino))
                    {
                        if (new TimeOnly(14,0) <= horaLimiteDia)
                            docente.AgregarBloqueDisponibilidad(new BloqueTiempo(Guid.NewGuid(), dia, new TimeOnly(12,0), new TimeOnly(14,0)));
                        if (new TimeOnly(16,0) <= horaLimiteDia)
                            docente.AgregarBloqueDisponibilidad(new BloqueTiempo(Guid.NewGuid(), dia, new TimeOnly(14,0), new TimeOnly(16,0)));
                        if (new TimeOnly(18,0) <= horaLimiteDia)
                            docente.AgregarBloqueDisponibilidad(new BloqueTiempo(Guid.NewGuid(), dia, new TimeOnly(16,0), new TimeOnly(18,0)));
                    }
                }
            }

            _logger.LogInformation("Lectura finalizada. Se procesaron {Cantidad} docentes.", docentes.Count);
            return docentesDict.Values;
        }

        public async Task<IEnumerable<Espacio>> LeerInventarioEspaciosAsync(Stream excelStream)
        {
            _logger.LogInformation("Iniciando lectura del inventario de espacios físicos.");
            var espacios = new List<Espacio>();

            using var paquete = new ExcelPackage();
            await paquete.LoadAsync(excelStream);

            var hoja = paquete.Workbook.Worksheets[0];
            var totalFilas = hoja.Dimension?.Rows ?? 0;

            for (int fila = 2; fila <= totalFilas; fila++)
            {
                var nombre      = hoja.Cells[fila, 1].Text.Trim();
                var tipoTexto   = hoja.Cells[fila, 2].Text.Trim();
                var capacidad   = hoja.Cells[fila, 3].Text.Trim();
                var edificio    = hoja.Cells[fila, 4].Text.Trim();
                var pisoTexto   = hoja.Cells[fila, 5].Text.Trim();

                if (string.IsNullOrWhiteSpace(nombre)) continue;

                int cap = int.TryParse(capacidad, out var c) ? c : 30;
                int? piso = int.TryParse(pisoTexto, out var p) ? p : null;
                TipoEspacio tipo = Enum.TryParse<TipoEspacio>(tipoTexto, true, out var t) ? t : TipoEspacio.Salon;

                espacios.Add(new Espacio(Guid.NewGuid(), nombre, tipo, cap, edificio, piso));
            }

            _logger.LogInformation("Lectura finalizada. Se encontraron {Cantidad} espacios.", espacios.Count);
            return espacios;
        }
    }
}
