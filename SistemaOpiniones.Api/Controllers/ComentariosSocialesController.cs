using Microsoft.AspNetCore.Mvc;
using SistemaOpiniones.Data.Models;

namespace SistemaOpiniones.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]

    public class ComentariosSocialesController : Controller
    {
        // responde peticiones get devolviendo la lista de comentarios de redes
        [HttpGet]
        public ActionResult<IEnumerable<ComentarioSocialRawModel>> GetComentarios()
        {
            // datos simulados de comentarios de twitter instagram y facebook
            var comentarios = new List<ComentarioSocialRawModel>
            {
                new()
                {
                    IdComentario = "T0001",
                    RedSocial = "Instagram",
                    UsuarioHandle = "@carlos_tech",
                    TextoComentario = "informacion suficiente sin mayor novedad el producto llego bien",
                    Likes = 15,
                    Compartidos = 2,
                    FechaPublicacion = DateTime.UtcNow.AddDays(-5)
                },
                new()
                {
                    IdComentario = "T0002",
                    RedSocial = "Twitter",
                    UsuarioHandle = "@maria_r",
                    TextoComentario = "muy mala calidad se rompio rapido no lo recomiendo",
                    Likes = 34,
                    Compartidos = 12,
                    FechaPublicacion = DateTime.UtcNow.AddDays(-4)
                },
                new()
                {
                    IdComentario = "T0003",
                    RedSocial = "Instagram",
                    UsuarioHandle = "@pedro_v",
                    TextoComentario = "gran relacion calidad precio muy satisfecho con la compra",
                    Likes = 52,
                    Compartidos = 7,
                    FechaPublicacion = DateTime.UtcNow.AddDays(-3)
                },
                new()
                {
                    IdComentario = "T0004",
                    RedSocial = "Twitter",
                    UsuarioHandle = "@laura_m",
                    TextoComentario = "pesima atencion al cliente tardaron mucho en responder",
                    Likes = 8,
                    Compartidos = 1,
                    FechaPublicacion = DateTime.UtcNow.AddDays(-2)
                },
                new()
                {
                    IdComentario = "T0005",
                    RedSocial = "Facebook",
                    UsuarioHandle = "@juan_perez",
                    TextoComentario = "excelente servicio y entrega rapida volvere a comprar",
                    Likes = 27,
                    Compartidos = 4,
                    FechaPublicacion = DateTime.UtcNow.AddDays(-1)
                }
            };

            // retorna codigo http 200 ok con la lista json
            return Ok(comentarios);
        }
    }
}



