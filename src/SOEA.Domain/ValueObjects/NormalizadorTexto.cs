using System;
using System.Globalization;
using System.Linq;

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
            s = new string(s.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());

            // Colapsar espacios múltiples
            while (s.Contains("  ")) s = s.Replace("  ", " ");

            return s;
        }
    }
}
