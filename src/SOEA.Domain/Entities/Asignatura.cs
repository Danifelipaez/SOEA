using System;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Asignatura académica de la malla curricular.
    /// Duración fija: el algoritmo la lee, nunca la modifica (decisión inamovible).
    /// </summary>
    public class Asignatura : BaseEntity
    {
        public string Nombre { get; private set; } = "";
        public string Codigo { get; private set; } = "";

        /// <summary>
        /// Número de bloques semanales (1 bloque = 2h o 3h según malla, dato de entrada fijo).
        /// </summary>
        public int BloquesSemanales { get; private set; }

        public bool RequiereLab { get; private set; }
        public TipoAlternancia Alternancia { get; private set; }
        public Guid ProgramaId { get; private set; }

        // Constructor privado para EF Core
        private Asignatura() : base() { }

        public Asignatura(
            Guid id,
            string nombre,
            string codigo,
            int bloquesSemanales,
            bool requiereLab,
            TipoAlternancia alternancia,
            Guid programaId) : base(id)
        {
            Validar(nombre, codigo, bloquesSemanales, programaId);

            Nombre = nombre;
            Codigo = codigo;
            BloquesSemanales = bloquesSemanales;
            RequiereLab = requiereLab;
            Alternancia = alternancia;
            ProgramaId = programaId;
        }

        /// <summary>
        /// Actualiza datos editables. Usado por UpdateAsync en Infrastructure.
        /// La duración (BloquesSemanales) es inmutable una vez creada (hard constraint).
        /// </summary>
        public void ActualizarDatos(string nombre, bool requiereLab, TipoAlternancia alternancia)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                throw new ArgumentException("El nombre no puede estar vacío.");
            Nombre = nombre;
            RequiereLab = requiereLab;
            Alternancia = alternancia;
        }

        private static void Validar(string nombre, string codigo, int bloquesSemanales, Guid programaId)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                throw new ArgumentException("El nombre de la asignatura no puede estar vacío.");
            if (string.IsNullOrWhiteSpace(codigo))
                throw new ArgumentException("El código de la asignatura no puede estar vacío.");
            if (bloquesSemanales <= 0)
                throw new ArgumentException("Los bloques semanales deben ser un valor positivo.");
            if (programaId == Guid.Empty)
                throw new ArgumentException("El ID del programa no puede ser vacío.");
        }
    }
}