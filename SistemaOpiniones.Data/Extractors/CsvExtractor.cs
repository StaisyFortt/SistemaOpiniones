using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using SistemaOpiniones.Data.Interfaces;
using SistemaOpiniones.Data.Models;

namespace SistemaOpiniones.Data.Extractors;

public class CsvExtractor : IExtractor<EncuestaRawModel>
{
    private readonly string _rutaArchivo;
    public string NombreFuente => "CSV_EncuestasInternas";

    public CsvExtractor(string rutaArchivo)
    {
        _rutaArchivo = rutaArchivo;
    }

    public async Task<IEnumerable<EncuestaRawModel>> ExtraerAsync(CancellationToken cancellationToken = default)
    {
        var resultados = new List<EncuestaRawModel>();

        // comprueba que el archivo exista antes de abrirlo
        if (!File.Exists(_rutaArchivo))
        {
            throw new FileNotFoundException($"el archivo {_rutaArchivo} no se encontro.");
        }

        // define reglas de lectura separadores y caracteres en espanol
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null,
            BadDataFound = null,
            DetectDelimiter = true,
            Encoding = Encoding.Latin1
        };

        // abre el archivo en modo solo lectura sin bloquear a otros programas
        using var stream = new FileStream(_rutaArchivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.Latin1);
        using var csv = new CsvReader(reader, config);

        // lee la primera fila de titulos si no hay nada se detiene
        if (!await csv.ReadAsync() || !csv.ReadHeader())
        {
            return resultados;
        }

        // busca columnas con o sin tilde de forma segura
        var clasifField = csv.HeaderRecord?.FirstOrDefault(h => h.StartsWith("Clasificac", StringComparison.OrdinalIgnoreCase)) ?? "Clasificación";
        var puntajeField = csv.HeaderRecord?.FirstOrDefault(h => h.StartsWith("Puntaje", StringComparison.OrdinalIgnoreCase)) ?? "PuntajeSatisfacción";

        while (await csv.ReadAsync())
        {
            // detiene la lectura si el worker service se apaga
            cancellationToken.ThrowIfCancellationRequested();

            // convierte los datos de la fila en un objeto modelo
            var modelo = new EncuestaRawModel
            {
                IdOpinion = csv.GetField("IdOpinion"),
                IdCliente = csv.GetField("IdCliente"),
                IdProducto = csv.GetField("IdProducto"),
                Fecha = csv.GetField("Fecha"),
                Comentario = csv.GetField("Comentario"),
                Clasificacion = csv.GetField(clasifField),
                PuntajeSatisfaccion = csv.GetField(puntajeField),
                Fuente = csv.GetField("Fuente")
            };

            // guarda el objeto en la lista final
            resultados.Add(modelo);
        }

        return resultados;
    }
}