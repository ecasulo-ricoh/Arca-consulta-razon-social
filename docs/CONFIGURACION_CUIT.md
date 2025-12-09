# ?? Configuración del CUIT Representada

## ?? Problema Resuelto

El error **"Este token no le permite actuar en representacion de la CUIT 20111111112"** se debe a que el CUIT configurado no coincide con el CUIT del certificado.

## ? Solución Automática Implementada

He agregado código que **extrae automáticamente el CUIT del certificado** en `PadronController.cs`.

### Cómo Funciona:

1. Al iniciar la aplicación, lee el certificado `certificado_arca.pfx`
2. Extrae el CUIT del Subject o SerialNumber del certificado
3. Usa ese CUIT automáticamente en todas las consultas

### Logs Esperados:

```
?? CUIT Representada: 20XXXXXXXXX
? CUIT extraído del certificado: 20XXXXXXXXX
```

---

## ?? Si la Extracción Automática No Funciona

Si ves este log:
```
?? No se pudo extraer el CUIT del certificado automáticamente
?? Debes configurar manualmente el CUIT
Subject del certificado: CN=...
```

### Opción A: Configurar Manualmente en appsettings.json

1. **Abre `appsettings.json`**

2. **Agrega esta configuración**:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Afip": {
    "CuitRepresentada": "TU_CUIT_AQUI"
  }
}
```

3. **Reemplaza `TU_CUIT_AQUI`** con el CUIT que está en tu certificado

4. **Modifica `PadronController.cs`**:
```csharp
public PadronController(IAfipAuthService authService, ILogger<PadronController> logger, IConfiguration configuration)
{
    _authService = authService;
    _logger = logger;
    
    // Intentar leer desde configuración primero
    _cuitRepresentada = configuration["Afip:CuitRepresentada"] ?? GetCuitFromCertificate();
    
    _logger.LogInformation("?? CUIT Representada: {Cuit}", _cuitRepresentada);
}
```

### Opción B: Configurar Directamente en el Código

1. **Abre `PadronController.cs`**

2. **Busca el método `GetCuitFromCertificate()`**

3. **En la última línea, cambia**:
```csharp
// Valor por defecto (deberás cambiarlo manualmente)
return "20111111112";
```

**Por tu CUIT real**:
```csharp
return "TU_CUIT_REAL_AQUI";
```

---

## ?? Cómo Encontrar tu CUIT en el Certificado

### Método 1: Ver el Subject del Certificado en los Logs

Ejecuta la aplicación y busca en los logs:
```
Certificado Subject: CN=CUIT 20XXXXXXXXX, O=MI EMPRESA, ...
```

El número de 11 dígitos que aparece después de "CUIT" es tu CUIT.

### Método 2: Usar PowerShell

```powershell
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2("certificado_arca.pfx", "1234")
$cert.Subject
$cert.SerialNumber
```

### Método 3: Usar OpenSSL (si lo tienes instalado)

```bash
openssl pkcs12 -in certificado_arca.pfx -nokeys -passin pass:1234 | openssl x509 -noout -subject
```

### Método 4: Verificar en el Panel de AFIP

1. Ingresa a https://auth.afip.gob.ar/
2. Ve a "Administrador de Relaciones de Clave Fiscal"
3. Busca el certificado que estás usando
4. El CUIT asociado aparecerá ahí

---

## ?? Probar la Corrección

### 1. Reiniciar la Aplicación

```powershell
# Si está corriendo, detenla (Ctrl+C)
dotnet run
```

### 2. Verificar el Log del CUIT

Al iniciar, deberías ver:
```
?? CUIT Representada: 20XXXXXXXXX
```

Si el CUIT es correcto (11 dígitos, empieza con 20/23/24/27/30/33/34), continúa.

### 3. Probar Health Check

```
GET /api/Padron/HealthCheck
```

Debería retornar:
```json
{
  "status": "OK",
  "message": "? Autenticación exitosa con AFIP"
}
```

### 4. Consultar un CUIT

```
GET /api/Padron/ConsultarCuit/30500001091
```

**IMPORTANTE**: Usa un CUIT que **SÍ exista** en la base de homologación de AFIP.

#### CUITs de Prueba Comunes en Homologación:

| CUIT | Descripción |
|------|-------------|
| `30500001091` | AFIP DGI |
| `20123456789` | Ejemplo genérico |
| `33693450239` | Ejemplo de empresa |
| **Tu propio CUIT** | Usa el CUIT de tu certificado |

### 5. Respuesta Esperada

```json
{
  "razonSocial": "NOMBRE O RAZON SOCIAL",
  "domicilio": "DIRECCION COMPLETA",
  "estado": "ACTIVO"
}
```

---

## ? Errores Comunes

### Error: "Este token no le permite actuar en representacion de la CUIT XXXXXXXX"

**Causa**: El CUIT configurado (`_cuitRepresentada`) no coincide con el CUIT del certificado.

**Solución**:
1. Verifica el log: `?? CUIT Representada: XXXXXXXX`
2. Compara con el Subject del certificado
3. Si no coinciden, configura manualmente (Opción A o B arriba)

### Error: "No se encontró información para el CUIT especificado"

**Causa**: El CUIT que estás consultando no existe en la base de homologación de AFIP.

**Solución**:
- Usa tu propio CUIT (el del certificado)
- O usa CUITs de prueba conocidos (ver tabla arriba)

### Error: "Token inválido o expirado"

**Causa**: Las credenciales expiraron.

**Solución**:
```powershell
Remove-Item .afip_credentials_cache.json
dotnet run
```

---

## ?? Flujo Correcto

```
1. App inicia
   ?
2. PadronController se inicializa
   ?
3. GetCuitFromCertificate() extrae CUIT del certificado
   ?
4. Log: "?? CUIT Representada: 20XXXXXXXXX"
   ?
5. Health Check ? Genera Token y Sign
   ?
6. Consulta CUIT ? Usa CUIT correcto
   ?
7. ? Respuesta exitosa de AFIP
```

---

## ? Checklist

- [ ] App inicia sin errores
- [ ] Log muestra CUIT extraído del certificado
- [ ] CUIT tiene 11 dígitos
- [ ] CUIT empieza con 20/23/24/27/30/33/34
- [ ] Health Check retorna "OK"
- [ ] Consulta CUIT retorna datos
- [ ] No hay error de "no le permite actuar"

---

## ?? Resumen

**Problema**: CUIT hardcodeado (`20111111112`) no coincidía con el certificado  
**Solución**: Extracción automática del CUIT desde el certificado  
**Fallback**: Configuración manual en `appsettings.json` o código  

**Ahora la aplicación debería funcionar correctamente con tu certificado.** ??
