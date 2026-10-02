using System.Net.Http;
using AchatRatio;
using AchatRatio.Services;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        // Timeout volontairement court : un appel cloud ne doit jamais bloquer
        // l'UI — le localStorage reste le repli hors-ligne.
        builder.Services.AddScoped(sp => new HttpClient
        {
            BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
            Timeout = TimeSpan.FromSeconds(15)
        });

        builder.Services.AddBlazoredLocalStorage();
        builder.Services.AddScoped<ArticleService>();
        builder.Services.AddScoped<SupabaseService>();

        await builder.Build().RunAsync();
    }
}
