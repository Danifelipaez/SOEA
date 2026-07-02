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
        public Guid? DocenteId { get; private set; }

        /// <summary>
        /// Espacio físico específico al que está asignada esta asignatura (proveniente del Excel de curriculum).
        /// Cuando está presente, el algoritmo DEBE asignar sus sesiones presenciales a este espacio (HC-S05).
        /// </summary>
        public Guid? EspacioFijoId { get; private set; }

        /// <summary>
        /// Categoría curricular que rige la prioridad de presencialidad (presencial-first).
        /// Andamiaje del modelo: la lógica de orden (SC-PRES) se implementa en etapas posteriores.
        /// </summary>
        public CategoriaAsignatura Categoria { get; private set; }

        /// <summary>
        /// Ventana horaria definida por la Secretaría Académica (CR-07). Acota la generación al rango
        /// [HoraInicioMin, HoraFinMax]. Null = sin acotamiento por asignatura (rigen los límites operativos globales).
        /// </summary>
        public TimeOnly? HoraInicioMin { get; private set; }
        public TimeOnly? HoraFinMax { get; private set; }

        // Constructor privado para EF Core
        private Asignatura() : base() { }

        /// <summary>Umbral por defecto de sesiones de lab/semestre que distingue TipoA de TipoB.</summary>
        public const int UmbralTipoAPorDefecto = 8;

        public Asignatura(
            Guid id,
            string nombre,
            string codigo,
            int horasPorSesion,
            int sesionesPorSemana,
            int sesionesLaboratorioSemestre,
            Guid programaId,
            int umbralTipoA = UmbralTipoAPorDefecto,
            CategoriaAsignatura categoria = CategoriaAsignatura.Obligatoria,
            TimeOnly? horaInicioMin = null,
            TimeOnly? horaFinMax = null) : base(id)
        {
            if (string.IsNullOrWhiteSpace(codigo))
            {
                // Generar código dummy único temporal
                var prefix = string.IsNullOrWhiteSpace(nombre) ? "UNK" : nombre.Substring(0, Math.Min(nombre.Length, 3)).ToUpperInvariant();
                codigo = $"{prefix}-{Guid.NewGuid().ToString().Substring(0, 5)}";
            }

            Validar(nombre, codigo, horasPorSesion, sesionesPorSemana, programaId);
            ValidarVentana(horaInicioMin, horaFinMax);

            Nombre = nombre;
            Codigo = codigo;
            HorasPorSesion = horasPorSesion;
            SesionesPorSemana = sesionesPorSemana;
            SesionesLaboratorioSemestre = sesionesLaboratorioSemestre;
            Alternancia = DeterminarAlternancia(sesionesLaboratorioSemestre, umbralTipoA);
            ProgramaId = programaId;
            Categoria = categoria;
            HoraInicioMin = horaInicioMin;
            HoraFinMax = horaFinMax;
        }

        public void AsignarDocente(Guid? docenteId)
        {
            DocenteId = docenteId;
        }

        public void AsignarEspacioFijo(Guid? espacioId)
        {
            EspacioFijoId = espacioId;
        }

        public void EstablecerCategoria(CategoriaAsignatura categoria)
        {
            Categoria = categoria;
        }

        /// <summary>
        /// Fija la ventana horaria por asignatura (Secretaría Académica, CR-07).
        /// Pasar (null, null) elimina el acotamiento.
        /// </summary>
        public void EstablecerVentanaHoraria(TimeOnly? horaInicioMin, TimeOnly? horaFinMax)
        {
            ValidarVentana(horaInicioMin, horaFinMax);
            HoraInicioMin = horaInicioMin;
            HoraFinMax = horaFinMax;
        }

        /// <summary>
        /// Override manual del tipo de alternancia. La lógica de Tipos es dinámica (p. ej. "solo
        /// química orgánica es TipoA"): este método permite fijar el tipo explícitamente, ignorando
        /// la inferencia por umbral. Lo usan la ingesta y el editor de tipos.
        /// </summary>
        public void EstablecerAlternancia(TipoAlternancia tipo)
        {
            Alternancia = tipo;
        }

        /// <summary>
        /// Actualiza datos desde la UI (ingesta) manteniendo validaciones de dominio.
        /// La duracion es un input fijo para el algoritmo, pero puede ser editada por el usuario.
        /// </summary>
        public void ActualizarDatos(
            string nombre,
            string? codigo,
            int horasPorSesion,
            int sesionesPorSemana,
            int sesionesLaboratorioSemestre,
            Guid programaId,
            TipoAlternancia? alternanciaExplicita = null,
            int umbralTipoA = UmbralTipoAPorDefecto,
            CategoriaAsignatura? categoria = null,
            TimeOnly? horaInicioMin = null,
            TimeOnly? horaFinMax = null)
        {
            var codigoFinal = string.IsNullOrWhiteSpace(codigo) ? Codigo : codigo;
            Validar(nombre, codigoFinal, horasPorSesion, sesionesPorSemana, programaId);
            ValidarVentana(horaInicioMin, horaFinMax);

            Nombre = nombre;
            Codigo = codigoFinal;
            HorasPorSesion = horasPorSesion;
            SesionesPorSemana = sesionesPorSemana;
            SesionesLaboratorioSemestre = sesionesLaboratorioSemestre;
            // Si se pasa un tipo explícito (override manual) se respeta; si no, se infiere por umbral.
            Alternancia = alternanciaExplicita ?? DeterminarAlternancia(sesionesLaboratorioSemestre, umbralTipoA);
            ProgramaId = programaId;
            // Si no se especifica categoría o ventana horaria, se conserva la actual
            // (evita que un PUT de campos no relacionados borre la ventana CR-07 fijada por Excel/otro flujo).
            Categoria = categoria ?? Categoria;
            HoraInicioMin = horaInicioMin ?? HoraInicioMin;
            HoraFinMax = horaFinMax ?? HoraFinMax;
        }

        /// <summary>
        /// Inferencia por defecto del tipo de alternancia a partir de las sesiones de lab/semestre.
        /// El umbral es configurable (la institución prueba 8/8, 8/11, etc.): por debajo del umbral
        /// no hay alternancia; igual al umbral ⇒ TipoA; por encima ⇒ TipoB.
        /// </summary>
        private static TipoAlternancia DeterminarAlternancia(int sesionesLaboratorioSemestre, int umbralTipoA)
        {
            if (sesionesLaboratorioSemestre == umbralTipoA)
                return TipoAlternancia.TipoA;
            if (sesionesLaboratorioSemestre > umbralTipoA)
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

        /// <summary>
        /// La ventana horaria es opcional, pero si ambos extremos están presentes el inicio debe
        /// ser estrictamente anterior al fin (guardrail de integridad de datos).
        /// </summary>
        private static void ValidarVentana(TimeOnly? horaInicioMin, TimeOnly? horaFinMax)
        {
            if (horaInicioMin.HasValue && horaFinMax.HasValue && horaInicioMin.Value >= horaFinMax.Value)
                throw new ArgumentException("La hora de inicio de la ventana debe ser anterior a la hora de fin.");
        }
    }
}
