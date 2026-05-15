using OfficeOpenXml;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
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
    /// Formato de entrada del horario existente (columnas A-J):
    ///   A: Facultad | B: Programa | C: Asignatura | D: Código (puede estar vacío)
    ///   E: Tipo de Espacio | F: Espacio Específico | G: Duración (horas)
    ///   H: Día | I: Hora | J: Docente (nombre completo)
    /// </summary>
    public class LectorExcel : ILectorExcel
    {
        private readonly ILogger<LectorExcel> _logger;

        public LectorExcel(ILogger<LectorExcel> logger)
        {
            _logger = logger;
        }

        public async Task<CurriculumExcelResult> LeerCurriculumAsync(Stream excelStream)
        {
            _logger.LogInformation("Iniciando lectura del currículum desde archivo Excel (formato horario existente).");

            // Catálogos en memoria para deduplicar
            var facultadesDict = new Dictionary<string, Facultad>(StringComparer.OrdinalIgnoreCase);
            var programasDict = new Dictionary<string, Programa>(StringComparer.OrdinalIgnoreCase);

            // Clave: (nombreAsignatura, programaId) → Asignatura (deduplicada)
            var asignaturasDict = new Dictionary<(string nombre, Guid programaId), Asignatura>();

            // Docentes con su disponibilidad extraída del día/hora del Excel
            var docentesDict = new Dictionary<string, Docente>(StringComparer.OrdinalIgnoreCase);

            var espaciosDict = new Dictionary<string, Espacio>(StringComparer.OrdinalIgnoreCase);
            var sesionesPredefinidas = new List<Sesion>();

            using var paquete = new ExcelPackage(excelStream);
            await paquete.LoadAsync(excelStream);

            var hoja = paquete.Workbook.Worksheets[0];
            var totalFilas = hoja.Dimension?.Rows ?? 0;

            for (int fila = 2; fila <= totalFilas; fila++)
            {
                var txtFacultad   = hoja.Cells[fila, 1].Text.Trim();
                var txtPrograma   = hoja.Cells[fila, 2].Text.Trim();
                var txtAsignatura = hoja.Cells[fila, 3].Text.Trim();
                var txtCodigo     = hoja.Cells[fila, 4].Text.Trim(); // Puede estar vacío
                // Col E: Tipo de espacio (para uso futuro)
                var txtTipoEspacio = hoja.Cells[fila, 5].Text.Trim();
                // Col F: Espacio específico
                var txtEspacioEspecifico = hoja.Cells[fila, 6].Text.Trim();
                var txtDuracion   = hoja.Cells[fila, 7].Text.Trim();
                var txtDia        = hoja.Cells[fila, 8].Text.Trim();
                var txtHora       = hoja.Cells[fila, 9].Text.Trim();
                var txtDocente    = hoja.Cells[fila, 10].Text.Trim();

                // Saltar filas sin datos esenciales
                if (string.IsNullOrWhiteSpace(txtFacultad) || string.IsNullOrWhiteSpace(txtPrograma) || string.IsNullOrWhiteSpace(txtAsignatura))
                {
                    _logger.LogWarning("Fila {Fila}: datos incompletos (Facultad/Programa/Asignatura vacíos), se omite.", fila);
                    continue;
                }

                // 1. Obtener o crear Facultad
                if (!facultadesDict.TryGetValue(txtFacultad, out var facultad))
                {
                    facultad = new Facultad(Guid.NewGuid(), txtFacultad);
                    facultadesDict[txtFacultad] = facultad;
                    _logger.LogDebug("Nueva Facultad detectada: {Facultad}", txtFacultad);
                }

                // 2. Obtener o crear Programa (clave compuesta nombre+facultad)
                var clavePrograma = $"{txtFacultad}|{txtPrograma}";
                if (!programasDict.TryGetValue(clavePrograma, out var programa))
                {
                    programa = new Programa(Guid.NewGuid(), txtPrograma, facultad.Id);
                    programasDict[clavePrograma] = programa;
                    _logger.LogDebug("Nuevo Programa detectado: {Programa} (Facultad: {Facultad})", txtPrograma, txtFacultad);
                }

                // 3. Obtener o crear Asignatura (deduplicar por nombre + programa)
                int duracion = int.TryParse(txtDuracion, out var d) && d > 0 ? d : 2;

                // Si la duración no se especificó claramente y la hora viene en formato rango, inferimos la duración
                if (string.IsNullOrWhiteSpace(txtDuracion) && (txtHora.Contains('-') || txtHora.Contains('–') || txtHora.Contains('—')))
                {
                    if (TryParseRangoHora(txtHora, 2, out var hIni, out var hFin))
                    {
                        var dif = (hFin - hIni).TotalHours;
                        if (dif > 0) duracion = (int)Math.Round(dif);
                    }
                }

                var claveAsignatura = (txtAsignatura.ToUpperInvariant(), programa.Id);

                if (!asignaturasDict.ContainsKey(claveAsignatura))
                {
                    // El código viene de col D; si vacío, el constructor de Asignatura genera uno temporal
                    var asignatura = new Asignatura(
                        id: Guid.NewGuid(),
                        nombre: txtAsignatura,
                        codigo: txtCodigo, // puede estar vacío; el dominio lo maneja
                        horasPorSesion: duracion,
                        sesionesPorSemana: 2, // Valor por defecto para el piloto (sin datos de grupo/cohorte)
                        sesionesLaboratorioSemestre: 0,
                        programaId: programa.Id
                    );
                    asignaturasDict[claveAsignatura] = asignatura;
                    _logger.LogDebug("Nueva Asignatura detectada: {Asignatura} (Programa: {Programa})", txtAsignatura, txtPrograma);
                }

                // 4. Obtener o crear Docente y cargar disponibilidad desde la columna H (día) e I (hora)
                if (!string.IsNullOrWhiteSpace(txtDocente))
                {
                    if (!docentesDict.TryGetValue(txtDocente, out var docente))
                    {
                        docente = new Docente(
                            Guid.NewGuid(),
                            txtDocente,
                            "",          // Departamento (no está en el Excel)
                            "",          // Correo (no está en el Excel)
                            40m,         // MaximoHorasSemanales por defecto
                            new List<FranjaHoraria> { FranjaHoraria.Matutino }
                        );
                        docentesDict[txtDocente] = docente;
                        _logger.LogDebug("Nuevo Docente detectado: {Docente}", txtDocente);
                    }

                    // Agregar bloque de disponibilidad basado en Día y Hora del horario existente
                    if (!string.IsNullOrWhiteSpace(txtDia) && !string.IsNullOrWhiteSpace(txtHora))
                    {
                        var bloqueDisponibilidad = ParsearBloqueDisponibilidad(txtDia, txtHora, duracion, fila);
                        if (bloqueDisponibilidad != null)
                        {
                            docente.AgregarBloqueDisponibilidad(bloqueDisponibilidad);
                        }
                    }
                }

                // 5. Obtener o crear Espacio
                Espacio espacioAsignado = null;
                if (!string.IsNullOrWhiteSpace(txtEspacioEspecifico))
                {
                    if (!espaciosDict.TryGetValue(txtEspacioEspecifico, out var espacio))
                    {
                        var tipo = TipoEspacio.Salon;
                        if (txtTipoEspacio.Contains("Laboratorio", StringComparison.OrdinalIgnoreCase)) tipo = TipoEspacio.Laboratorio;
                        
                        espacio = new Espacio(Guid.NewGuid(), txtEspacioEspecifico, tipo, 30, "", null);
                        espaciosDict[txtEspacioEspecifico] = espacio;
                        _logger.LogDebug("Nuevo Espacio detectado: {Espacio}", txtEspacioEspecifico);
                    }
                    espacioAsignado = espacio;
                }

                // 6. Crear la Sesión correspondiente a esta fila
                if (!string.IsNullOrWhiteSpace(txtDocente) && docentesDict.TryGetValue(txtDocente, out var docenteFinal))
                {
                    var asignaturaFinal = asignaturasDict[claveAsignatura];
                    var sesion = new Sesion(
                        id: Guid.NewGuid(),
                        asignaturaId: asignaturaFinal.Id,
                        docenteId: docenteFinal.Id,
                        bloqueId: Guid.Empty,
                        espacioId: espacioAsignado?.Id,
                        grupoId: null,
                        alternancia: asignaturaFinal.Alternancia,
                        modalidad: Modalidad.Presencial,
                        duracionHoras: duracion,
                        esBloque: false,
                        estaDividida: false
                    );
                    sesionesPredefinidas.Add(sesion);
                }
            }

            var resultado = new CurriculumExcelResult(
                facultades:  facultadesDict.Values.ToList().AsReadOnly(),
                programas:   programasDict.Values.ToList().AsReadOnly(),
                asignaturas: asignaturasDict.Values.ToList().AsReadOnly(),
                docentes:    docentesDict.Values.ToList().AsReadOnly(),
                sesionesPredefinidas: sesionesPredefinidas.AsReadOnly(),
                espacios: espaciosDict.Values.ToList().AsReadOnly()
            );

            _logger.LogInformation(
                "Lectura finalizada. Facultades: {F}, Programas: {P}, Asignaturas: {A}, Docentes: {D}, Sesiones: {S}, Espacios: {E}.",
                resultado.Facultades.Count,
                resultado.Programas.Count,
                resultado.Asignaturas.Count,
                resultado.Docentes.Count,
                resultado.SesionesPredefinidas.Count,
                resultado.Espacios.Count
            );

            return resultado;
        }

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
            string[] formatos = { "H:mm", "HH:mm", "h:mm tt", "hh:mm tt" };
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

            using var paquete = new ExcelPackage(excelStream);
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

                // 4. Docente (Solo creación básica, la disponibilidad se cargará con el Excel 3)
                if (!string.IsNullOrWhiteSpace(txtDocente))
                {
                    if (!docentesDict.TryGetValue(txtDocente, out var docente))
                    {
                        docente = new Docente(Guid.NewGuid(), txtDocente, "", "", 40m, new List<FranjaHoraria> { FranjaHoraria.Matutino });
                        docentesDict[txtDocente] = docente;
                    }
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
                        null, asignaturaFinal.Alternancia, Modalidad.Presencial, duracion, false, false);
                    sesionesPredefinidas.Add(sesion);
                }
            }

            return new CurriculumExcelResult(
                facultadesDict.Values.ToList().AsReadOnly(),
                programasDict.Values.ToList().AsReadOnly(),
                asignaturasDict.Values.ToList().AsReadOnly(),
                docentesDict.Values.ToList().AsReadOnly(),
                sesionesPredefinidas.AsReadOnly(),
                espaciosDict.Values.ToList().AsReadOnly()
            );
        }

        public async Task<IEnumerable<Docente>> LeerDisponibilidadDocentesAsync(Stream excelStream, IEnumerable<Docente> docentesExistentes)
        {
            _logger.LogInformation("Iniciando lectura del Excel secundario de disponibilidad de docentes.");
            var docentes = new List<Docente>();
            var docentesDict = docentesExistentes.ToDictionary(d => d.Nombre, StringComparer.OrdinalIgnoreCase);

            using var paquete = new ExcelPackage(excelStream);
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

            using var paquete = new ExcelPackage(excelStream);
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
