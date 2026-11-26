using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.SqlClient;
using SistemaOpiniones.Data.Models;
using SistemaOpiniones.Data.Interfaces;
using System.Diagnostics.CodeAnalysis;

namespace SistemaOpiniones.Data.Loaders
{
    public class DimensionLoader : IDimensionLoader
    {
        private readonly string _origenConnectionString;
        private readonly string _dwConnectionString;

        public DimensionLoader(string origenConnectionString, string dwConnectionString)
        {
            _origenConnectionString = origenConnectionString;
            _dwConnectionString = dwConnectionString;
        }

        public async Task<DimensionLoadResult> CargarDimSentimientoAsync(IEnumerable<EncuestaRawModel> encuestas, CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            var resultado = new DimensionLoadResult { NombreDimension = "Dim_Sentimiento" };
            // diccionario con valores y su polaridad
            var sentimientos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Positiva"] = "Favorable",
                ["Neutra"] = "Neutral",
                ["Negativa"] = "Desfavorable",
                ["Sin Clasificar"] = "Indeterminado"
            };

            // agrega clasificaciones distintas encontradas en el csv si no estaban
            foreach (var enc in encuestas)
            {
                var clasif = enc.Clasificacion?.Trim();
                if (!string.IsNullOrWhiteSpace(clasif) && !sentimientos.ContainsKey(clasif))
                {
                    sentimientos[clasif] = "Indeterminado";
                }
            }
            using var conn = new SqlConnection(_dwConnectionString);
            await conn.OpenAsync(cancellationToken);
            using var tx = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);
            try
            {
                // inserta solo si la clasificacion no existe aun en el dw
                const string sql = @"
                IF NOT EXISTS (SELECT 1 FROM dbo.Dim_Sentimiento WHERE clasificacion = @clasificacion)
                BEGIN
                    INSERT INTO dbo.Dim_Sentimiento (clasificacion, polaridad)
                    VALUES (@clasificacion, @polaridad);
                    SELECT 1;
                END
                ELSE
                BEGIN
                    SELECT 0;
                END";
                foreach (var item in sentimientos)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var cmd = new SqlCommand(sql, conn, tx);
                    cmd.Parameters.AddWithValue("@clasificacion", item.Key);
                    cmd.Parameters.AddWithValue("@polaridad", item.Value);
                    var accion = (int)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0);
                    if (accion == 1) resultado.Insertados++;
                    else resultado.Omitidos++;
                }
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
            sw.Stop();
            resultado.DuracionMs = sw.ElapsedMilliseconds;
            return resultado;
        }

        public async Task<DimensionLoadResult> CargarDimCanalFuenteAsync(IEnumerable<EncuestaRawModel> encuestas, IEnumerable<ComentarioSocialRawModel> comentarios, CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            var resultado = new DimensionLoadResult { NombreDimension = "Dim_Canal_Fuente" };

            // diccionario para evitar canales duplicados en memoria antes de ir al dw
            var canales = new Dictionary<string, (int? IdOrigen, string TipoOrigen)>(StringComparer.OrdinalIgnoreCase);

            // lee las fuentes existentes desde opinionesdb
            using (var origenConn = new SqlConnection(_origenConnectionString))
            {
                await origenConn.OpenAsync(cancellationToken);
                const string origenSql = "SELECT id_fuente, nombre_fuente, tipo_origen FROM dbo.fuentes_datos WITH (NOLOCK);";
                using var cmd = new SqlCommand(origenSql, origenConn);
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var idOrigen = reader.GetInt32(0);
                    var nombre = reader.GetString(1).Trim();
                    var tipoRaw = reader.IsDBNull(2) ? null : reader.GetString(2).Trim();
                    canales[nombre] = (idOrigen, ClasificarTipoOrigen(nombre, tipoRaw));
                }
            }

            // agrega canales provenientes de las encuestas csv
            foreach (var enc in encuestas)
            {
                var fuente = enc.Fuente?.Trim();
                if (!string.IsNullOrWhiteSpace(fuente) && !canales.ContainsKey(fuente))
                {
                    canales[fuente] = (null, ClasificarTipoOrigen(fuente, "Encuesta Interna"));
                }
            }

            // agrega redes sociales provenientes de la api rest
            foreach (var com in comentarios)
            {
                var red = com.RedSocial?.Trim();
                if (!string.IsNullOrWhiteSpace(red) && !canales.ContainsKey(red))
                {
                    canales[red] = (null, "Red Social");
                }
            }

            using var dwConn = new SqlConnection(_dwConnectionString);
            await dwConn.OpenAsync(cancellationToken);
            using var tx = (SqlTransaction)await dwConn.BeginTransactionAsync(cancellationToken);
            try
            {
                const string sql = @"
                IF NOT EXISTS (SELECT 1 FROM dbo.Dim_Canal_Fuente WHERE nombre_canal = @nombre)
                BEGIN
                    INSERT INTO dbo.Dim_Canal_Fuente (id_fuente_origen, nombre_canal, tipo_origen)
                    VALUES (@idOrigen, @nombre, @tipoOrigen);
                    SELECT 1;
                END
                ELSE
                BEGIN
                    UPDATE dbo.Dim_Canal_Fuente
                    SET tipo_origen = @tipoOrigen
                    WHERE nombre_canal = @nombre AND tipo_origen <> @tipoOrigen;
                    SELECT CASE WHEN @@ROWCOUNT > 0 THEN 2 ELSE 0 END;
                END";
                foreach (var item in canales)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var cmd = new SqlCommand(sql, dwConn, tx);
                    cmd.Parameters.AddWithValue("@idOrigen", (object?)item.Value.IdOrigen ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@nombre", item.Key);
                    cmd.Parameters.AddWithValue("@tipoOrigen", item.Value.TipoOrigen);
                    var accion = (int)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0);
                    if (accion == 1) resultado.Insertados++;
                    else if (accion == 2) resultado.Actualizados++;
                    else resultado.Omitidos++;
                }
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
            sw.Stop();
            resultado.DuracionMs = sw.ElapsedMilliseconds;
            return resultado;
        }

        public async Task<DimensionLoadResult> CargarDimTiempoAsync(IEnumerable<DateTime> fechas, CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            var resultado = new DimensionLoadResult { NombreDimension = "Dim_Tiempo" };
            var culturaEs = new CultureInfo("es-ES");

            // toma solo las fechas sin hora y elimina duplicados
            var fechasUnicas = fechas
                .Select(f => f.Date)
                .Distinct()
                .OrderBy(f => f)
                .ToList();

            using var dwConn = new SqlConnection(_dwConnectionString);
            await dwConn.OpenAsync(cancellationToken);
            using var tx = (SqlTransaction)await dwConn.BeginTransactionAsync(cancellationToken);
            try
            {
                const string sql = @"
                IF NOT EXISTS (SELECT 1 FROM dbo.Dim_Tiempo WHERE id_tiempo = @idTiempo)
                BEGIN
                    INSERT INTO dbo.Dim_Tiempo
                        (id_tiempo, fecha, anio, trimestre, mes, nombre_mes, dia, dia_semana, es_fin_semana)
                    VALUES
                        (@idTiempo, @fecha, @anio, @trimestre, @mes, @nombreMes, @dia, @diaSemana, @esFinSemana);
                    SELECT 1;
                END
                ELSE
                BEGIN
                    SELECT 0;
                END";
                foreach (var fecha in fechasUnicas)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // genera clave entera yyyymmdd ej 20261125
                    var idTiempo = (fecha.Year * 10000) + (fecha.Month * 100) + fecha.Day;
                    var trimestre = ((fecha.Month - 1) / 3) + 1;
                    var nombreMes = culturaEs.TextInfo.ToTitleCase(fecha.ToString("MMMM", culturaEs));
                    var diaSemana = culturaEs.TextInfo.ToTitleCase(fecha.ToString("dddd", culturaEs));
                    var esFinSemana = fecha.DayOfWeek == DayOfWeek.Saturday || fecha.DayOfWeek == DayOfWeek.Sunday;
                    using var cmd = new SqlCommand(sql, dwConn, tx);
                    cmd.Parameters.AddWithValue("@idTiempo", idTiempo);
                    cmd.Parameters.AddWithValue("@fecha", fecha);
                    cmd.Parameters.AddWithValue("@anio", fecha.Year);
                    cmd.Parameters.AddWithValue("@trimestre", trimestre);
                    cmd.Parameters.AddWithValue("@mes", fecha.Month);
                    cmd.Parameters.AddWithValue("@nombreMes", nombreMes);
                    cmd.Parameters.AddWithValue("@dia", fecha.Day);
                    cmd.Parameters.AddWithValue("@diaSemana", diaSemana);
                    cmd.Parameters.AddWithValue("@esFinSemana", esFinSemana);
                    var accion = (int)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0);
                    if (accion == 1) resultado.Insertados++;
                    else resultado.Omitidos++;
                }
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
            sw.Stop();
            resultado.DuracionMs = sw.ElapsedMilliseconds;
            return resultado;
        }

        public async Task<DimensionLoadResult> CargarDimProductoAsync(CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            var resultado = new DimensionLoadResult { NombreDimension = "Dim_Producto" };

            var productosOrigen = new List<(long IdOrigen, string Sku, string Nombre, string Categoria, decimal Precio)> ();

            // lee productos unidos con su categoria desde opinionesdb
            using (var origenConn = new SqlConnection(_origenConnectionString))
            {
                await origenConn.OpenAsync(cancellationToken);
                const string origenSql = @"
                SELECT 
                    p.id_producto,
                    p.sku,
                    c.nombre AS categoria,
                    p.precio
                FROM dbo.productos p WITH (NOLOCK)
                INNER JOIN dbo.categorias c WITH (NOLOCK) ON p.id_categoria = c.id_categoria;";

                using var cmd = new SqlCommand(origenSql, origenConn);
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    var idProd = reader.GetInt64(0);
                    var sku = reader.GetString(1).Trim();
                    var categoria = reader.GetString(2).Trim();
                    var precio = reader.GetDecimal(3);
                    var nombreProducto = $"Producto {idProd}";

                    productosOrigen.Add((idProd, sku, nombreProducto, categoria, precio));
                }
            }

            using var dwConn = new SqlConnection(_dwConnectionString);
            await dwConn.OpenAsync(cancellationToken);
            using var tx = (SqlTransaction)await dwConn.BeginTransactionAsync(cancellationToken);

            try
            {
                // upsert scd tipo 1 inserta si no existe o actualiza si cambio algun atributo
                const string sql = @"
                IF NOT EXISTS (SELECT 1 FROM dbo.Dim_Producto WHERE id_producto_origen = @idOrigen)
                BEGIN
                    INSERT INTO dbo.Dim_Producto
                        (id_producto_origen, sku, nombre_producto, categoria, precio_referencia)
                    VALUES
                        (@idOrigen, @sku, @nombre, @categoria, @precio);
                    SELECT 1;
                END
                ELSE
                BEGIN
                    UPDATE dbo.Dim_Producto
                    SET sku = @sku,
                        nombre_producto = @nombre,
                        categoria = @categoria,
                        precio_referencia = @precio
                    WHERE id_producto_origen = @idOrigen
                      AND (sku <> @sku OR nombre_producto <> @nombre OR categoria <> @categoria OR precio_referencia <> @precio);
                    SELECT CASE WHEN @@ROWCOUNT > 0 THEN 2 ELSE 0 END;
                END";

                foreach (var prod in productosOrigen)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var cmd = new SqlCommand(sql, dwConn, tx);
                    cmd.Parameters.AddWithValue("@idOrigen", prod.IdOrigen);
                    cmd.Parameters.AddWithValue("@sku", prod.Sku);
                    cmd.Parameters.AddWithValue("@nombre", prod.Nombre);
                    cmd.Parameters.AddWithValue("@categoria", prod.Categoria);
                    cmd.Parameters.AddWithValue("@precio", prod.Precio);
                    var accion = (int)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0);
                    if (accion == 1) resultado.Insertados++;
                    else if (accion == 2) resultado.Actualizados++;
                    else resultado.Omitidos++;
                }
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
            sw.Stop();
            resultado.DuracionMs = sw.ElapsedMilliseconds;
            return resultado;
        }

        public async Task<DimensionLoadResult> CargarDimClienteAsync(CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            var resultado = new DimensionLoadResult { NombreDimension = "Dim_Cliente" };
            var clientesOrigen = new List<(long IdOrigen, string Nombre, string Email, string Segmento, string Pais)>();

            // lee los clientes desde opinionesdb y calcula su segmento
            using (var origenConn = new SqlConnection(_origenConnectionString))
            {
                await origenConn.OpenAsync(cancellationToken);
                const string origenSql = "SELECT id_cliente, nombre, email FROM dbo.clientes WITH (NOLOCK);";
                using var cmd = new SqlCommand(origenSql, origenConn);
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var idCli = reader.GetInt64(0);
                    var nombre = reader.GetString(1).Trim();
                    var email = reader.GetString(2).Trim().ToLowerInvariant();
                    var segmento = (idCli % 3) switch
                    {
                        0 => "Cliente Frecuente",
                        1 => "Cliente Ocasional",
                        _ => "Nuevo Cliente"
                    };
                    clientesOrigen.Add((idCli, nombre, email, segmento, "Republica Dominicana"));
                }
            }

            using var dwConn = new SqlConnection(_dwConnectionString);
            await dwConn.OpenAsync(cancellationToken);
            using var tx = (SqlTransaction)await dwConn.BeginTransactionAsync(cancellationToken);
            try
            {
                // asegura que exista el registro 0 para comentarios anonimos de redes sociales
                const string anonimoSql = @"
                IF NOT EXISTS (SELECT 1 FROM dbo.Dim_Cliente WHERE id_dim_cliente = 0)
                BEGIN
                    SET IDENTITY_INSERT dbo.Dim_Cliente ON;
                    INSERT INTO dbo.Dim_Cliente
                        (id_dim_cliente, id_cliente_origen, nombre_cliente, email, segmento_cliente, pais)
                    VALUES
                        (0, NULL, 'Usuario Anonimo / Red Social', NULL, 'Anonimo', 'No Especificado');
                    SET IDENTITY_INSERT dbo.Dim_Cliente OFF;
                    SELECT 1;
                END
                ELSE
                BEGIN
                    SELECT 0;
                END";

                using (var anonCmd = new SqlCommand(anonimoSql, dwConn, tx))
                {
                    var accionAnon = (int)(await anonCmd.ExecuteScalarAsync(cancellationToken) ?? 0);
                    if (accionAnon == 1) resultado.Insertados++;
                }

                // upsert scd tipo 1 para clientes registrados
                const string sql = @"
                IF NOT EXISTS (SELECT 1 FROM dbo.Dim_Cliente WHERE id_cliente_origen = @idOrigen)
                BEGIN
                    INSERT INTO dbo.Dim_Cliente
                        (id_cliente_origen, nombre_cliente, email, segmento_cliente, pais)
                    VALUES
                        (@idOrigen, @nombre, @email, @segmento, @pais);
                    SELECT 1;
                END
                ELSE
                BEGIN
                    UPDATE dbo.Dim_Cliente
                    SET nombre_cliente = @nombre,
                        email = @email,
                        segmento_cliente = @segmento,
                        pais = @pais
                    WHERE id_cliente_origen = @idOrigen
                      AND (nombre_cliente <> @nombre OR ISNULL(email, '') <> @email OR segmento_cliente <> @segmento OR pais <> @pais);
                    SELECT CASE WHEN @@ROWCOUNT > 0 THEN 2 ELSE 0 END;
                END";
                foreach (var cli in clientesOrigen)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var cmd = new SqlCommand(sql, dwConn, tx);
                    cmd.Parameters.AddWithValue("@idOrigen", cli.IdOrigen);
                    cmd.Parameters.AddWithValue("@nombre", cli.Nombre);
                    cmd.Parameters.AddWithValue("@email", cli.Email);
                    cmd.Parameters.AddWithValue("@segmento", cli.Segmento);
                    cmd.Parameters.AddWithValue("@pais", cli.Pais);
                    var accion = (int)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0);
                    if (accion == 1) resultado.Insertados++;
                    else if (accion == 2) resultado.Actualizados++;
                    else resultado.Omitidos++;
                }
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
            sw.Stop();
            resultado.DuracionMs = sw.ElapsedMilliseconds;
            return resultado;
        }

        private static string ClasificarTipoOrigen(string nombreCanal, string? tipoActual)
        {
            if (nombreCanal.Equals("Instagram", StringComparison.OrdinalIgnoreCase) ||
                nombreCanal.Equals("Twitter", StringComparison.OrdinalIgnoreCase) ||
                nombreCanal.Equals("Facebook", StringComparison.OrdinalIgnoreCase))
            {
                return "Red Social";
            }
            if (nombreCanal.Contains("Web", StringComparison.OrdinalIgnoreCase))
            {
                return "Sitio Web";
            }
            if (nombreCanal.Contains("Encuesta", StringComparison.OrdinalIgnoreCase))
            {
                return "Encuesta Interna";
            }
            return string.IsNullOrWhiteSpace(tipoActual) ? "Otro" : tipoActual;
        }
    }

}
