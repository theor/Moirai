using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using CommandLine;
using Microsoft.AspNetCore.SpaServices.ReactDevelopmentServer;
using Moirai.Core;
using MoiraiWebServer.Hubs;

internal class Program
{
    public class Options
    {
        [Option('v', "verbose", Required = false, HelpText = "Set output to verbose messages.")]
        public bool Verbose { get; set; }

        [Option('p', "profile", Required = false,
            HelpText = "Profile each simulation run: per-event/per-trigger timings, counts and trigger hit rates printed to the console.")]
        public bool Profile { get; set; }

        [Option('d', "debug-port", Required = false, Default = 0,
            HelpText = "If > 0, start a Debug Adapter Protocol server on this loopback TCP port for step-through debugging from VS Code.")]
        public int DebugPort { get; set; }

        [Value(0, MetaName = "inputfile", HelpText = "The Moirai file to load")]
        public string InputFile { get; set; } = "";
    }
    internal static Options OptionsInstance;
    
    public static FileSystemEventHandler Debounce(FileSystemEventHandler func, int milliseconds = 300)
    {
        CancellationTokenSource? cancelTokenSource = null;

        return (arg,arg1) =>
        {
            cancelTokenSource?.Cancel();
            cancelTokenSource = new CancellationTokenSource();

            Task.Delay(milliseconds, cancelTokenSource.Token)
                .ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        func(arg, arg1);
                    }
                }, TaskScheduler.Default);
        };
    }
    
    public static void Main(string[] args)
    {
        var parseResult = CommandLine.Parser.Default.ParseArguments<Options>(args);

        if (parseResult.Errors.Any())
        {
            Console.Error.WriteLine("Error parsing command line arguments:\n" + string.Join("\n", parseResult.Errors));
            return;
        }

        var options = parseResult.Value;
        while(string.IsNullOrEmpty(options.InputFile) || !File.Exists(options.InputFile))
        {
            Console.Error.WriteLine((!string.IsNullOrEmpty(options.InputFile)
                ? "File does not exist."
                : "") +  "Specify an input file:");
            options.InputFile = (Console.ReadLine() ?? "").Replace("\\", "/");
        }
        Console.WriteLine($"Input file: {options.InputFile}");
        OptionsInstance = options;

        
        var watcher = new FileSystemWatcher(Path.GetDirectoryName(options.InputFile), Path.GetFileName(options.InputFile));
        watcher.Changed += Debounce((sender, eventArgs) =>
        {
            Console.WriteLine("Changed: " + eventArgs.FullPath);
            // TODO ugly
            ChatHub.ReloadRequested();
        });
        watcher.EnableRaisingEvents = true;

        if (options.DebugPort > 0)
        {
            var debugHost = new MoiraiWebServer.MoiraiDebugHost();
            new Moirai.DebugAdapter.DapListener(debugHost, options.DebugPort).Start();
            Console.WriteLine($"Debug adapter listening on 127.0.0.1:{options.DebugPort}");
        }

        var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
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
        if (!app.Environment.IsDevelopment())
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
            // endpoints.MapFallbackToFile("index.html");
        });
#pragma warning restore ASP0014
        if (app.Environment.IsDevelopment())
            app.UseSpa(spaBuilder =>
            {
                spaBuilder.Options.SourcePath = "ClientAppSvelte";
                spaBuilder.Options.StartupTimeout = TimeSpan.FromSeconds(15);

                spaBuilder.Options.DevServerPort = 3000;
                var loggerFactory = spaBuilder.ApplicationBuilder.ApplicationServices.GetService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger("Microsoft.AspNetCore.SpaServices");
                // relies on the npm script printing 'Starting the development server' so npm dev echoes that before starting vite
                // spaBuilder.UseReactDevelopmentServer("dev");
                var p = new Process();
                    p.StartInfo =(new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = "/c yarn run dev",
                    WorkingDirectory = Path.Combine(Directory.GetCurrentDirectory(), "ClientAppSvelte"),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    
                });
                    
                p.EnableRaisingEvents = true;
                p.OutputDataReceived += (sender, eventArgs) => logger.LogInformation(eventArgs.Data);
                p.ErrorDataReceived += (sender, eventArgs) => logger.LogError(eventArgs.Data);
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                spaBuilder.UseProxyToSpaDevelopmentServer("http://localhost:3000");
      
            });
        app.Run();
    }
}
