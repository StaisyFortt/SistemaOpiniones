
namespace SistemaOpiniones.Data.Models
{
    public class ComentarioSocialRawModel
    {
        public string IdComentario { get; set; } = string.Empty;
        public string RedSocial { get; set; } = string.Empty;
        public string UsuarioHandle { get; set; } = string.Empty;
        public string TextoComentario { get; set; } = string.Empty;
        public int Likes { get; set; }
        public int Compartidos { get; set; }
        public DateTime FechaPublicacion { get; set; }

    }
}
