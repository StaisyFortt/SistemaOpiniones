
namespace SistemaOpiniones.Data.Models
{
    public class ResenaWebRawModel
    {
        public long IdOpinion { get; set; }
        public long? IdCliente { get; set; }
        public long? IdProducto { get; set; }
        public string TipoOpinion { get; set; } = string.Empty;
        public string Contenido { get; set; } = string.Empty;
        public short? Puntuacion { get; set; }
        public DateTime FechaEmision { get; set; }
        public string NombreFuente { get; set; } = string.Empty;
    }
}
