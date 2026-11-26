using SistemaOpiniones.Data.Extractors;
using SistemaOpiniones.Data.Staging;
using SistemaOpiniones.Data.Loaders;
using SistemaOpiniones.Data.Interfaces;
using SistemaOpiniones.WkService;

var builder = Host.CreateApplicationBuilder(args);

var etlConfig = builder.Configuration.GetSection("EtlConfig");
var rutaCsv = etlConfig.GetValue<string>("RutaCsvEncuestas") ?? "";
var cadenaBd = etlConfig.GetValue<string>("CadenaConexionBd") ?? "";
var cadenaDw = etlConfig.GetValue<string>("CadenaConexionDw") ?? "";
var urlApi = etlConfig.GetValue<string>("UrlApiRedesSociales") ?? "";
var dirStaging = etlConfig.GetValue<string>("DirectorioStaging") ?? "Staging";

builder.Services.AddHttpClient("ApiRedes")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

builder.Services.AddSingleton<IStagingService>(new StagingService(dirStaging));

builder.Services.AddTransient(sp => new CsvExtractor(rutaCsv));
builder.Services.AddTransient(sp => new DatabaseExtractor(cadenaBd));
builder.Services.AddTransient(sp =>
{
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("ApiRedes");
    return new ApiExtractor(httpClient, urlApi);
});

builder.Services.AddTransient<IDimensionLoader>(sp => new DimensionLoader(cadenaBd, cadenaDw));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();