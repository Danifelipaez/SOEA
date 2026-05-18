using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Engine.Genetic
{
    /// <summary>
    /// Operadores genéticos: selección por torneo, cruce de un punto,
    /// mutación y reparación de restricciones duras.
    /// </summary>
    public class OperadoresGeneticos
    {
        private readonly Random _rng;
        private readonly List<Sesion> _sesiones;
        private readonly int _maxBloques;
        private readonly int _maxEspacios;

        public OperadoresGeneticos(List<Sesion> sesiones, int maxBloques, int maxEspacios, Random? rng = null)
        {
            _sesiones = sesiones;
            _maxBloques = maxBloques;
            _maxEspacios = maxEspacios;
            _rng = rng ?? new Random();
        }

        /// <summary>
        /// Selección por torneo: selecciona k candidatos aleatorios y retorna el de menor fitness.
        /// </summary>
        public CromosomaHorario SeleccionTorneo(List<(CromosomaHorario cromosoma, decimal fitness)> poblacion, int k = 5)
        {
            CromosomaHorario? mejor = null;
            decimal mejorFitness = decimal.MaxValue;

            for (int i = 0; i < k; i++)
            {
                var idx = _rng.Next(poblacion.Count);
                if (poblacion[idx].fitness < mejorFitness)
                {
                    mejorFitness = poblacion[idx].fitness;
                    mejor = poblacion[idx].cromosoma;
                }
            }

            return mejor!;
        }

        /// <summary>
        /// Cruce de un punto: combina dos padres intercambiando segmentos de genes.
        /// </summary>
        public CromosomaHorario Cruce(CromosomaHorario padre1, CromosomaHorario padre2, double probabilidad = 0.8)
        {
            if (_rng.NextDouble() > probabilidad)
                return padre1.Clonar();

            int punto = _rng.Next(1, padre1.CantidadGenes);
            var bloques = new int[padre1.CantidadGenes];
            var espacios = new int[padre1.CantidadGenes];

            for (int i = 0; i < padre1.CantidadGenes; i++)
            {
                if (i < punto)
                {
                    bloques[i] = padre1.BloqueIndices[i];
                    espacios[i] = padre1.EspacioIndices[i];
                }
                else
                {
                    bloques[i] = padre2.BloqueIndices[i];
                    espacios[i] = padre2.EspacioIndices[i];
                }
            }

            return new CromosomaHorario((Guid[])padre1.SesionIds.Clone(), bloques, espacios);
        }

        /// <summary>
        /// Mutación: para cada gen, con cierta probabilidad lo reasigna a un bloque y espacio aleatorios.
        /// </summary>
        public void Mutar(CromosomaHorario cromosoma, double probabilidadPorGen = 0.05)
        {
            for (int i = 0; i < cromosoma.CantidadGenes; i++)
            {
                if (_rng.NextDouble() < probabilidadPorGen)
                {
                    cromosoma.BloqueIndices[i] = _rng.Next(_maxBloques);
                    // Fix #3: keep room assignment consistent with block mutation
                    if (_maxEspacios > 0)
                        cromosoma.EspacioIndices[i] = _rng.Next(_maxEspacios);
                }
            }
        }

        /// <summary>
        /// Operador de reparación: verifica HC-I01 (conflicto de docente) y HC-S01 (conflicto de espacio)
        /// y repara asignando bloques/espacios alternativos libres.
        /// </summary>
        public void Reparar(CromosomaHorario cromosoma)
        {
            // HC-I01: mismo docente, mismo bloque
            var asignacionesDocente = new Dictionary<(Guid docenteId, int bloqueIdx), int>();

            for (int i = 0; i < cromosoma.CantidadGenes; i++)
            {
                var clave = (_sesiones[i].DocenteId, cromosoma.BloqueIndices[i]);
                if (asignacionesDocente.ContainsKey(clave))
                {
                    for (int intento = 0; intento < 10; intento++)
                    {
                        var nuevoBloque = _rng.Next(_maxBloques);
                        var nuevaClave = (_sesiones[i].DocenteId, nuevoBloque);
                        if (!asignacionesDocente.ContainsKey(nuevaClave))
                        {
                            cromosoma.BloqueIndices[i] = nuevoBloque;
                            asignacionesDocente[nuevaClave] = i;
                            break;
                        }
                    }
                    // Register whatever ended up assigned (repair may have failed after 10 attempts)
                    asignacionesDocente.TryAdd((_sesiones[i].DocenteId, cromosoma.BloqueIndices[i]), i);
                }
                else
                {
                    asignacionesDocente[clave] = i;
                }
            }

            // Fix #4 — HC-S01: mismo espacio, mismo bloque (solo sesiones presenciales)
            if (_maxEspacios > 0)
            {
                var asignacionesEspacio = new Dictionary<(int espacioIdx, int bloqueIdx), int>();

                for (int i = 0; i < cromosoma.CantidadGenes; i++)
                {
                    if (_sesiones[i].Modalidad == Modalidad.Virtual) continue;

                    var clave = (cromosoma.EspacioIndices[i], cromosoma.BloqueIndices[i]);
                    if (asignacionesEspacio.ContainsKey(clave))
                    {
                        // Try reassigning to a different space in the same block
                        bool reparado = false;
                        for (int intento = 0; intento < 10; intento++)
                        {
                            var nuevoEspacio = _rng.Next(_maxEspacios);
                            var nuevaClave = (nuevoEspacio, cromosoma.BloqueIndices[i]);
                            if (!asignacionesEspacio.ContainsKey(nuevaClave))
                            {
                                cromosoma.EspacioIndices[i] = nuevoEspacio;
                                asignacionesEspacio[nuevaClave] = i;
                                reparado = true;
                                break;
                            }
                        }
                        if (!reparado)
                            asignacionesEspacio.TryAdd((cromosoma.EspacioIndices[i], cromosoma.BloqueIndices[i]), i);
                    }
                    else
                    {
                        asignacionesEspacio[clave] = i;
                    }
                }
            }
        }
    }
}
