using SOEA.Application.Features.Horario.Requests;
using SOEA.Application.Features.Horario.Responses;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;

namespace SOEA.Application.Features.Horario
{
    /// <summary>
    /// Petición 13 — recálculo mínimo al editar: mueve una sesión ya generada a un nuevo
    /// (día, hora, espacio) sin volver a ejecutar el pipeline completo. Solo la sesión editada
    /// y las que ahora chocan con ella (mismo grupo o mismo espacio, franjas solapadas) se
    /// recalculan; el resto conserva EXACTAMENTE su AsignacionSemanal previa — esto se garantiza
    /// aquí mismo (no confiando en que CP-SAT respete la igualdad de espacio de las fijas, que
    /// solo fija el bloque — ver MotorConstraintProgramming), tomando del resultado del solver
    /// únicamente las filas de la editada y las liberadas.
    /// </summary>
    public class ReacomodarHorarioService
    {
        private readonly IHorarioRepositorio           _horarioRepo;
        private readonly ISesionRepositorio             _sesionRepo;
        private readonly IAsignacionSemanalRepositorio  _asignacionRepo;
        private readonly IEspacioRepositorio            _espacioRepo;
        private readonly IDocenteRepositorio            _docenteRepo;
        private readonly IGrupoRepositorio              _grupoRepo;
        private readonly IAsignaturaRepositorio         _asignaturaRepo;
        private readonly IMotorConstraintProgramming    _fase2;
        private readonly IUnitOfWork                    _uow;

        public ReacomodarHorarioService(
            IHorarioRepositorio          horarioRepo,
            ISesionRepositorio           sesionRepo,
            IAsignacionSemanalRepositorio asignacionRepo,
            IEspacioRepositorio          espacioRepo,
            IDocenteRepositorio          docenteRepo,
            IGrupoRepositorio            grupoRepo,
            IAsignaturaRepositorio       asignaturaRepo,
            IMotorConstraintProgramming  fase2,
            IUnitOfWork                  uow)
        {
            _horarioRepo    = horarioRepo;
            _sesionRepo     = sesionRepo;
            _asignacionRepo = asignacionRepo;
            _espacioRepo    = espacioRepo;
            _docenteRepo    = docenteRepo;
            _grupoRepo      = grupoRepo;
            _asignaturaRepo = asignaturaRepo;
            _fase2          = fase2;
            _uow            = uow;
        }

        public async Task<ReacomodarHorarioResponse> EjecutarAsync(ReacomodarHorarioRequest req, CancellationToken ct = default)
        {
            var horario = await _horarioRepo.GetByIdAsync(req.HorarioId)
                ?? throw new KeyNotFoundException("No se encontró el horario indicado. Puede haber sido regenerado — recargue la vista de horario.");

            var sesiones = new List<Sesion>();
            foreach (var id in horario.SesioneIds)
            {
                var s = await _sesionRepo.GetByIdAsync(id);
                if (s != null) sesiones.Add(s);
            }

            var sesionEditada = sesiones.FirstOrDefault(s => s.Id == req.SesionEditadaId)
                ?? throw new KeyNotFoundException("La sesión editada no pertenece al horario actual. Puede haber sido regenerado — recargue la vista de horario.");

            var dia = CrearSesionManualService.MapearDia(req.Dia)
                ?? throw new ArgumentException($"Día no reconocido: '{req.Dia}'.");
            if (!TimeOnly.TryParse(req.HoraInicio, out var horaInicio))
                throw new ArgumentException($"Hora no válida: '{req.HoraInicio}'. Formato esperado HH:mm.");

            // ponytail: mover a un espacio concreto está soportado; "vaciar" el espacio de una
            // sesión presencial no (Sesion no expone esa mutación) — EspacioId vacío/null aquí
            // significa "no tocar el espacio actual", no "volverla virtual".
            Guid? espacioNuevo = Guid.TryParse(req.EspacioId, out var eid) ? eid : null;

            // Grilla canónica completa, regenerada aquí igual que en GenerarHorarioService — sus
            // ids son determinísticos por (día, hora) (GrillaInstitucional.IdDeterministico), así
            // que coinciden con los BloqueTiempoId que ya quedaron persistidos en una corrida previa.
            var bloquesGrid = GrillaInstitucional.GenerarBloques();
            var idxPorBloque = Enumerable.Range(0, bloquesGrid.Count).ToDictionary(i => bloquesGrid[i].Id, i => i);
            var bloqueNuevo = bloquesGrid.FirstOrDefault(b => b.Dia == dia && b.HoraInicio == horaInicio)
                ?? throw new ArgumentException($"({req.Dia} {req.HoraInicio}) no coincide con ningún bloque de la grilla canónica.");
            var inicioEditada = idxPorBloque[bloqueNuevo.Id];

            var asignacionesActuales = await _asignacionRepo.GetBySesionIdsAsync(sesiones.Select(s => s.Id));
            var asignPorSesion = asignacionesActuales.GroupBy(a => a.SesionId).ToDictionary(g => g.Key, g => g.ToList());

            // ── Detectar SOLO las sesiones que ahora chocan con la editada en su nuevo slot ──
            int duracionEditada = Math.Max(1, (int)Math.Ceiling(sesionEditada.DuracionHoras));
            int finEditada = inicioEditada + duracionEditada;
            bool Solapa(int inicio, int duracion) => inicio < finEditada && inicioEditada < inicio + duracion;

            var freedIds = new HashSet<Guid>();
            foreach (var s in sesiones.Where(s => s.Id != sesionEditada.Id))
            {
                if (!asignPorSesion.TryGetValue(s.Id, out var asigs)) continue;
                int dur = Math.Max(1, (int)Math.Ceiling(s.DuracionHoras));
                bool choca = asigs.Any(a =>
                    idxPorBloque.TryGetValue(a.BloqueTiempoId, out var inicio) &&
                    Solapa(inicio, dur) &&
                    // Cohorte: independiente de la semana (la sesión ocupa el tiempo del grupo
                    // todas las semanas, presencial o virtualmente).
                    ((s.GrupoId.HasValue && s.GrupoId == sesionEditada.GrupoId) ||
                     // Aula: solo si ambas la ocupan alguna semana en común. Una pareja de
                     // alternancia comparte aula y bloque a propósito y no es conflicto.
                     (espacioNuevo.HasValue && a.EspacioId == espacioNuevo &&
                      ModalidadSemanal.CompartenSemanaDeEspacio(s, sesionEditada))));
                if (choca) freedIds.Add(s.Id);
            }

            sesionEditada.AsignarBloqueTiempo(bloqueNuevo.Id);
            if (espacioNuevo.HasValue) sesionEditada.AsignarEspacio(espacioNuevo.Value);

            var advertencias = new List<string>();
            List<AsignacionSemanal> nuevasAsignaciones;

            if (freedIds.Count == 0)
            {
                // Camino común: nada choca, no hace falta invocar CP-SAT — se reconstruye
                // directamente la fila de la editada, igual que una sesión manual.
                nuevasAsignaciones = CrearSesionManualService.CrearAsignaciones(sesionEditada, bloqueNuevo.Id);
            }
            else
            {
                var espacios    = await _espacioRepo.GetAllAsync();
                var gruposMotor = await _grupoRepo.GetAllAsync();
                var asignaturas = await _asignaturaRepo.GetAllAsync();
                var ventanaPorAsig = asignaturas.ToDictionary(
                    a => a.Id, a => ((TimeOnly?)a.HoraInicioMin, (TimeOnly?)a.HoraFinMax));

                // Regla 8 (horario base): todo lo que NO chocó queda fijo por igualdad — CP-SAT
                // resuelve casi al instante porque solo las liberadas tienen dominio real.
                var sesionesFijasIds = sesiones.Select(s => s.Id).Where(id => !freedIds.Contains(id)).ToHashSet();

                var resultado = await _fase2.ResolverFactibilidadAsync(
                    sesiones, bloquesGrid, espacios,
                    grupos: gruposMotor,
                    sesionesFijasIds: sesionesFijasIds,
                    ventanaPorAsignatura: ventanaPorAsig,
                    ct: ct);

                if (!resultado.EsFactible)
                    return new ReacomodarHorarioResponse
                    {
                        EsFactible = false,
                        MensajeError = resultado.MensajeError
                    };

                // Postcondición dura: de la salida del solver solo se toman la editada y las
                // liberadas — el resto del horario NO se toca, pase lo que pase con el solver.
                nuevasAsignaciones = resultado.Asignaciones
                    .Where(a => a.SesionId == sesionEditada.Id || freedIds.Contains(a.SesionId))
                    .ToList();

                foreach (var fid in freedIds)
                {
                    var libre    = sesiones.First(s => s.Id == fid);
                    var asigLibre = nuevasAsignaciones.FirstOrDefault(a => a.SesionId == fid);
                    if (asigLibre == null) continue;
                    libre.AsignarBloqueTiempo(asigLibre.BloqueTiempoId);
                    if (asigLibre.EspacioId.HasValue) libre.AsignarEspacio(asigLibre.EspacioId.Value);
                }
                advertencias.Add($"{freedIds.Count} sesión(es) en conflicto se reubicaron automáticamente.");
            }

            // ── Persistir: reemplazar solo las filas de la editada + liberadas ──
            var idsAReemplazar = new HashSet<Guid>(freedIds) { sesionEditada.Id };
            var asignacionesViejasAEliminar = asignacionesActuales.Where(a => idsAReemplazar.Contains(a.SesionId)).ToList();

            await _uow.BeginTransactionAsync();
            try
            {
                // B4 auditoría: delete uno por uno en vez de un DeleteRangeAsync — ineficiencia, no
                // bug (misma transacción, y freedIds es siempre un puñado de sesiones en conflicto,
                // nunca la tabla completa). No se agrega DeleteRangeAsync a IAsignacionSemanalRepositorio
                // solo para esto: el costo de tocar los 11 fakes de test que implementan la interfaz
                // no se justifica para este volumen.
                foreach (var vieja in asignacionesViejasAEliminar)
                    await _asignacionRepo.DeleteAsync(vieja.Id);
                await _asignacionRepo.AddRangeAsync(nuevasAsignaciones);

                await _sesionRepo.UpdateAsync(sesionEditada);
                foreach (var fid in freedIds)
                    await _sesionRepo.UpdateAsync(sesiones.First(s => s.Id == fid));

                await _uow.CommitAsync();
            }
            catch
            {
                await _uow.RollbackAsync();
                throw;
            }

            // ── Respuesta: horario completo refrescado (fijas intactas + las que se movieron) ──
            var grupos = await _grupoRepo.GetAllAsync();
            var asignacionesFinal = asignacionesActuales
                .Where(a => !idsAReemplazar.Contains(a.SesionId))
                .Concat(nuevasAsignaciones)
                .ToList();

            return new ReacomodarHorarioResponse
            {
                EsFactible   = true,
                Advertencias = advertencias,
                Sesiones     = GenerarHorarioService.ConstruirSesionesDto(sesiones, asignacionesFinal, grupos, bloquesGrid)
            };
        }
    }
}
