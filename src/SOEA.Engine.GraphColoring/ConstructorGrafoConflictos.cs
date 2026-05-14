using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Engine.GraphColoring
{
    public class ConstructorGrafoConflictos
    {
        /// <summary>
        /// Construye un grafo de conflictos representados como un diccionario de adyacencia.
        /// Retorna un diccionario donde la clave es el Id de la sesión y el valor es un HashSet con los Ids de las sesiones vecinas.
        /// </summary>
        public Dictionary<Guid, HashSet<Guid>> Construir(IEnumerable<Sesion> sesiones)
        {
            var sesionesLista = sesiones.ToList();
            var grafo = new Dictionary<Guid, HashSet<Guid>>();

            foreach (var s in sesionesLista)
            {
                grafo[s.Id] = new HashSet<Guid>();
            }

            for (int i = 0; i < sesionesLista.Count; i++)
            {
                for (int j = i + 1; j < sesionesLista.Count; j++)
                {
                    var s1 = sesionesLista[i];
                    var s2 = sesionesLista[j];

                    if (TienenConflicto(s1, s2))
                    {
                        grafo[s1.Id].Add(s2.Id);
                        grafo[s2.Id].Add(s1.Id);
                    }
                }
            }

            return grafo;
        }

        private bool TienenConflicto(Sesion s1, Sesion s2)
        {
            // 1. Mismo Docente
            if (s1.DocenteId != Guid.Empty && s1.DocenteId == s2.DocenteId)
                return true;

            // 2. Mismo Grupo (Cohorte)
            // Desactivado temporalmente para el piloto (no hay datos de grupo).
            // if (s1.GrupoId.HasValue && s1.GrupoId != Guid.Empty && s1.GrupoId == s2.GrupoId)
            //    return true;

            // 3. Conflictos de Alternancia (si aplican al mismo espacio físico o lógica compartida)
            // Aquí se valida: TipoA + TipoA chocan. TipoB + TipoB chocan. SinAlternancia choca con cualquiera.
            // La única excepción es TipoA + TipoB que pueden convivir.
            if (s1.Alternancia == s2.Alternancia)
                return true;

            if (s1.Alternancia == TipoAlternancia.SinAlternancia || s2.Alternancia == TipoAlternancia.SinAlternancia)
                return true;

            return false;
        }
    }
}
