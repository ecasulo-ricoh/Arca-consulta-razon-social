# ARCA Razón Social - API de Integración con AFIP

Esta Web API permite consultar información del Padrón de AFIP Argentina utilizando autenticación mediante certificado digital.

## ?? Requisitos Previos

- .NET 8 SDK
- Certificado digital AFIP (archivo `certificado_arca.pfx` en la raíz del proyecto)
- Visual Studio 2022 o VS Code

## ?? Configuración Inicial

### 1. Agregar Referencias de Servicio WCF

Los archivos en `ServiceReferences/` son stubs temporales. Debes reemplazarlos con las referencias reales:

#### Servicio de Autenticación (WSAA)

1. Click derecho en el proyecto ? **Add** ? **Connected Service**
2. Selecciona **WCF Web Service**
3. **URL del WSDL**: `https://wsaahomo.afip.gov.ar/ws/services/LoginCms?WSDL`
4. **Namespace**: `WsaaService`
5. Click **Finish**

#### Servicio de Padrón

1. Click derecho en el proyecto ? **Add** ? **Connected Service**
2. Selecciona **WCF Web Service**
3. **URL del WSDL**: `https://awshomo.afip.gov.ar/sr-padron/webservices/personaServiceA13?WSDL`
4. **Namespace**: `PadronService`
5. Click **Finish**

### 2. Certificado Digital

Coloca tu certificado `certificado_arca.pfx` en la raíz del proyecto. 

La contraseña está configurada como `'1234'` en `Services/AfipAuthService.cs`. Si tu certificado tiene otra contraseña, modifica la línea:

```csharp
_certificatePassword = "1234"; // Cambiar aquí
```

### 3. CUIT Representada

Por defecto, el CUIT representada está hardcodeado como `20111111112` en `Controllers/PadronController.cs`. Para cambiarlo:

```csharp
private const string CuitRepresentada = "20111111112"; // Cambiar por tu CUIT
```

## ?? Ejecución

```bash
dotnet restore
dotnet run
```

La API estará disponible en:
- **HTTPS**: `https://localhost:5001`
- **HTTP**: `http://localhost:5000`
- **Swagger UI**: `https://localhost:5001/swagger`

## ?? Endpoints

### Consultar CUIT

```http
GET /api/Padron/ConsultarCuit/{cuit}
```

**Parámetros:**
- `cuit` (path): CUIT de 11 dígitos a consultar

**Ejemplo:**
```bash
curl https://localhost:5001/api/Padron/ConsultarCuit/20123456789
```

**Respuesta exitosa (200):**
```json
{
  "razonSocial": "EMPRESA SA",
  "domicilio": "AV CORRIENTES 1234 CABA CAPITAL FEDERAL",
  "estado": "ACTIVO"
}
```

**Errores:**
- `400 Bad Request`: CUIT inválido (debe tener 11 dígitos)
- `500 Internal Server Error`: Error en AFIP o autenticación

## ?? Flujo de Autenticación

1. **Generación de TRA** (Login Ticket Request):
   - Se crea un XML con datos de solicitud para el servicio `ws_sr_padron_a13`
   
2. **Firma CMS/PKCS#7**:
   - El TRA se firma usando el certificado digital con `System.Security.Cryptography.Pkcs`
   - Se genera un string Base64

3. **Autenticación WSAA**:
   - Se llama al método `loginCms` del servicio WSAA
   - Se obtiene `Token` y `Sign` válidos por ~12 horas

4. **Consulta al Padrón**:
   - Se usa el Token y Sign para autenticar la consulta
   - Se obtiene la información del contribuyente

## ?? Estructura del Proyecto

```
ARCA-razon-social/
??? Controllers/
?   ??? PadronController.cs          # Endpoint de consulta
??? Models/
?   ??? AfipCredentials.cs           # Modelo de credenciales
?   ??? PadronResponse.cs            # Modelo de respuesta
??? Services/
?   ??? AfipAuthService.cs           # Servicio de autenticación
??? ServiceReferences/
?   ??? WsaaService.cs               # Stub WSAA (reemplazar)
?   ??? PadronService.cs             # Stub Padrón (reemplazar)
??? Program.cs                       # Configuración de la API
??? certificado_arca.pfx             # Certificado digital (agregar)
??? ARCA-razon-social.csproj
```

## ?? Caché de Credenciales

El servicio `AfipAuthService` implementa caché en memoria:
- Las credenciales se reutilizan mientras sean válidas
- Se refrescan automáticamente 5 minutos antes de expirar
- Esto reduce las llamadas al WSAA

## ?? Testing con Swagger

1. Inicia la aplicación
2. Abre el navegador en `https://localhost:5001/swagger`
3. Expande el endpoint `GET /api/Padron/ConsultarCuit/{cuit}`
4. Click en **Try it out**
5. Ingresa un CUIT válido (ej: `20123456789`)
6. Click en **Execute**

## ?? Notas Importantes

- **Ambiente de Homologación**: Los endpoints actuales apuntan a los servicios de homologación de AFIP
- **Producción**: Para producción, cambiar las URLs:
  - WSAA: `https://wsaa.afip.gov.ar/ws/services/LoginCms`
  - Padrón: `https://aws.afip.gov.ar/sr-padron/webservices/personaServiceA13`
  
- **Certificado**: El certificado debe estar habilitado para el servicio `ws_sr_padron_a13` en AFIP

## ?? Logs

Los logs se muestran en la consola e incluyen:
- Solicitudes de autenticación
- Consultas al padrón
- Errores detallados

## ??? Personalización

### Cambiar tiempo de expiración del TRA

En `AfipAuthService.cs`:

```csharp
var expirationTime = DateTime.UtcNow.AddHours(12); // Cambiar aquí
```

### Agregar más datos de la respuesta

Modificar `PadronController.cs` para incluir más campos del objeto `persona` en la respuesta.

## ?? Soporte

Para más información sobre los servicios de AFIP:
- [Documentación AFIP Web Services](https://www.afip.gob.ar/ws/)
- [Padrón A13](https://www.afip.gob.ar/ws/ws_sr_padron_a13/)
