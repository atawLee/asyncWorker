using AsyncWorker.Data;
using AsyncWorker.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Singleton Services
builder.Services.AddSingleton<ProcessInstanceManager>();
builder.Services.AddSingleton<JobConcurrencyManager>();

// Scoped Services
builder.Services.AddScoped<JobExecutionService>();

// Hosted Services
builder.Services.AddHostedService<JobRecoveryService>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
