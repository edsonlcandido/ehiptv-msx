using ehiptv_msx.Models;
using Microsoft.Extensions.Options;

namespace ehiptv_msx.Services;

/// <summary>
/// Monta as telas do app MSX (4 telas + paginas de detalhe) a partir do
/// catalogo do Xtream Codes.
///
/// Telas (wireframe do usuario):
///  1. Inicio   - 3 cards (Canais / Filmes / Series)
///  2. Canais   - categorias de TV (linhas largas) -> canais da categoria
///  3. Filmes   - grade de posteres -> reproduz o filme
///  4. Series   - grade de posteres -> episodios da serie
/// </summary>
public sealed class MsxContentFactory
{
    private readonly XtreamClient _xtream;
    private readonly XtreamOptions _options;

    public MsxContentFactory(XtreamClient xtream, IOptions<XtreamOptions> options)
    {
        _xtream = xtream;
        _options = options.Value;
    }

    // ------------------------------------------------------------------
    // Start / Menu
    // ------------------------------------------------------------------

    /// <summary>Start Object: aponta para o menu, ja com a credencial na URL.</summary>
    public MsxStart BuildStart(HttpContext context, XtreamCredential credential) => new()
    {
        Name = "Eh! IPTV",
        Version = "1.0.0",
        Parameter = $"menu:{MenuUrl(context, credential)}"
    };

    /// <summary>
    /// Menu Root Object (coluna da esquerda): Inicio, Canais, Filmes, Series e
    /// as categorias de cada secao.
    /// </summary>
    public async Task<MsxMenu> BuildMenuAsync(HttpContext context, XtreamCredential credential)
    {
        var menu = new MsxMenu
        {
            Headline = "Eh! IPTV",
            Menu =
            [
                new MsxMenuItem
                {
                    Icon = "home",
                    Label = "Início",
                    Data = ContentUrl(context, credential, "home.json")
                },
                new MsxMenuItem
                {
                    Icon = "msx-white-soft:live-tv",
                    Label = "Canais",
                    Data = ContentUrl(context, credential, "live.json")
                },
                new MsxMenuItem
                {
                    Icon = "msx-white-soft:movie",
                    Label = "Filmes",
                    Data = ContentUrl(context, credential, "vod.json")
                },
                new MsxMenuItem
                {
                    Icon = "msx-white-soft:video-library",
                    Label = "Séries",
                    Data = ContentUrl(context, credential, "series.json")
                },
                new MsxMenuItem { Type = "separator" }
            ]
        };

        var liveCategories = await _xtream.GetCategoriesAsync(credential, "live");
        var vodCategories = await _xtream.GetCategoriesAsync(credential, "vod");
        var seriesCategories = await _xtream.GetCategoriesAsync(credential, "series");

        foreach (var category in liveCategories)
        {
            menu.Menu.Add(new MsxMenuItem
            {
                Label = category.CategoryName,
                Data = ContentUrl(context, credential, $"live/{category.CategoryId}.json")
            });
        }

        menu.Menu.Add(new MsxMenuItem { Type = "separator" });

        foreach (var category in vodCategories)
        {
            menu.Menu.Add(new MsxMenuItem
            {
                Label = category.CategoryName,
                Data = ContentUrl(context, credential, $"vod/{category.CategoryId}.json")
            });
        }

        menu.Menu.Add(new MsxMenuItem { Type = "separator" });

        foreach (var category in seriesCategories)
        {
            menu.Menu.Add(new MsxMenuItem
            {
                Label = category.CategoryName,
                Data = ContentUrl(context, credential, $"series/{category.CategoryId}.json")
            });
        }

        return menu;
    }

    // ------------------------------------------------------------------
    // Tela 1 - Inicio
    // ------------------------------------------------------------------

    public MsxContent BuildHome(HttpContext context, XtreamCredential credential) => new()
    {
        Type = "pages",
        Headline = "Eh! IPTV",
        // No MSX, "items" na raiz do Content Root so funciona com "template".
        // O template define o tamanho (4x4) e o auto-flow encaixa os 3 cards
        // lado a lado (3 x 4 = 12 colunas).
        Template = new MsxTemplate
        {
            Type = "default",
            Layout = "0,1,4,4",
            Color = "msx-glass"
        },
        Items =
        [
            HomeCard("msx-white-soft:live-tv", "Canais ao vivo",
                ContentUrl(context, credential, "live.json")),
            HomeCard("msx-white-soft:movie", "Filmes e VOD",
                ContentUrl(context, credential, "vod.json")),
            HomeCard("msx-white-soft:video-library", "Séries e episódios",
                ContentUrl(context, credential, "series.json"))
        ]
    };

    private static MsxContentItem HomeCard(string icon, string title, string url) => new()
    {
        Type = "default",
        Icon = icon,
        IconSize = "large",
        Title = title,
        Action = $"content:{url}"
    };

    // ------------------------------------------------------------------
    // Tela 2 - Canais (categorias -> canais)
    // ------------------------------------------------------------------

    public async Task<MsxContent> BuildLiveHubAsync(
        HttpContext context, XtreamCredential credential, int? take = null)
    {
        var categories = await _xtream.GetCategoriesAsync(credential, "live");

        if (take is not null)
        {
            categories = categories.Take(take.Value).ToList();
        }

        return new MsxContent
        {
            Type = "list",
            Headline = "Categorias",
            Hint = $"{categories.Count} categorias de TV",
            Template = new MsxTemplate
            {
                // "default" (e nao "separate"): sem imagem, o "separate" colocaria
                // o titulo na area vazia da imagem, no topo da celula.
                Type = "default",
                Layout = "0,0,12,1",
                Color = "msx-glass"
            },
            Items = categories.Select(category => new MsxContentItem
            {
                Title = category.CategoryName,
                Action = $"content:{ContentUrl(context, credential, $"live/{category.CategoryId}.json")}"
            }).ToList()
        };
    }

    public async Task<MsxContent> BuildLiveCategoryAsync(
        HttpContext context, XtreamCredential credential, string categoryId)
    {
        var categories = await _xtream.GetCategoriesAsync(credential, "live");
        var category = categories.FirstOrDefault(c => c.CategoryId == categoryId);
        var channels = await _xtream.GetLiveAsync(credential, categoryId);

        var visible = channels.Take(_options.MaxChannelItems).ToList();

        return new MsxContent
        {
            Type = "list",
            Headline = category?.CategoryName ?? "Canais",
            Hint = $"{visible.Count} canais",
            Template = new MsxTemplate
            {
                Type = "separate",
                Layout = "0,1,12,1",
                Color = "msx-glass",
                ImageFiller = "height-left"
            },
            Items = visible.Select(channel => new MsxContentItem
            {
                Title = channel.Name,
                Image = NormalizeImage(channel.StreamIcon),
                ImageWidth = 0.18,
                Action = $"video:{credential.Base("live", channel.StreamId, "m3u8")}",
                PlayerLabel = channel.Name
            }).ToList()
        };
    }

    // ------------------------------------------------------------------
    // Tela 3 - Filmes (grade de posteres)
    // ------------------------------------------------------------------

    public async Task<MsxContent> BuildVodHubAsync(
        HttpContext context, XtreamCredential credential, int? take = null)
    {
        var categories = await _xtream.GetCategoriesAsync(credential, "vod");
        var category = categories.FirstOrDefault(c => c.CategoryId == _options.DefaultVodCategory);
        var movies = await _xtream.GetVodAsync(credential, _options.DefaultVodCategory);

        return BuildPosterGrid(movies, category?.CategoryName ?? "Filmes", "filmes", credential, take);
    }

    public async Task<MsxContent> BuildVodCategoryAsync(
        HttpContext context, XtreamCredential credential, string categoryId, int? take = null)
    {
        var categories = await _xtream.GetCategoriesAsync(credential, "vod");
        var category = categories.FirstOrDefault(c => c.CategoryId == categoryId);
        var movies = await _xtream.GetVodAsync(credential, categoryId);

        return BuildPosterGrid(movies, category?.CategoryName ?? "Filmes", "filmes", credential, take);
    }

    private MsxContent BuildPosterGrid(
        List<XtreamVodStream> movies,
        string headline,
        string label,
        XtreamCredential credential,
        int? take)
    {
        var limit = take ?? _options.MaxPosterItems;
        var visible = movies.Take(limit).ToList();

        return new MsxContent
        {
            Type = "pages",
            Headline = headline,
            Hint = $"{visible.Count} {label}",
            Template = new MsxTemplate
            {
                Type = "separate",
                Layout = "0,0,2,3",
                Color = "msx-glass",
                ImageFiller = "cover"
            },
            Items = visible.Select(movie =>
            {
                var title = string.IsNullOrWhiteSpace(movie.Title) ? movie.Name : movie.Title!;
                var extension = string.IsNullOrWhiteSpace(movie.ContainerExtension) ? "mp4" : movie.ContainerExtension!;

                return new MsxContentItem
                {
                    Title = TitleWithYear(title, movie.Year),
                    Image = NormalizeImage(movie.StreamIcon),
                    Badge = string.IsNullOrWhiteSpace(movie.Rating) ? null : movie.Rating,
                    Action = $"video:{credential.Base("movie", movie.StreamId, extension)}",
                    PlayerLabel = title
                };
            }).ToList()
        };
    }

    // ------------------------------------------------------------------
    // Tela 4 - Series (grade de posteres -> episodios)
    // ------------------------------------------------------------------

    public async Task<MsxContent> BuildSeriesHubAsync(
        HttpContext context, XtreamCredential credential, int? take = null)
    {
        var categories = await _xtream.GetCategoriesAsync(credential, "series");
        var category = categories.FirstOrDefault(c => c.CategoryId == _options.DefaultSeriesCategory);
        var series = await _xtream.GetSeriesAsync(credential, _options.DefaultSeriesCategory);

        return BuildSeriesGrid(context, credential, series, category?.CategoryName ?? "Séries", take);
    }

    public async Task<MsxContent> BuildSeriesCategoryAsync(
        HttpContext context, XtreamCredential credential, string categoryId, int? take = null)
    {
        var categories = await _xtream.GetCategoriesAsync(credential, "series");
        var category = categories.FirstOrDefault(c => c.CategoryId == categoryId);
        var series = await _xtream.GetSeriesAsync(credential, categoryId);

        return BuildSeriesGrid(context, credential, series, category?.CategoryName ?? "Séries", take);
    }

    private MsxContent BuildSeriesGrid(
        HttpContext context,
        XtreamCredential credential,
        List<XtreamSeries> series,
        string headline,
        int? take)
    {
        var limit = take ?? _options.MaxPosterItems;
        var visible = series.Take(limit).ToList();

        return new MsxContent
        {
            Type = "pages",
            Headline = headline,
            Hint = $"{visible.Count} séries",
            Template = new MsxTemplate
            {
                Type = "separate",
                Layout = "0,0,2,3",
                Color = "msx-glass",
                ImageFiller = "cover"
            },
            Items = visible.Select(item =>
            {
                var title = string.IsNullOrWhiteSpace(item.Title) ? item.Name : item.Title!;

                return new MsxContentItem
                {
                    Title = TitleWithYear(title, item.Year),
                    Image = NormalizeImage(item.Cover),
                    Badge = string.IsNullOrWhiteSpace(item.Rating) ? null : item.Rating,
                    Action = $"content:{ContentUrl(context, credential, $"series-info/{item.SeriesId}.json")}",
                    PlayerLabel = title
                };
            }).ToList()
        };
    }

    public async Task<MsxContent> BuildSeriesInfoAsync(
        HttpContext context, XtreamCredential credential, string seriesId)
    {
        var info = await _xtream.GetSeriesInfoAsync(credential, seriesId);
        var episodes = (info?.Episodes ?? [])
            .SelectMany(pair => pair.Value.Select(episode => (Season: pair.Key, Episode: episode)))
            .OrderBy(pair => int.TryParse(pair.Season, out var season) ? season : 0)
            .ThenBy(pair => int.TryParse(pair.Episode.EpisodeNum, out var number) ? number : 0)
            .ToList();

        return new MsxContent
        {
            Type = "list",
            Headline = $"S{episodes.FirstOrDefault().Season ?? "01"} - {episodes.Count} episódios",
            Hint = $"{episodes.Count} episódios",
            Template = new MsxTemplate
            {
                Type = "default",
                Layout = "0,0,12,1",
                Color = "msx-glass"
            },
            Items = episodes.Select(pair =>
            {
                var episode = pair.Episode;
                var season = int.TryParse(pair.Season, out var seasonNumber) ? seasonNumber : 1;
                var number = int.TryParse(episode.EpisodeNum, out var episodeNumber) ? episodeNumber : 1;
                var episodeTitle = episode.Info?.Title ?? episode.Title ?? $"S{season:00}E{number:00}";
                var extension = string.IsNullOrWhiteSpace(episode.ContainerExtension) ? "mp4" : episode.ContainerExtension!;

                return new MsxContentItem
                {
                    Title = $"S{season:00}E{number:00} - {episodeTitle}",
                    Action = $"video:{credential.Base("series", episode.Id, extension)}",
                    PlayerLabel = episodeTitle
                };
            }).ToList()
        };
    }

    // ------------------------------------------------------------------
    // Preview (base64, autocontido, para o site msx.benzac.de)
    // ------------------------------------------------------------------

    /// <summary>
    /// Menu compacto com o conteudo embutido (sem fetch): serve para gerar o
    /// link de teste https://msx.benzac.de/?start=menu:json:{BASE64}.
    ///
    /// Os cards da tela Inicio usam "content:json:{BASE64}" (acao com o JSON
    /// inline), assim o preview funciona sem depender do servidor.
    /// </summary>
    public async Task<MsxMenu> BuildPreviewMenuAsync(HttpContext context, XtreamCredential credential)
    {
        var live = await BuildLiveHubAsync(context, credential, take: 8);
        var vod = await BuildVodHubAsync(context, credential, take: 6);
        var series = await BuildSeriesHubAsync(context, credential, take: 6);

        var home = BuildHome(context, credential);

        if (home.Items is { Count: 3 })
        {
            home.Items[0].Action = $"content:json:{ToBase64(live)}";
            home.Items[1].Action = $"content:json:{ToBase64(vod)}";
            home.Items[2].Action = $"content:json:{ToBase64(series)}";
        }

        return new MsxMenu
        {
            Headline = "Eh! IPTV",
            Menu =
            [
                new MsxMenuItem { Icon = "home", Label = "Início", Data = home },
                new MsxMenuItem { Icon = "msx-white-soft:live-tv", Label = "Canais", Data = live },
                new MsxMenuItem { Icon = "msx-white-soft:movie", Label = "Filmes", Data = vod },
                new MsxMenuItem { Icon = "msx-white-soft:video-library", Label = "Séries", Data = series }
            ]
        };
    }

    /// <summary>Serializa um Content Root Object em base64 (para acoes inline).</summary>
    private static string ToBase64(MsxContent content) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(content, MsxJson.Options)));

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Base publica do proprio app, sempre preservando o prefixo de credencial
    /// do host ("<usuario>-<senha>.localhost:8080"): o usuario e a senha
    /// ficam no inicio da URL e valem para todas as chamadas que o MSX faz.
    ///
    /// O PublicBaseUrl so entra quando o host da requisicao nao traz o
    /// prefixo (por exemplo atras de um proxy que reescreve o Host) - e, mesmo
    /// assim, o prefixo e recolocado na frente do host publico.
    /// </summary>
    private string BaseUrl(HttpContext context)
    {
        var scheme = context.Request.Scheme;
        var requestHost = context.Request.Host.Host;
        var hostAndPort = context.Request.Host.Value ?? requestHost;
        var publicUrl = _options.PublicBaseUrl?.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(publicUrl))
        {
            var uri = new Uri(publicUrl);
            scheme = uri.Scheme;

            var publicHost = uri.IsDefaultPort
                ? uri.Host
                : $"{uri.Host}:{uri.Port}";

            var prefix = CredentialResolver.CredentialPrefix(requestHost);

            hostAndPort = prefix is not null && !publicHost.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? $"{prefix}.{publicHost}"
                : publicHost;
        }

        return $"{scheme}://{hostAndPort}";
    }

    private string MenuUrl(HttpContext context, XtreamCredential credential) =>
        $"{BaseUrl(context)}/msx/{credential.PathSegment}/menu.json";

    private string ContentUrl(HttpContext context, XtreamCredential credential, string relative) =>
        $"{BaseUrl(context)}/msx/{credential.PathSegment}/content/{relative}";

    /// <summary>
    /// Junta o ano ao titulo. O provedor ja entrega alguns nomes com o ano
    /// ("A odisseia (2026)"), entao evita duplicar.
    /// </summary>
    private static string TitleWithYear(string title, string? year)
    {
        if (string.IsNullOrWhiteSpace(year))
        {
            return title;
        }

        var yearText = year.Trim();

        return title.Contains(yearText, StringComparison.OrdinalIgnoreCase)
            ? title
            : $"{title} ({yearText})";
    }

    /// <summary>
    /// O provedor entrega as capas em http://lgfp.one (Cloudflare). Forcando
    /// https o preview no navegador (msx.benzac.de) nao sofre mixed content.
    /// </summary>
    private static string? NormalizeImage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return url.StartsWith("http://lgfp.one", StringComparison.OrdinalIgnoreCase)
            ? "https://" + url["http://".Length..]
            : url;
    }
}
