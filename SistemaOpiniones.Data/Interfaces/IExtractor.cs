
namespace SistemaOpiniones.Data.Interfaces
{
    public interface IExtractor<T>
    {
        string NombreFuente { get; }
        Task<IEnumerable<T>> ExtraerAsync(CancellationToken cancellationToken = default);
    }
}
