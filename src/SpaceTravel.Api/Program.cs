using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SpaceTravel.Api.Code.Dispatch;
using SpaceTravel.Api.Code.Endpoints;
using SpaceTravel.Api.Code.Errors;
using SpaceTravel.Api.Code.Options;
using SpaceTravel.Api.Code.Simulation;
using SpaceTravel.Api.Code.Time;
using SpaceTravel.Api.Data;
using SpaceTravel.Api.Features.CallShuttle;
using SpaceTravel.Api.Features.GetFleet;
using SpaceTravel.Api.Features.GetTravelHistory;

var builder = WebApplication.CreateBuilder(args);

// ---- Options (Code/Options) -------------------------------------------------
builder.Services.Configure<PlanetOptions>(
    builder.Configuration.GetSection(PlanetOptions.SectionName));
builder.Services.Configure<FleetOptions>(
    builder.Configuration.GetSection(FleetOptions.SectionName));
builder.Services.Configure<SimulationOptions>(
    builder.Configuration.GetSection(SimulationOptions.SectionName));

// ---- Infrastructure (Code) --------------------------------------------------
builder.Services.AddSingleton<IClock, SystemClock>();

// One gate for the whole process: dispatch and the simulation tick take turns,
// so a shuttle can never be booked past its capacity.
builder.Services.AddSingleton<IDispatchGate, DispatchGate>();
builder.Services.AddHostedService<SimulationTickService>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddHttpLogging(o =>
    o.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestPath
                      | Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestMethod
                      | Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.ResponseStatusCode);

// ---- Persistence (Data) -----------------------------------------------------
builder.Services.AddDbContext<SpaceTravelDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("SpaceTravel")
                      ?? "Data Source=spacetravel.db"));

builder.Services.AddScoped<FleetRepository>();
builder.Services.AddScoped<FleetAdvancer>();
builder.Services.AddScoped<DbSeeder>();

// ---- Feature handlers -------------------------------------------------------
builder.Services.AddScoped<CallShuttleHandler>();
builder.Services.AddScoped<GetFleetHandler>();
builder.Services.AddScoped<GetTravelHistoryHandler>();

// ---- Validators (Features/*/...Validator.cs) --------------------------------
builder.Services.AddValidatorsFromAssemblyContaining<Program>(includeInternalTypes: true);

var app = builder.Build();

// Migrate and seed before serving: one command to run the whole system.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DbSeeder>().SeedAsync();
}

app.UseExceptionHandler();
app.UseHttpLogging();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .WithName("Health");

// Every slice registers its own routes.
app.MapEndpoints();

app.Run();

/// <summary>Exposed so integration tests can boot the app with WebApplicationFactory.</summary>
public partial class Program;
