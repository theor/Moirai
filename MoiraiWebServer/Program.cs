using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SpaServices.ReactDevelopmentServer;
using Moirai.Core;
using MoiraiWebServer.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// builder.Services.AddSpaStaticFiles(x => x.RootPath=".");
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

app.UseDefaultFiles();
app.UseStaticFiles();
// app.UseSpaStaticFiles();
app.UseRouting();
// app.MapHub doesn' t intercept calls and let them go and fail in the spa middleware ??
#pragma warning disable ASP0014
app.UseEndpoints(endpoints =>
{
    endpoints.MapHub<ChatHub>("/hub");
    endpoints.MapFallbackToFile("index.html");
});
#pragma warning restore ASP0014
if (app.Environment.IsDevelopment())
    app.UseSpa(spaBuilder =>
    {
        spaBuilder.Options.SourcePath = "ClientApp";
        spaBuilder.Options.StartupTimeout = TimeSpan.FromSeconds(5);

        spaBuilder.Options.DevServerPort = 3000;
        // relies on the npm script printing 'Starting the development server' so npm dev echoes that before starting vite
            spaBuilder.UseReactDevelopmentServer("dev");
      
    });
app.Run();
