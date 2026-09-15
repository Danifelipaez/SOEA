using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SOEA.Domain.ValueObjects
{
    /// <summary>
    /// Utilidades de normalización de texto para comparaciones tolerantes a tildes, mayúsculas y espacios.
    /// </summary>
    public static class NormalizadorTexto
    {
        /// <summary>
        /// Normaliza un texto para comparación: minúsculas, sin tildes, sin espacios repetidos.
        /// Ej: "Víctor Macías" → "victor macias"
        /// </summary>
        public static string Normalizar(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

            var s = texto.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
            s = string.Concat(s.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));

            s = Regex.Replace(s, @" {2,}", " ");

            return s;
        }

        /// <summary>
        /// Correo sintético para un docente sin correo real (import Excel/JSON). DUP auditoría:
        /// antes esta fórmula vivía duplicada en ImportarCurriculumService y en
        /// ImportController.MapDocentesDto, solo con el nombre normalizado — dos docentes reales
        /// con el mismo nombre (un homónimo, el caso que DetectorDocentesDuplicados existe para
        /// atender) sintetizaban el MISMO correo y chocaban contra el índice único de Docente.Correo.
        /// Se agrega un fragmento del Id (único por construcción) para eliminar la colisión sin
        /// perder la legibilidad del nombre.
        /// </summary>
        public static string CorreoSintetico(string nombre, Guid id) =>
            $"{Normalizar(nombre).Replace(" ", ".")}.{id:N}@soea.local";
    }
}
