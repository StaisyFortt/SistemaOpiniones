using System.Text.Json;
using SistemaOpiniones.Data.Models;
using SistemaOpiniones.Data.Interfaces;

namespace SistemaOpiniones.Data.Extractors
{
    public class ApiExtractor : IExtractor<ComentarioSocialRawModel>
    {
        private readonly HttpClient _httpClient;
        // url del endpoint de la api
        private readonly string _endpointUrl;
        public string NombreFuente => "API_RedesSociales";

        public ApiExtractor(HttpClient httpClient, string endpointUrl)
        {
            _httpClient = httpClient;
            _endpointUrl = endpointUrl;
        }

        // realiza la peticion get asincrona y convierte el json a objetos
        public async Task<IEnumerable<ComentarioSocialRawModel>> ExtraerAsync(CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.GetAsync(_endpointUrl, cancellationToken);

            // verifica que la api respondio con exito 200 ok si falla lanza excepcion
            response.EnsureSuccessStatusCode();

            // lee el contenido de la respuesta directamente como flujo de bytes
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            // permite leer json sin importar mayusculas o minusculas en las propiedades
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            // deserializa el json a una lista de modelos
            var resultado = await JsonSerializer.DeserializeAsync<List<ComentarioSocialRawModel>>(stream, options, cancellationToken);
            // retorna la lista o una lista vacia si venia nula
            return resultado ?? new List<ComentarioSocialRawModel>();
        }
    }
}
