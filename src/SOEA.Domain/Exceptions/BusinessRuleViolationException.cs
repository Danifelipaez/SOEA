using System;

namespace SOEA.Domain.Exceptions
{
    /// <summary>
    /// Violación de una regla de negocio o restricción dura (HC-I01, HC-S01, HC-SEP, "no se puede
    /// eliminar: tiene N dependientes"...) que el usuario puede entender y corregir — a diferencia
    /// de una <see cref="InvalidOperationException"/> genuinamente interna (EF Core, el contenedor
    /// de DI) que nunca debe llegar al cliente con su mensaje crudo.
    ///
    /// ERR1/ERR2 auditoría: antes ambos casos usaban <see cref="InvalidOperationException"/>, así
    /// que <c>GlobalExceptionHandler</c> no podía distinguirlos y mapeaba TODO InvalidOperationException
    /// a 409 con el mensaje crudo — incluidos fallos internos de EF Core ("The instance of entity
    /// type 'Sesion' cannot be tracked…"). <c>GlobalExceptionHandler</c> solo traduce este tipo;
    /// cualquier otra <see cref="InvalidOperationException"/> cae al ProblemDetails genérico de 500
    /// sin exponer detalles internos.
    /// </summary>
    public class BusinessRuleViolationException : Exception
    {
        public BusinessRuleViolationException(string message) : base(message) { }
    }
}
