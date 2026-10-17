using Rackline.Infrastructure;
using Rackline.Web.Api;
using Rackline.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var connectionString = builder.Configuration.GetConnectionString("Rackline")
    ?? throw new InvalidOperationException("Connection string 'Rackline' is not configured.");
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddRacklineApi();
builder.Services.AddPublicApi(builder.Configuration);

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateAndSeedOnStartup"))
{
    // The administrator's credentials come from user-secrets or environment variables, never the repository.
    var adminEmail = app.Configuration["Seed:AdminEmail"];
    var adminPassword = app.Configuration["Seed:AdminPassword"];
    await app.Services.MigrateAndSeedAsync(
        adminEmail is not null && adminPassword is not null ? (adminEmail, adminPassword) : null);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseApiProblemResponses();
app.UseHttpsRedirection();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// After authentication, so signed-in users can be told apart from anonymous callers.
app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRacklineApi();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
