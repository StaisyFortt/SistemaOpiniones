using System.Diagnostics;
using SistemaOpiniones.Data.Extractors;
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

        // recibe los extractores, el logger y staging por inyeccion
        public Worker(
            ILogger<Worker> logger,
            IConfiguration config,
            CsvExtractor csvExtractor,
            DatabaseExtractor dbExtractor,
            ApiExtractor apiExtractor,
            IStagingService stagingService)
        {
            _logger = logger;
            _config = config;
            _csvExtractor = csvExtractor;
            _dbExtractor = dbExtractor;
            _apiExtractor = apiExtractor;
            _stagingService = stagingService;
        }

        // ciclo continuo en segundo plano que ejecuta el proceso etl
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("servicio etl worker iniciado a las: {time}", DateTimeOffset.Now);

            // espera 2 segundos iniciales para que la api local arranque primero
            await Task.Delay(2000, stoppingToken);

            var minutos = _config.GetValue<int>("EtlConfig:IntervaloMinutos");
            var delay = TimeSpan.FromMinutes(minutos > 0 ? minutos : 1);
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("================================================================================");
                _logger.LogInformation("iniciando ciclo de extraccion etl a las: {time}", DateTimeOffset.Now);
                _logger.LogInformation("================================================================================");
                var cronometroTotal = Stopwatch.StartNew();
                // extraccion desde archivo csv
                await ProcesarCsvAsync(stoppingToken);
                // extraccion desde base de datos relacional
                await ProcesarDatabaseAsync(stoppingToken);
                // extraccion desde api rest externa
                await ProcesarApiAsync(stoppingToken);
                cronometroTotal.Stop();
                _logger.LogInformation("================================================================================");
                _logger.LogInformation("ciclo de extraccion completado con exito en {ms} ms", cronometroTotal.ElapsedMilliseconds);
                _logger.LogInformation("proxima ejecucion programada en {min} minutos...", delay.TotalMinutes);
                _logger.LogInformation("================================================================================\n");
                // espera el intervalo configurado antes de la siguiente extraccion
                await Task.Delay(delay, stoppingToken);
            }
        }

        // procesa la extraccion del archivo csv de encuestas
        private async Task ProcesarCsvAsync(CancellationToken ct)
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
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "[CSV] error al extraer encuestas despues de {ms} ms", sw.ElapsedMilliseconds);
            }
        }

        // procesa la extraccion de resenas desde la base de datos sql
        private async Task ProcesarDatabaseAsync(CancellationToken ct)
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
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "[SQL] error al extraer resenas despues de {ms} ms", sw.ElapsedMilliseconds);
            }
        }

        // procesa la extraccion de comentarios desde la api rest
        private async Task ProcesarApiAsync(CancellationToken ct)
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
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "[API] error al consumir comentarios sociales despues de {ms} ms", sw.ElapsedMilliseconds);
            }
        }
    }
}
