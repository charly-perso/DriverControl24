using DriverConfig.Web.Components;
using DriverConfig.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Sans cela, lancer l'exe depuis bin/ (environnement Production, sans `dotnet publish`) ne sert pas
// les scripts Blazor : la page s'affiche mais reste inerte (boutons grisés en permanence).
builder.WebHost.UseStaticWebAssets();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSingleton<ConfigService>();
builder.Services.AddScoped<Loc>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Langue : français si le navigateur est en français (Accept-Language), anglais pour toutes les autres langues.
var supportedCultures = new[] { "en", "fr" };
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("en")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures));

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
