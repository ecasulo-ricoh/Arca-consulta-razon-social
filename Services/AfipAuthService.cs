using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using ARCA_razon_social.Models;

namespace ARCA_razon_social.Services;

public interface IAfipAuthService
{
    Task<AfipCredentials> GetCredentialsAsync();
}

public class AfipAuthService : IAfipAuthService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AfipAuthService> _logger;
    private readonly string _certificatePath;
    private readonly string _certificatePassword;
    private readonly string _credentialsCachePath;
    private AfipCredentials? _cachedCredentials;

    public AfipAuthService(IConfiguration configuration, ILogger<AfipAuthService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _certificatePath = Path.Combine(Directory.GetCurrentDirectory(), "certificado_arca.pfx");
        _certificatePassword = "1234";
        _credentialsCachePath = Path.Combine(Directory.GetCurrentDirectory(), ".afip_credentials_cache.json");
        
        _logger.LogInformation("?? Ruta del certificado: {Path}", _certificatePath);
        
        if (!File.Exists(_certificatePath))
        {
            _logger.LogWarning("?? No se encontró el certificado en: {Path}", _certificatePath);
        }
        else
        {
            _logger.LogInformation("? Certificado encontrado");
        }
        
        // Intentar cargar credenciales del caché en disco
        LoadCredentialsFromDisk();
    }

    private void LoadCredentialsFromDisk()
    {
        try
        {
            if (File.Exists(_credentialsCachePath))
            {
                var json = File.ReadAllText(_credentialsCachePath);
                var credentials = System.Text.Json.JsonSerializer.Deserialize<AfipCredentials>(json);
                
                if (credentials != null && credentials.ExpirationTime > DateTime.UtcNow)
                {
                    _cachedCredentials = credentials;
                    var minutesLeft = (int)(credentials.ExpirationTime - DateTime.UtcNow).TotalMinutes;
                    _logger.LogInformation("?? Credenciales cargadas desde disco (expiran en {Minutes} minutos)", minutesLeft);
                    _logger.LogInformation("?? Token: {TokenLength} chars, Sign: {SignLength} chars", 
                        credentials.Token.Length, credentials.Sign.Length);
                }
                else
                {
                    _logger.LogInformation("??? Credenciales en disco están expiradas, se eliminarán");
                    File.Delete(_credentialsCachePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "?? No se pudieron cargar credenciales desde disco, continuando sin caché");
            // No es crítico, continuar sin caché
        }
    }

    private void SaveCredentialsToDisk(AfipCredentials credentials)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(credentials, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_credentialsCachePath, json);
            _logger.LogInformation("?? Credenciales guardadas en disco para persistencia");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "?? No se pudieron guardar credenciales en disco (no crítico)");
            // No es crítico, continuar sin persistencia
        }
    }

    public async Task<AfipCredentials> GetCredentialsAsync()
    {
        // Verificar si tenemos credenciales en caché y aún son válidas
        // Usamos un margen de 2 minutos antes de la expiración
        if (_cachedCredentials != null && _cachedCredentials.ExpirationTime > DateTime.UtcNow.AddMinutes(2))
        {
            _logger.LogInformation("?? Usando credenciales en caché (expiran en {Minutes} minutos)", 
                (int)(_cachedCredentials.ExpirationTime - DateTime.UtcNow).TotalMinutes);
            return _cachedCredentials;
        }

        // Si hay credenciales en caché pero están cerca de expirar, intentar renovarlas
        // pero si falla, usar las existentes si aún son válidas (sin el margen)
        var hasValidCachedCredentials = _cachedCredentials != null && 
                                        _cachedCredentials.ExpirationTime > DateTime.UtcNow;

        _logger.LogInformation("?? Intentando obtener nuevas credenciales de AFIP...");

        try
        {
            // Generar nuevas credenciales
            var tra = GenerateLoginTicketRequest();
            _logger.LogDebug("?? TRA generado: {TRA}", tra.Substring(0, Math.Min(100, tra.Length)) + "...");
            
            var cms = SignTRA(tra);
            _logger.LogInformation("?? TRA firmado correctamente (CMS length: {Length})", cms.Length);
            
            var credentials = await CallLoginCms(cms);
            _logger.LogInformation("? Credenciales nuevas obtenidas exitosamente (expiran: {Expiration})", credentials.ExpirationTime);
            
            _cachedCredentials = credentials;
            SaveCredentialsToDisk(credentials); // Guardar en disco
            return credentials;
        }
        catch (Exception ex)
        {
            // CASO ESPECIAL 1: El stub no está implementado correctamente (respuesta null)
            if (ex.Message.Contains("stub temporal") || ex.Message.Contains("Connected Service"))
            {
                _logger.LogError("? ERROR CRÍTICO: Stub de WsaaService no implementado");
                _logger.LogError("?? SOLUCIÓN REQUERIDA:");
                _logger.LogError("   1. Abrir Visual Studio");
                _logger.LogError("   2. Click derecho en proyecto ? Add ? Connected Service");
                _logger.LogError("   3. Seleccionar 'WCF Web Service'");
                _logger.LogError("   4. URL: https://wsaahomo.afip.gov.ar/ws/services/LoginCms?WSDL");
                _logger.LogError("   5. Namespace: WsaaService");
                _logger.LogError("   6. Eliminar el archivo ServiceReferences/WsaaService.cs (stub temporal)");
                
                throw new Exception(
                    "El stub de WsaaService no implementa la comunicación real con AFIP. " +
                    "DEBES agregar la referencia WCF real. " +
                    "Instrucciones: Visual Studio ? Click derecho en proyecto ? Add ? Connected Service ? " +
                    "WCF Web Service ? URL: https://wsaahomo.afip.gov.ar/ws/services/LoginCms?WSDL ? " +
                    "Namespace: WsaaService", ex);
            }
            
            // CASO ESPECIAL 2: Ya existe un TA válido en AFIP
            if (ex.Message.Contains("TA_VALIDO_EN_AFIP") || 
                ex.Message.Contains("El CEE ya posee un TA valido"))
            {
                _logger.LogWarning("?? AFIP reporta que ya existe un TA válido");
                
                // Si tenemos credenciales en caché, usarlas
                if (_cachedCredentials != null)
                {
                    _logger.LogInformation("?? Reutilizando credenciales en caché existentes");
                    _logger.LogInformation("?? Token en caché: {TokenLength} chars, Sign: {SignLength} chars", 
                        _cachedCredentials.Token.Length, _cachedCredentials.Sign.Length);
                    
                    // Si la expiración está en el pasado, extenderla
                    if (_cachedCredentials.ExpirationTime <= DateTime.UtcNow)
                    {
                        var oldExpiration = _cachedCredentials.ExpirationTime;
                        // Extender basado en la duración del TRA (10 minutos)
                        _cachedCredentials.ExpirationTime = DateTime.UtcNow.AddMinutes(8);
                        SaveCredentialsToDisk(_cachedCredentials);
                        _logger.LogInformation("? Expiración actualizada de {Old} a {New}", 
                            oldExpiration, _cachedCredentials.ExpirationTime);
                    }
                    
                    return _cachedCredentials;
                }
                else
                {
                    // No hay credenciales en caché pero AFIP dice que hay un TA válido
                    _logger.LogError("? AFIP generó el TA pero NO está en caché");
                    _logger.LogError("?? Posibles causas:");
                    _logger.LogError("   1. Primera llamada a AFIP retornó null (problema con stub)");
                    _logger.LogError("   2. AFIP generó el TA pero no pudimos capturar la respuesta");
                    _logger.LogError("   3. Otro proceso/aplicación generó el TA con el mismo certificado");
                    _logger.LogError("");
                    _logger.LogError("?? SOLUCIONES:");
                    _logger.LogError("   OPCIÓN A: Agregar referencia WCF real (solución permanente)");
                    _logger.LogError("      - Eliminar ServiceReferences/WsaaService.cs");
                    _logger.LogError("      - Add ? Connected Service ? WCF Web Service");
                    _logger.LogError("      - URL: https://wsaahomo.afip.gov.ar/ws/services/LoginCms?WSDL");
                    _logger.LogError("");
                    _logger.LogError("   OPCIÓN B: Esperar a que expire (10-15 minutos)");
                    _logger.LogError("      - El TA actual expirará automáticamente");
                    _logger.LogError("      - Luego podrás generar uno nuevo");
                    
                    throw new Exception(
                        "AFIP indica que existe un TA válido pero no está disponible en caché. " +
                        "\n\nCAUSA PROBABLE: El stub de WsaaService retornó null en la primera llamada, " +
                        "por lo que no pudimos capturar el Token y Sign que AFIP SÍ generó." +
                        "\n\nSOLUCIÓN PERMANENTE: Agregar la referencia WCF real de AFIP (ver logs para instrucciones)." +
                        "\n\nSOLUCIÓN TEMPORAL: Esperar 10-15 minutos para que el TA expire.", ex);
                }
            }
            
            // Para otros errores, re-lanzar
            _logger.LogError(ex, "? Error al obtener credenciales de AFIP");
            
            // Si teníamos credenciales válidas en caché, usarlas como fallback
            if (hasValidCachedCredentials)
            {
                _logger.LogWarning("?? Usando credenciales en caché como fallback debido al error");
                return _cachedCredentials!;
            }
            
            throw;
        }
    }

    private string GenerateLoginTicketRequest()
    {
        // 1. UniqueId: Usamos Unix Timestamp (segundos) para evitar números gigantescos que rompen el schema integer
        var uniqueId = DateTimeOffset.Now.ToUnixTimeSeconds().ToString();

        // 2. Fechas: Usamos DateTimeOffset para manejar la hora actual con precisión
        // Restamos 2 minutos al inicio para evitar problemas de sincronización de relojes (Error 10.7 manual)
        var generationTime = DateTimeOffset.Now.AddMinutes(-2).ToString("yyyy-MM-ddTHH:mm:ss");

        // Sumamos 10 minutos para la expiración
        var expirationTime = DateTimeOffset.Now.AddMinutes(+10).ToString("yyyy-MM-ddTHH:mm:ss");

        // 3. Service: El servicio al que quieres acceder
        var service = "ws_sr_padron_a13";

        // 4. Construcción MANUAL del XML
        // Esto asegura que el header "encoding=UTF-8" esté presente y coincida con los bytes que firmaremos después.
        var xmlTra = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
        <loginTicketRequest version=""1.0"">
            <header>
                <uniqueId>{uniqueId}</uniqueId>
                <generationTime>{generationTime}</generationTime>
                <expirationTime>{expirationTime}</expirationTime>
            </header>
            <service>{service}</service>
        </loginTicketRequest>";

        _logger.LogDebug("TRA XML generado: {XML}", xmlTra);

        return xmlTra;
    }

    private string SignTRA(string tra)
    {
        try
        {
            _logger.LogInformation("?? Cargando certificado desde: {Path}", _certificatePath);
            
            // Cargar el certificado
            var certificate = new X509Certificate2(_certificatePath, _certificatePassword);
            
            _logger.LogInformation("?? Certificado cargado - Subject: {Subject}", certificate.Subject);
            _logger.LogInformation("?? Válido desde: {NotBefore} hasta: {NotAfter}", 
                certificate.NotBefore, certificate.NotAfter);
            
            // Codificar el TRA en bytes
            var traBytes = Encoding.UTF8.GetBytes(tra);
            
            // Crear el mensaje firmado usando PKCS#7
            var contentInfo = new ContentInfo(traBytes);
            var signedCms = new SignedCms(contentInfo);
            
            // Crear el firmante
            var cmsSigner = new CmsSigner(certificate)
            {
                IncludeOption = X509IncludeOption.EndCertOnly
            };
            
            // Firmar
            signedCms.ComputeSignature(cmsSigner);
            
            // Obtener el mensaje firmado en formato PKCS#7
            var encodedMessage = signedCms.Encode();
            
            // Convertir a Base64
            return Convert.ToBase64String(encodedMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error al firmar el TRA");
            throw new Exception($"Error al firmar el TRA: {ex.Message}", ex);
        }
    }

    private async Task<AfipCredentials> CallLoginCms(string cms)
    {
        try
        {
            _logger.LogInformation("?? Conectando a WSAA: https://wsaahomo.afip.gov.ar/ws/services/LoginCms");
            
            // IMPORTANTE: AFIP requiere TLS 1.2 explícitamente
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            _logger.LogDebug("?? Protocolo de seguridad configurado: TLS 1.2");
            
            // Crear el cliente del servicio WSAA usando la clase generada
            var binding = new System.ServiceModel.BasicHttpBinding(System.ServiceModel.BasicHttpSecurityMode.Transport);
            binding.Security.Transport.ClientCredentialType = System.ServiceModel.HttpClientCredentialType.None;
            binding.MaxReceivedMessageSize = 65536;
            binding.MaxBufferSize = 65536;
            binding.ReaderQuotas.MaxStringContentLength = 65536;
            
            var endpoint = new System.ServiceModel.EndpointAddress("https://wsaahomo.afip.gov.ar/ws/services/LoginCms");
            
            // Usar la clase generada por el servicio conectado
            var client = new WSAA.LoginCMSClient(binding, endpoint);
            
            _logger.LogInformation("?? Enviando solicitud loginCms...");
            _logger.LogDebug("CMS enviado (primeros 100 caracteres): {CMS}", cms.Substring(0, Math.Min(100, cms.Length)));
            
            // Llamar al método loginCms pasando el CMS directamente como string
            var response = await client.loginCmsAsync(cms);
            
            // VALIDACIÓN: Verificar si la respuesta es null
            if (response == null)
            {
                _logger.LogError("? La respuesta de WSAA es null");
                throw new Exception("La respuesta del servicio WSAA es null. Verifica la conectividad y el certificado.");
            }
            
            _logger.LogInformation("?? Respuesta recibida de WSAA");
            
            // Extraer el XML de la respuesta (la clase generada tiene la propiedad loginCmsReturn)
            var xmlResponse = response.loginCmsReturn;
            
            _logger.LogDebug("XML Response length: {Length}", xmlResponse?.Length ?? 0);
            
            // Validar que el XML no esté vacío
            if (string.IsNullOrWhiteSpace(xmlResponse))
            {
                _logger.LogError("? La respuesta XML está vacía");
                throw new Exception("La respuesta de WSAA está vacía");
            }
            
            _logger.LogDebug("Respuesta XML (primeros 200 caracteres): {Response}", 
                xmlResponse.Substring(0, Math.Min(200, xmlResponse.Length)) + "...");
            
            // Parsear la respuesta XML
            var xmlDoc = new XmlDocument();
            try
            {
                xmlDoc.LoadXml(xmlResponse);
            }
            catch (XmlException xmlEx)
            {
                _logger.LogError(xmlEx, "? Error al parsear XML de respuesta");
                _logger.LogError("XML recibido: {XML}", xmlResponse);
                throw new Exception($"Error al parsear XML de WSAA: {xmlEx.Message}", xmlEx);
            }
            
            var token = xmlDoc.SelectSingleNode("//token")?.InnerText ?? string.Empty;
            var sign = xmlDoc.SelectSingleNode("//sign")?.InnerText ?? string.Empty;
            var expirationTimeStr = xmlDoc.SelectSingleNode("//expirationTime")?.InnerText ?? string.Empty;
            
            // Validar que se hayan obtenido los datos necesarios
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(sign))
            {
                _logger.LogError("? Token o Sign vacíos en la respuesta");
                _logger.LogError("XML completo: {XML}", xmlResponse);
                throw new Exception("No se pudieron extraer Token y Sign de la respuesta de AFIP");
            }
            
            _logger.LogInformation("? Token length: {TokenLength}, Sign length: {SignLength}", 
                token.Length, sign.Length);
            
            DateTime expirationTime;
            if (!DateTime.TryParse(expirationTimeStr, out expirationTime))
            {
                _logger.LogWarning("?? No se pudo parsear expirationTime, usando 10 minutos por defecto");
                expirationTime = DateTime.UtcNow.AddMinutes(10);
            }
            else
            {
                _logger.LogInformation("? Credenciales expirarán en: {ExpirationTime}", expirationTime);
            }
            
            return new AfipCredentials
            {
                Token = token,
                Sign = sign,
                ExpirationTime = expirationTime
            };
        }
        catch (System.ServiceModel.FaultException faultEx)
        {
            // CASO ESPECIAL: Ya existe un TA válido
            if (faultEx.Message.Contains("El CEE ya posee un TA valido") || 
                faultEx.Reason?.ToString().Contains("El CEE ya posee un TA valido") == true)
            {
                _logger.LogWarning("?? AFIP indica que ya existe un Ticket de Acceso válido");
                _logger.LogInformation("?? Esto significa que AFIP generó el TA exitosamente en una llamada anterior");
                _logger.LogInformation("?? El TA está activo en el sistema de AFIP pero puede no estar en nuestro caché");
                
                // Re-lanzar con mensaje especial para GetCredentialsAsync
                throw new Exception(
                    "TA_VALIDO_EN_AFIP", 
                    faultEx);
            }
            
            _logger.LogError(faultEx, "? Error SOAP Fault de WSAA");
            _logger.LogError("Fault Code: {Code}", faultEx.Code);
            _logger.LogError("Fault Reason: {Reason}", faultEx.Reason);
            throw new Exception($"Error SOAP de AFIP: {faultEx.Reason}", faultEx);
        }
        catch (System.ServiceModel.CommunicationException commEx)
        {
            _logger.LogError(commEx, "? Error de comunicación con WSAA");
            throw new Exception($"Error de comunicación con AFIP: {commEx.Message}. Verifica conectividad y firewall.", commEx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error al llamar a LoginCms");
            throw new Exception($"Error al llamar a LoginCms: {ex.Message}", ex);
        }
    }
}
