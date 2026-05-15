using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;

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
        /// Mutación: para cada gen, con cierta probabilidad lo reasigna a un bloque aleatorio.
        /// </summary>
        public void Mutar(CromosomaHorario cromosoma, double probabilidadPorGen = 0.05)
        {
            for (int i = 0; i < cromosoma.CantidadGenes; i++)
            {
                if (_rng.NextDouble() < probabilidadPorGen)
                {
                    cromosoma.BloqueIndices[i] = _rng.Next(_maxBloques);
                }
            }
        }

        /// <summary>
        /// Operador de reparación: verifica restricciones duras básicas
        /// (conflicto de docente por bloque) y repara intercambiando a otro bloque libre.
        /// </summary>
        public void Reparar(CromosomaHorario cromosoma)
        {
            // Reparar HC-I01: mismo docente, mismo bloque
            var asignaciones = new Dictionary<(Guid docenteId, int bloqueIdx), int>(); // value = gene index

            for (int i = 0; i < cromosoma.CantidadGenes; i++)
            {
                var clave = (_sesiones[i].DocenteId, cromosoma.BloqueIndices[i]);
                if (asignaciones.ContainsKey(clave))
                {
                    // Conflicto: reasignar este gen a otro bloque aleatorio
                    for (int intento = 0; intento < 10; intento++)
                    {
                        var nuevoBloque = _rng.Next(_maxBloques);
                        var nuevaClave = (_sesiones[i].DocenteId, nuevoBloque);
                        if (!asignaciones.ContainsKey(nuevaClave))
                        {
                            cromosoma.BloqueIndices[i] = nuevoBloque;
                            asignaciones[nuevaClave] = i;
                            break;
                        }
                    }
                }
                else
                {
                    asignaciones[clave] = i;
                }
            }
        }
    }
}
