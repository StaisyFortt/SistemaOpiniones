using System.Data;
using Microsoft.Data.SqlClient;
using SistemaOpiniones.Data.Interfaces;
using SistemaOpiniones.Data.Models;

namespace SistemaOpiniones.Data.Extractors;

// extrae resenas publicadas en la web desde la base de datos relacional
public class DatabaseExtractor : IExtractor<ResenaWebRawModel>
{
    private readonly string _connectionString;

    public string NombreFuente => "SQL_ResenasWeb";

    public DatabaseExtractor(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<ResenaWebRawModel>> ExtraerAsync(CancellationToken cancellationToken = default)
    {
        var resultados = new List<ResenaWebRawModel>();

        // query que une opiniones con fuentes usando nolock para no bloquear tablas
        const string query = @"
            SELECT 
                o.id_opinion,
                o.id_cliente,
                o.id_producto,
                o.tipo_opinion,
                o.contenido,
                o.puntuacion,
                o.fecha_emision,
                f.nombre_fuente
            FROM opiniones o WITH (NOLOCK)
            INNER JOIN fuentes_datos f WITH (NOLOCK) ON o.id_fuente = f.id_fuente
            WHERE o.tipo_opinion = 'Reseña Web';";

        // abre la conexion de forma asincrona hacia sql server
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // prepara el comando sql
        using var cmd = new SqlCommand(query, conn);

        // ejecuta la lectura y cierra la conexion automaticamente al terminar
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection, cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            // mapea las columnas y valida valores nulos
            var modelo = new ResenaWebRawModel
            {
                IdOpinion = reader.GetInt64(0),
                IdCliente = reader.IsDBNull(1) ? null : reader.GetInt64(1),
                IdProducto = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                TipoOpinion = reader.GetString(3),
                Contenido = reader.GetString(4),
                Puntuacion = reader.IsDBNull(5) ? null : reader.GetInt16(5),
                FechaEmision = reader.GetDateTime(6),
                NombreFuente = reader.GetString(7)
            };

            // agrega el registro a la lista final
            resultados.Add(modelo);
        }

        return resultados;
    }
}
