using System.Net.Http.Json;
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

    public XtreamClient(
        IHttpClientFactory httpFactory,
        IMemoryCache cache,
        ILogger<XtreamClient> logger)
    {
        _httpFactory = httpFactory;
        _cache = cache;
        _logger = logger;
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

    /// <summary>URL completa do player_api.php (usada no diagnostico e no proxy).</summary>
    public static string BuildUrl(XtreamCredential credential, string action, string? extra = null)
    {
        var url = $"{credential.BaseUrl.TrimEnd('/')}/player_api.php" +
                  $"?username={Uri.EscapeDataString(credential.Username)}" +
                  $"&password={Uri.EscapeDataString(credential.Password)}" +
                  $"&action={action}";

        return extra is null ? url : $"{url}&{extra}";
    }

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
        string action,
        string? extra,
        TimeSpan ttl)
    {
        var cacheKey = $"xtream:{credential.Username}:{action}:{extra}";

        if (_cache.TryGetValue(cacheKey, out var cached) && cached is T typed)
        {
            return typed;
        }

        var url = BuildUrl(credential, action, extra);

        try
        {
            var client = _httpFactory.CreateClient("xtream");
            using var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
            _cache.Set(cacheKey, value, ttl);
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao consultar o Xtream Codes (action={Action}).", action);
            return default;
        }
    }
}
