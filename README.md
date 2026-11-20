# Sistema de Análisis de Opiniones de Clientes - Proceso de Extracción (ETL)

Solución desarrollada en **C# (.NET 8)** bajo principios de **Clean Architecture** y **SOLID** que implementa el proceso de extracción automatizado mediante un **Worker Service** desde tres fuentes heterogéneas hacia una zona de aterrizaje (**Staging Area**).

## Estructura de la Solución

* **`SistemaOpiniones.Api`**: Web API en ASP.NET Core (.NET 8) que expone el endpoint REST `GET /api/ComentariosSociales` con comentarios provenientes de redes sociales.
* **`SistemaOpiniones.Data`**: Biblioteca de clases con los modelos de datos crudos (`Models`), el contrato genérico `IExtractor<T>` (`Interfaces`), las implementaciones de extracción (`CsvExtractor`, `DatabaseExtractor`, `ApiExtractor`) y el servicio de persistencia temporal (`StagingService`).
* **`SistemaOpiniones.WkService`**: Worker Service en segundo plano (`BackgroundService`) que orquesta periódicamente la extracción asíncrona, mide tiempos de rendimiento con `Stopwatch` y registra eventos con `ILogger`.
* **`Staging/`**: Carpeta de aterrizaje donde se almacenan los lotes extraídos en formato JSON con marca temporal (*Timestamp*).
