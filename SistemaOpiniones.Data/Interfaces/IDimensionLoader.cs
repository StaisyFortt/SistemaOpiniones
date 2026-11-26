using SistemaOpiniones.Data.Models;

namespace SistemaOpiniones.Data.Interfaces
{
    public interface IDimensionLoader
    {
        Task<DimensionLoadResult> CargarDimSentimientoAsync(IEnumerable<EncuestaRawModel> encuestas, CancellationToken cancellationToken = default);

        Task<DimensionLoadResult> CargarDimCanalFuenteAsync(IEnumerable<EncuestaRawModel> encuestas, IEnumerable<ComentarioSocialRawModel> comentarios, CancellationToken cancellationToken = default);

        Task<DimensionLoadResult> CargarDimTiempoAsync(IEnumerable<DateTime> fechas, CancellationToken cancellationToken = default);
        
        Task<DimensionLoadResult> CargarDimProductoAsync(CancellationToken cancellationToken = default);

        Task<DimensionLoadResult> CargarDimClienteAsync(CancellationToken cancellationToken = default);
    }
}
