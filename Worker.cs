using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text;
using System.Xml;

namespace ColaWorker
{
    public class GestorDeColas : BackgroundService
    {
        private readonly ILogger<GestorDeColas> _logger;
        private readonly string _connectionString;
        private readonly int _maxHilos;
        private readonly int _tiempoEsperaMs;

        public GestorDeColas(ILogger<GestorDeColas> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                                ?? throw new ArgumentNullException("ConnectionStrings:DefaultConnection");
            
            // Configuración por defecto: 2 hilos, 1 segundo de espera si cola vacía
            _maxHilos = configuration.GetValue<int>("WorkerSettings:MaxHilos", 2);
            _tiempoEsperaMs = configuration.GetValue<int>("WorkerSettings:TiempoEsperaSiVacioMs", 1000);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation($"Iniciando Gestor de Colas (Modo Ejecución C#) con {_maxHilos} hilos.");

            var tareasWorkers = new List<Task>();

            // Lanzamos N tareas en paralelo
            for (int i = 0; i < _maxHilos; i++)
            {
                int workerId = i + 1;
                tareasWorkers.Add(Task.Run(() => CicloDeProcesamiento(workerId, stoppingToken), stoppingToken));
            }

            // Mantenemos la ejecución viva hasta que se cancele el servicio
            await Task.WhenAll(tareasWorkers);
        }

        private async Task CicloDeProcesamiento(int workerId, CancellationToken token)
        {
            _logger.LogInformation($"Worker #{workerId} listo para procesar.");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 1. Obtener Siguiente Tarea (Reserva en DB usando SP atómico)
                    var tarea = await ObtenerSiguienteTareaAsync();

                    if (tarea == null)
                    {
                        // Cola vacía, dormir para no saturar CPU/DB
                        await Task.Delay(_tiempoEsperaMs, token);
                        continue;
                    }

                    _logger.LogInformation($"Worker #{workerId}: Procesando petición {tarea.Id}...");

                    // 2. Ejecutar la Query del Usuario y serializar a XML
                    string? resultadoXml = null;
                    string? errorMsg = null;

                    try
                    {
                        resultadoXml = await EjecutarQueryUsuarioYSerializar(tarea.QuerySql);
                    }
                    catch (Exception ex)
                    {
                        // Capturamos error de sintaxis o ejecución SQL del usuario
                        errorMsg = ex.Message;
                        _logger.LogError($"Worker #{workerId}: Error en query usuario. {ex.Message}");
                    }

                    // 3. Guardar Resultado o Error en DB
                    await GuardarRespuestaAsync(tarea.Id, resultadoXml, errorMsg);
                }
                catch (Exception ex)
                {
                    // Error crítico de infraestructura (ej. se cayó la conexión con el servidor de colas)
                    _logger.LogError(ex, $"Error de infraestructura en Worker #{workerId}. Reintentando en 5s...");
                    await Task.Delay(5000, token);
                }
            }
        }

        // DTO simple interna
        private class TareaCola { public Guid Id { get; set; } public string QuerySql { get; set; } = string.Empty; }

        private async Task<TareaCola?> ObtenerSiguienteTareaAsync()
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand("dbo.usp_ObtenerSiguientePeticion", conn);
            cmd.CommandType = CommandType.StoredProcedure;

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new TareaCola
                {
                    Id = reader.GetGuid(0),
                    QuerySql = reader.GetString(1)
                };
            }
            return null;
        }

        private async Task GuardarRespuestaAsync(Guid idPeticion, string? xml, string? error)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand("dbo.usp_GuardarRespuesta", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@IdPeticion", idPeticion);
            // Manejo correcto de DBNull para parámetros opcionales
            cmd.Parameters.AddWithValue("@ResultadoXml", (object?)xml ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@MensajeError", (object?)error ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<string?> EjecutarQueryUsuarioYSerializar(string querySql)
        {
            // Ejecutamos la query arbitraria del usuario
            // NOTA: Aquí podrías usar una cadena de conexión distinta (ej. solo lectura) si quisieras aislar entornos.
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(querySql, conn);
            // Importante: Aumentar timeout para queries pesadas que pueda mandar el usuario
            cmd.CommandTimeout = 300; 

            // Usamos DataAdapter para llenar un DataSet automáticamente
            // Esto maneja SPs, SELECTs y resultados vacíos sin problemas.
            using var adapter = new SqlDataAdapter(cmd);
            var ds = new DataSet();
            
            adapter.Fill(ds);

            // Si no devuelve tablas o filas
            if (ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                return null; 
            }

            DataTable dt = ds.Tables[0];
            
            // Serialización manual a XML compatible con el formato que espera T-SQL
            // Estructura esperada por usp_EsperarRespuesta: <Resultado><Fila><Col1>Val</Col1></Fila>...</Resultado>
            
            var sb = new StringBuilder();
            // OmitXmlDeclaration para no generar <?xml version...?> que a veces molesta en SQL
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };

            using (var writer = XmlWriter.Create(sb, settings))
            {
                writer.WriteStartElement("Resultado");
                
                foreach (DataRow row in dt.Rows)
                {
                    writer.WriteStartElement("Fila");
                    foreach (DataColumn col in dt.Columns)
                    {
                        var val = row[col];
                        if (val != DBNull.Value)
                        {
                            // Si la columna no tiene nombre (ej: SELECT GETDATE()), le damos uno genérico
                            string colName = string.IsNullOrWhiteSpace(col.ColumnName) ? "Valor_Sin_Alias" : col.ColumnName;
                            
                            // Aseguramos que el nombre de la columna sea un tag XML válido (espacios a guiones bajos, etc.)
                            colName = XmlConvert.EncodeName(colName);

                            writer.WriteStartElement(colName);
                            writer.WriteString(val.ToString());
                            writer.WriteEndElement();
                        }
                    }
                    writer.WriteEndElement(); // Fin Fila
                }
                
                writer.WriteEndElement(); // Fin Resultado
            }

            return sb.ToString();
        }
    }
}