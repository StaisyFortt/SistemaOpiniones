namespace SistemaOpiniones.Data.Models
{
    // guarda las metricas del resultado de cargar una dimension
    public class DimensionLoadResult
    {
        public string NombreDimension { get; set; } = string.Empty;
        public int Insertados { get; set; }
        public int Actualizados { get; set; }
        public int Omitidos { get; set; }
        public long DuracionMs { get; set; }
    }
}
