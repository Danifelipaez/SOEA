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
    public sealed record RequisitoEspacio(
        TipoSesion TipoSesion,
        Guid? EspacioId,
        TipoEspacio TipoEspacio,
        int Sesiones);
}
