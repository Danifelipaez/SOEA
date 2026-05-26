using System;
using System.Text.Json;

namespace SOEA.Application.Features.Docentes
{
    public class DocenteUiDto
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = "";
        public string Cedula { get; set; } = "";
        public double MaxHoras { get; set; }
        public JsonElement? Disponibilidad { get; set; }
    }
}
