using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Moirai.Core;
using MoiraiWebServer.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR(hubOptions => {
        
    hubOptions.KeepAliveInterval = TimeSpan.FromSeconds(15);
    hubOptions.HandshakeTimeout = TimeSpan.FromSeconds(15);
    hubOptions.EnableDetailedErrors = true;})
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.IncludeFields = true;
        options.PayloadSerializerOptions.IgnoreReadOnlyProperties = true; // WriteIndented = true,
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.PayloadSerializerOptions.Converters.Add(new EntityIdConverter());
        options.PayloadSerializerOptions.Converters.Add(new PropertyIdConverter());
        options.PayloadSerializerOptions.Converters.Add(new EntityTypeIdConverter());
        options.PayloadSerializerOptions.Converters.Add(new ValueTypeConverter());
    });
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsAllowAll",
        builder => builder.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()
            .AllowCredentials());               // allow credentials );  
});
var app = builder.Build();
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    // app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.MapHub<ChatHub>("/hub"
    // , x => x.Transports = HttpTransportType.LongPolling
    );

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
