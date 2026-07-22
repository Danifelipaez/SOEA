using System;
using System.Collections.Generic;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Grupo (sección) de estudiantes inscritos en una Asignatura concreta.
    /// Un grupo = una asignatura + N estudiantes + disponibilidad horaria.
    /// La disponibilidad es el eje de optimización del pipeline presencial-first (HC-G01):
    /// el sistema NO puede asignar sesiones fuera del rango declarado.
    /// </summary>
    public class Grupo : EntidadBase
    {
        // ── Identidad ────────────────────────────────────────────────────────────
        /// <summary>Código único del grupo, e.g. "QO-I-2024-A".</summary>
        public string? Codigo { get; private set; }
        public string Nombre { get; private set; } = "";

        // ── Relaciones ───────────────────────────────────────────────────────────
        /// <summary>Asignatura a la que pertenece este grupo (eje del modelo presencial-first).</summary>
        public Guid? AsignaturaId { get; private set; }
        /// <summary>Facultad a la que pertenece; desnormalizado para acceso directo.</summary>
        public Guid? FacultadId { get; private set; }
        /// <summary>Programa académico del grupo (heredado del modelo anterior, se conserva).</summary>
        public Guid ProgramaId { get; private set; }
        /// <summary>
        /// Docente que dicta la asignatura para ESTE grupo. La relación docente↔grupo (no
        /// docente↔asignatura) es la correcta: la misma asignatura la dictan docentes distintos
        /// en grupos distintos del mismo programa. Opcional: el docente se puede asignar después.
        /// La pipeline de generación lo ignora (el docente se agenda tras generar).
        /// </summary>
        public Guid? DocenteId { get; private set; }

        // ── Datos académicos ─────────────────────────────────────────────────────
        public int EstudiantesInscritos { get; private set; }
        public TipoAlternancia Alternancia { get; private set; }

        // ── Disponibilidad (eje de optimización, HC-G01) ─────────────────────────
        /// <summary>
        /// Franjas en las que el grupo puede recibir clases (Matutino / Vespertino).
        /// Lista vacía = sin restricción de franja (equivalente a "cualquier hora").
        /// HC-G01 (hard): CP-SAT rechaza slots fuera de esta disponibilidad.
        /// </summary>
        public List<FranjaHoraria> Disponibilidad { get; private set; } = new();

        /// <summary>JSON crudo con la disponibilidad por día ingresada desde la UI.</summary>
        public string? DisponibilidadUiJson { get; private set; }

        // ── Constructores ─────────────────────────────────────────────────────────
        private Grupo() : base() { }

        public Grupo(
            Guid id,
            string nombre,
            Guid programaId,
            int estudiantesInscritos,
            TipoAlternancia alternancia = TipoAlternancia.SinAlternancia,
            string? codigo = null,
            Guid? asignaturaId = null,
            Guid? facultadId = null,
            Guid? docenteId = null,
            List<FranjaHoraria>? disponibilidad = null) : base(id)
        {
            Validar(nombre, estudiantesInscritos);

            Codigo              = codigo;
            Nombre              = nombre;
            ProgramaId          = programaId;
            EstudiantesInscritos = estudiantesInscritos;
            Alternancia         = alternancia;
            AsignaturaId        = asignaturaId;
            FacultadId          = facultadId;
            DocenteId           = docenteId;
            Disponibilidad      = disponibilidad ?? new();
        }

        // ── Mutadores ─────────────────────────────────────────────────────────────

        public void ActualizarCodigo(string? codigo) => Codigo = codigo;

        public void ActualizarNombre(string nuevoNombre)
        {
            if (string.IsNullOrWhiteSpace(nuevoNombre))
                throw new ArgumentException("El nombre del grupo no puede estar vacío.");
            Nombre = nuevoNombre;
        }

        public void ActualizarAsignatura(Guid? asignaturaId, Guid? facultadId)
        {
            AsignaturaId = asignaturaId;
            FacultadId   = facultadId;
        }

        public void ActualizarEstudiantes(int nuevaCantidad)
        {
            if (nuevaCantidad <= 0)
                throw new ArgumentException("La cantidad de estudiantes debe ser un valor positivo.");
            EstudiantesInscritos = nuevaCantidad;
        }

        public void ActualizarPrograma(Guid programaId) => ProgramaId = programaId;

        /// <summary>Asigna (o desasigna con null) el docente que dicta la asignatura para este grupo.</summary>
        public void AsignarDocente(Guid? docenteId) => DocenteId = docenteId;

        public void ActualizarAlternancia(TipoAlternancia nuevaAlternancia) =>
            Alternancia = nuevaAlternancia;

        /// <summary>
        /// Establece la disponibilidad horaria del grupo (eje HC-G01).
        /// Lista vacía = sin restricción de franja.
        /// </summary>
        public void ActualizarDisponibilidad(List<FranjaHoraria> disponibilidad)
        {
            Disponibilidad = disponibilidad ?? new();
        }

        public void ActualizarDisponibilidadUi(string? disponibilidadUiJson)
        {
            DisponibilidadUiJson = disponibilidadUiJson;
        }

        // ── Validación ────────────────────────────────────────────────────────────
        private static void Validar(string nombre, int estudiantesInscritos)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                throw new ArgumentException("El nombre del grupo no puede estar vacío.");
            if (estudiantesInscritos <= 0)
                throw new ArgumentException("La cantidad de estudiantes debe ser un valor positivo.");
        }
    }
}
