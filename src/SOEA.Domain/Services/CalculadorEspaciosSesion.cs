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
        /// Parseo único de <see cref="TipoFlujo"/> desde texto. IMP2/DUP7 auditoría: antes existían
        /// dos copias con defaults OPUESTOS (<c>CrearSesionManualService</c> → AulaVirtual,
        /// <c>GenerarHorarioService</c> → Laboratorio), y los dos puntos de importación (Excel y
        /// JSON) omitían el argumento por completo — el constructor de <see cref="Sesion"/> por sí
        /// solo defaultea a Laboratorio, así que TODA sesión importada quedaba marcada como
        /// laboratorio. Default acordado: <see cref="TipoFlujo.AulaVirtual"/> (teoría) — es la
        /// mayoría de las sesiones reales, y confundir teoría con laboratorio es precisamente lo
        /// que satura los pocos laboratorios y hace infactible la generación.
        /// </summary>
        public static TipoFlujo ParseTipoFlujo(string? tipoFlujo) =>
            tipoFlujo?.Trim().ToLowerInvariant() switch
            {
                "laboratorio" => TipoFlujo.Laboratorio,
                _             => TipoFlujo.AulaVirtual
            };

        /// <summary>
        /// HC-S03: true si <paramref name="espacio"/> es válido para <paramref name="tipoSesion"/>,
        /// dado el requisito de espacio declarado por el grupo (si lo hay). Sin requisito, o con
        /// requisito sin tipo explícito (M6: <see cref="RequisitoEspacio.TipoEspacio"/> nullable):
        /// Laboratorio exige laboratorio; TeoriaPresencial EXCLUYE laboratorio (petición 7 — antes
        /// una teoría presencial podía caer en un laboratorio porque solo se protegían los labs);
        /// TeoriaVirtual no consume espacio (no debería llegar aquí).
        /// </summary>
        public static bool CumpleTipo(Espacio espacio, TipoSesion tipoSesion, RequisitoEspacio? requisito)
        {
            if (requisito?.EspacioId is Guid fijo) return espacio.Id == fijo;
            if (requisito?.TipoEspacio is TipoEspacio tipoExplicito) return espacio.Tipo == tipoExplicito;
            return tipoSesion switch
            {
                TipoSesion.Laboratorio => espacio.Tipo == TipoEspacio.Laboratorio,
                TipoSesion.TeoriaPresencial => espacio.Tipo != TipoEspacio.Laboratorio,
                _ => true
            };
        }

        /// <summary>
        /// Índices en <paramref name="espacios"/> candidatos para <paramref name="sesion"/>:
        /// HC-S05 (espacio fijo de la sesión, o del requisito del grupo) ∩ HC-S03 (tipo, vía
        /// <see cref="CumpleTipo"/>) — las dos se exigen SIEMPRE, también cuando hay espacio fijo
        /// (VAL2 auditoría). Antes un espacio fijo se devolvía como único candidato sin comprobar su
        /// tipo, así que una sesión de laboratorio fijada a un salón (dato de entrada inconsistente,
        /// p. ej. un horario base sin <c>TipoFlujo</c> explícito) generaba y persistía igual — el
        /// único lugar que lo detectaba era <c>ValidadorRestriccionesDuras</c>, ya con el horario
        /// generado y sin forma de arreglarlo salvo descartar toda la corrida.
        /// Si el espacio fijo no está en <paramref name="espacios"/>, o no cumple el tipo, no hay
        /// NINGÚN candidato (M17 auditoría) — antes cualquiera de los dos casos caía en silencio al
        /// filtrado genérico por tipo y podía devolver un aula DISTINTA de la exigida.
        /// No aplica HC-CAP — el llamador lo filtra aparte (ver docstring de la clase).
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
                    if (CumpleTipo(espacios[e], tipoSesion, requisito))
                        yield return e;
                    yield break;
                }
                yield break;
            }

            for (int e = 0; e < espacios.Count; e++)
                if (CumpleTipo(espacios[e], tipoSesion, requisito))
                    yield return e;
        }
    }
}
