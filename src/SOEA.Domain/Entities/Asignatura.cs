using SOEA.Domain.Enums;

namespace SOEA.Domain.Entities
{
    public class Asignatura
    {
        public Guid Id { get; private set; }
        public string Nombre { get; private set; } ="";
        public string Codigo { get; private set; } ="";
        public int BloquesSemanales { get; private set; }
        public bool RequiereLab { get; private set; }
        public TipoAlternancia Alternancia { get; private set; }
        public Guid ProgramaId { get; private set; }

        private Asignatura(){}
    public Asignatura(Guid id, string nombre, string codigo, int bloquesSemanales, bool requiereLab, TipoAlternancia alternancia, Guid programaId)
        {
            if(programaId == Guid.Empty)
                throw new ArgumentException("El ID del programa no puede ser vacío.");
            if(string.IsNullOrWhiteSpace(nombre))
                throw new ArgumentException("El nombre de la asignatura no puede estar vacío.");
            if(string.IsNullOrWhiteSpace(codigo))
                throw new ArgumentException("El código de la asignatura no puede estar vacío.");
            if(bloquesSemanales <= 0)
                throw new ArgumentException("El número de bloques semanales debe ser un valor positivo.");
            // TODO: Definir BloquesSemanales máximo con stakeholders
            // TODO: Código de asignatura: longitud/formato específicos?

            Id = id;
            Nombre = nombre;
            Codigo = codigo;
            BloquesSemanales = bloquesSemanales;
            RequiereLab = requiereLab;
            Alternancia = alternancia;
            ProgramaId = programaId;

        }
        
    }
}