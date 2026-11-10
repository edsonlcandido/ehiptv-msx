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
    // Tela 2 - Canais (navegador no estilo XCIPTV)
    // ------------------------------------------------------------------

    /// <summary>
    /// Tela de Canais no mesmo desenho das telas de Filmes e Series: barra
    /// lateral com as categorias de TV a esquerda e, a direita, os canais como
    /// cards horizontais (logo do canal a esquerda, nome a direita). O tipo
    /// "list" empilha as paginas na vertical: navegar para baixo carrega mais
    /// canais da mesma categoria.
    /// </summary>
    public async Task<MsxContent> BuildLiveBrowserAsync(
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

        var cards = visible.Select(channel => new MsxContentItem
        {
            // "default" (e nao "separate"): o "separate" reserva uma linha da
            // celula para o titulo (cartao de poster). Aqui o titulo fica ao
            // lado da imagem, dentro da mesma celula.
            Type = "default",
            Title = channel.Name,
            Image = NormalizeImage(channel.StreamIcon),
            ImageWidth = ChannelImageWidth,
            ImageFiller = "fit",
            Color = "msx-glass",
            Action = $"video:{credential.BuildFrom(playbackBase, "live", channel.StreamId, "m3u8")}",
            PlayerLabel = channel.Name
        }).ToList();

        return BuildBrowser(
            categories,
            activeId,
            HeadlineName(active?.CategoryName, "Canais"),
            "canais",
            id => ContentUrl(context, credential, $"live/{id}.json"),
            ContentUrl(context, credential, "live-categories.json"),
            cards,
            ChannelPerPage,
            PlaceChannelCard);
    }

    /// <summary>Lista completa de categorias de TV (destino do "Mais categorias").</summary>
    public Task<MsxContent> BuildLiveCategoriesAsync(HttpContext context, XtreamCredential credential) =>
        BuildCategoriesAsync(
            _xtream.GetCategoriesAsync(credential, "live"),
            "Categorias de TV",
            id => ContentUrl(context, credential, $"live/{id}.json"));

    // ------------------------------------------------------------------
    // Telas 3 e 4 - Filmes e Series (navegador no estilo XCIPTV)
    // ------------------------------------------------------------------

    /// <summary>
    /// Grade comprimida do MSX: 16 colunas x 8 linhas (o padrao e 12x6).
    /// A grade comprimida e a unica que permite dividir a tela em uma barra
    /// lateral de categorias + uma grade de cartazes com proporcao de poster.
    /// </summary>
    private const int GridColumns = 16;
    private const int GridRows = 8;

    /// <summary>Colunas da barra lateral de categorias (0..3).</summary>
    private const int SidebarColumns = 4;

    /// <summary>
    /// A barra lateral tem UMA coluna de categorias (1 item por linha da
    /// grade). Com 8 linhas da grade, cabem 8 categorias por pagina.
    /// </summary>
    private const int SidebarItemColumns = 4;

    /// <summary>Largura de cada cartaz (em colunas da grade comprimida).</summary>
    private const int PosterColumns = 2;

    /// <summary>
    /// Altura do cartaz: 3 linhas de imagem + 1 linha de titulo (o item
    /// "separate" reserva a ultima linha para o texto).
    /// </summary>
    private const int PosterHeight = 4;

    /// <summary>Cartazes por linha, por pagina e vagas da barra lateral.</summary>
    private const int PosterPerRow = (GridColumns - SidebarColumns) / PosterColumns;
    private const int PosterRowsPerPage = GridRows / PosterHeight;
    private const int PosterPerPage = PosterPerRow * PosterRowsPerPage;
    private const int SidebarSlots = GridRows;

    /// <summary>
    /// Quantas categorias a janela da barra lateral avanca a cada pagina
    /// rolada. A coluna mostra a categoria ativa fixa no topo + esta janela
    /// + o botao "Mais categorias" no fim.
    /// </summary>
    private const int SidebarWindow = SidebarSlots - 2;

    /// <summary>
    /// Largura do card de canal (em colunas da grade comprimida). O card de
    /// canal e horizontal: logo do canal a esquerda, nome a direita.
    /// </summary>
    private const int ChannelCardColumns = 6;

    /// <summary>Cards de canal por linha e por pagina.</summary>
    private const int ChannelCardsPerRow = (GridColumns - SidebarColumns) / ChannelCardColumns;
    private const int ChannelPerPage = ChannelCardsPerRow * GridRows;

    /// <summary>
    /// Largura da imagem dentro do card de canal. O MSX mede a imagem na
    /// mesma unidade do "layout" do item (colunas da grade), entao 1.8 de 6
    /// colunas = 30% do card, deixando o nome do canal ao lado.
    /// </summary>
    private const double ChannelImageWidth = 1.8;

    /// <summary>Posiciona um cartaz de filme/serie na grade da area de conteudo.</summary>
    private static void PlacePoster(MsxContentItem item, int index)
    {
        var column = index % PosterPerRow;
        var row = index / PosterPerRow;

        item.Layout =
            $"{SidebarColumns + column * PosterColumns},{row * PosterHeight},{PosterColumns},{PosterHeight}";
    }

    /// <summary>Posiciona um card de canal na grade da area de conteudo.</summary>
    private static void PlaceChannelCard(MsxContentItem item, int index)
    {
        var column = index % ChannelCardsPerRow;
        var row = index / ChannelCardsPerRow;

        item.Layout =
            $"{SidebarColumns + column * ChannelCardColumns},{row},{ChannelCardColumns},1";
    }

    /// <summary>
    /// Tela de Filmes no estilo XCIPTV: barra lateral com as categorias a
    /// esquerda e a grade de cartazes (proporcao poster) a direita. O tipo
    /// "list" empilha as paginas na vertical: navegar para baixo carrega
    /// mais cartazes da mesma categoria.
    /// </summary>
    public async Task<MsxContent> BuildVodBrowserAsync(
        HttpContext context,
        XtreamCredential credential,
        string? categoryId = null,
        int? take = null)
    {
        var categories = await _xtream.GetCategoriesAsync(credential, "vod");
        var activeId = ResolveCategoryId(categories, categoryId, _options.DefaultVodCategory);
        var active = categories.FirstOrDefault(c => c.CategoryId == activeId);
        var movies = await _xtream.GetVodAsync(credential, activeId);
        var playbackBase = await _xtream.ResolvePlaybackBaseAsync(credential);

        var visible = movies.Take(take ?? _options.MaxPosterItems).ToList();

        var posters = visible.Select(movie =>
        {
            var title = string.IsNullOrWhiteSpace(movie.Title) ? movie.Name : movie.Title!;
            var extension = string.IsNullOrWhiteSpace(movie.ContainerExtension) ? "mp4" : movie.ContainerExtension!;

            return new MsxContentItem
            {
                Type = "separate",
                Title = TitleWithYear(title, movie.Year),
                Image = NormalizeImage(movie.StreamIcon),
                ImageFiller = "cover",
                Badge = string.IsNullOrWhiteSpace(movie.Rating) ? null : movie.Rating,
                Action = $"video:{credential.BuildFrom(playbackBase, "movie", movie.StreamId, extension)}",
                PlayerLabel = title
            };
        }).ToList();

        return BuildBrowser(
            categories,
            activeId,
            HeadlineName(active?.CategoryName, "Filmes"),
            "filmes",
            id => ContentUrl(context, credential, $"vod/{id}.json"),
            ContentUrl(context, credential, "vod-categories.json"),
            posters,
            PosterPerPage,
            PlacePoster);
    }

    /// <summary>
    /// Tela de Series no estilo XCIPTV (mesmo desenho da tela de Filmes).
    /// </summary>
    public async Task<MsxContent> BuildSeriesBrowserAsync(
        HttpContext context,
        XtreamCredential credential,
        string? categoryId = null,
        int? take = null)
    {
        var categories = await _xtream.GetCategoriesAsync(credential, "series");
        var activeId = ResolveCategoryId(categories, categoryId, _options.DefaultSeriesCategory);
        var active = categories.FirstOrDefault(c => c.CategoryId == activeId);
        var series = await _xtream.GetSeriesAsync(credential, activeId);

        var visible = series.Take(take ?? _options.MaxPosterItems).ToList();

        var posters = visible.Select(item =>
        {
            var title = string.IsNullOrWhiteSpace(item.Title) ? item.Name : item.Title!;

            return new MsxContentItem
            {
                Type = "separate",
                Title = TitleWithYear(title, item.Year),
                Image = NormalizeImage(item.Cover),
                ImageFiller = "cover",
                Badge = string.IsNullOrWhiteSpace(item.Rating) ? null : item.Rating,
                Action = $"content:{ContentUrl(context, credential, $"series-info/{item.SeriesId}.json")}",
                PlayerLabel = title
            };
        }).ToList();

        return BuildBrowser(
            categories,
            activeId,
            HeadlineName(active?.CategoryName, "Séries"),
            "séries",
            id => ContentUrl(context, credential, $"series/{id}.json"),
            ContentUrl(context, credential, "series-categories.json"),
            posters,
            PosterPerPage,
            PlacePoster);
    }

    /// <summary>
    /// Monta o navegador: barra lateral de categorias + area de conteudo
    /// paginada na vertical. A barra lateral e montada por pagina porque a
    /// janela de categorias avanca junto com a rolagem (a grade do MSX tem
    /// apenas 8 linhas, entao uma coluna so comporta 8 categorias por vez).
    ///
    /// A area de conteudo e generica: "entries" sao os itens (cartazes de
    /// filme/serie ou cards de canal) e "place" posiciona cada um na grade.
    /// </summary>
    private MsxContent BuildBrowser(
        List<XtreamCategory> categories,
        string activeCategoryId,
        string headline,
        string unit,
        Func<string, string> categoryUrl,
        string categoriesUrl,
        List<MsxContentItem> entries,
        int perPage,
        Action<MsxContentItem, int> place)
    {
        var pages = new List<MsxContentPage>();

        for (var offset = 0; offset < entries.Count; offset += perPage)
        {
            var items = BuildSidebar(categories, activeCategoryId, categoryUrl, categoriesUrl, pages.Count);

            for (var index = 0; index < perPage && offset + index < entries.Count; index++)
            {
                var entry = entries[offset + index];

                place(entry, index);
                entry.Focus = offset == 0 && index == 0 ? true : null;

                items.Add(entry);
            }

            pages.Add(new MsxContentPage { Items = items });
        }

        if (pages.Count == 0)
        {
            pages.Add(new MsxContentPage
            {
                Items =
                [
                    .. BuildSidebar(categories, activeCategoryId, categoryUrl, categoriesUrl, 0),
                    new MsxContentItem
                    {
                        Layout = $"{SidebarColumns},0,{GridColumns - SidebarColumns},{GridRows}",
                        Type = "default",
                        Title = "Nenhum item nesta categoria",
                        Color = "msx-glass"
                    }
                ]
            });
        }

        return new MsxContent
        {
            Compress = true,
            Type = "list",
            Headline = headline,
            Hint = $"{entries.Count} {unit}",
            Pages = pages
        };
    }

    /// <summary>
    /// Barra lateral: UMA coluna de categorias. A categoria ativa fica fixa no
    /// topo (destacada), as demais entram numa janela que avanca a cada pagina
    /// rolada — assim todas as categorias do provedor ficam na mesma coluna —
    /// e a ultima vaga leva para a lista completa.
    /// </summary>
    private List<MsxContentItem> BuildSidebar(
        List<XtreamCategory> categories,
        string activeCategoryId,
        Func<string, string> categoryUrl,
        string categoriesUrl,
        int pageIndex)
    {
        var items = new List<MsxContentItem>();
        var active = categories.FirstOrDefault(c => c.CategoryId == activeCategoryId);

        if (active is not null)
        {
            items.Add(SidebarItem(active, 0, true, categoryUrl));
        }

        var others = categories.Where(c => c.CategoryId != activeCategoryId).ToList();

        if (others.Count > 0)
        {
            var start = (pageIndex * SidebarWindow) % others.Count;

            for (var index = 0; index < SidebarWindow; index++)
            {
                var category = others[(start + index) % others.Count];
                items.Add(SidebarItem(category, index + 1, false, categoryUrl));
            }
        }

        // Ultima vaga: lista completa de categorias.
        items.Add(new MsxContentItem
        {
            Layout = $"0,{SidebarSlots - 1},{SidebarItemColumns},1",
            Type = "default",
            Title = "Mais categorias",
            Color = "msx-glass",
            Action = $"content:{categoriesUrl}"
        });

        return items;
    }

    private static MsxContentItem SidebarItem(
        XtreamCategory category, int row, bool isActive, Func<string, string> categoryUrl) =>
        new()
        {
            Layout = $"0,{row},{SidebarItemColumns},1",
            Type = "default",
            Title = CleanCategoryName(category.CategoryName),
            Color = isActive ? "msx-blue" : "msx-glass",
            Action = $"content:{categoryUrl(category.CategoryId)}"
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
    /// Os cards da tela Inicio usam "content:json:{BASE64}" (acao com o JSON
    /// inline), assim o preview funciona sem depender do servidor.
    /// </summary>
    public async Task<MsxMenu> BuildPreviewMenuAsync(HttpContext context, XtreamCredential credential)
    {
        var live = await BuildLiveBrowserAsync(context, credential, take: ChannelPerPage);
        var vod = await BuildVodBrowserAsync(context, credential, take: PosterPerPage);
        var series = await BuildSeriesBrowserAsync(context, credential, take: PosterPerPage);

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
