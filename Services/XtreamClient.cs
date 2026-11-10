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
        GetListAsync<XtreamCategory>(credential, $"get_{kind}_categories", null, TimeSpan.FromMinutes(30));

    public Task<List<XtreamLiveStream>> GetLiveAsync(XtreamCredential credential, string categoryId) =>
        GetListAsync<XtreamLiveStream>(credential, "get_live_streams", $"category_id={categoryId}", TimeSpan.FromMinutes(5));

    public Task<List<XtreamVodStream>> GetVodAsync(XtreamCredential credential, string categoryId) =>
        GetListAsync<XtreamVodStream>(credential, "get_vod_streams", $"category_id={categoryId}", TimeSpan.FromMinutes(5));

    public Task<List<XtreamSeries>> GetSeriesAsync(XtreamCredential credential, string categoryId) =>
        GetListAsync<XtreamSeries>(credential, "get_series", $"category_id={categoryId}", TimeSpan.FromMinutes(5));

    public Task<XtreamSeriesInfo?> GetSeriesInfoAsync(XtreamCredential credential, string seriesId) =>
        GetAsync<XtreamSeriesInfo>(credential, "get_series_info", $"series_id={seriesId}", TimeSpan.FromMinutes(30));

    /// <summary>
    /// Informacoes da conta e do servidor (player_api.php sem "action"). E daqui
    /// que sai a base das URLs de playback.
    /// </summary>
    public Task<XtreamAuthResponse?> GetServerInfoAsync(XtreamCredential credential) =>
        GetAsync<XtreamAuthResponse>(credential, null, null, TimeSpan.FromMinutes(30));

    /// <summary>
    /// Base das URLs de playback no padrao IPTV Xtream Codes.
    ///
    /// A base vem do "server_info" devolvido pelo player_api.php
    /// (ex.: http://<provedor-alternativo>) - e nao do host do painel - porque e o
    /// endereco que o proprio provedor publica para os clientes. Se o
    /// server_info nao estiver disponivel, cai no host configurado.
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
    public static string BuildUrl(XtreamCredential credential, string? action, string? extra = null)
    {
        var url = $"{credential.BaseUrl.TrimEnd('/')}/player_api.php" +
                  $"?username={Uri.EscapeDataString(credential.Username)}" +
                  $"&password={Uri.EscapeDataString(credential.Password)}";

        if (!string.IsNullOrWhiteSpace(action))
        {
            url += $"&action={action}";
        }

        return extra is null ? url : $"{url}&{extra}";
    }

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
        var cacheKey = $"xtream:{credential.Username}:{action}:{extra}";

        if (_cache.TryGetValue(cacheKey, out var cached) && cached is string raw)
        {
            return raw;
        }

        var url = BuildUrl(credential, action, extra);

        try
        {
            var client = _httpFactory.CreateClient("xtream");
            using var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            _cache.Set(cacheKey, body, ttl);
            return body;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao consultar o Xtream Codes (action={Action}).", action ?? "(default)");
            return null;
        }
    }
}
