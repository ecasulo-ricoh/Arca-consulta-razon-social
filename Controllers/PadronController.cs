using ARCA_razon_social.Models;
using ARCA_razon_social.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography.X509Certificates;

namespace ARCA_razon_social.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PadronController : ControllerBase
{
    private readonly IAfipAuthService _authService;
    private readonly ILogger<PadronController> _logger;
    private readonly string _cuitRepresentada;

    public PadronController(IAfipAuthService authService, ILogger<PadronController> logger, IConfiguration configuration)
    {
        _authService = authService;
        _logger = logger;
        
        // Obtener el CUIT del certificado automáticamente
        _cuitRepresentada = GetCuitFromCertificate();
        
        _logger.LogInformation("?? CUIT Representada: {Cuit}", _cuitRepresentada);
    }
    
    private string GetCuitFromCertificate()
    {
        try
        {
            var certificatePath = Path.Combine(Directory.GetCurrentDirectory(), "certificado_arca.pfx");
            var certificatePassword = "1234";
            
            var certificate = new X509Certificate2(certificatePath, certificatePassword);
            
            // Buscar el CUIT en el Subject del certificado
            var subject = certificate.Subject;
            
            _logger.LogDebug("Certificado Subject: {Subject}", subject);
            
            // El CUIT suele estar en el CN (Common Name) o en el serialNumber
            // Formato típico: "CN=CUIT 20123456789", "serialNumber=CUIT 20123456789", etc.
            
            // Intentar extraer del Subject
            var parts = subject.Split(',');
            foreach (var part in parts)
            {
                var trimmedPart = part.Trim();
                
                // Buscar CN= o SERIALNUMBER=
                if (trimmedPart.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ||
                    trimmedPart.StartsWith("SERIALNUMBER=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = trimmedPart.Split('=')[1].Trim();
                    
                    // Extraer solo los números (CUIT tiene 11 dígitos)
                    var digitsOnly = new string(value.Where(char.IsDigit).ToArray());
                    
                    if (digitsOnly.Length == 11)
                    {
                        _logger.LogInformation("? CUIT extraído del certificado: {Cuit}", digitsOnly);
                        return digitsOnly;
                    }
                }
            }
            
            // Si no se encuentra, intentar con el SerialNumber del certificado
            var serialNumber = certificate.SerialNumber;
            var serialDigits = new string(serialNumber.Where(char.IsDigit).ToArray());
            
            if (serialDigits.Length >= 11)
            {
                // Tomar los primeros 11 dígitos
                var cuit = serialDigits.Substring(0, 11);
                _logger.LogInformation("? CUIT extraído del SerialNumber: {Cuit}", cuit);
                return cuit;
            }
            
            // Si no se pudo extraer, mostrar advertencia y usar valor por defecto
            _logger.LogWarning("?? No se pudo extraer el CUIT del certificado automáticamente");
            _logger.LogWarning("?? Debes configurar manualmente el CUIT en appsettings.json o en el código");
            _logger.LogWarning("?? Subject del certificado: {Subject}", subject);
            
            // Valor por defecto (deberás cambiarlo manualmente)
            return "20111111112";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error al extraer CUIT del certificado");
            return "20111111112"; // Valor por defecto
        }
    }

    /// <summary>
    /// Verifica la conectividad con AFIP y que el certificado funcione correctamente
    /// </summary>
    [HttpGet("HealthCheck")]
    public async Task<ActionResult> HealthCheck()
    {
        try
        {
            _logger.LogInformation("?? Ejecutando health check de AFIP...");

            var credentials = await _authService.GetCredentialsAsync();

            return Ok(new
            {
                status = "OK",
                message = "? Autenticación exitosa con AFIP",
                tokenLength = credentials.Token.Length,
                signLength = credentials.Sign.Length,
                expirationTime = credentials.ExpirationTime,
                minutesUntilExpiration = (credentials.ExpirationTime - DateTime.UtcNow).TotalMinutes
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error en health check");
            return StatusCode(500, new
            {
                status = "ERROR",
                message = "Error al autenticar con AFIP",
                error = ex.Message,
                innerError = ex.InnerException?.Message
            });
        }
    }

    /// <summary>
    /// Consulta información de un CUIT en el Padrón de AFIP
    /// </summary>
    /// <param name="cuit">CUIT de 11 dígitos a consultar (ej: 20123456789)</param>
    /// <returns>Información del contribuyente</returns>
    [HttpGet("ConsultarCuit/{cuit}")]
    public async Task<ActionResult<PadronResponse>> ConsultarCuit(string cuit)
    {
        try
        {
            // Validar formato de CUIT
            if (string.IsNullOrWhiteSpace(cuit) || cuit.Length != 11)
            {
                return BadRequest(new { error = "El CUIT debe tener 11 dígitos" });
            }

            if (!long.TryParse(cuit, out _))
            {
                return BadRequest(new { error = "El CUIT debe contener solo números" });
            }

            // Obtener credenciales de AFIP
            _logger.LogInformation("?? Obteniendo credenciales de AFIP...");
            var credentials = await _authService.GetCredentialsAsync();

            // Llamar al servicio de Padrón
            _logger.LogInformation("?? Consultando CUIT {Cuit} en el servicio de Padrón...", cuit);
            var personaData = await GetPersonaFromAfip(cuit, credentials.Token, credentials.Sign);

            _logger.LogInformation("? Consulta exitosa para CUIT {Cuit}", cuit);
            return Ok(personaData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error al consultar CUIT {Cuit}", cuit);
            return StatusCode(500, new { error = "Error al consultar el CUIT en AFIP", details = ex.Message });
        }
    }

    private async Task<PadronResponse> GetPersonaFromAfip(string cuit, string token, string sign)
    {
        try
        {
            _logger.LogInformation("?? Conectando al servicio de Padrón A13...");

            // Crear el cliente del servicio de Padrón usando la clase generada
            var binding = new System.ServiceModel.BasicHttpBinding(System.ServiceModel.BasicHttpSecurityMode.Transport);
            binding.Security.Transport.ClientCredentialType = System.ServiceModel.HttpClientCredentialType.None;
            binding.MaxReceivedMessageSize = 655360;
            binding.MaxBufferSize = 655360;
            binding.ReaderQuotas.MaxStringContentLength = 655360;

            var endpoint = new System.ServiceModel.EndpointAddress("https://awshomo.afip.gov.ar/sr-padron/webservices/personaServiceA13");

            // Usar la clase generada por el servicio conectado
            var client = new Padron.PersonaServiceA13Client(binding, endpoint);

            _logger.LogInformation("?? Enviando consulta para CUIT: {Cuit}", cuit);

            // Llamar al servicio usando el método generado
            var response = await client.getPersonaAsync(token, sign, long.Parse(_cuitRepresentada), long.Parse(cuit));

            _logger.LogInformation("?? Respuesta recibida del servicio de Padrón");

            // Extraer datos de la respuesta
            var personaReturn = response.personaReturn;

            if (personaReturn == null || personaReturn.persona == null)
            {
                _logger.LogWarning("?? No se encontró información para el CUIT: {Cuit}", cuit);
                throw new Exception("No se encontró información para el CUIT especificado");
            }

            var persona = personaReturn.persona;

            // Construir domicilio desde el array de domicilios
            var domicilio = string.Empty;
            if (persona.domicilio != null && persona.domicilio.Length > 0)
            {
                // Buscar domicilio fiscal (tipo 1) o tomar el primero
                var domicilioFiscal = persona.domicilio.FirstOrDefault(d => d.tipoDomicilio == "1")
                                     ?? persona.domicilio.FirstOrDefault();

                if (domicilioFiscal != null)
                {
                    var partes = new List<string>();

                    if (!string.IsNullOrEmpty(domicilioFiscal.direccion))
                        partes.Add(domicilioFiscal.direccion);

                    if (!string.IsNullOrEmpty(domicilioFiscal.localidad))
                        partes.Add(domicilioFiscal.localidad);

                    if (!string.IsNullOrEmpty(domicilioFiscal.descripcionProvincia))
                        partes.Add(domicilioFiscal.descripcionProvincia);

                    domicilio = string.Join(" ", partes);
                }
            }

            // Obtener razón social o nombre
            var razonSocial = !string.IsNullOrEmpty(persona.razonSocial)
                ? persona.razonSocial
                : $"{persona.nombre ?? ""} {persona.apellido ?? ""}".Trim();

            if (string.IsNullOrEmpty(razonSocial))
            {
                razonSocial = "Sin datos";
            }

            // Determinar estado
            var estado = "ACTIVO";
            if (persona.fechaFallecimientoSpecified && persona.fechaFallecimiento != DateTime.MinValue)
            {
                estado = "FALLECIDO";
            }
            else if (!string.IsNullOrEmpty(persona.estadoClave))
            {
                estado = persona.estadoClave;
            }

            _logger.LogInformation("? Datos obtenidos - Razón Social: {RazonSocial}, Estado: {Estado}", razonSocial, estado);

            return new PadronResponse
            {
                RazonSocial = razonSocial,
                Domicilio = domicilio,
                Estado = estado
            };
        }
        catch (System.ServiceModel.FaultException<Padron.SRValidationException> faultEx)
        {
            _logger.LogError(faultEx, "? Error de validación del servicio de Padrón");
            throw new Exception($"Error de validación AFIP: {faultEx.Message}", faultEx);
        }
        catch (System.ServiceModel.FaultException faultEx)
        {
            _logger.LogError(faultEx, "? Error SOAP del servicio de Padrón");
            throw new Exception($"Error SOAP de AFIP Padrón: {faultEx.Message}", faultEx);
        }
        catch (System.ServiceModel.CommunicationException commEx)
        {
            _logger.LogError(commEx, "? Error de comunicación con el servicio de Padrón");
            throw new Exception($"Error de comunicación con AFIP Padrón: {commEx.Message}", commEx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error al consultar el servicio de Padrón");
            throw new Exception($"Error al consultar el servicio de Padrón: {ex.Message}", ex);
        }
    }
}
