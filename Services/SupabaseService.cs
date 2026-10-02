using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AchatRatio.Models;
using Blazored.LocalStorage;

namespace AchatRatio.Services;

/// <summary>Client REST minimal pour Supabase (PostgREST) — aucune dépendance NuGet.
/// Conserve aussi les réglages (URL, clés) dans le localStorage.</summary>
public class SupabaseService(HttpClient http, ILocalStorageService storage)
{
    private const string CleReglages = "achatratio.supabase.v1";

    /// <summary>Mapping automatique Article ↔ colonnes (PrixAchat → prix_achat…).</summary>
    private static readonly JsonSerializerOptions OptionsJson =
        new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public string Url { get; private set; } = "";
    public string Cle { get; private set; } = "";
    public string CleApp { get; private set; } = "";   // secret facultatif (durcissement RLS, étape 5)

    public bool EstConfigure => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Cle);

    private bool _reglagesCharges;

    public async Task InitialiserAsync()
    {
        if (_reglagesCharges) return;
        _reglagesCharges = true;

        var r = await storage.GetItemAsync<ReglagesSupabase>(CleReglages);
        if (r is null) return;
        Url = r.Url.Trim().TrimEnd('/');
        Cle = r.Cle.Trim();
        CleApp = (r.CleApp ?? "").Trim();
    }

    /// <summary>Teste la connexion SANS enregistrer les réglages. En cas d'échec,
    /// le message contient le code HTTP et le corps de la réponse de Supabase.</summary>
    public async Task TesterAsync(string url, string cle, string cleApp = "")
    {
        url = NormaliserUrl(url);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidOperationException(
                "L'URL doit être de la forme https://xxxx.supabase.co " +
                "(pas l'URL du dashboard, ni la chaîne de connexion Postgres).");

        using var reponse = await EnvoyerAsync(url, cle.Trim(), cleApp.Trim(),
                                               HttpMethod.Get, "articles?select=id&limit=1");

        if (reponse.IsSuccessStatusCode) return;

        var corps = await reponse.Content.ReadAsStringAsync();
        throw new HttpRequestException($"HTTP {(int)reponse.StatusCode} — {Tronquer(corps, 300)}");
    }

    public async Task ConfigurerAsync(string url, string cle, string cleApp = "")
    {
        Url = NormaliserUrl(url);
        Cle = cle.Trim();
        CleApp = cleApp.Trim();
        await storage.SetItemAsync(CleReglages,
            new ReglagesSupabase { Url = Url, Cle = Cle, CleApp = CleApp });
    }

    private static string NormaliserUrl(string url)
    {
        url = url.Trim().TrimEnd('/');
        // Tolérance : URL copiée avec le suffixe /rest/v1 déjà inclus
        if (url.EndsWith("/rest/v1", StringComparison.OrdinalIgnoreCase))
            url = url[..^"/rest/v1".Length];
        return url;
    }

    private static string Tronquer(string texte, int max)
        => texte.Length <= max ? texte : texte[..max] + "…";

    public async Task DeconnecterAsync()
    {
        Url = Cle = CleApp = "";
        await storage.RemoveItemAsync(CleReglages);
    }

    // ── Appels distants (lèvent une exception en cas d'échec) ──

    public async Task<List<Article>> RecupererAsync()
    {
        using var reponse = await EnvoyerConfigureAsync(HttpMethod.Get,
                                                        "articles?select=*&limit=1000");
        reponse.EnsureSuccessStatusCode();

        await using var flux = await reponse.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<List<Article>>(flux, OptionsJson) ?? [];
    }

    /// <summary>Insère ou met à jour un article (upsert sur l'id).</summary>
    public async Task PousserAsync(Article a)
    {
        using var contenu = VersJson(a);
        using var reponse = await EnvoyerConfigureAsync(HttpMethod.Post, "articles?on_conflict=id",
            contenu, "return=minimal,resolution=merge-duplicates");
        reponse.EnsureSuccessStatusCode();
    }

    public async Task SupprimerAsync(Guid id)
    {
        using var reponse = await EnvoyerConfigureAsync(HttpMethod.Delete,
            $"articles?id=eq.{id}", prefer: "return=minimal");
        reponse.EnsureSuccessStatusCode();
    }

    /// <summary>La liste fournie devient le contenu exact de la table distante
    /// (tout effacer, tout réinsérer).</summary>
    public async Task RemplacerToutAsync(IReadOnlyList<Article> articles)
    {
        // PostgREST exige un filtre pour un DELETE global : on exclut un uuid impossible.
        using (var vide = await EnvoyerConfigureAsync(HttpMethod.Delete,
                   "articles?id=neq.00000000-0000-0000-0000-000000000000",
                   prefer: "return=minimal"))
        {
            vide.EnsureSuccessStatusCode();
        }

        if (articles.Count == 0) return;

        using var contenu = VersJson(articles);
        using var reponse = await EnvoyerConfigureAsync(HttpMethod.Post, "articles?on_conflict=id",
            contenu, "return=minimal,resolution=merge-duplicates");
        reponse.EnsureSuccessStatusCode();
    }

    // ── Interne ──

    private StringContent VersJson(object valeur)
        => new(JsonSerializer.Serialize(valeur, OptionsJson), Encoding.UTF8, "application/json");

    private void ExigerConfiguration()
    {
        if (!EstConfigure)
            throw new InvalidOperationException("Supabase n'est pas configuré.");
    }

    private Task<HttpResponseMessage> EnvoyerConfigureAsync(HttpMethod methode, string chemin,
        StringContent? contenu = null, string? prefer = null)
    {
        ExigerConfiguration();
        return EnvoyerAsync(Url, Cle, CleApp, methode, chemin, contenu, prefer);
    }

    private async Task<HttpResponseMessage> EnvoyerAsync(string url, string cle, string cleApp,
        HttpMethod methode, string chemin, StringContent? contenu = null, string? prefer = null)
    {
        using var requete = new HttpRequestMessage(methode, $"{url}/rest/v1/{chemin}");
        requete.Headers.Add("apikey", cle);
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cle);
        if (cleApp.Length > 0)
            requete.Headers.Add("x-cle-app", cleApp);
        if (prefer is not null)
            requete.Headers.Add("Prefer", prefer);
        if (contenu is not null)
            requete.Content = contenu;

        return await http.SendAsync(requete);
    }
}

public class ReglagesSupabase
{
    public string Url { get; set; } = "";
    public string Cle { get; set; } = "";
    public string? CleApp { get; set; }
}
