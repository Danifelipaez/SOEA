using System;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Asignatura académica de la malla curricular.
    /// Duración fija: el algoritmo la lee, nunca la modifica (decisión inamovible).
    /// Un curso es modular: combina hasta 3 tracks independientes (teoría presencial, teoría
    /// virtual fija, laboratorio), cada uno con su propio número de sesiones/semana y duración.
    /// </summary>
    public class Asignatura : EntidadBase
    {
        public string Nombre { get; private set; } = "";
        public string Codigo { get; private set; } = "";

        /// <summary>Sesiones de teoría presencial por semana.</summary>
        public int SesionesTeoriaPresencialSemana { get; private set; }

        /// <summary>Duración en horas de cada sesión de teoría presencial.</summary>
        public int HorasTeoriaPresencial { get; private set; }

        /// <summary>
        /// Sesiones de teoría virtual por semana. Modo fijo e independiente de <see cref="Alternancia"/>:
        /// siempre se dictan en modalidad virtual, sin contraparte presencial ni toggle semanal.
        /// </summary>
        public int SesionesTeoriaVirtualSemana { get; private set; }

        /// <summary>Duración en horas de cada sesión de teoría virtual.</summary>
        public int HorasTeoriaVirtual { get; private set; }

        /// <summary>
        /// Sesiones de laboratorio por semana. Distinto de <see cref="SesionesLaboratorioSemestre"/>
        /// (total semestral, solo alimenta la inferencia de <see cref="Alternancia"/>). Es el único
        /// track sujeto a Alternancia (TipoA/TipoB).
        /// </summary>
        public int SesionesLaboratorioSemana { get; private set; }

        /// <summary>Duración en horas de cada sesión de laboratorio.</summary>
        public int HorasLaboratorio { get; private set; }

        /// <summary>
        /// Cantidad de sesiones de laboratorio que requiere en el semestre (total, no semanal).
        /// Alimenta únicamente <see cref="DeterminarAlternancia"/> — no genera sesiones por sí sola.
        /// </summary>
        public int SesionesLaboratorioSemestre { get; private set; }

        public TipoAlternancia Alternancia { get; private set; }
        public Guid ProgramaId { get; private set; }
        // El docente ya NO vive en la asignatura: se movió a Grupo (la misma asignatura la dictan
        // docentes distintos en grupos distintos). Ver Grupo.DocenteId.

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

        /// <summary>
        /// Marca la asignatura como candidata a ceder a alternancia si el algoritmo agota el espacio
        /// físico disponible (cesión por saturación de espacio). Lo fija el departamento dueño de la
        /// asignatura — no es automático ni depende de la categoría curricular.
        /// </summary>
        public bool EsCandidataAlternancia { get; private set; }

        /// <summary>Alias legado de <see cref="HorasTeoriaPresencial"/>. Lo lee ImportarCurriculumService
        /// sobre entidades construidas por LectorExcel (shape legado) — no usar en código nuevo.</summary>
        public int HorasPorSesion => HorasTeoriaPresencial;

        /// <summary>Alias legado de <see cref="SesionesTeoriaPresencialSemana"/>. Ídem <see cref="HorasPorSesion"/>.</summary>
        public int SesionesPorSemana => SesionesTeoriaPresencialSemana;

        // Constructor privado para EF Core
        private Asignatura() : base() { }

        /// <summary>Umbral por defecto de sesiones de lab/semestre que distingue TipoA de TipoB.</summary>
        public const int UmbralTipoAPorDefecto = 8;

        /// <summary>Horas por defecto para un track que el shape legado no especifica.</summary>
        public const int HorasPorSesionPorDefecto = 2;

        public Asignatura(
            Guid id,
            string nombre,
            string codigo,
            int sesionesTeoriaPresencialSemana,
            int horasTeoriaPresencial,
            int sesionesTeoriaVirtualSemana,
            int horasTeoriaVirtual,
            int sesionesLaboratorioSemana,
            int horasLaboratorio,
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

            Validar(nombre, codigo,
                sesionesTeoriaPresencialSemana, horasTeoriaPresencial,
                sesionesTeoriaVirtualSemana, horasTeoriaVirtual,
                sesionesLaboratorioSemana, horasLaboratorio,
                programaId);
            ValidarVentana(horaInicioMin, horaFinMax);

            Nombre = nombre;
            Codigo = codigo;
            SesionesTeoriaPresencialSemana = sesionesTeoriaPresencialSemana;
            HorasTeoriaPresencial = horasTeoriaPresencial;
            SesionesTeoriaVirtualSemana = sesionesTeoriaVirtualSemana;
            HorasTeoriaVirtual = horasTeoriaVirtual;
            SesionesLaboratorioSemana = sesionesLaboratorioSemana;
            HorasLaboratorio = horasLaboratorio;
            SesionesLaboratorioSemestre = sesionesLaboratorioSemestre;
            Alternancia = DeterminarAlternancia(sesionesLaboratorioSemestre, umbralTipoA);
            ProgramaId = programaId;
            Categoria = categoria;
            HoraInicioMin = horaInicioMin;
            HoraFinMax = horaFinMax;
        }

        /// <summary>
        /// Shape legado (pre-desglose por tipo de sesión): un solo bloque de sesiones, mapeado como
        /// teoría presencial. Lo usan LectorExcel / ImportarCurriculumService / ImportController
        /// (carga por Excel — fuera de alcance del desglose por tipo). No usar en código nuevo.
        /// </summary>
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
            TimeOnly? horaFinMax = null)
            : this(id, nombre, codigo,
                   sesionesTeoriaPresencialSemana: sesionesPorSemana, horasTeoriaPresencial: horasPorSesion,
                   sesionesTeoriaVirtualSemana: 0, horasTeoriaVirtual: HorasPorSesionPorDefecto,
                   sesionesLaboratorioSemana: 0, horasLaboratorio: HorasPorSesionPorDefecto,
                   sesionesLaboratorioSemestre: sesionesLaboratorioSemestre, programaId: programaId,
                   umbralTipoA: umbralTipoA, categoria: categoria, horaInicioMin: horaInicioMin, horaFinMax: horaFinMax)
        {
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
        /// Fija si la asignatura es candidata a ceder a alternancia por saturación de espacio
        /// (ver <see cref="EsCandidataAlternancia"/>).
        /// </summary>
        public void EstablecerElegibilidadAlternancia(bool elegible)
        {
            EsCandidataAlternancia = elegible;
        }

        /// <summary>
        /// Actualiza datos desde la UI (ingesta) manteniendo validaciones de dominio.
        /// La duracion es un input fijo para el algoritmo, pero puede ser editada por el usuario.
        /// </summary>
        public void ActualizarDatos(
            string nombre,
            string? codigo,
            int sesionesTeoriaPresencialSemana,
            int horasTeoriaPresencial,
            int sesionesTeoriaVirtualSemana,
            int horasTeoriaVirtual,
            int sesionesLaboratorioSemana,
            int horasLaboratorio,
            int sesionesLaboratorioSemestre,
            Guid programaId,
            TipoAlternancia? alternanciaExplicita = null,
            int umbralTipoA = UmbralTipoAPorDefecto,
            CategoriaAsignatura? categoria = null,
            TimeOnly? horaInicioMin = null,
            TimeOnly? horaFinMax = null)
        {
            var codigoFinal = string.IsNullOrWhiteSpace(codigo) ? Codigo : codigo;
            Validar(nombre, codigoFinal,
                sesionesTeoriaPresencialSemana, horasTeoriaPresencial,
                sesionesTeoriaVirtualSemana, horasTeoriaVirtual,
                sesionesLaboratorioSemana, horasLaboratorio,
                programaId);
            ValidarVentana(horaInicioMin, horaFinMax);

            Nombre = nombre;
            Codigo = codigoFinal;
            SesionesTeoriaPresencialSemana = sesionesTeoriaPresencialSemana;
            HorasTeoriaPresencial = horasTeoriaPresencial;
            SesionesTeoriaVirtualSemana = sesionesTeoriaVirtualSemana;
            HorasTeoriaVirtual = horasTeoriaVirtual;
            SesionesLaboratorioSemana = sesionesLaboratorioSemana;
            HorasLaboratorio = horasLaboratorio;
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
        /// Shape legado de <see cref="ActualizarDatos"/> — ver constructor legado. No usar en código nuevo.
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
            ActualizarDatos(nombre, codigo,
                sesionesTeoriaPresencialSemana: sesionesPorSemana, horasTeoriaPresencial: horasPorSesion,
                sesionesTeoriaVirtualSemana: 0, horasTeoriaVirtual: HorasPorSesionPorDefecto,
                sesionesLaboratorioSemana: 0, horasLaboratorio: HorasPorSesionPorDefecto,
                sesionesLaboratorioSemestre: sesionesLaboratorioSemestre, programaId: programaId,
                alternanciaExplicita: alternanciaExplicita, umbralTipoA: umbralTipoA, categoria: categoria,
                horaInicioMin: horaInicioMin, horaFinMax: horaFinMax);
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

        private static void Validar(
            string nombre, string codigo,
            int sesionesTeoriaPresencialSemana, int horasTeoriaPresencial,
            int sesionesTeoriaVirtualSemana, int horasTeoriaVirtual,
            int sesionesLaboratorioSemana, int horasLaboratorio,
            Guid programaId)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                throw new ArgumentException("El nombre de la asignatura no puede estar vacío.");
            if (string.IsNullOrWhiteSpace(codigo))
                throw new ArgumentException("El código de la asignatura no puede estar vacío.");
            if (sesionesTeoriaPresencialSemana < 0 || sesionesTeoriaVirtualSemana < 0 || sesionesLaboratorioSemana < 0)
                throw new ArgumentException("Las sesiones por semana no pueden ser negativas.");
            if (sesionesTeoriaPresencialSemana == 0 && sesionesTeoriaVirtualSemana == 0 && sesionesLaboratorioSemana == 0)
                throw new ArgumentException("La asignatura debe tener al menos un tipo de sesión (teoría presencial, teoría virtual o laboratorio) con al menos una sesión por semana.");
            if (sesionesTeoriaPresencialSemana > 0 && horasTeoriaPresencial <= 0)
                throw new ArgumentException("Las horas por sesión de teoría presencial deben ser un valor positivo.");
            if (sesionesTeoriaVirtualSemana > 0 && horasTeoriaVirtual <= 0)
                throw new ArgumentException("Las horas por sesión de teoría virtual deben ser un valor positivo.");
            if (sesionesLaboratorioSemana > 0 && horasLaboratorio <= 0)
                throw new ArgumentException("Las horas por sesión de laboratorio deben ser un valor positivo.");
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
