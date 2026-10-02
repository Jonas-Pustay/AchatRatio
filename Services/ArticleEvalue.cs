using AchatRatio.Models;

namespace AchatRatio.Services;

/// <summary>Article associé au résultat de ses calculs (listes, classements).</summary>
public record ArticleEvalue(Article Article, ResultatRatio Ratio);
