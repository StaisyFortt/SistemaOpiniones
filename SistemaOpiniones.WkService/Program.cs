using SistemaOpiniones.Data.Extractors;
using SistemaOpiniones.Data.Staging;
using SistemaOpiniones.WkService;

var builder = Host.CreateApplicationBuilder(args);

// lee la configuracion desde appsettings.json
var etlConfig = builder.Configuration.GetSection("EtlConfig");
var rutaCsv = etlConfig.GetValue<string>("RutaCsvEncuestas") ?? "";
var cadenaBd = etlConfig.GetValue<string>("CadenaConexionBd") ?? "";
var urlApi = etlConfig.GetValue<string>("UrlApiRedesSociales") ?? "";
var dirStaging = etlConfig.GetValue<string>("DirectorioStaging") ?? "Staging";

// registra httpclientfactory para consumo seguro de apis locales
builder.Services.AddHttpClient("ApiRedes")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

// registra el servicio de staging
builder.Services.AddSingleton<IStagingService>(new StagingService(dirStaging));

// registra los extractores en el contenedor de dependencias
builder.Services.AddTransient(sp => new CsvExtractor(rutaCsv));
builder.Services.AddTransient(sp => new DatabaseExtractor(cadenaBd));
builder.Services.AddTransient(sp =>
{
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("ApiRedes");
    return new ApiExtractor(httpClient, urlApi);
});

// registra el worker service en segundo plano
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();