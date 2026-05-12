using OfficeOpenXml;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SOEA.Infrastructure.Excel
{
    public class LectorExcel : ILectorExcel
    {
        public LectorExcel()
        {
            // La licencia se configura en el DependencyInjection
        }

        public async Task<IEnumerable<Asignatura>> LeerCurriculumAsync(Stream excelStream)
        {
            var asignaturas = new List<Asignatura>();

            using var paquete = new ExcelPackage(excelStream);
            await paquete.LoadAsync(excelStream);

            var hojaTrabajo = paquete.Workbook.Worksheets[0]; // Asumimos la primera hoja
            var cantidadFilas = hojaTrabajo.Dimension?.Rows ?? 0;

            // Asumimos fila 1 como encabezado.
            for (int fila = 2; fila <= cantidadFilas; fila++)
            {
                var nombre = hojaTrabajo.Cells[fila, 1].Text;
                var codigo = hojaTrabajo.Cells[fila, 2].Text;
                var creditosTexto = hojaTrabajo.Cells[fila, 3].Text; // Usaremos creditos para inferir horas, o si hay columna de horas.
                var horasTexto = hojaTrabajo.Cells[fila, 4].Text;
                var requiereLabTexto = hojaTrabajo.Cells[fila, 5].Text;

                if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(codigo))
                    continue;

                int creditos = int.TryParse(creditosTexto, out var c) ? c : 3;
                int horasSemanales = int.TryParse(horasTexto, out var h) ? h : 4;
                bool requiereLab = bool.TryParse(requiereLabTexto, out var r) ? r : false;

                // Creamos un Asignatura básico (sin programaId de momento, asumiendo un Guid genérico si es necesario)
                var asignatura = new Asignatura(Guid.NewGuid(), nombre, codigo, horasSemanales, requiereLab, TipoAlternancia.SinAlternancia, Guid.Empty);
                asignaturas.Add(asignatura);
            }

            return asignaturas;
        }

        public async Task<IEnumerable<Docente>> LeerDisponibilidadDocentesAsync(Stream excelStream)
        {
            var docentes = new List<Docente>();

            using var paquete = new ExcelPackage(excelStream);
            await paquete.LoadAsync(excelStream);

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

            return docentes;
        }

        public async Task<IEnumerable<Espacio>> LeerInventarioEspaciosAsync(Stream excelStream)
        {
            var espacios = new List<Espacio>();

            using var paquete = new ExcelPackage(excelStream);
            await paquete.LoadAsync(excelStream);

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

            return espacios;
        }
    }
}
