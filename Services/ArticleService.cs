using System.IO;
using System.Text.Json;
using AchatRatio.Models;
using Blazored.LocalStorage;

namespace AchatRatio.Services;

/// <summary>Persistance des articles.
/// localStorage = cache hors-ligne, TOUJOURS écrit en premier (aucune perte possible).
/// Supabase = copie cloud, synchronisée en file d'attente (si configuré).</summary>
public class ArticleService(ILocalStorageService storage, SupabaseService supabase)
{
    private const string Key = "achatratio.articles.v1";
    private const string CleEnAttente = "achatratio.synchro-en-attente";

    private static readonly JsonSerializerOptions OptionsExport =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private List<Article> _articles = [];
    private bool _charges;
    private bool _synchroDemarree;

    private Task _envoiEnCours = Task.CompletedTask;
    private int _envoisEnFile;

    public IReadOnlyList<Article> Articles => _articles;
    public event Action? Changed;

    public bool SynchroActive => supabase.EstConfigure;

    /// <summary>Vrai si des modifications locales n'ont pas encore atteint le cloud.</summary>
    public bool ModificationsEnAttente { get; private set; }

    /// <summary>Dernier problème réseau rencontré (null = tout va bien).</summary>
    public string? DerniereErreur { get; private set; }

    // ── Chargement ────────────────────────────────────────────

    public async Task ChargerAsync()
    {
        await supabase.InitialiserAsync();

        if (!_charges)
        {
            _articles = await storage.GetItemAsync<List<Article>>(Key) ?? [];
            ModificationsEnAttente = await storage.GetItemAsync<bool>(CleEnAttente);
            _charges = true;
        }

        // Toujours notifier : une page qui se remonte (retour de navigation)
        // recalcule son affichage à partir de l'état en mémoire, sans relecture.
        Changed?.Invoke();

        if (supabase.EstConfigure && !_synchroDemarree)
        {
            _synchroDemarree = true;
            _ = SynchroniserAuDemarrageAsync();   // tâche de fond : l'UI n'attend jamais
        }
    }

    private async Task SynchroniserAuDemarrageAsync()
    {
        try
        {
            if (ModificationsEnAttente)
            {
                // Des modifications locales n'avaient pas pu être envoyées : l'appareil fait foi.
                await supabase.RemplacerToutAsync(_articles);
                await MarquerEnAttenteAsync(false);
            }
            else
            {
                var distants = await supabase.RecupererAsync();

                if (distants.Count > 0 || _articles.Count == 0)
                {
                    _articles = distants;                  // le cloud fait foi
                    await PersistCacheAsync();
                }
                else
                {
                    // Table distante vide alors que l'appareil a des données : on les envoie.
                    await supabase.RemplacerToutAsync(_articles);
                    await MarquerEnAttenteAsync(false);
                }
            }

            DerniereErreur = null;
            Changed?.Invoke();
        }
        catch (Exception)
        {
            DerniereErreur = "☁️ Cloud injoignable — données locales utilisées.";
            Changed?.Invoke();
        }
    }

    // ── Modifications ─────────────────────────────────────────

    public async Task SauvegarderAsync(Article a)
    {
        var i = _articles.FindIndex(x => x.Id == a.Id);
        if (i >= 0) _articles[i] = a; else _articles.Add(a);

        await PersistCacheAsync();

        if (supabase.EstConfigure)
        {
            await MarquerEnAttenteAsync(true);   // sera levé dès que l'envoi aboutit
            Enfiler(async () =>
            {
                try
                {
                    await supabase.PousserAsync(a);
                    DerniereErreur = null;
                    return true;
                }
                catch (Exception)
                {
                    DerniereErreur = "☁️ Modifié localement — envoi reporté jusqu'au retour du réseau.";
                    return false;
                }
            });
        }

        Changed?.Invoke();
    }

    public async Task SupprimerAsync(Guid id)
    {
        _articles.RemoveAll(x => x.Id == id);
        await PersistCacheAsync();

        if (supabase.EstConfigure)
        {
            await MarquerEnAttenteAsync(true);
            Enfiler(async () =>
            {
                try
                {
                    await supabase.SupprimerAsync(id);
                    DerniereErreur = null;
                    return true;
                }
                catch (Exception)
                {
                    DerniereErreur = "☁️ Supprimé localement — envoi reporté jusqu'au retour du réseau.";
                    return false;
                }
            });
        }

        Changed?.Invoke();
    }

    public async Task<int> ImporterJsonAsync(string json)
    {
        var articles = JsonSerializer.Deserialize<List<Article>>(json, OptionsExport)
            ?? throw new InvalidDataException("Fichier illisible ou vide.");

        if (articles.Any(a => string.IsNullOrWhiteSpace(a.Nom) || a.PrixAchat <= 0))
            throw new InvalidDataException(
                "Certains articles du fichier sont invalides (nom ou prix).");

        _articles = articles;
        await PersistCacheAsync();

        if (supabase.EstConfigure)
        {
            await MarquerEnAttenteAsync(true);
            Enfiler(async () =>
            {
                try
                {
                    await supabase.RemplacerToutAsync(_articles);
                    DerniereErreur = null;
                    return true;
                }
                catch (Exception)
                {
                    DerniereErreur = "☁️ Import local — envoi reporté jusqu'au retour du réseau.";
                    return false;
                }
            });
        }

        Changed?.Invoke();
        return _articles.Count;
    }

    // ── Synchronisation manuelle (boutons de la carte Sauvegarde) ──

    /// <summary>L'appareil devient la source de vérité : tout est envoyé au cloud.</summary>
    public async Task<bool> EnvoyerToutAsync()
    {
        if (!supabase.EstConfigure) return false;
        try
        {
            await AttendreFileAsync();
            await supabase.RemplacerToutAsync(_articles);
            await MarquerEnAttenteAsync(false);
            DerniereErreur = null;
            Changed?.Invoke();
            return true;
        }
        catch (Exception)
        {
            DerniereErreur = "☁️ Envoi impossible — vérifiez votre connexion.";
            Changed?.Invoke();
            return false;
        }
    }

    /// <summary>Le cloud devient la source de vérité : tout est récupéré.</summary>
    public async Task<bool> RecupererToutAsync()
    {
        if (!supabase.EstConfigure) return false;
        try
        {
            await AttendreFileAsync();
            _articles = await supabase.RecupererAsync();
            await PersistCacheAsync();
            await MarquerEnAttenteAsync(false);
            DerniereErreur = null;
            Changed?.Invoke();
            return true;
        }
        catch (Exception)
        {
            DerniereErreur = "☁️ Récupération impossible — vérifiez votre connexion.";
            Changed?.Invoke();
            return false;
        }
    }

    // ── Export fichier ────────────────────────────────────────

    public string ExporterJson() => JsonSerializer.Serialize(_articles, OptionsExport);

    // ── Interne ───────────────────────────────────────────────

    private async Task PersistCacheAsync()
        => await storage.SetItemAsync(Key, _articles);

    private async Task MarquerEnAttenteAsync(bool enAttente)
    {
        ModificationsEnAttente = enAttente;
        await storage.SetItemAsync(CleEnAttente, enAttente);
    }

    /// <summary>Exécute les envois réseau un par un, dans l'ordre. Quand le dernier
    /// de la file réussit, le drapeau « en attente » est levé.</summary>
    private void Enfiler(Func<Task<bool>> envoi)
    {
        _envoisEnFile++;
        var precedent = _envoiEnCours;

        async Task Executer()
        {
            try { await precedent; } catch { /* la file ne doit jamais se bloquer */ }

            var ok = await envoi();
            _envoisEnFile--;

            if (_envoisEnFile == 0 && ok)
                await MarquerEnAttenteAsync(false);

            Changed?.Invoke();
        }

        _envoiEnCours = Executer();
    }

    private async Task AttendreFileAsync()
    {
        try { await _envoiEnCours; } catch { }
    }
}
