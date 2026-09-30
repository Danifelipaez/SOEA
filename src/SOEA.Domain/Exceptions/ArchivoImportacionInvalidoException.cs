using System;

namespace SOEA.Domain.Exceptions
{
    /// <summary>
    /// El archivo de importación se pudo abrir pero uno de sus datos no es válido (p. ej. una duración
    /// de 99 h en la fila 12). El mensaje ya viene en español y con el número de fila, así que sí se le
    /// muestra al usuario — a diferencia de un fallo genuino de lectura (archivo corrupto), que el
    /// controller sigue reportando de forma genérica.
    /// </summary>
    public class ArchivoImportacionInvalidoException : Exception
    {
        public ArchivoImportacionInvalidoException(string message) : base(message) { }
    }
}
