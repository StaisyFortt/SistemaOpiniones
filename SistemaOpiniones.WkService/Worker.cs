using System.Diagnostics;
using System.Globalization;
using SistemaOpiniones.Data.Extractors;
using SistemaOpiniones.Data.Interfaces;
using SistemaOpiniones.Data.Models;
using SistemaOpiniones.Data.Staging;

namespace SistemaOpiniones.WkService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _config;
        private readonly CsvExtractor _csvExtractor;
        private readonly DatabaseExtractor _dbExtractor;
        private readonly ApiExtractor _apiExtractor;
        private readonly IStagingService _stagingService;
        private readonly IDimensionLoader _dimensionLoader;

        // recibe los extractores, el logger y staging por inyeccion
        public Worker(
            ILogger<Worker> logger,
            IConfiguration config,
            CsvExtractor csvExtractor,
            DatabaseExtractor dbExtractor,
            ApiExtractor apiExtractor,
            IStagingService stagingService,
            IDimensionLoader dimensionLoader)
        {
            _logger = logger;
            _config = config;
            _csvExtractor = csvExtractor;
            _dbExtractor = dbExtractor;
            _apiExtractor = apiExtractor;
            _stagingService = stagingService;
            _dimensionLoader = dimensionLoader;
        }

        // ciclo continuo en segundo plano que ejecuta el proceso etl
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("servicio etl worker iniciado a las: {time}", DateTimeOffset.Now);

            // espera 2 segundos para que la api arranque primero
            await Task.Delay(2000, stoppingToken);

            var minutos = _config.GetValue<int>("EtlConfig:IntervaloMinutos");
            var delay = TimeSpan.FromMinutes(minutos > 0 ? minutos : 1);
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("================================================================================");
                _logger.LogInformation("iniciando ciclo etl (extraccion + carga de dimensiones) a las: {time}", DateTimeOffset.Now);
                _logger.LogInformation("================================================================================");
                var cronometroTotal = Stopwatch.StartNew();

                // 1 extraccion de las fuentes
                var encuestas = await ProcesarCsvAsync(stoppingToken);
                var resenas = await ProcesarDatabaseAsync(stoppingToken);
                var comentarios = await ProcesarApiAsync(stoppingToken);

                // 2 carga de las dimensiones en opinionesdw
                await CargarDimensionesAsync(encuestas, resenas, comentarios, stoppingToken);
                cronometroTotal.Stop();
                _logger.LogInformation("================================================================================");
                _logger.LogInformation("ciclo etl completado con exito en {ms} ms", cronometroTotal.ElapsedMilliseconds);
                _logger.LogInformation("proxima ejecucion programada en {min} minutos...", delay.TotalMinutes);
                _logger.LogInformation("================================================================================\n");
                // espera el intervalo configurado antes de la siguiente ejecucion
                await Task.Delay(delay, stoppingToken);
            }
        }

        // procesa la extraccion del archivo csv de encuestas
        private async Task<List<EncuestaRawModel>> ProcesarCsvAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                _logger.LogInformation("[CSV] extrayendo encuestas internas...");
                var datos = (await _csvExtractor.ExtraerAsync(ct)).ToList();
                sw.Stop();
                _logger.LogInformation("[CSV] exito: {cant} registros extraidos en {ms} ms", datos.Count, sw.ElapsedMilliseconds);
                var ruta = await _stagingService.GuardarAsync(_csvExtractor.NombreFuente, datos, ct);
                _logger.LogInformation("[STAGING] datos csv guardados en: {ruta}", Path.GetFileName(ruta));
                return datos;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "[CSV] error al extraer encuestas despues de {ms} ms", sw.ElapsedMilliseconds);
                return new List<EncuestaRawModel>();
            }
        }

        // procesa la extraccion de resenas desde la base de datos sql
        private async Task<List<ResenaWebRawModel>> ProcesarDatabaseAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                _logger.LogInformation("[SQL] extrayendo resenas web desde base de datos...");
                var datos = (await _dbExtractor.ExtraerAsync(ct)).ToList();
                sw.Stop();
                _logger.LogInformation("[SQL] exito: {cant} registros extraidos en {ms} ms", datos.Count, sw.ElapsedMilliseconds);
                var ruta = await _stagingService.GuardarAsync(_dbExtractor.NombreFuente, datos, ct);
                _logger.LogInformation("[STAGING] datos sql guardados en: {ruta}", Path.GetFileName(ruta));
                return datos;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "[SQL] error al extraer resenas despues de {ms} ms", sw.ElapsedMilliseconds);
                return new List<ResenaWebRawModel>();
            }
        }

        // procesa la extraccion de comentarios desde la api rest
        private async Task<List<ComentarioSocialRawModel>> ProcesarApiAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                _logger.LogInformation("[API] extrayendo comentarios sociales desde endpoint rest...");
                var datos = (await _apiExtractor.ExtraerAsync(ct)).ToList();
                sw.Stop();
                _logger.LogInformation("[API] exito: {cant} registros extraidos en {ms} ms", datos.Count, sw.ElapsedMilliseconds);
                var ruta = await _stagingService.GuardarAsync(_apiExtractor.NombreFuente, datos, ct);
                _logger.LogInformation("[STAGING] datos api guardados en: {ruta}", Path.GetFileName(ruta));
                return datos;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "[API] error al consumir comentarios sociales despues de {ms} ms", sw.ElapsedMilliseconds);
                return new List<ComentarioSocialRawModel>();
            }
        }

        // ejecuta la carga de las tablas de dimensiones en el dw
        private async Task CargarDimensionesAsync(
            List<EncuestaRawModel> encuestas,
            List<ResenaWebRawModel> resenas,
            List<ComentarioSocialRawModel> comentarios,
            CancellationToken ct)
        {
            _logger.LogInformation("--------------------------------------------------------------------------------");
            _logger.LogInformation("[DW] iniciando carga de dimensiones en OpinionesDW...");
            // recolecta todas las fechas validas de las fuentes para dim_tiempo
            var fechas = new List<DateTime>();
            foreach (var enc in encuestas)
            {
                if (DateTime.TryParse(enc.Fecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaCsv))
                {
                    fechas.Add(fechaCsv);
                }
            }
            fechas.AddRange(resenas.Select(r => r.FechaEmision));
            fechas.AddRange(comentarios.Select(c => c.FechaPublicacion));
            await EjecutarCargaDimensionAsync(() => _dimensionLoader.CargarDimSentimientoAsync(encuestas, ct));
            await EjecutarCargaDimensionAsync(() => _dimensionLoader.CargarDimCanalFuenteAsync(encuestas, comentarios, ct));
            await EjecutarCargaDimensionAsync(() => _dimensionLoader.CargarDimTiempoAsync(fechas, ct));
            await EjecutarCargaDimensionAsync(() => _dimensionLoader.CargarDimProductoAsync(ct));
            await EjecutarCargaDimensionAsync(() => _dimensionLoader.CargarDimClienteAsync(ct));
        }

        // ejecuta una carga de dimension con manejo de errores y registro de metricas
        private async Task EjecutarCargaDimensionAsync(Func<Task<DimensionLoadResult>> accionCarga)
        {
            try
            {
                var res = await accionCarga();
                _logger.LogInformation(
                    "[DW] {dim,-18} | insertados: {ins,4} | actualizados: {act,4} | omitidos: {omi,4} | tiempo: {ms} ms",
                    res.NombreDimension, res.Insertados, res.Actualizados, res.Omitidos, res.DuracionMs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DW] error cargando dimension en OpinionesDW");
            }
        }
    }
}
