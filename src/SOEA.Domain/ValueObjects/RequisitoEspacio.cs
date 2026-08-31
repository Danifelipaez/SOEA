using System;
using SOEA.Domain.Enums;

namespace SOEA.Domain.ValueObjects
{
    /// <summary>
    /// Requisito de espacio de un grupo para un tipo de sesión concreto: un espacio específico
    /// (<see cref="EspacioId"/>) o, en su defecto, cualquier espacio del tipo declarado
    /// (<see cref="TipoEspacio"/>). Reemplaza a <c>Asignatura.EspacioFijoId</c> (un solo uuid por
    /// asignatura, sólo pensado para laboratorio): ahora vive por grupo y por tipo de sesión.
    /// </summary>
    /// <param name="TipoEspacio">
    /// Null = sin preferencia de tipo — <see cref="Services.CalculadorEspaciosSesion.CumpleTipo"/>
    /// usa la regla por defecto según <see cref="TipoSesion"/> (M6: antes este campo no era
    /// nullable y <c>TipoEspacio.Salon == 0</c> hacía indistinguibles "sin preferencia" de "sólo
    /// salón", así que un requisito con el tipo ausente en el JSON forzaba silenciosamente Salon).
    /// </param>
    public sealed record RequisitoEspacio(
        TipoSesion TipoSesion,
        Guid? EspacioId,
        TipoEspacio? TipoEspacio,
        int Sesiones);
}
