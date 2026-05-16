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

            // 2. Mismo Espacio (Laboratorio/Salón)
            if (s1.EspacioId.HasValue && s1.EspacioId != Guid.Empty && s1.EspacioId == s2.EspacioId)
            {
                // Regla ALT-01: TipoA y TipoB pueden compartir espacio
                if ((s1.Alternancia == TipoAlternancia.TipoA && s2.Alternancia == TipoAlternancia.TipoB) ||
                    (s1.Alternancia == TipoAlternancia.TipoB && s2.Alternancia == TipoAlternancia.TipoA))
                {
                    return false;
                }
                return true;
            }

            return false;
        }
    }
}
