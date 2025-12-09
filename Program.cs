using ARCA_razon_social.Services;

var builder = WebApplication.CreateBuilder(args);

// Agregar servicios al contenedor
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "AFIP Padrón API",
        Version = "v1",
        Description = "API para consultar información del Padrón de AFIP Argentina"
    });
});

// Agregar CORS para desarrollo
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

// Registrar el servicio de autenticación AFIP
builder.Services.AddSingleton<IAfipAuthService, AfipAuthService>();

// Configurar logging con nivel Debug para ver detalles del TRA XML
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

var app = builder.Build();

// Configurar el pipeline HTTP - Swagger siempre habilitado para testing
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "AFIP Padrón API v1");
    c.RoutePrefix = string.Empty; // Swagger en la raíz (https://localhost:5001)
});

app.UseCors("AllowAll");
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Logger.LogInformation("🚀 AFIP Padrón API iniciada");
app.Logger.LogInformation("📄 Swagger UI disponible en: https://localhost:5001");
app.Logger.LogInformation("🐛 Logging en modo DEBUG - verás XML del TRA completo");

app.Run();
