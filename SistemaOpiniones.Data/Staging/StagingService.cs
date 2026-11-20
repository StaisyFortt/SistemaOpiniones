using System.Text.Json;

namespace SistemaOpiniones.Data.Staging
{

    public interface IStagingService
    {
        Task<string> GuardarAsync<T>(string nombreFuente, IEnumerable<T> datos, CancellationToken cancellationToken = default);
    }

    // guarda los datos crudos extraidos en archivos json dentro de la carpeta staging
    public class StagingService : IStagingService
    {
        private readonly string _directorioStaging;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true
        };

        public StagingService(string directorioStaging)
        {
            _directorioStaging = directorioStaging;
        }

        public async Task<string> GuardarAsync<T>(string nombreFuente, IEnumerable<T> datos, CancellationToken cancellationToken = default)
        {
            // crea la carpeta staging en el disco si no existe
            if (!Directory.Exists(_directorioStaging))
            {
                Directory.CreateDirectory(_directorioStaging);
            }

            // genera un nombre unico con timestamp ej staging_csv_encuestas_20261120_001500.json
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var nombreArchivo = $"staging_{nombreFuente}_{timestamp}.json";
            var rutaCompleta = Path.Combine(_directorioStaging, nombreArchivo);

            // escribe el archivo en disco
            await using var stream = new FileStream(rutaCompleta, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, datos, _jsonOptions, cancellationToken);

            // retorna la ruta donde quedo guardado para registrarla en los logs
            return rutaCompleta;
        }

    }
}
