using System.ComponentModel.DataAnnotations;

namespace AchatRatio.Models;

public class Article
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "Le nom est obligatoire.")]
    [StringLength(60, ErrorMessage = "60 caractères maximum.")]
    public string Nom { get; set; } = "";

    [StringLength(40)]
    public string Marque { get; set; } = "";

    public string Categorie { get; set; } = "Divers";

    [Range(0.01, 100_000, ErrorMessage = "Le prix doit être supérieur à 0.")]
    public decimal PrixAchat { get; set; }

    public DateOnly DateAchat { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public StatutArticle Statut { get; set; } = StatutArticle.EnCours;

    public DateOnly? DateFin { get; set; }

    [Range(0, 10, ErrorMessage = "La note doit être comprise entre 0 et 10.")]
    public int Note { get; set; } = 5;

    public string? ImageUrl { get; set; }

    // Facultatif — sert au calcul des économies (dashboard)
    public decimal? PrixBasDeGamme { get; set; }
    public int? DureeVieBasDeGammeMois { get; set; }

    /// <summary>Copie indépendante : on édite la copie, on ne touche pas aux données
    /// persistées tant que l'utilisateur n'a pas enregistré.</summary>
    public Article Clone() => new()
    {
        Id = Id,
        Nom = Nom,
        Marque = Marque,
        Categorie = Categorie,
        PrixAchat = PrixAchat,
        DateAchat = DateAchat,
        Statut = Statut,
        DateFin = DateFin,
        Note = Note,
        ImageUrl = ImageUrl,
        PrixBasDeGamme = PrixBasDeGamme,
        DureeVieBasDeGammeMois = DureeVieBasDeGammeMois
    };
}
