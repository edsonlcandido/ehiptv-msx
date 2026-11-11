using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ehiptv_msx.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ehiptv_msx.Services;

/// <summary>
/// Cliente do Xtream Codes (player_api.php). Fala direto com o provedor,
/// guarda as respostas em cache (o catalogo nao muda a cada request) e nunca
/// loga usuario/senha.
///
/// O cache guarda o JSON bruto: cada requisicao desserializa uma copia nova,
/// assim nenhum chamador consegue alterar o que ficou guardado em cache.
/// </summary>
public sealed class XtreamClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    static XtreamClient()
    {
        // O provedor manda "parent_id": 0 (numero) em alguns endpoints e
        // "parent_id": "0" em outros: o conversor aceita os dois.
        JsonOptions.Converters.Add(new FlexibleStringConverter());
    }

    private readonly IHttpClientFactory _httpFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<XtreamClient> _logger;
    private readonly XtreamOptions _options;

    /// <summary>Ultima base que respondeu OK (evita repetir a que falha).</summary>
    private string? _workingBase;

    public XtreamClient(
        IHttpClientFactory httpFactory,
        IMemoryCache cache,
        ILogger<XtreamClient> logger,
        IOptions<XtreamOptions> options)
    {
        _httpFactory = httpFactory;
        _cache = cache;
        _logger = logger;
        _options = options.Value;
    }

    public Task<List<XtreamCategory>> GetCategoriesAsync(XtreamCredential credential, string kind) =>
        GetListAsync<XtreamCategory>(
            credential,
            $"get_{kind}_categories",
            null,
            TimeSpan.FromMinutes(Math.Max(1, _options.CategoriesCacheMinutes)));

    public Task<List<XtreamLiveStream>> GetLiveAsync(XtreamCredential credential, string categoryId) =>
        GetListAsync<XtreamLiveStream>(credential, "get_live_streams", $"category_id={categoryId}", TimeSpan.FromMinutes(5));

    public Task<List<XtreamVodStream>> GetVodAsync(XtreamCredential credential, string categoryId) =>
        GetListAsync<XtreamVodStream>(credential, "get_vod_streams", $"category_id={categoryId}", TimeSpan.FromMinutes(5));

    public Task<List<XtreamSeries>> GetSeriesAsync(XtreamCredential credential, string categoryId) =>
        GetListAsync<XtreamSeries>(credential, "get_series", $"category_id={categoryId}", TimeSpan.FromMinutes(5));

    // ------------------------------------------------------------------
    // Busca
    // ------------------------------------------------------------------

    /// <summary>
    /// Indice de busca do catalogo (canais, filmes ou series).
    ///
    /// O provedor ignora o parametro "search" do player_api.php: ele devolve
    /// o catalogo inteiro, como faz com o "series_id" no get_series. Por isso a
    /// busca acontece aqui: o catalogo completo e guardado em cache e
    /// filtrado localmente, sem acento e sem diferenciar maiusculas.
    ///
    /// O indice guarda so os campos que a tela de resultado precisa (o JSON
    /// bruto de 44 mil filmes ficaria pesado na memoria).
    /// </summary>
    public async Task<List<XtreamSearchEntry>> GetSearchIndexAsync(XtreamCredential credential, string kind)
    {
        var cacheKey = $"search-index:{kind}:{credential.Username}";

        if (_cache.TryGetValue(cacheKey, out List<XtreamSearchEntry>? cached) && cached is not null)
        {
            return cached;
        }

        var entries = kind switch
        {
            "live" => await BuildLiveIndexAsync(credential),
            "vod" => await BuildVodIndexAsync(credential),
            "series" => await BuildSeriesIndexAsync(credential),
            _ => []
        };

        _cache.Set(cacheKey, entries, SearchCatalogTtl);

        return entries;
    }

    /// <summary>Canais: nome, logomarca e categoria de origem.</summary>
    private async Task<List<XtreamSearchEntry>> BuildLiveIndexAsync(XtreamCredential credential)
    {
        var categories = await GetCategoriesAsync(credential, "live");

        var names = categories.ToDictionary(
            category => category.CategoryId,
            category => category.CategoryName,
            StringComparer.OrdinalIgnoreCase);

        var streams = await GetListAsync<XtreamLiveStream>(
            credential, "get_live_streams", null, SearchCatalogTtl);

        return streams
            .Select(stream => new XtreamSearchEntry
            {
                Id = stream.StreamId,
                Title = stream.Name,
                Cover = stream.StreamIcon,
                CategoryId = stream.CategoryId,
                CategoryName = names.TryGetValue(stream.CategoryId, out var name) ? name : null,
                SearchKey = NormalizeSearchText(stream.Name)
            })
            .Where(entry => entry.SearchKey.Length > 0)
            .ToList();
    }

    /// <summary>Filmes: titulo, ano, cartaz, nota e extensao do arquivo.</summary>
    private async Task<List<XtreamSearchEntry>> BuildVodIndexAsync(XtreamCredential credential)
    {
        var movies = await GetListAsync<XtreamVodStream>(
            credential, "get_vod_streams", null, SearchCatalogTtl);

        return movies
            .Select(movie =>
            {
                var title = string.IsNullOrWhiteSpace(movie.Title) ? movie.Name : movie.Title!;

                return new XtreamSearchEntry
                {
                    Id = movie.StreamId,
                    Title = title,
                    Year = movie.Year,
                    Cover = movie.StreamIcon,
                    Rating = movie.Rating,
                    CategoryId = movie.CategoryId,
                    Extension = movie.ContainerExtension,
                    SearchKey = NormalizeSearchText(title)
                };
            })
            .Where(entry => entry.SearchKey.Length > 0)
            .ToList();
    }

    /// <summary>Series: titulo, ano, cartaz e nota.</summary>
    private async Task<List<XtreamSearchEntry>> BuildSeriesIndexAsync(XtreamCredential credential)
    {
        var series = await GetListAsync<XtreamSeries>(
            credential, "get_series", null, SearchCatalogTtl);

        return series
            .Select(item =>
            {
                var title = string.IsNullOrWhiteSpace(item.Title) ? item.Name : item.Title!;

                return new XtreamSearchEntry
                {
                    Id = item.SeriesId,
                    Title = title,
                    Year = item.Year,
                    Cover = item.Cover,
                    Rating = item.Rating,
                    CategoryId = item.CategoryId,
                    SearchKey = NormalizeSearchText(title)
                };
            })
            .Where(entry => entry.SearchKey.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Tempo de cache do catalogo completo usado na busca. O catalogo de filmes
    /// tem 44 mil itens (30 MB de JSON), entao ele e buscado uma vez e
    /// reaproveitado por um tempo maior que o das listas por categoria.
    /// </summary>
    private TimeSpan SearchCatalogTtl =>
        TimeSpan.FromMinutes(Math.Max(1, _options.SearchIndexCacheMinutes));

    /// <summary>
    /// Texto normalizado para a busca: minusculas e sem acentos, para que
    /// "acao" ache "Ação" e "SIMPSONS" ache "Simpsons".
    /// </summary>
    public static string NormalizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().ToLowerInvariant().Trim();
    }

    /// <summary>
    /// Procura os metadados de uma serie (capa, sinopse, elenco, nota).
    ///
    /// O provedor ignora o "series_id" no "get_series" e devolve o catalogo
    /// inteiro, entao a serie e procurada dentro da lista da categoria. Como a
    /// tela de detalhes sempre vem de uma grade de categoria, o "categoryId"
    /// resolve na primeira tentativa; sem ele (ou se a serie nao estiver la),
    /// algumas outras categorias sao varridas em paralelo.
    /// </summary>
    public async Task<XtreamSeries?> FindSeriesAsync(
        XtreamCredential credential, string seriesId, string? categoryId = null)
    {
        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            var inCategory = (await GetSeriesAsync(credential, categoryId!))
                .FirstOrDefault(series => series.SeriesId == seriesId);

            if (inCategory is not null)
            {
                return inCategory;
            }
        }

        var categories = await GetCategoriesAsync(credential, "series");

        var candidates = categories
            .Where(category => category.CategoryId != categoryId)
            .Take(SeriesScanLimit)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var lists = await Task.WhenAll(
            candidates.Select(category => GetSeriesAsync(credential, category.CategoryId)));

        return lists
            .SelectMany(list => list)
            .FirstOrDefault(series => series.SeriesId == seriesId);
    }

    /// <summary>Quantas categorias de series sao varridas quando a serie nao esta na categoria informada.</summary>
    private const int SeriesScanLimit = 6;

    public Task<XtreamSeriesInfo?> GetSeriesInfoAsync(XtreamCredential credential, string seriesId) =>
        GetAsync<XtreamSeriesInfo>(credential, "get_series_info", $"series_id={seriesId}", TimeSpan.FromMinutes(30));

    /// <summary>
    /// EPG do canal (programa atual + os proximos). O provedor responde com
    /// "epg_listings"; o titulo e a descricao vem em base64.
    /// O cache e curto (2 min) porque a lista comeca no programa atual.
    /// </summary>
    public async Task<List<XtreamEpgListing>> GetShortEpgAsync(
        XtreamCredential credential, string streamId, int limit = 2)
    {
        var response = await GetAsync<XtreamEpgResponse>(
            credential,
            "get_short_epg",
            $"stream_id={Uri.EscapeDataString(streamId)}&limit={Math.Clamp(limit, 1, 20)}",
            TimeSpan.FromMinutes(2));

        return response?.Listings ?? [];
    }

    /// <summary>
    /// Informacoes da conta e do servidor (player_api.php sem "action"). E daqui
    /// que sai a base das URLs de playback.
    /// </summary>
    public Task<XtreamAuthResponse?> GetServerInfoAsync(XtreamCredential credential) =>
        GetAsync<XtreamAuthResponse>(credential, null, null, TimeSpan.FromMinutes(30));

    /// <summary>
    /// Base das URLs de playback no padrao IPTV Xtream Codes.
    ///
    /// A base vem do "server_info" devolvido pelo player_api.php - e nao do
    /// host do painel - porque e o endereco que o proprio provedor publica
    /// para os clientes. Se o server_info nao estiver disponivel, cai no host
    /// configurado.
    /// </summary>
    public async Task<string> ResolvePlaybackBaseAsync(XtreamCredential credential)
    {
        if (!string.IsNullOrWhiteSpace(_options.PlaybackBaseUrl))
        {
            return _options.PlaybackBaseUrl!.TrimEnd('/');
        }

        if (!_options.UseServerInfo)
        {
            return credential.BaseUrl.TrimEnd('/');
        }

        var serverInfo = (await GetServerInfoAsync(credential))?.ServerInfo;

        if (serverInfo is null || string.IsNullOrWhiteSpace(serverInfo.Url))
        {
            return credential.BaseUrl.TrimEnd('/');
        }

        return BuildServerBase(serverInfo);
    }

    /// <summary>
    /// Monta "scheme://host[:port]" a partir do server_info. A porta padrao
    /// da esquema e omitida (http/80 e https/443).
    /// </summary>
    public static string BuildServerBase(XtreamServerInfo serverInfo)
    {
        var host = (serverInfo.Url ?? string.Empty).Trim().Trim('/');

        if (host.Contains("://", StringComparison.Ordinal))
        {
            var parsed = new Uri(host);
            host = parsed.Host;
        }

        var scheme = string.IsNullOrWhiteSpace(serverInfo.HttpsPort) ? "http" : "https";
        var port = scheme == "https" ? serverInfo.HttpsPort : serverInfo.Port;

        var defaultPort = scheme == "https" ? "443" : "80";
        var hasPort = !string.IsNullOrWhiteSpace(port) && port != defaultPort;

        return hasPort ? $"{scheme}://{host}:{port}" : $"{scheme}://{host}";
    }

    /// <summary>URL completa do player_api.php (usada no diagnostico e no proxy).</summary>
    public static string BuildUrl(XtreamCredential credential, string? action, string? extra = null) =>
        BuildUrl(credential.BaseUrl, credential, action, extra);

    /// <summary>URL completa do player_api.php em uma base especifica.</summary>
    public static string BuildUrl(string baseUrl, XtreamCredential credential, string? action, string? extra = null)
    {
        var url = $"{baseUrl.TrimEnd('/')}/player_api.php" +
                  $"?username={Uri.EscapeDataString(credential.Username)}" +
                  $"&password={Uri.EscapeDataString(credential.Password)}";

        if (!string.IsNullOrWhiteSpace(action))
        {
            url += $"&action={action}";
        }

        return extra is null ? url : $"{url}&{extra}";
    }

    /// <summary>
    /// Bases candidatas, na ordem de tentativa: a que funcionou por ultimo,
    /// depois a principal e as alternativas configuradas.
    /// </summary>
    public List<string> CandidateBases()
    {
        var list = new List<string>();

        if (!string.IsNullOrWhiteSpace(_workingBase))
        {
            list.Add(_workingBase);
        }

        foreach (var candidate in new[] { _options.BaseUrl }.Concat(_options.FallbackUrls))
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var trimmed = candidate.TrimEnd('/');

            if (!list.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(trimmed);
            }
        }

        if (!string.IsNullOrWhiteSpace(_options.ApiProxyUrl))
        {
            var relay = _options.ApiProxyUrl.TrimEnd('/');

            if (!list.Contains(relay, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(relay);
            }
        }

        return list;
    }

    /// <summary>
    /// Sonda cada base candidata e devolve o que cada uma respondeu. Usado pelo
    /// endpoint de diagnostico para descobrir por que o provedor falha.
    /// </summary>
    public async Task<List<XtreamProbeResult>> ProbeAsync(XtreamCredential credential)
    {
        var results = new List<XtreamProbeResult>();

        foreach (var baseUrl in CandidateBases())
        {
            var url = BuildUrl(baseUrl, credential, "get_live_categories");
            var started = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var client = _httpFactory.CreateClient("xtream");
                using var response = await client.GetAsync(url);

                var body = response.IsSuccessStatusCode
                    ? await response.Content.ReadAsStringAsync()
                    : await SafeReadAsync(response);

                results.Add(new XtreamProbeResult
                {
                    Base = baseUrl,
                    Url = Redact(url),
                    Status = (int)response.StatusCode,
                    ElapsedMs = started.ElapsedMilliseconds,
                    Sample = Sample(body)
                });
            }
            catch (Exception ex)
            {
                results.Add(new XtreamProbeResult
                {
                    Base = baseUrl,
                    Url = Redact(url),
                    Error = $"{ex.GetType().Name}: {ex.Message}",
                    ElapsedMs = started.ElapsedMilliseconds
                });
            }
        }

        return results;
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Troca a senha por "***" (a URL vai para o log/diagnostico).</summary>
    public static string Redact(string url) =>
        System.Text.RegularExpressions.Regex.Replace(url, "password=[^&]*", "password=***");

    private static string? Sample(string? body) =>
        string.IsNullOrWhiteSpace(body) ? null : body![..Math.Min(body.Length, 160)].Replace("\n", " ").Replace("\r", " ");

    /// <summary>
    /// JSON bruto do player_api.php para uma acao (cache de 30 min para o
    /// server_info, 5 min para as listas). Usado pelo endpoint /player_api.php
    /// para repassar a resposta no padrao do provedor.
    /// </summary>
    public Task<string?> GetRawAsync(
        XtreamCredential credential,
        string? action,
        string? extra = null,
        TimeSpan? ttl = null) =>
        GetRawCoreAsync(credential, action, extra, ttl ?? TimeSpan.FromMinutes(5));

    private async Task<List<T>> GetListAsync<T>(
        XtreamCredential credential,
        string action,
        string? extra,
        TimeSpan ttl)
    {
        var value = await GetAsync<List<T>>(credential, action, extra, ttl);
        return value ?? [];
    }

    private async Task<T?> GetAsync<T>(
        XtreamCredential credential,
        string? action,
        string? extra,
        TimeSpan ttl)
    {
        var raw = await GetRawCoreAsync(credential, action, extra, ttl);

        if (raw is null)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(raw, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao desserializar o Xtream Codes (action={Action}).", action ?? "(default)");
            return default;
        }
    }

    private async Task<string?> GetRawCoreAsync(
        XtreamCredential credential,
        string? action,
        string? extra,
        TimeSpan ttl)
    {
        // Tenta cada base candidata: alguns provedores bloqueiam o IP do
        // servidor (404) e so respondem por outro dominio.
        foreach (var baseUrl in CandidateBases())
        {
            var raw = await FetchAsync(baseUrl, credential, action, extra, ttl);

            if (raw is not null)
            {
                _workingBase = baseUrl;
                return raw;
            }
        }

        return null;
    }

    private async Task<string?> FetchAsync(
        string baseUrl,
        XtreamCredential credential,
        string? action,
        string? extra,
        TimeSpan ttl)
    {
        var url = BuildUrl(baseUrl, credential, action, extra);
        var cacheKey = $"xtream:{baseUrl}:{credential.Username}:{action}:{extra}";

        if (_cache.TryGetValue(cacheKey, out var cached) && cached is string raw)
        {
            return raw;
        }

        try
        {
            var client = _httpFactory.CreateClient("xtream");
            using var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Xtream Codes respondeu {Status} em {Base} (action={Action}).",
                    (int)response.StatusCode, baseUrl, action ?? "(default)");

                return null;
            }

            var body = await response.Content.ReadAsStringAsync();
            _cache.Set(cacheKey, body, ttl);
            return body;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Falha ao consultar o Xtream Codes em {Base} (action={Action}).",
                baseUrl, action ?? "(default)");

            return null;
        }
    }
}
