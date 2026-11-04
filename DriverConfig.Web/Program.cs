using DriverConfig.Web.Components;
using DriverConfig.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Sans cela, lancer l'exe depuis bin/ (environnement Production, sans `dotnet publish`) ne sert pas
// les scripts Blazor : la page s'affiche mais reste inerte (boutons grisés en permanence).
builder.WebHost.UseStaticWebAssets();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSingleton<ConfigService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
