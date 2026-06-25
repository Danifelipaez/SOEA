using SOEA.Application.Features.Horario.Requests;
using SOEA.Application.Features.Horario.Responses;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;

namespace SOEA.Application.Features.Horario
{
    /// <summary>
    /// Crea una sesión manual sin re-ejecutar el modelo de optimización.
    /// Valida HC-I01 (docente libre), HC-S01 (espacio libre) y HC-S05 (espacio fijo)
    /// contra el estado actual de la BD antes de persistir.
    /// </summary>
    public class CrearSesionManualService
    {
        private readonly IBloqueTiempoRepositorio     _bloques;
        private readonly IAsignaturaRepositorio        _asignaturas;
        private readonly ISesionRepositorio            _sesiones;
        private readonly IAsignacionSemanalRepositorio _asignaciones;

        public CrearSesionManualService(
            IBloqueTiempoRepositorio     bloques,
            IAsignaturaRepositorio        asignaturas,
            ISesionRepositorio            sesiones,
            IAsignacionSemanalRepositorio asignaciones)
        {
            _bloques      = bloques;
            _asignaturas  = asignaturas;
            _sesiones     = sesiones;
            _asignaciones = asignaciones;
        }

        /// <returns>
        /// Las 1 o 2 filas (<see cref="SesionGeneradaDto"/>) creadas, listas para el frontend.
        /// En caso de violación de hard constraint lanza <see cref="InvalidOperationException"/>
        /// con mensaje descriptivo en español.
        /// </returns>
        public async Task<List<SesionGeneradaDto>> EjecutarAsync(CrearSesionManualRequest req)
        {
            // ── Mapear día ────────────────────────────────────────────────────────
            var dia = MapearDia(req.Dia)
                ?? throw new ArgumentException($"Día no reconocido: '{req.Dia}'. Use lunes, martes, miercoles, jueves, viernes o sabado.");

            // ── Buscar BloqueTiempo ───────────────────────────────────────────────
            if (!TimeOnly.TryParse(req.HoraInicio, out var horaInicio))
                throw new ArgumentException($"Hora no válida: '{req.HoraInicio}'. Formato esperado HH:mm.");

            var bloque = await _bloques.FindByDiaHoraAsync(dia, horaInicio)
                ?? throw new InvalidOperationException(
                    $"No existe franja horaria para {req.Dia} a las {req.HoraInicio}. " +
                    "Verifique que la hora esté dentro del horario académico (06:00–20:00 L-V, 06:00–13:00 Sáb).");

            // ── Resolver alternancia ──────────────────────────────────────────────
            if (!Enum.TryParse<TipoAlternancia>(req.Alternancia, ignoreCase: true, out var alternancia))
                alternancia = TipoAlternancia.SinAlternancia;

            // ── HC-S05: espacio fijo ──────────────────────────────────────────────
            var asignatura = await _asignaturas.GetByIdAsync(req.AsignaturaId);
            if (asignatura?.EspacioFijoId is Guid espacioFijo &&
                req.EspacioId.HasValue &&
                req.EspacioId.Value != espacioFijo)
            {
                throw new InvalidOperationException(
                    "HC-S05: Esta asignatura tiene un laboratorio fijo asignado en el currículum. " +
                    "La sesión debe crearse en ese mismo laboratorio.");
            }

            // ── HC-I01: conflicto de docente ──────────────────────────────────────
            // Verificamos a nivel de BloqueTiempoId (slot de inicio). El frontend ya validó
            // el rango completo; aquí hacemos la guardia de BD para el start slot.
            var sesionesDocente = (await _sesiones.GetAllAsync())
                .Where(s => s.DocenteId == req.DocenteId)
                .ToList();

            // Las asignaciones de esas sesiones en el mismo bloque de inicio
            if (sesionesDocente.Any(s => s.BloqueTiempoId == bloque.Id))
                throw new InvalidOperationException(
                    "HC-I01: El docente ya tiene otra sesión que comienza en esa misma franja horaria. " +
                    "Elija una hora diferente o cambie el docente.");

            // ── HC-S01: conflicto de espacio (solo filas presenciales) ────────────
            if (req.EspacioId.HasValue)
            {
                var idsEnBloque = sesionesDocente.Select(s => s.Id).ToHashSet();
                var todas = await _sesiones.GetAllAsync();
                var sesionesEnBloque = todas
                    .Where(s => s.BloqueTiempoId == bloque.Id)
                    .ToList();

                // Obtener las asignaciones presenciales en ese bloque para el mismo espacio
                if (sesionesEnBloque.Any())
                {
                    var asignacionesBD = await _asignaciones.GetBySesionIdsAsync(
                        sesionesEnBloque.Select(s => s.Id));

                    bool ocupado = asignacionesBD.Any(a =>
                        a.EspacioId == req.EspacioId &&
                        a.Modalidad == Modalidad.Presencial);

                    if (ocupado)
                        throw new InvalidOperationException(
                            "HC-S01: El laboratorio ya está ocupado por otra sesión presencial en esa franja horaria. " +
                            "Elija otro laboratorio u otra hora.");
                }
            }

            // ── Crear Sesion ──────────────────────────────────────────────────────
            var sesion = new Sesion(
                id:           Guid.NewGuid(),
                asignaturaId: req.AsignaturaId,
                docenteId:    req.DocenteId,
                bloqueId:     bloque.Id,
                espacioId:    req.EspacioId,
                grupoId:      null,
                alternancia:  alternancia,
                modalidad:    Modalidad.Presencial,
                duracionHoras: req.DuracionHoras,
                esBloque:     false,
                estaDividida: false);

            await _sesiones.AddAsync(sesion);

            // ── Crear AsignacionSemanal (1 o 2 filas según alternancia) ───────────
            var asignacionesList = CrearAsignaciones(sesion, bloque.Id, req.EspacioId, alternancia);
            await _asignaciones.AddRangeAsync(asignacionesList);

            // ── Construir respuesta ───────────────────────────────────────────────
            var horaFin = horaInicio.AddHours((double)req.DuracionHoras).ToString("HH:mm");
            var diaStr  = req.Dia.ToLowerInvariant();
            var labId   = req.EspacioId?.ToString();

            return asignacionesList.Select(a => new SesionGeneradaDto
            {
                Id           = sesion.Id.ToString(),
                AsignaturaId = sesion.AsignaturaId.ToString(),
                DocenteId    = sesion.DocenteId?.ToString() ?? string.Empty,
                EspacioId    = a.EspacioId?.ToString(),
                EspacioIdHogar = labId,           // lab de origen (aunque sea virtual)
                Dia          = diaStr,
                HoraInicio   = req.HoraInicio,
                HoraFin      = horaFin,
                DuracionHoras = req.DuracionHoras,
                Alternancia  = alternancia.ToString(),
                Virtual      = a.Modalidad == Modalidad.Virtual,
                Semana       = a.Semana.ToString()
            }).ToList();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static List<AsignacionSemanal> CrearAsignaciones(
            Sesion sesion, Guid bloqueId, Guid? espacioId, TipoAlternancia alternancia)
        {
            Modalidad ModalidadParaSemana(SemanaAcademica s) =>
                alternancia switch
                {
                    TipoAlternancia.TipoA => s == SemanaAcademica.A ? Modalidad.Presencial : Modalidad.Virtual,
                    TipoAlternancia.TipoB => s == SemanaAcademica.B ? Modalidad.Presencial : Modalidad.Virtual,
                    _                     => Modalidad.Presencial    // SinAlternancia → siempre presencial
                };

            return new List<AsignacionSemanal>
            {
                new(Guid.NewGuid(), sesion.Id, SemanaAcademica.A, bloqueId,
                    ModalidadParaSemana(SemanaAcademica.A) == Modalidad.Presencial ? espacioId : null,
                    ModalidadParaSemana(SemanaAcademica.A)),

                new(Guid.NewGuid(), sesion.Id, SemanaAcademica.B, bloqueId,
                    ModalidadParaSemana(SemanaAcademica.B) == Modalidad.Presencial ? espacioId : null,
                    ModalidadParaSemana(SemanaAcademica.B))
            };
        }

        private static DiaDeSemana? MapearDia(string dia) =>
            dia.ToLowerInvariant().Trim() switch
            {
                "lunes"     => DiaDeSemana.Lunes,
                "martes"    => DiaDeSemana.Martes,
                "miercoles" => DiaDeSemana.Miercoles,
                "jueves"    => DiaDeSemana.Jueves,
                "viernes"   => DiaDeSemana.Viernes,
                "sabado"    => DiaDeSemana.Sábado,
                _           => null
            };
    }
}
