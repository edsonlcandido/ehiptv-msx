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

    /// <summary>
    /// Start Object: aponta direto para a tela inicial (os 3 itens).
    /// Usar "content:" em vez de "menu:" e o que faz o MSX NAO mostrar a barra
    /// lateral esquerda.
    /// </summary>
    public MsxStart BuildStart(HttpContext context, XtreamCredential credential) => new()
    {
        Name = "Eh! IPTV",
        Version = "1.0.0",
        Parameter = $"content:{ContentUrl(context, credential, "home.json")}"
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
                    Data = SectionMenuUrl(context, "live.json")
                },
                new MsxMenuItem
                {
                    Icon = "msx-white-soft:movie",
                    Label = "Filmes",
                    Data = SectionMenuUrl(context, "vod.json")
                },
                new MsxMenuItem
                {
                    Icon = "msx-white-soft:video-library",
                    Label = "Séries",
                    Data = SectionMenuUrl(context, "series.json")
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
                Data = PagingAction(context, $"vod/{category.CategoryId}.json")
            });
        }

        menu.Menu.Add(new MsxMenuItem { Type = "separator" });

        foreach (var category in seriesCategories)
        {
            menu.Menu.Add(new MsxMenuItem
            {
                Label = category.CategoryName,
                Data = PagingAction(context, $"series/{category.CategoryId}.json")
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
            // "Canais", "Filmes" e "Series" abrem o MENU do app (coluna
            // nativa): as categorias ficam na coluna da esquerda, que rola e
            // mostra todas, e o conteudo abre em tela cheia.
            HomeCard("msx-white-soft:live-tv", "Canais ao vivo",
                SectionMenuUrl(context, "live.json"), "menu"),
            HomeCard("msx-white-soft:movie", "Filmes e VOD",
                SectionMenuUrl(context, "vod.json"), "menu"),
            HomeCard("msx-white-soft:video-library", "Séries e episódios",
                SectionMenuUrl(context, "series.json"), "menu")
        ]
    };

    private static MsxContentItem HomeCard(string icon, string title, string url, string action = "content") => new()
    {
        Type = "default",
        Icon = icon,
        IconSize = "large",
        Title = title,
        Action = $"{action}:{url}"
    };

    // ------------------------------------------------------------------
    // Tela 2 - Canais (lista no estilo "Channel List" do MSX)
    // ------------------------------------------------------------------

    /// <summary>
    /// Tela de Canais: a lista de canais no estilo do demo "Channel List" do
    /// MSX — um canal por linha, logomarca a esquerda, nome do canal, programa
    /// no ar, horario e barra de progresso.
    ///
    /// O programa no ar vem do servico de live data ("/msx/live/epg"): o
    /// MSX chama o servico quando o canal fica visivel e atualiza o item
    /// sozinho (programa, horario e progresso), sem recarregar a tela.
    ///
    /// As categorias ficam na coluna nativa do app (o menu do MSX, que rola
    /// e mostra todas), e nao dentro do conteudo.
    /// </summary>
    public async Task<MsxContent> BuildLiveChannelListAsync(
        HttpContext context,
        XtreamCredential credential,
        string? categoryId = null,
        int? take = null)
    {
        var categories = await _xtream.GetCategoriesAsync(credential, "live");
        var activeId = ResolveCategoryId(categories, categoryId, _options.DefaultLiveCategory);
        var active = categories.FirstOrDefault(c => c.CategoryId == activeId);
        var channels = await _xtream.GetLiveAsync(credential, activeId);
        var playbackBase = await _xtream.ResolvePlaybackBaseAsync(credential);

        var visible = channels.Take(take ?? _options.MaxChannelItems).ToList();

        // A lista usa paginas explicitas (como o demo "Channel List" do MSX):
        // cada canal ocupa 1,5 linha da grade (12x6), assim o bloco de texto
        // — nome, programa no ar, horario e barra de progresso — cabe sem
        // ficar cortado.
        var pages = new List<MsxContentPage>();

        for (var offset = 0; offset < visible.Count; offset += ChannelRowsPerPage)
        {
            var items = new List<MsxContentItem>();

            for (var index = 0; index < ChannelRowsPerPage && offset + index < visible.Count; index++)
            {
                var channel = visible[offset + index];
                var y = index * ChannelRowPitch;
                var row = (int)Math.Round(y, MidpointRounding.AwayFromZero);
                var shift = y - row;

                items.Add(new MsxContentItem
                {
                    Id = $"ch_{channel.StreamId}",
                    Type = "default",
                    Layout = $"0,{row},12,1",
                    Offset = $"0,{Format(shift)},0,0.5",
                    // O nome do canal vai em "text" (como no demo "Channel
                    // List"): o texto ocupa a primeira linha do bloco e o
                    // programa no ar, que o MSX injeta no titleHeader, fica
                    // logo abaixo.
                    Text = WhiteText(channel.Name),
                    Image = NormalizeImage(channel.StreamIcon),
                    ImageWidth = ChannelImageWidth,
                    ImageFiller = "fit",
                    Action = $"video:{credential.BuildFrom(playbackBase, "live", channel.StreamId, "m3u8")}",
                    PlayerLabel = channel.Name,
                    // O MSX so atualiza um campo que ja veio no proprio item (o
                    // que define o programa no ar, o horario e a barra de
                    // progresso). Se esses campos ficam so no template, o
                    // update nao aparece.
                    TitleHeader = string.Empty,
                    TitleFooter = string.Empty,
                    Progress = -1,
                    Properties = new Dictionary<string, string>
                    {
                        ["progress:type"] = "fix:Live"
                    },
                    Live = new MsxLiveObject
                    {
                        Type = "setup",
                        Action = $"execute:service:{EpgServiceUrl(context, channel.StreamId)}"
                    },
                    Focus = offset == 0 && index == 0 ? true : null
                });
            }

            pages.Add(new MsxContentPage { Items = items });
        }

        return new MsxContent
        {
            Type = "list",
            Headline = HeadlineName(active?.CategoryName, "Canais"),
            Hint = $"{visible.Count} canais",
            Pages = pages
        };
    }

    /// <summary>Canais por pagina na lista de TV (4 linhas de 1,5 na grade).</summary>
    private const int ChannelRowsPerPage = 4;

    /// <summary>
    /// Largura da imagem dentro do card de canal. O MSX mede a imagem na
    /// mesma unidade do "layout" do item (colunas da grade), entao 1.6 de 12
    /// colunas = 13% do card, deixando o nome do canal ao lado.
    /// </summary>
    private const double ChannelImageWidth = 1.6;

    /// <summary>Espacamento entre um canal e o seguinte, em linhas da grade.</summary>
    private const double ChannelRowPitch = 1.5;

    /// <summary>Numero no padrao do MSX (sempre com ponto, nunca virgula).</summary>
    private static string Format(double value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Texto em branco, como no demo "Channel List" do MSX. As chaves sao
    /// removidas do texto para nao quebrar a inline expression.
    /// </summary>
    private static string WhiteText(string value) =>
        "{txt:msx-white:" + value.Replace("{", "").Replace("}", "") + "}";

    /// <summary>
    /// Menu (coluna nativa do app) com as categorias de TV. A coluna do MSX
    /// rola e mostra todas as categorias — diferente da barra lateral dentro
    /// do conteudo, que cabe so 8 por pagina.
    /// </summary>
    public async Task<MsxMenu> BuildLiveMenuAsync(HttpContext context, XtreamCredential credential)
    {
        var categories = await _xtream.GetCategoriesAsync(credential, "live");

        return new MsxMenu
        {
            Headline = "Canais",
            Menu = categories.Select(category => new MsxMenuItem
            {
                Id = $"cat_{category.CategoryId}",
                Label = CleanCategoryName(category.CategoryName),
                Data = ContentUrl(context, credential, $"live/{category.CategoryId}.json")
            }).ToList()
        };
    }

    /// <summary>Endereco do servico de live data (EPG) de um canal.</summary>
    public string EpgServiceUrl(HttpContext context, string streamId) =>
        $"{BaseUrl(context)}/msx/live/epg?stream_id={Uri.EscapeDataString(streamId)}";

    // ------------------------------------------------------------------
    // Telas 3 e 4 - Filmes e Series (menu nativo + paging das capas)
    // ------------------------------------------------------------------

    /// <summary>
    /// Grade comprimida do MSX: 16 colunas x 8 linhas (o padrao e 12x6).
    /// </summary>
    private const int GridColumns = 16;
    private const int GridRows = 8;

    /// <summary>Largura de cada cartaz (em colunas da grade comprimida).</summary>
    private const int PosterColumns = 2;

    /// <summary>
    /// Altura do cartaz: 3 linhas de imagem + 1 linha de titulo (o item
    /// "separate" reserva a ultima linha para o texto).
    /// </summary>
    private const int PosterHeight = 4;

    /// <summary>Cartazes por pagina na grade comprimida (8 por linha x 2 linhas).</summary>
    private const int PosterPerPage = (GridColumns / PosterColumns) * (GridRows / PosterHeight);

    /// <summary>
    /// Menu (coluna nativa do app) com as categorias de filmes. Cada item
    /// abre a grade de cartazes da categoria, paginada pelo Paging Plugin.
    /// </summary>
    public async Task<MsxMenu> BuildVodMenuAsync(HttpContext context, XtreamCredential credential)
    {
        var categories = await _xtream.GetCategoriesAsync(credential, "vod");

        return new MsxMenu
        {
            Headline = "Filmes",
            Menu = categories.Select(category => new MsxMenuItem
            {
                Id = $"cat_{category.CategoryId}",
                Label = CleanCategoryName(category.CategoryName),
                Data = PagingAction(context, $"vod/{category.CategoryId}.json")
            }).ToList()
        };
    }

    /// <summary>Menu (coluna nativa do app) com as categorias de series.</summary>
    public async Task<MsxMenu> BuildSeriesMenuAsync(HttpContext context, XtreamCredential credential)
    {
        var categories = await _xtream.GetCategoriesAsync(credential, "series");

        return new MsxMenu
        {
            Headline = "Séries",
            Menu = categories.Select(category => new MsxMenuItem
            {
                Id = $"cat_{category.CategoryId}",
                Label = CleanCategoryName(category.CategoryName),
                Data = PagingAction(context, $"series/{category.CategoryId}.json")
            }).ToList()
        };
    }

    /// <summary>
    /// Acao do Paging Plugin do MSX: o plugin carrega a primeira pagina de
    /// cartazes e, quando o ultimo cartaz da pagina fica visivel, pede a
    /// proxima pagina (offset/limit) ao content service.
    ///
    /// O prefixo e "request:interaction:" (sem "content:") porque este valor
    /// vai no campo "data" de um item de menu: para o MSX, "content:" e sintaxe
    /// de acao e nao um valor valido para o "data" do menu.
    /// </summary>
    public string PagingAction(HttpContext context, string relative) =>
        $"request:interaction:{BaseUrl(context)}/msx/service/{relative}" +
        $"?offset={{OFFSET}}&limit={{LIMIT}}|{_options.PagingLimit}@{_options.PagingPluginUrl}";

    /// <summary>
    /// Filmes: grade de cartazes (proporcao de poster) com paginacao sob
    /// demanda. O "template" define o cartaz (2 colunas x 4 linhas) e o MSX
    /// posiciona os itens automaticamente.
    /// </summary>
    public async Task<MsxContent> BuildVodContentAsync(
        HttpContext context,
        XtreamCredential credential,
        string? categoryId = null,
        int offset = 0,
        int limit = PosterPerPage)
    {
        var categories = await _xtream.GetCategoriesAsync(credential, "vod");
        var activeId = ResolveCategoryId(categories, categoryId, _options.DefaultVodCategory);
        var active = categories.FirstOrDefault(c => c.CategoryId == activeId);
        var movies = (await _xtream.GetVodAsync(credential, activeId))
            .Take(_options.MaxPosterItems)
            .ToList();
        var playbackBase = await _xtream.ResolvePlaybackBaseAsync(credential);

        var visible = movies.Skip(offset).Take(limit).ToList();

        var items = visible.Select((movie, index) =>
        {
            var title = string.IsNullOrWhiteSpace(movie.Title) ? movie.Name : movie.Title!;
            var extension = string.IsNullOrWhiteSpace(movie.ContainerExtension) ? "mp4" : movie.ContainerExtension!;

            return new MsxContentItem
            {
                Title = TitleWithYear(title, movie.Year),
                Image = NormalizeImage(movie.StreamIcon),
                Badge = string.IsNullOrWhiteSpace(movie.Rating) ? null : movie.Rating,
                Action = $"video:{credential.BuildFrom(playbackBase, "movie", movie.StreamId, extension)}",
                PlayerLabel = title,
                Focus = offset == 0 && index == 0 ? true : null
            };
        }).ToList();

        return new MsxContent
        {
            Compress = true,
            Type = "list",
            Headline = HeadlineName(active?.CategoryName, "Filmes"),
            Extension = $"{movies.Count} filmes",
            Template = PosterTemplate(),
            Items = items
        };
    }

    /// <summary>
    /// Series: mesma grade de cartazes dos filmes. Cada cartaz abre a pagina
    /// de episodios da serie.
    /// </summary>
    public async Task<MsxContent> BuildSeriesContentAsync(
        HttpContext context,
        XtreamCredential credential,
        string? categoryId = null,
        int offset = 0,
        int limit = PosterPerPage)
    {
        var categories = await _xtream.GetCategoriesAsync(credential, "series");
        var activeId = ResolveCategoryId(categories, categoryId, _options.DefaultSeriesCategory);
        var active = categories.FirstOrDefault(c => c.CategoryId == activeId);
        var series = (await _xtream.GetSeriesAsync(credential, activeId))
            .Take(_options.MaxPosterItems)
            .ToList();

        var visible = series.Skip(offset).Take(limit).ToList();

        var items = visible.Select((item, index) =>
        {
            var title = string.IsNullOrWhiteSpace(item.Title) ? item.Name : item.Title!;

            return new MsxContentItem
            {
                Title = TitleWithYear(title, item.Year),
                Image = NormalizeImage(item.Cover),
                Badge = string.IsNullOrWhiteSpace(item.Rating) ? null : item.Rating,
                Action = $"content:{ContentUrl(context, credential, $"series-info/{item.SeriesId}.json")}",
                PlayerLabel = title,
                Focus = offset == 0 && index == 0 ? true : null
            };
        }).ToList();

        return new MsxContent
        {
            Compress = true,
            Type = "list",
            Headline = HeadlineName(active?.CategoryName, "Séries"),
            Extension = $"{series.Count} séries",
            Template = PosterTemplate(),
            Items = items
        };
    }

    /// <summary>Template do cartaz: proporcao vertical de poster.</summary>
    private static MsxTemplate PosterTemplate() => new()
    {
        Type = "separate",
        Layout = "0,0,2,4",
        Color = "msx-glass",
        ImageFiller = "cover"
    };


    /// <summary>Lista completa de categorias (destino do "Mais categorias").</summary>
    public Task<MsxContent> BuildVodCategoriesAsync(HttpContext context, XtreamCredential credential) =>
        BuildCategoriesAsync(
            _xtream.GetCategoriesAsync(credential, "vod"),
            "Categorias de filmes",
            id => ContentUrl(context, credential, $"vod/{id}.json"));

    /// <summary>Lista completa de categorias (destino do "Mais categorias").</summary>
    public Task<MsxContent> BuildSeriesCategoriesAsync(HttpContext context, XtreamCredential credential) =>
        BuildCategoriesAsync(
            _xtream.GetCategoriesAsync(credential, "series"),
            "Categorias de séries",
            id => ContentUrl(context, credential, $"series/{id}.json"));

    private static async Task<MsxContent> BuildCategoriesAsync(
        Task<List<XtreamCategory>> categoriesTask,
        string headline,
        Func<string, string> categoryUrl)
    {
        var categories = await categoriesTask;

        return new MsxContent
        {
            Type = "list",
            Headline = headline,
            Hint = $"{categories.Count} categorias",
            Template = new MsxTemplate
            {
                Type = "default",
                Layout = "0,0,12,1",
                Color = "msx-glass"
            },
            Items = categories.Select(category => new MsxContentItem
            {
                Title = CleanCategoryName(category.CategoryName),
                Action = $"content:{categoryUrl(category.CategoryId)}"
            }).ToList()
        };
    }

    /// <summary>
    /// Descobre qual categoria abrir: a pedida, a padrao ou a primeira.
    /// </summary>
    private static string ResolveCategoryId(
        List<XtreamCategory> categories, string? requested, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(requested) &&
            categories.Any(c => c.CategoryId == requested))
        {
            return requested!;
        }

        if (categories.Any(c => c.CategoryId == fallback))
        {
            return fallback;
        }

        return categories.FirstOrDefault()?.CategoryId ?? requested ?? fallback;
    }

    /// <summary>
    /// O provedor prefixa as categorias ("Filmes | Acao", "Series | Netflix").
    /// Na barra lateral sobra so o nome da categoria.
    /// </summary>
    private static string CleanCategoryName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Sem nome";
        }

        var text = name.Trim();
        var separator = text.IndexOf('|');

        if (separator > 0 && separator <= 16)
        {
            text = text[(separator + 1)..].Trim();
        }

        return text;
    }

    /// <summary>
    /// Titulo da tela: o nome da categoria sem o prefixo do provedor
    /// ("Canais | Abertos" vira "Abertos"), com um texto de reserva.
    /// </summary>
    private static string HeadlineName(string? name, string fallback)
    {
        var clean = CleanCategoryName(name);

        return clean == "Sem nome" ? fallback : clean;
    }

    public async Task<MsxContent> BuildSeriesInfoAsync(
        HttpContext context, XtreamCredential credential, string seriesId)
    {
        var info = await _xtream.GetSeriesInfoAsync(credential, seriesId);
        var playbackBase = await _xtream.ResolvePlaybackBaseAsync(credential);
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
                    Action = $"video:{credential.BuildFrom(playbackBase, "series", episode.Id, extension)}",
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
    /// O card de Canais usa "content:json:{BASE64}" (acao com o JSON inline),
    /// assim o preview funciona sem depender do servidor. Filmes e Series
    /// abrem o menu nativo (com as categorias), que precisa do servidor.
    /// </summary>
    public async Task<MsxMenu> BuildPreviewMenuAsync(HttpContext context, XtreamCredential credential)
    {
        var live = await BuildLiveChannelListAsync(context, credential, take: 8);

        var home = BuildHome(context, credential);

        if (home.Items is { Count: 3 })
        {
            home.Items[0].Action = $"content:json:{ToBase64(live)}";
        }

        return new MsxMenu
        {
            Headline = "Eh! IPTV",
            Menu =
            [
                new MsxMenuItem { Icon = "home", Label = "Início", Data = home },
                new MsxMenuItem { Icon = "msx-white-soft:live-tv", Label = "Canais", Data = live },
                new MsxMenuItem
                {
                    Icon = "msx-white-soft:movie",
                    Label = "Filmes",
                    Data = SectionMenuUrl(context, "vod.json")
                },
                new MsxMenuItem
                {
                    Icon = "msx-white-soft:video-library",
                    Label = "Séries",
                    Data = SectionMenuUrl(context, "series.json")
                }
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
    /// Base publica do proprio app.
    ///
    /// A credencial fica no PRIMEIRO TRECHO do host ("<usuario>-<senha>.dominio"),
    /// nunca em sub-caminho. Atras de um proxy o "Host" chega interno
    /// (ex.: localhost:3000), entao o host publico vem do X-Forwarded-Host e o
    /// prefixo de credencial e reaplicado na frente dele.
    /// </summary>
    private string BaseUrl(HttpContext context)
    {
        var scheme = CredentialResolver.PublicScheme(context, _options.PublicScheme);
        var publicHost = CredentialResolver.PublicHost(context);
        var hostAndPort = publicHost;
        var publicUrl = _options.PublicBaseUrl?.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(publicUrl))
        {
            var uri = new Uri(publicUrl);
            scheme = uri.Scheme;

            var configuredHost = uri.IsDefaultPort
                ? uri.Host
                : $"{uri.Host}:{uri.Port}";

            // Se o host publico configurado ja tem o prefixo de credencial,
            // ele manda; senao reaplica o prefixo do host que chegou.
            var prefix = CredentialResolver.CredentialPrefix(publicHost);

            hostAndPort = prefix is not null && !configuredHost.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? $"{prefix}.{configuredHost}"
                : configuredHost;
        }

        // O esquema configurado tem a ultima palavra: o provedor so serve
        // HTTP e HTTPS aqui daria mixed content no navegador.
        if (!string.IsNullOrWhiteSpace(_options.PublicScheme))
        {
            scheme = _options.PublicScheme.Trim();
        }

        return $"{scheme}://{hostAndPort}";
    }

    private string MenuUrl(HttpContext context, XtreamCredential credential) =>
        $"{BaseUrl(context)}/msx/menu.json";

    /// <summary>
    /// Menu de uma secao (coluna nativa do app). O "menu:" no start.json ou
    /// na acao de um card faz o MSX mostrar essa coluna a esquerda.
    /// </summary>
    private string SectionMenuUrl(HttpContext context, string relative) =>
        $"{BaseUrl(context)}/msx/menu/{relative}";

    private string ContentUrl(HttpContext context, XtreamCredential credential, string relative) =>
        $"{BaseUrl(context)}/msx/content/{relative}";

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
