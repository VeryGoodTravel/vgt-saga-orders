using Microsoft.EntityFrameworkCore;
using NLog;
using NLog.Extensions.Logging;
using RabbitMQ.Client.Exceptions;
using vgt_saga_orders;
using vgt_saga_orders.Models;
using vgt_saga_orders.Orchestrator;
using vgt_saga_orders.OrderService;
using ILogger = NLog.ILogger;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
try
{
    builder.Configuration.AddJsonFile("appsettings.json", false, true).AddEnvironmentVariables().Build();
}
catch (InvalidDataException e)
{
    Console.WriteLine(e);
    Environment.Exit(0);
}


try
{
    builder.Logging.ClearProviders();
    builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
    var options = new NLogProviderOptions
    {
        AutoShutdown = true
    };
    options.Configure(builder.Configuration.GetSection("NLog"));
    builder.Logging.AddNLog(options);
}
catch (InvalidDataException e)
{
    Console.WriteLine(e);
    Environment.Exit(0);
}
var logger = LogManager.GetCurrentClassLogger();
builder.Services.AddDbContext<SagaDbContext>(options => options.UseNpgsql(SecretUtils.GetConnectionString(builder.Configuration, "DB_NAME_SAGA", logger)));

var app = builder.Build();

var lf = app.Services.GetRequiredService<ILoggerFactory>();
logger.Info("Hello word");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}

await using var scope = app.Services.CreateAsyncScope();
{
    await using var db = scope.ServiceProvider.GetService<SagaDbContext>();
    {
        logger.Info("CAN CONNECT {v}" ,db.Database.CanConnect());
        db.Database.EnsureDeleted();
        await db.Database.MigrateAsync();
    }
}


app.UseHttpsRedirection();

Orchestrator? orchestrator = null;
OrderService? orderService = null;

try
{
    orchestrator = new Orchestrator(app.Configuration, lf);
    orderService = new OrderService(app.Configuration, lf);
}
catch (BrokerUnreachableException)
{
    GracefulExit(app, logger, [orchestrator, orderService]);
}
catch (ArgumentException)
{
    GracefulExit(app, logger, [orchestrator, orderService]);
}

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
    {
        var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]
                ))
            .ToArray();
        return forecast;
    })
    .WithName("GetWeatherForecast")
    .WithOpenApi();

app.Run();

return;

// dispose objects and close connections
void GracefulExit(WebApplication wA, ILogger log, List<IDisposable?> toDispose)
{
    foreach (var obj in toDispose)
    {
        obj?.Dispose();
    }

    try
    {
        wA.Lifetime.StopApplication();
        AwaitAppStop(wA).Wait();
    }
    catch (ObjectDisposedException)
    {
        log.Info("App already disposed off");
    }

    LogManager.Shutdown();
    Environment.Exit(0);
    throw new Exception("Kill the rest of the app");
}

async Task AwaitAppStop(WebApplication wA)
{
    await wA.StopAsync();
    await wA.DisposeAsync();
}

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}