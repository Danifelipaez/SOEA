using System;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Asignatura académica de la malla curricular.
    /// Duración fija: el algoritmo la lee, nunca la modifica (decisión inamovible).
    /// </summary>
    public class Asignatura : EntidadBase
    {
        public string Nombre { get; private set; } = "";
        public string Codigo { get; private set; } = "";

        /// <summary>
        /// Duración de cada sesión en horas (ej: 2 o 3 horas).
        /// </summary>
        public int HorasPorSesion { get; private set; }

        /// <summary>
        /// Número de veces que se dicta la asignatura a la semana.
        /// </summary>
        public int SesionesPorSemana { get; private set; }

        /// <summary>
        /// Cantidad de sesiones de laboratorio que requiere en el semestre.
        /// </summary>
        public int SesionesLaboratorioSemestre { get; private set; }

        public TipoAlternancia Alternancia { get; private set; }
        public Guid ProgramaId { get; private set; }

        // Constructor privado para EF Core
        private Asignatura() : base() { }

        public Asignatura(
            Guid id,
            string nombre,
            string codigo,
            int horasPorSesion,
            int sesionesPorSemana,
            int sesionesLaboratorioSemestre,
            Guid programaId) : base(id)
        {
            Validar(nombre, codigo, horasPorSesion, sesionesPorSemana, programaId);

            Nombre = nombre;
            Codigo = codigo;
            HorasPorSesion = horasPorSesion;
            SesionesPorSemana = sesionesPorSemana;
            SesionesLaboratorioSemestre = sesionesLaboratorioSemestre;
            Alternancia = DeterminarAlternancia(sesionesLaboratorioSemestre);
            ProgramaId = programaId;
        }

        /// <summary>
        /// Actualiza datos editables. Usado por UpdateAsync en Infrastructure.
        /// La duración (HorasPorSesion y SesionesPorSemana) es inmutable una vez creada (hard constraint).
        /// </summary>
        public void ActualizarDatos(string nombre, int sesionesLaboratorioSemestre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                throw new ArgumentException("El nombre no puede estar vacío.");
            
            Nombre = nombre;
            SesionesLaboratorioSemestre = sesionesLaboratorioSemestre;
            Alternancia = DeterminarAlternancia(sesionesLaboratorioSemestre);
        }

        private static TipoAlternancia DeterminarAlternancia(int sesionesLabSemestre)
        {
            if (sesionesLabSemestre == 8)
                return TipoAlternancia.TipoA;
            if (sesionesLabSemestre > 8)
                return TipoAlternancia.TipoB;
            
            return TipoAlternancia.SinAlternancia;
        }

        private static void Validar(string nombre, string codigo, int horasPorSesion, int sesionesPorSemana, Guid programaId)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                throw new ArgumentException("El nombre de la asignatura no puede estar vacío.");
            if (string.IsNullOrWhiteSpace(codigo))
                throw new ArgumentException("El código de la asignatura no puede estar vacío.");
            if (horasPorSesion <= 0)
                throw new ArgumentException("Las horas por sesión deben ser un valor positivo.");
            if (sesionesPorSemana <= 0)
                throw new ArgumentException("Las sesiones por semana deben ser un valor positivo.");
            if (programaId == Guid.Empty)
                throw new ArgumentException("El ID del programa no puede ser vacío.");
        }
    }
}
