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
                // Col F: Espacio específico (para uso futuro)
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
            }

            var resultado = new CurriculumExcelResult(
                facultades:  facultadesDict.Values.ToList().AsReadOnly(),
                programas:   programasDict.Values.ToList().AsReadOnly(),
                asignaturas: asignaturasDict.Values.ToList().AsReadOnly(),
                docentes:    docentesDict.Values.ToList().AsReadOnly()
            );

            _logger.LogInformation(
                "Lectura finalizada. Facultades: {F}, Programas: {P}, Asignaturas: {A}, Docentes: {D}.",
                resultado.Facultades.Count,
                resultado.Programas.Count,
                resultado.Asignaturas.Count,
                resultado.Docentes.Count
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

            if (!TryParseHora(txtHora, out var horaInicio))
            {
                _logger.LogWarning("Fila {Fila}: no se pudo parsear la hora '{Hora}'.", fila, txtHora);
                return null;
            }

            var horaFin = horaInicio.AddHours(duracionHoras);
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
                _ => (DiaDeSemana)(-1)
            };

            return (int)dia >= 0;
        }

        private static bool TryParseHora(string texto, out TimeOnly hora)
        {
            hora = TimeOnly.MinValue;
            if (string.IsNullOrWhiteSpace(texto)) return false;

            // Intentar formatos comunes
            string[] formatos = { "H:mm", "HH:mm", "h:mm tt", "hh:mm tt", "H", "HH" };
            if (TimeOnly.TryParseExact(texto.Trim(), formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out hora))
                return true;

            // Último recurso: solo número de hora (ej. "7")
            if (int.TryParse(texto.Trim(), out var soloHora) && soloHora >= 0 && soloHora <= 23)
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

        public async Task<IEnumerable<Docente>> LeerDisponibilidadDocentesAsync(Stream excelStream)
        {
            _logger.LogInformation("Iniciando lectura del Excel secundario de disponibilidad de docentes.");
            var docentes = new List<Docente>();

            using var paquete = new ExcelPackage(excelStream);
            await paquete.LoadAsync(excelStream);

            var hoja = paquete.Workbook.Worksheets[0];
            var totalFilas = hoja.Dimension?.Rows ?? 0;

            for (int fila = 2; fila <= totalFilas; fila++)
            {
                var nombreCompleto = hoja.Cells[fila, 1].Text.Trim();
                var correo         = hoja.Cells[fila, 2].Text.Trim();
                var txtMaxHoras    = hoja.Cells[fila, 3].Text.Trim();

                if (string.IsNullOrWhiteSpace(nombreCompleto) || string.IsNullOrWhiteSpace(correo))
                    continue;

                decimal maxHoras = decimal.TryParse(txtMaxHoras, out var m) ? m : 40m;

                var docente = new Docente(
                    Guid.NewGuid(), nombreCompleto, "", correo, maxHoras,
                    new List<FranjaHoraria> { FranjaHoraria.Matutino }
                );
                docentes.Add(docente);
            }

            _logger.LogInformation("Lectura finalizada. Se encontraron {Cantidad} docentes.", docentes.Count);
            return docentes;
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
