using OfficeOpenXml;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SOEA.Infrastructure.Excel
{
    public class LectorExcel : ILectorExcel
    {
        private readonly ILogger<LectorExcel> _logger;

        public LectorExcel(ILogger<LectorExcel> logger)
        {
            _logger = logger;
            // La licencia se configura en el DependencyInjection
        }

        public async Task<IEnumerable<Asignatura>> LeerCurriculumAsync(Stream flujoExcel)
        {
            _logger.LogInformation("Iniciando lectura del currículum (Asignaturas) desde archivo Excel.");
            var asignaturas = new List<Asignatura>();

            using var paquete = new ExcelPackage(flujoExcel);
            await paquete.LoadAsync(flujoExcel);

            var hojaTrabajo = paquete.Workbook.Worksheets[0]; // Asumimos la primera hoja
            var cantidadFilas = hojaTrabajo.Dimension?.Rows ?? 0;

            // Asumimos fila 1 como encabezado.
            for (int fila = 2; fila <= cantidadFilas; fila++)
            {
                var nombre = hojaTrabajo.Cells[fila, 1].Text;
                var codigo = hojaTrabajo.Cells[fila, 2].Text;
                var horasPorSesionTexto = hojaTrabajo.Cells[fila, 3].Text;
                var sesionesPorSemanaTexto = hojaTrabajo.Cells[fila, 4].Text;
                var sesionesLabSemestreTexto = hojaTrabajo.Cells[fila, 5].Text;

                if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(codigo))
                    continue;

                int horasPorSesion = int.TryParse(horasPorSesionTexto, out var hps) && hps > 0 ? hps : 2;
                int sesionesPorSemana = int.TryParse(sesionesPorSemanaTexto, out var sps) && sps > 0 ? sps : 2;
                int sesionesLabSemestre = int.TryParse(sesionesLabSemestreTexto, out var sls) && sls >= 0 ? sls : 0;

                // Creamos un Asignatura básico (sin programaId de momento, asumiendo un Guid genérico si es necesario)
                var asignatura = new Asignatura(Guid.NewGuid(), nombre, codigo, horasPorSesion, sesionesPorSemana, sesionesLabSemestre, Guid.Empty);
                asignaturas.Add(asignatura);
            }

            _logger.LogInformation("Lectura finalizada. Se encontraron {Cantidad} asignaturas válidas.", asignaturas.Count);
            return asignaturas;
        }

        public async Task<IEnumerable<Docente>> LeerDisponibilidadDocentesAsync(Stream flujoExcel)
        {
            _logger.LogInformation("Iniciando lectura de disponibilidad de docentes desde archivo Excel.");
            var docentes = new List<Docente>();

            using var paquete = new ExcelPackage(flujoExcel);
            await paquete.LoadAsync(flujoExcel);

            var hojaTrabajo = paquete.Workbook.Worksheets[0];
            var cantidadFilas = hojaTrabajo.Dimension?.Rows ?? 0;

            for (int fila = 2; fila <= cantidadFilas; fila++)
            {
                var nombreCompleto = hojaTrabajo.Cells[fila, 1].Text;
                var correo = hojaTrabajo.Cells[fila, 2].Text;
                var maximoHorasSemanalesTexto = hojaTrabajo.Cells[fila, 3].Text;

                if (string.IsNullOrWhiteSpace(nombreCompleto) || string.IsNullOrWhiteSpace(correo))
                    continue;

                decimal maximoHorasSemanales = decimal.TryParse(maximoHorasSemanalesTexto, out var m) ? m : 40m;

                var docente = new Docente(Guid.NewGuid(), nombreCompleto, "", correo, maximoHorasSemanales, new List<FranjaHoraria> { FranjaHoraria.Matutino });
                // Aquí se podría leer la disponibilidad y agregar los bloques de tiempo
                // docente.Disponibilidad = ...

                docentes.Add(docente);
            }

            _logger.LogInformation("Lectura finalizada. Se encontraron {Cantidad} docentes válidos.", docentes.Count);
            return docentes;
        }

        public async Task<IEnumerable<Espacio>> LeerInventarioEspaciosAsync(Stream flujoExcel)
        {
            _logger.LogInformation("Iniciando lectura del inventario de espacios físicos desde archivo Excel.");
            var espacios = new List<Espacio>();

            using var paquete = new ExcelPackage(flujoExcel);
            await paquete.LoadAsync(flujoExcel);

            var hojaTrabajo = paquete.Workbook.Worksheets[0];
            var cantidadFilas = hojaTrabajo.Dimension?.Rows ?? 0;

            for (int fila = 2; fila <= cantidadFilas; fila++)
            {
                var nombre = hojaTrabajo.Cells[fila, 1].Text;
                var tipoTexto = hojaTrabajo.Cells[fila, 2].Text; // Ej. "Salon"
                var capacidadTexto = hojaTrabajo.Cells[fila, 3].Text;
                var edificio = hojaTrabajo.Cells[fila, 4].Text;
                var pisoTexto = hojaTrabajo.Cells[fila, 5].Text;

                if (string.IsNullOrWhiteSpace(nombre))
                    continue;

                int capacidad = int.TryParse(capacidadTexto, out var c) ? c : 30;
                int? piso = int.TryParse(pisoTexto, out var p) ? p : null;
                
                TipoEspacio tipo = Enum.TryParse<TipoEspacio>(tipoTexto, true, out var t) ? t : TipoEspacio.Salon;

                var espacio = new Espacio(Guid.NewGuid(), nombre, tipo, capacidad, edificio, piso);
                espacios.Add(espacio);
            }

            _logger.LogInformation("Lectura finalizada. Se encontraron {Cantidad} espacios válidos.", espacios.Count);
            return espacios;
        }
    }
}
