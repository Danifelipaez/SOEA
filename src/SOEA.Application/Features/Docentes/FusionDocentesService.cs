using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;

namespace SOEA.Application.Features.Docentes
{
    /// <summary>
    /// Fusiona docentes que son la misma persona pero quedaron fragmentados en varios registros
    /// por variantes de nombre en el Excel — causa raíz del síntoma "un docente con 2 sesiones a
    /// la misma hora" (el motor los ve como personas distintas y los agenda en paralelo).
    /// La fusión es MANUAL: el usuario elige el registro canónico y cuáles absorber. Reasigna las
    /// asignaturas de los duplicados al canónico y elimina los duplicados. NO toca las sesiones
    /// directamente: son transitorias (se regeneran en cada corrida — regla 8); las que ya
    /// persisten con Sesion.DocenteId apuntando a un duplicado quedan con ese campo en null tras
    /// el borrado (M14 auditoría: docente_id ahora SÍ tiene FK con DeleteBehavior.SetNull — antes
    /// de esa migración esta misma fila quedaba con un id colgante).
    /// La disponibilidad del canónico se conserva tal cual; por eso el usuario debe elegir como
    /// canónico el registro con la disponibilidad correcta.
    /// </summary>
    public class FusionDocentesService
    {
        private readonly IDocenteRepositorio _docenteRepo;
        private readonly IGrupoRepositorio _grupoRepo;
        private readonly DocenteService _docenteService;
        private readonly IUnitOfWork _uow;

        public FusionDocentesService(
            IDocenteRepositorio docenteRepo,
            IGrupoRepositorio grupoRepo,
            DocenteService docenteService,
            IUnitOfWork uow)
        {
            _docenteRepo = docenteRepo;
            _grupoRepo = grupoRepo;
            _docenteService = docenteService;
            _uow = uow;
        }

        /// <summary>
        /// Grupos de docentes que probablemente son la misma persona, para revisión/fusión manual.
        /// Cada grupo trae 2+ docentes mapeados al DTO de UI.
        /// </summary>
        public async Task<List<List<DocenteUiDto>>> SugerirDuplicadosAsync()
        {
            var docentes = await _docenteService.GetAllAsync();
            var porId = docentes.ToDictionary(d => d.Id);

            var grupos = DetectorDocentesDuplicados.AgruparPosiblesDuplicados(
                docentes.Select(d => (d.Id, d.Nombre)));

            return grupos
                .Select(g => g.Where(x => porId.ContainsKey(x.Id)).Select(x => porId[x.Id]).ToList())
                .Where(g => g.Count > 1)
                .ToList();
        }

        /// <summary>
        /// Fusiona los <paramref name="duplicadosIds"/> en <paramref name="canonicoId"/>: mueve sus
        /// grupos al canónico y elimina los registros duplicados.
        /// </summary>
        public async Task<FusionResultado> FusionarAsync(Guid canonicoId, IReadOnlyCollection<Guid> duplicadosIds)
        {
            if (await _docenteRepo.GetByIdAsync(canonicoId) is null)
                throw new ArgumentException("El docente canónico no existe.");

            var aAbsorber = (duplicadosIds ?? Array.Empty<Guid>())
                .Where(id => id != canonicoId)
                .Distinct()
                .ToHashSet();

            if (aAbsorber.Count == 0)
                throw new ArgumentException("Debe indicar al menos un docente duplicado distinto del canónico.");

            // H6 auditoría: reasignar N grupos y borrar M docentes eran N+M escrituras
            // independientes (cada una con su propio commit) — un fallo a mitad de camino dejaba
            // algunos grupos ya reasignados y otros no, sin ninguna forma de saber hasta dónde
            // llegó. Una sola transacción para toda la fusión.
            await _uow.BeginTransactionAsync();
            try
            {
                // Reasignar los grupos de los duplicados al canónico (el docente vive en Grupo).
                var grupos = await _grupoRepo.GetAllAsync();
                int reasignadas = 0;
                foreach (var g in grupos)
                {
                    if (g.DocenteId.HasValue && aAbsorber.Contains(g.DocenteId.Value))
                    {
                        g.AsignarDocente(canonicoId);
                        await _grupoRepo.UpdateAsync(g);
                        reasignadas++;
                    }
                }

                // Eliminar los registros duplicados.
                int eliminados = 0;
                foreach (var dupId in aAbsorber)
                {
                    if (await _docenteRepo.GetByIdAsync(dupId) is null) continue;
                    await _docenteRepo.DeleteAsync(dupId);
                    eliminados++;
                }

                await _uow.CommitAsync();
                return new FusionResultado(canonicoId, eliminados, reasignadas);
            }
            catch
            {
                await _uow.RollbackAsync();
                throw;
            }
        }
    }

    public readonly record struct FusionResultado(Guid CanonicoId, int DocentesEliminados, int GruposReasignados);
}
