using System;
using System.Collections.Generic;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.ValueObjects;

namespace SOEA.Domain.Services
{
    /// <summary>
    /// Fuente ÚNICA de espacios candidatos para una sesión presencial: HC-S05 (espacio fijo,
    /// de la sesión o del requisito del grupo) ∩ HC-S03 (tipo de espacio según <see cref="TipoSesion"/>).
    /// Espejo de <see cref="CalculadorDominioSesion"/> pero para el eje espacial. Antes esta lógica
    /// vivía cuadruplicada: MotorConstraintProgramming, AsignadorEspacios, ValidadorRestriccionesDuras
    /// y horario.component.ts. HC-CAP (aforo) se aplica aparte por cada llamador, que ya conoce los
    /// estudiantes del grupo y necesita distinguir "sin candidato por tipo" de "sin candidato por aforo"
    /// para su propio mensaje de error.
    /// </summary>
    public static class CalculadorEspaciosSesion
    {
        /// <summary>A3: tipo de sesión derivado de (TipoFlujo, Modalidad) — función pura, nada persistido.</summary>
        public static TipoSesion TipoSesionDe(Sesion sesion) => sesion.TipoFlujo switch
        {
            TipoFlujo.Laboratorio => TipoSesion.Laboratorio,
            _ => sesion.Modalidad == Modalidad.Virtual ? TipoSesion.TeoriaVirtual : TipoSesion.TeoriaPresencial
        };

        /// <summary>
        /// HC-S03: true si <paramref name="espacio"/> es válido para <paramref name="tipoSesion"/>,
        /// dado el requisito de espacio declarado por el grupo (si lo hay). Sin requisito: Laboratorio
        /// exige laboratorio; TeoriaPresencial EXCLUYE laboratorio (petición 7 — antes una teoría
        /// presencial podía caer en un laboratorio porque solo se protegían los labs); TeoriaVirtual
        /// no consume espacio (no debería llegar aquí).
        /// </summary>
        public static bool CumpleTipo(Espacio espacio, TipoSesion tipoSesion, RequisitoEspacio? requisito)
        {
            if (requisito?.EspacioId is Guid fijo) return espacio.Id == fijo;
            if (requisito is not null) return espacio.Tipo == requisito.TipoEspacio;
            return tipoSesion switch
            {
                TipoSesion.Laboratorio => espacio.Tipo == TipoEspacio.Laboratorio,
                TipoSesion.TeoriaPresencial => espacio.Tipo != TipoEspacio.Laboratorio,
                _ => true
            };
        }

        /// <summary>
        /// Índices en <paramref name="espacios"/> candidatos para <paramref name="sesion"/>:
        /// HC-S05 (espacio fijo de la sesión, o del requisito del grupo si existe en la lista)
        /// ∩ HC-S03 (tipo, vía <see cref="CumpleTipo"/>). Con espacio fijo ausente de la lista,
        /// cae al filtrado normal por tipo (mismo criterio pre-existente). No aplica HC-CAP —
        /// el llamador lo filtra aparte (ver docstring de la clase).
        /// </summary>
        public static IEnumerable<int> Candidatos(
            Sesion sesion,
            IReadOnlyList<Espacio> espacios,
            RequisitoEspacio? requisito)
        {
            var tipoSesion = TipoSesionDe(sesion);

            Guid? espacioFijo = sesion.EspacioId ?? requisito?.EspacioId;
            if (espacioFijo.HasValue)
            {
                for (int e = 0; e < espacios.Count; e++)
                {
                    if (espacios[e].Id != espacioFijo.Value) continue;
                    yield return e;
                    yield break;
                }
            }

            for (int e = 0; e < espacios.Count; e++)
                if (CumpleTipo(espacios[e], tipoSesion, requisito))
                    yield return e;
        }
    }
}
