using DateroWebB.Models;

namespace DateroWebB.Services;

public class APIEnvioService
{
    private readonly HttpClient _httpClient;
    private readonly ServiciosExternosSettings _config;

    public APIEnvioService(ServiciosExternosSettings config)
    {
        _config = config;

        // En .NET 10 se usa HttpClientHandler para ignorar SSL (solo desarrollo/pruebas)
        var handler = new HttpClientHandler();
        if (_config.IgnorarErroresSsl)
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(_config.HttpTimeoutSegundos)
        };
    }

    public async Task<bool> ActualizarRutaact(string placa)
    {
        try
        {
            var url = $"{_config.ApiActualizarRutaactUrl.TrimEnd('/')}/{placa}";
            Console.WriteLine($"\n[API] Llamando a ActualizarRutaact para {placa}...");

            var response = await _httpClient.PutAsync(url, null);

            if (response.IsSuccessStatusCode)
            {
                var resultado = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[API] ✓ ActualizarRutaact exitoso: {resultado}");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[API] ❌ Error ActualizarRutaact: {response.StatusCode} - {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[API] ❌ Excepción en ActualizarRutaact: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> EndRuta(string deviceID)
    {
        try
        {
            var url = $"{_config.ApiEndRutaUrl.TrimEnd('/')}/{deviceID}";
            Console.WriteLine($"\n[API] Llamando a endruta para {deviceID}...");

            var response = await _httpClient.PostAsync(url, null);

            if (response.IsSuccessStatusCode)
            {
                var resultado = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[API] ✓ EndRuta exitoso: {resultado}");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[API] ❌ Error EndRuta: {response.StatusCode} - {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[API] ❌ Excepción en EndRuta: {ex.Message}");
            return false;
        }
    }
}
