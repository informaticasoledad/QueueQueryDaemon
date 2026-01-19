using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Data;

namespace ColaWorker
{
    public class GestorDeColas : BackgroundService
    {
        private readonly ILogger<GestorDeColas> _logger;
        private readonly string _connectionString;
        private readonly int _maxHilos;
        private readonly int _tiempoEsperaMs;

        private readonly string _uspProcesarSiguiente ;

        public GestorDeColas(ILogger<GestorDeColas> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                                ?? throw new ArgumentNullException("ConnectionStrings:DefaultConnection");
            
            _maxHilos = configuration.GetValue<int>("WorkerSettings:MaxHilos", 2);
            _tiempoEsperaMs = configuration.GetValue<int>("WorkerSettings:TiempoEsperaSiVacioMs", 1000);
            _uspProcesarSiguiente = configuration.GetValue<string>("WorkerSettings:UspProcesarSiguiente", "");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation($"Iniciando Gestor de Colas con {_maxHilos} hilos concurrentes.");

            // Creamos una lista de Tareas (Tasks), cada una representa un "Hilo" o Worker independiente
            var tareasWorkers = new List<Task>();

            for (int i = 0; i < _maxHilos; i++)
            {
                int workerId = i + 1;
                // Iniciamos cada worker en paralelo sin esperar a que termine (Fire and Forget controlado)
                tareasWorkers.Add(Task.Run(() => CicloDeProcesamiento(workerId, stoppingToken), stoppingToken));
            }

            // Esperamos a que todos terminen (solo ocurrirá si se cancela la app)
            await Task.WhenAll(tareasWorkers);
        }

        private async Task CicloDeProcesamiento(int workerId, CancellationToken token)
        {
            _logger.LogInformation($"Worker #{workerId} iniciado.");

            while (!token.IsCancellationRequested)
            {
                bool trabajoEncontrado = false;

                try
                {
                    // Llamamos a la base de datos
                    trabajoEncontrado = await EjecutarSPProcesarSiguiente();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error crítico en Worker #{workerId}. Reintentando en 5 seg...");
                    try { await Task.Delay(5000, token); } catch { /* Ignorar cancelación durante espera error */ }
                    continue; 
                }

                if (!trabajoEncontrado)
                {
                    // Si la cola estaba vacía, dormimos un poco para no saturar la CPU/DB
                    // Esto convierte el bucle en un "Polling eficiente"
                    // _logger.LogDebug($"Worker #{workerId}: Cola vacía, durmiendo...");
                    try 
                    { 
                        await Task.Delay(_tiempoEsperaMs, token); 
                    } 
                    catch (TaskCanceledException) 
                    { 
                        break; 
                    }
                }
                else
                {
                    // Si encontró trabajo, no dormimos. Volvemos a consultar inmediatamente
                    // para vaciar la cola lo más rápido posible.
                    _logger.LogInformation($"Worker #{workerId}: Tarea procesada con éxito.");
                }
            }

            _logger.LogInformation($"Worker #{workerId} detenido.");
        }

        private async Task<bool> EjecutarSPProcesarSiguiente()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqlCommand(_uspProcesarSiguiente, connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    
                    // Capturamos el valor de retorno (RETURN @TrabajoRealizado)
                    var returnParameter = command.Parameters.Add("@ReturnVal", SqlDbType.Int);
                    returnParameter.Direction = ParameterDirection.ReturnValue;

                    // El timeout debe ser alto porque el SP simula trabajo (WAITFOR)
                    command.CommandTimeout = 300; 

                    await command.ExecuteNonQueryAsync();

                    // 1 = Hubo trabajo, 0 = No hubo trabajo
                    int resultado = (int)returnParameter.Value;
                    return resultado == 1;
                }
            }
        }
    }
}