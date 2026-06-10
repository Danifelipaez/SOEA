using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Application.Features.Asignaturas.Requests;
using SOEA.Application.Features.Asignaturas.Responses;

namespace SOEA.Application.Features.Asignaturas
{
    /// <summary>
    /// Actualiza los datos editables de una asignatura existente desde la UI de Ingesta.
    /// La duración (horas/sesión, sesiones/semana) es editable por el usuario aunque sea
    /// un input fijo para el algoritmo.
    /// </summary>
    public class UpdateAsignaturaService
    {
        private readonly IAsignaturaRepositorio _repository;

        public UpdateAsignaturaService(IAsignaturaRepositorio repository)
        {
            _repository = repository;
        }

        public async Task<AsignaturaResponse> ExecuteAsync(Guid id, UpdateAsignaturaRequest request)
        {
            var asignatura = await _repository.GetByIdAsync(id)
                ?? throw new InvalidOperationException($"Asignatura con ID {id} no encontrada.");

            asignatura.ActualizarDatos(
                request.Nombre,
                request.Codigo,
                request.HorasPorSesion,
                request.SesionesPorSemana,
                request.SesionesLaboratorioSemestre,
                request.ProgramaId,
                request.Alternancia);
            asignatura.AsignarDocente(request.DocenteId);
            asignatura.AsignarEspacioFijo(request.EspacioFijoId);

            await _repository.UpdateAsync(asignatura);
            return MapToResponse(asignatura);
        }

        private static AsignaturaResponse MapToResponse(Asignatura asignatura)
        {
            return new AsignaturaResponse
            {
                Id = asignatura.Id,
                Nombre = asignatura.Nombre,
                Codigo = asignatura.Codigo,
                HorasPorSesion = asignatura.HorasPorSesion,
                SesionesPorSemana = asignatura.SesionesPorSemana,
                SesionesLaboratorioSemestre = asignatura.SesionesLaboratorioSemestre,
                Alternancia = asignatura.Alternancia,
                ProgramaId = asignatura.ProgramaId,
                DocenteId = asignatura.DocenteId,
                EspacioFijoId = asignatura.EspacioFijoId
            };
        }
    }
}
