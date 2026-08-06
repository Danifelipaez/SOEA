namespace SOEA.Domain.Enums
{
    /// <summary>
    /// Clasificación de una sesión por su naturaleza física, no por su flujo de programación
    /// (<see cref="TipoFlujo"/>). Fuente única de "¿qué tipo de espacio necesita esta sesión?".
    /// </summary>
    public enum TipoSesion
    {
        TeoriaPresencial,
        TeoriaVirtual,
        Laboratorio
    }
}
