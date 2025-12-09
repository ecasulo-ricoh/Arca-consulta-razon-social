# ? PROYECTO ADAPTADO - Servicios WCF Reales de AFIP

## ?? Estado: Listo para Probar

El proyecto ha sido **completamente adaptado** para usar los servicios WCF reales de AFIP que agregaste mediante "Connected Services".

---

## ?? Cambios Aplicados

### 1. **Servicio WSAA (Autenticación)**
- ? Namespace: `WSAA`
- ? Clase: `LoginCMSClient`
- ? Método: `loginCmsAsync(string cms)`
- ? Ubicación: `Connected Services\WSAA\Reference.cs`

### 2. **Servicio de Padrón (Consulta)**
- ? Namespace: `Padron`
- ? Clase: `PersonaServiceA13Client`
- ? Método: `getPersonaAsync(string token, string sign, long cuitRepresentada, long idPersona)`
- ? Ubicación: `Connected Services\Padron\Reference.cs`

### 3. **Código Actualizado**
- ? `Services/AfipAuthService.cs` - Adaptado para usar `WSAA.LoginCMSClient`
- ? `Controllers/PadronController.cs` - Adaptado para usar `Padron.PersonaServiceA13Client`
- ? Manejo de respuestas con las clases generadas reales
- ? Extracción de datos de las estructuras de AFIP

---

## ?? Cómo Probar

### Paso 1: Limpiar Caché Anterior

```powershell
cd C:\Proyectos\ARCA-razon-social
Remove-Item .afip_credentials_cache.json -ErrorAction SilentlyContinue
```

### Paso 2: Ejecutar la Aplicación

```powershell
dotnet run
```

O presiona **F5** en Visual Studio.

### Paso 3: Probar Health Check

Abre Swagger: `https://localhost:5001`

**Endpoint**: `GET /api/Padron/HealthCheck`

**Resultado Esperado**:
```json
{
  "status": "OK",
  "message": "? Autenticación exitosa con AFIP",
  "tokenLength": 765,
  "signLength": 344,
  "expirationTime": "2024-12-09T15:00:00Z",
  "minutesUntilExpiration": 8.5
}
```

### Paso 4: Consultar un CUIT

**Endpoint**: `GET /api/Padron/ConsultarCuit/20123456789`

**Resultado Esperado**:
```json
{
  "razonSocial": "CONTRIBUYENTE EJEMPLO SA",
  "domicilio": "AV CORRIENTES 1234 CAPITAL FEDERAL",
  "estado": "ACTIVO"
}
```

---

## ?? Logs Esperados

### Durante la Autenticación:

```
?? AFIP Padrón API iniciada
?? Swagger UI disponible en: https://localhost:5001
?? Ruta del certificado: C:\Proyectos\ARCA-razon-social\certificado_arca.pfx
? Certificado encontrado
?? Intentando obtener nuevas credenciales de AFIP...
?? Cargando certificado desde: ...
?? Certificado cargado - Subject: CN=...
?? TRA firmado correctamente (CMS length: 2456)
?? Conectando a WSAA: https://wsaahomo.afip.gov.ar/ws/services/LoginCms
?? Protocolo de seguridad configurado: TLS 1.2
?? Enviando solicitud loginCms...
?? Respuesta recibida de WSAA
? Token length: 765, Sign length: 344
? Credenciales expirarán en: 2024-12-09T15:00:00
?? Credenciales guardadas en disco para persistencia
? Credenciales nuevas obtenidas exitosamente
```

### Durante la Consulta de CUIT:

```
?? Usando credenciales en caché (expiran en 8 minutos)
?? Conectando al servicio de Padrón A13...
?? Enviando consulta para CUIT: 20123456789
?? Respuesta recibida del servicio de Padrón
? Datos obtenidos - Razón Social: CONTRIBUYENTE EJEMPLO SA, Estado: ACTIVO
? Consulta exitosa para CUIT 20123456789
```

---

## ?? Diferencias Clave vs Código Anterior

| Aspecto | Antes (Stub) | Ahora (Servicio Real) |
|---------|--------------|----------------------|
| **WSAA Namespace** | `WsaaService` | `WSAA` |
| **WSAA Clase** | `LoginCmsPortTypeClient` | `LoginCMSClient` |
| **WSAA Método** | `loginCmsAsync(string)` ? null | `loginCmsAsync(string)` ? `loginCmsResponse` |
| **Padrón Namespace** | `PadronService` | `Padron` |
| **Padrón Clase** | `personaServiceA13SoapClient` | `PersonaServiceA13Client` |
| **Padrón Método** | `getPersonaAsync(request)` ? null | `getPersonaAsync(...)` ? `getPersonaResponse` |
| **Respuestas** | ? Siempre null | ? XML con datos reales |

---

## ? Checklist de Verificación

Después de ejecutar:

- [ ] Aplicación inicia sin errores
- [ ] Health Check retorna "OK"
- [ ] Token tiene ~700+ caracteres
- [ ] Sign tiene ~300+ caracteres
- [ ] Archivo `.afip_credentials_cache.json` se crea
- [ ] Consulta CUIT retorna datos (razonSocial, domicilio, estado)
- [ ] Logs muestran comunicación real con AFIP
- [ ] Segunda consulta usa caché automáticamente

---

## ?? Troubleshooting

### Error: "Could not find endpoint element"

**Causa**: El binding no coincide con la configuración generada.

**Solución**: Ya está manejado en el código. Si persiste, verifica que las URLs sean correctas:
- WSAA: `https://wsaahomo.afip.gov.ar/ws/services/LoginCms`
- Padrón: `https://awshomo.afip.gov.ar/sr-padron/webservices/personaServiceA13`

### Error: "No se encontró información para el CUIT"

**Causa**: El CUIT no existe en la base de datos de homologación de AFIP.

**Solución**: Prueba con otros CUITs:
- `20123456789`
- `30500001091`
- `33693450239`

### Error: "El CEE ya posee un TA válido"

**Causa**: Ya existe un ticket activo en AFIP.

**Solución**: El código ahora usa automáticamente el caché. Si persiste:
1. Espera 10 minutos
2. O borra el caché: `Remove-Item .afip_credentials_cache.json`

---

## ?? Estructura de Archivos Generados

```
ARCA-razon-social/
??? Connected Services/
?   ??? WSAA/
?   ?   ??? Reference.cs (Generado automáticamente)
?   ?   ??? ConnectedService.json
?   ??? Padron/
?       ??? Reference.cs (Generado automáticamente)
?       ??? ConnectedService.json
??? Services/
?   ??? AfipAuthService.cs (? Adaptado)
??? Controllers/
?   ??? PadronController.cs (? Adaptado)
??? .afip_credentials_cache.json (Se crea automáticamente)
```

---

## ?? Próximos Pasos

1. **Ejecutar**: `dotnet run`
2. **Probar Health Check** en Swagger
3. **Consultar un CUIT**
4. **Verificar logs** en la consola
5. **Si funciona** ? ¡Felicidades! Ya estás conectado a AFIP ??

---

## ?? Cambios para Producción

Cuando estés listo para producción, cambia las URLs en el código:

**WSAA**:
- Homologación: `https://wsaahomo.afip.gov.ar/ws/services/LoginCms`
- Producción: `https://wsaa.afip.gov.ar/ws/services/LoginCms`

**Padrón**:
- Homologación: `https://awshomo.afip.gov.ar/sr-padron/webservices/personaServiceA13`
- Producción: `https://aws.afip.gov.ar/sr-padron/webservices/personaServiceA13`

**Certificado**:
- Reemplaza `certificado_arca.pfx` con el certificado de producción
- Actualiza la contraseña en `AfipAuthService.cs`

**CUIT Representada**:
- Actualiza el valor en `PadronController.cs` con tu CUIT real

---

**?? ¡El proyecto está listo para probar con AFIP!**
