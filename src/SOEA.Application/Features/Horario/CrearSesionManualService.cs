using SOEA.Application.Features.Horario.Requests;
using SOEA.Application.Features.Horario.Responses;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;

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
        private readonly ISesionRepositorio            _sesiones;
        private readonly IAsignacionSemanalRepositorio _asignaciones;
        private readonly IAsignaturaRepositorio?       _asignaturas;

        // asignaturas es opcional (default null → mensaje degradado sin nombre de asignatura)
        // para no romper la firma del constructor en tests existentes que no la proveen.
        public CrearSesionManualService(
            IBloqueTiempoRepositorio     bloques,
            ISesionRepositorio            sesiones,
            IAsignacionSemanalRepositorio asignaciones,
            IAsignaturaRepositorio?       asignaturas = null)
        {
            _bloques      = bloques;
            _sesiones     = sesiones;
            _asignaciones = asignaciones;
            _asignaturas  = asignaturas;
        }

        private async Task<string> NombreAsignaturaAsync(Guid asignaturaId) =>
            _asignaturas is not null ? (await _asignaturas.GetByIdAsync(asignaturaId))?.Nombre ?? "asignatura sin nombre" : "asignatura sin nombre";

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

            // ── Resolver tipo de sesión, modalidad y alternancia ──────────────────
            var tipoFlujo = ParseTipoFlujo(req.TipoFlujo);
            // Teoría virtual es fija e independiente de Alternancia (decisión de diseño): solo el
            // track de laboratorio alterna. Espacio nunca aplica a una sesión virtual (regla 9).
            var modalidad = tipoFlujo == TipoFlujo.AulaVirtual && req.EsVirtual
                ? Modalidad.Virtual
                : Modalidad.Presencial;

            if (!Enum.TryParse<TipoAlternancia>(req.Alternancia, ignoreCase: true, out var alternancia))
                alternancia = TipoAlternancia.SinAlternancia;
            var alternanciaFinal = tipoFlujo == TipoFlujo.Laboratorio ? alternancia : TipoAlternancia.SinAlternancia;

            Guid? espacioFinal = modalidad == Modalidad.Virtual ? null : req.EspacioId;

            // ponytail: HC-S05 (espacio fijo) se validaba aquí contra Asignatura.EspacioFijoId,
            // eliminado — el requisito ahora vive por grupo (Grupo.RequisitosEspacio). Recuperar
            // esta validación cuando CrearSesionManualRequest reciba GrupoId (P2/P4).

            // ── HC-I01: conflicto de docente ──────────────────────────────────────
            // Verificamos a nivel de BloqueTiempoId (slot de inicio). El frontend ya validó
            // el rango completo; aquí hacemos la guardia de BD para el start slot.
            var sesionesDocente = (await _sesiones.GetAllAsync())
                .Where(s => s.DocenteId == req.DocenteId)
                .ToList();

            // Las asignaciones de esas sesiones en el mismo bloque de inicio
            var choqueDocente = sesionesDocente.FirstOrDefault(s => s.BloqueTiempoId == bloque.Id);
            if (choqueDocente is not null)
            {
                var nombreOtra = await NombreAsignaturaAsync(choqueDocente.AsignaturaId);
                throw new InvalidOperationException(
                    $"HC-I01: el docente ya tiene otra sesión que comienza en esa misma franja horaria " +
                    $"({req.Dia} {req.HoraInicio}). Sesión 1: {await NombreAsignaturaAsync(req.AsignaturaId)} " +
                    $"({req.Dia} {req.HoraInicio}). Sesión 2: {nombreOtra} ({req.Dia} {req.HoraInicio}). " +
                    "Elija una hora diferente o cambie el docente de una de las dos sesiones.");
            }

            // ── HC-S01: conflicto de espacio (solo filas presenciales) ────────────
            if (espacioFinal.HasValue)
            {
                var todas = await _sesiones.GetAllAsync();
                var sesionesEnBloque = todas
                    .Where(s => s.BloqueTiempoId == bloque.Id)
                    .ToList();

                // Obtener las asignaciones presenciales en ese bloque para el mismo espacio
                if (sesionesEnBloque.Any())
                {
                    var asignacionesBD = await _asignaciones.GetBySesionIdsAsync(
                        sesionesEnBloque.Select(s => s.Id));

                    // Solo es conflicto si las dos ocupan el aula ALGUNA semana en común: dos
                    // sesiones que alternan en semanas opuestas comparten aula y bloque a propósito.
                    var sesionPorIdBloque = sesionesEnBloque.ToDictionary(x => x.Id);
                    var ocupadaPor = asignacionesBD.FirstOrDefault(a =>
                        a.EspacioId == espacioFinal &&
                        a.Modalidad == Modalidad.Presencial &&
                        sesionPorIdBloque.TryGetValue(a.SesionId, out var ocupante) &&
                        ModalidadSemanal.CompartenSemanaDeEspacio(
                            alternanciaFinal, modalidad, ocupante));

                    if (ocupadaPor is not null)
                    {
                        var sesionOcupante = sesionesEnBloque.First(s => s.Id == ocupadaPor.SesionId);
                        var nombreOcupante = await NombreAsignaturaAsync(sesionOcupante.AsignaturaId);
                        throw new InvalidOperationException(
                            $"HC-S01: el espacio ya está ocupado por otra sesión presencial en esa franja horaria. " +
                            $"Sesión 1: {await NombreAsignaturaAsync(req.AsignaturaId)} ({req.Dia} {req.HoraInicio}). " +
                            $"Sesión 2: {nombreOcupante} ({req.Dia} {req.HoraInicio}). " +
                            "Elija otro espacio u otra hora.");
                    }
                }
            }

            // ── Crear Sesion ──────────────────────────────────────────────────────
            var sesion = new Sesion(
                id:           Guid.NewGuid(),
                asignaturaId: req.AsignaturaId,
                docenteId:    req.DocenteId,
                bloqueId:     bloque.Id,
                espacioId:    espacioFinal,
                grupoId:      null,
                alternancia:  alternanciaFinal,
                modalidad:    modalidad,
                duracionHoras: req.DuracionHoras,
                esBloque:     false,
                estaDividida: false,
                tipoFlujo:    tipoFlujo);

            // ── HC-SEP: separación mínima de días entre sesiones semanales (petición 11) ──
            // ponytail: agrupa por (asignatura, tipo de sesión) — CrearSesionManualRequest no
            // trae GrupoId todavía (mismo gap que el comentario HC-S05 de arriba). Subir a
            // (grupo, asignatura, tipo) cuando el request lo incluya (P2/P4).
            var tipoSesionNueva = CalculadorEspaciosSesion.TipoSesionDe(sesion);
            var mismaAsignaturaYTipo = (await _sesiones.GetAllAsync())
                .Where(s => s.AsignaturaId == req.AsignaturaId && CalculadorEspaciosSesion.TipoSesionDe(s) == tipoSesionNueva)
                .ToList();
            if (mismaAsignaturaYTipo.Count > 0)
            {
                var bloquePorId = (await _bloques.GetAllAsync()).ToDictionary(b => b.Id);
                foreach (var otra in mismaAsignaturaYTipo)
                {
                    if (!bloquePorId.TryGetValue(otra.BloqueTiempoId, out var otroBloque)) continue;
                    if (!ReglasSesion.SeparacionDiasOk(dia, otroBloque.Dia))
                        throw new InvalidOperationException(
                            $"HC-SEP: ya existe otra sesión de esta asignatura/tipo el {otroBloque.Dia}. " +
                            "Las sesiones semanales repetidas necesitan al menos un día de separación.");
                }
            }

            await _sesiones.AddAsync(sesion);

            // ── Crear AsignacionSemanal (una fila: aplica a todas las semanas) ────
            var asignacionesList = CrearAsignaciones(sesion, bloque.Id);
            await _asignaciones.AddRangeAsync(asignacionesList);

            // ── Construir respuesta ───────────────────────────────────────────────
            var horaFin = horaInicio.AddHours((double)req.DuracionHoras).ToString("HH:mm");
            var diaStr  = req.Dia.ToLowerInvariant();
            var labId   = espacioFinal?.ToString();

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
                Alternancia  = alternanciaFinal.ToString(),
                Virtual      = a.Modalidad == Modalidad.Virtual,
                Semana       = alternanciaFinal == TipoAlternancia.SinAlternancia ? string.Empty : a.Semana.ToString(),
                TipoFlujo    = sesion.TipoFlujo.ToString(),
                MotivoConflicto = sesion.MotivoConflicto
            }).ToList();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static TipoFlujo ParseTipoFlujo(string? tipoFlujo) =>
            tipoFlujo?.Trim().ToLowerInvariant() switch
            {
                "laboratorio" => TipoFlujo.Laboratorio,
                _             => TipoFlujo.AulaVirtual   // default: teoría
            };

        /// <summary>
        /// UNA fila por sesión, en su semana canónica (ver <see cref="ModalidadSemanal"/>): la
        /// franja y el aula son un dato único que aplica a todas las semanas (regla 9 / ALT-05).
        /// internal (no private): reutilizado por <see cref="ReacomodarHorarioService"/> (P5) para
        /// reconstruir la fila tras mover una sesión.
        /// </summary>
        internal static List<AsignacionSemanal> CrearAsignaciones(Sesion sesion, Guid bloqueId)
        {
            var modalidad = ModalidadSemanal.ModalidadCanonica(sesion);
            return new List<AsignacionSemanal>
            {
                new(Guid.NewGuid(), sesion.Id, ModalidadSemanal.SemanaCanonica(sesion), bloqueId,
                    modalidad == Modalidad.Presencial ? sesion.EspacioId : null, modalidad)
            };
        }

        /// <summary>internal (no private): reutilizado por <see cref="ReacomodarHorarioService"/> (P5).</summary>
        internal static DiaDeSemana? MapearDia(string dia) =>
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
