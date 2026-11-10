using System.Net;
using System.Text;
using System.Text.Json;
using ehiptv_msx.Models;
using ehiptv_msx.Services;

var builder = WebApplication.CreateBuilder(args);

// Query keys que nao sao repassadas para o player_api.php do provedor.
string[] reservedQueryKeys = ["username", "password", "u", "p", "action"];

// Esquema das URLs geradas. O padrao e "http": o provedor so serve HTTP e
// servir o app em HTTPS cai no bloqueio de mixed content do navegador.
string? publicScheme = builder.Configuration["Xtream:PublicScheme"];

builder.Services.AddMemoryCache();

builder.Services.AddHttpClient("xtream", client =>
{
    client.Timeout = TimeSpan.FromSeconds(90);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; ehiptv-msx/1.0)");
});

builder.Services.Configure<XtreamOptions>(builder.Configuration.GetSection("Xtream"));

// As bases do provedor e as credenciais ficam FORA do repositorio:
//   - variaveis de ambiente (Docker/Easypanel): BASE_URL, FALLBACK_URL,
//     PROXY_URL (e o padrao Xtream__Username / Xtream__Password);
//   - arquivo local de secrets: %USERPROFILE%\.minimax\secrets\ehiptv-msx.json
builder.Services.PostConfigure<XtreamOptions>(options =>
{
    var envBase = Environment.GetEnvironmentVariable("BASE_URL");
    if (!string.IsNullOrWhiteSpace(envBase))
    {
        options.BaseUrl = envBase.Trim();
    }

    var envFallback = Environment.GetEnvironmentVariable("FALLBACK_URL");
    if (!string.IsNullOrWhiteSpace(envFallback))
    {
        options.FallbackUrls = envFallback
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    var envProxy = Environment.GetEnvironmentVariable("PROXY_URL");
    if (!string.IsNullOrWhiteSpace(envProxy))
    {
        options.ApiProxyUrl = envProxy.Trim();
    }

    var secrets = SecretsLoader.TryLoad();

    if (secrets is not null)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            options.BaseUrl = secrets.BaseUrl;
        }

        if (options.FallbackUrls.Length == 0)
        {
            options.FallbackUrls = secrets.FallbackUrls;
        }

        if (string.IsNullOrWhiteSpace(options.Username))
        {
            options.Username = secrets.Username;
        }

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            options.Password = secrets.Password;
        }
    }
});
builder.Services.AddSingleton<CredentialResolver>();
builder.Services.AddSingleton<XtreamClient>();
builder.Services.AddSingleton<XtreamApiService>();
builder.Services.AddSingleton<EpgService>();
builder.Services.AddSingleton<MsxContentFactory>();

// CORS: o MSX busca o start.json/menu.json com withCredentials=true.
// Em requisicao com credencial o wildcard "*" e invalido (o navegador bloqueia
// a resposta e o XHR falha com "Server responded with status: 0"), por isso a
// origem tem de ser ecoada de volta, com Access-Control-Allow-Credentials.
builder.Services.AddCors(options =>
{
    options.AddPolicy("MsxClients", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowCredentials()
              .WithHeaders("Origin", "Content-Type", "Accept")
              .WithMethods("GET", "POST", "OPTIONS");
    });
});

var app = builder.Build();

app.UseCors("MsxClients");

// ---------------------------------------------------------------------------
// Rotas
// ---------------------------------------------------------------------------

// A credencial fica no PRIMEIRO TRECHO do host:
//   http://<usuario>-<senha>.dominio/msx/start.json
// Nao existe mais sub-caminho com usuario/senha.
app.MapGet("/", () => Results.Redirect("/msx/start.json"));

app.MapGet("/health", () => Results.Json(new { ok = true }));

// ---------------------------------------------------------------------------
// player_api.php (padrao IPTV Xtream Codes)
// ---------------------------------------------------------------------------

// O proprio app serve a API do Xtream Codes: qualquer player de IPTV pode
// apontar para este dominio com o mesmo usuario e senha. A resposta e a do
// provedor, com o "direct_source" completo no padrao Xtream Codes.
//
//   /player_api.php?username=<usuario>&password=<senha>
//   /player_api.php?username=<usuario>&password=<senha>&action=get_live_streams
//   /player_api.php?username=<usuario>&password=<senha>&action=get_vod_streams&category_id=25
//   /player_api.php?username=<usuario>&password=<senha>&action=get_series&category_id=3178
//   /player_api.php?username=<usuario>&password=<senha>&action=get_series_info&series_id=123
app.MapGet("/player_api.php", async (HttpContext context, CredentialResolver resolver, XtreamApiService api) =>
{
    var credential = resolver.Resolve(context);

    if (!credential.HasCredentials)
    {
        return Results.Json(
            new { user_info = new { auth = 0, status = "Unauthorized", message = "Informe usuario e senha." } },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    var action = context.Request.Query["action"].FirstOrDefault();

    // Tudo que nao for credencial ou action vai para o provedor
    // (category_id, series_id, vod_id, stream_id, limit...).
    var forwarded = context.Request.Query
        .Where(q => !reservedQueryKeys.Contains(q.Key))
        .SelectMany(q => q.Value
            .Where(v => v is not null)
            .Select(v => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(v!)}"))
        .ToArray();

    var json = await api.HandleAsync(
        credential, action, forwarded.Length == 0 ? null : string.Join("&", forwarded));

    if (json is null)
    {
        return Results.Json(
            new { error = "O provedor Xtream Codes nao respondeu." },
            statusCode: StatusCodes.Status502BadGateway);
    }

    return Results.Content(json, "application/json; charset=utf-8");
});

// ---------------------------------------------------------------------------
// Telas do MSX
// ---------------------------------------------------------------------------

// Start Object: MSX sempre busca em {SERVER}/msx/start.json.
// O "parameter" aponta para a tela inicial (content:), e nao para o menu (menu:),
// por isso o app NAO mostra a barra lateral esquerda.
app.MapGet("/msx/start.json", (HttpContext context, CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);

    if (!credential.HasCredentials)
    {
        return Results.Json(new MsxStart
        {
            Name = "Eh! IPTV",
            Version = "1.0.0",
            Parameter = $"content:{HelpUrl(context)}"
        }, MsxJson.Options);
    }

    return Results.Json(factory.BuildStart(context, credential), MsxJson.Options);
});

// Menu Root (barra lateral) -kept para uso opcional/aviso.
app.MapGet("/msx/menu.json", async (HttpContext context, CredentialResolver resolver,
    MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var menu = await factory.BuildMenuAsync(context, credential);
    return Results.Json(menu, MsxJson.Options);
});

// Ajuda: como montar a URL com usuario e senha.
app.MapGet("/msx/ajuda.json", (HttpContext context) =>
    Results.Json(BuildHelpContent(context), MsxJson.Options));

// Tela 1 - Inicio (os 3 itens: TV, Filmes, Series).
app.MapGet("/msx/content/home.json", (HttpContext context, CredentialResolver resolver,
    MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    return Results.Json(factory.BuildHome(context, credential), MsxJson.Options);
});

// Canais: menu com as categorias de TV (coluna nativa do app, que rola e
// mostra todas). Cada item aponta para a lista de canais da categoria.
app.MapGet("/msx/menu/live.json", async (HttpContext context, CredentialResolver resolver,
    MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var menu = await factory.BuildLiveMenuAsync(context, credential);
    return Results.Json(menu, MsxJson.Options);
});

// Canais: lista de canais da categoria (estilo "Channel List" do MSX, com o
// programa no ar que vem do servico de live data).
app.MapGet("/msx/content/live.json", async (HttpContext context, CredentialResolver resolver,
    MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var content = await factory.BuildLiveChannelListAsync(context, credential);
    return Results.Json(content, MsxJson.Options);
});

app.MapGet("/msx/content/live/{categoryId}.json", async (HttpContext context, string categoryId,
    CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var content = await factory.BuildLiveChannelListAsync(context, credential, categoryId);
    return Results.Json(content, MsxJson.Options);
});

// Servico de live data (EPG) do MSX: o app chama este endereco quando um
// canal fica visivel e recebe o programa atual (horario e barra de
// progresso) no formato "update:content:{ID}".
//
// O MSX chama o servico com POST, mantendo a query string
// (?stream_id=...) e mandando {"data":null} no corpo. O "stream_id" e lido da
// query, do form e do JSON, para funcionar em qualquer variante.
app.MapMethods("/msx/live/epg", ["GET", "POST"], async (HttpContext context, CredentialResolver resolver,
    EpgService epg, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var streamId = await ReadParameterAsync(context, "stream_id");

    if (string.IsNullOrWhiteSpace(streamId))
    {
        return Results.Json(new MsxLiveResponse
        {
            Response = new MsxResponseEnvelope
            {
                Status = 400,
                Text = "Bad Request",
                Message = "stream_id obrigatorio."
            }
        }, MsxJson.Options);
    }

    var response = await epg.BuildResponseAsync(
        credential, streamId, $"ch_{streamId}", factory.EpgServiceUrl(context, streamId));

    return Results.Json(response, MsxJson.Options);
});

// Filmes: menu com as categorias (coluna nativa do app) + grade de
// cartazes paginada pelo Paging Plugin do MSX.
app.MapGet("/msx/menu/vod.json", async (HttpContext context, CredentialResolver resolver,
    MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var menu = await factory.BuildVodMenuAsync(context, credential);
    return Results.Json(menu, MsxJson.Options);
});

// Filmes: cartazes da categoria (link direto, sem paginacao).
app.MapGet("/msx/content/vod.json", async (HttpContext context, CredentialResolver resolver,
    MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var content = await factory.BuildVodContentAsync(context, credential);
    return Results.Json(content, MsxJson.Options);
});

app.MapGet("/msx/content/vod/{categoryId}.json", async (HttpContext context, string categoryId,
    CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var content = await factory.BuildVodContentAsync(context, credential, categoryId);
    return Results.Json(content, MsxJson.Options);
});

// Content service do Paging Plugin: devolve uma pagina de cartazes no
// envelope do MSX ({"response":{"status":200,...,"data":{...}}}).
app.MapGet("/msx/service/vod/{categoryId}.json", async (HttpContext context, string categoryId,
    CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var (offset, limit) = ReadPaging(context);
    var content = await factory.BuildVodContentAsync(context, credential, categoryId, offset, limit);

    return Results.Json(new MsxServiceResponse
    {
        Response = new MsxResponseEnvelope { Status = 200, Text = "OK", Data = content }
    }, MsxJson.Options);
});

// Lista completa de categorias de filmes.
app.MapGet("/msx/content/vod-categories.json", async (HttpContext context, CredentialResolver resolver,
    MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var content = await factory.BuildVodCategoriesAsync(context, credential);
    return Results.Json(content, MsxJson.Options);
});

// Series: menu com as categorias (coluna nativa do app).
app.MapGet("/msx/menu/series.json", async (HttpContext context, CredentialResolver resolver,
    MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var menu = await factory.BuildSeriesMenuAsync(context, credential);
    return Results.Json(menu, MsxJson.Options);
});

// Series: cartazes da categoria (link direto, sem paginacao).
app.MapGet("/msx/content/series.json", async (HttpContext context, CredentialResolver resolver,
    MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var content = await factory.BuildSeriesContentAsync(context, credential);
    return Results.Json(content, MsxJson.Options);
});

app.MapGet("/msx/content/series/{categoryId}.json", async (HttpContext context, string categoryId,
    CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var content = await factory.BuildSeriesContentAsync(context, credential, categoryId);
    return Results.Json(content, MsxJson.Options);
});

// Content service do Paging Plugin (series).
app.MapGet("/msx/service/series/{categoryId}.json", async (HttpContext context, string categoryId,
    CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var (offset, limit) = ReadPaging(context);
    var content = await factory.BuildSeriesContentAsync(context, credential, categoryId, offset, limit);

    return Results.Json(new MsxServiceResponse
    {
        Response = new MsxResponseEnvelope { Status = 200, Text = "OK", Data = content }
    }, MsxJson.Options);
});

// Lista completa de categorias de series (destino do "Mais categorias").
app.MapGet("/msx/content/series-categories.json", async (HttpContext context, CredentialResolver resolver,
    MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var content = await factory.BuildSeriesCategoriesAsync(context, credential);
    return Results.Json(content, MsxJson.Options);
});

app.MapGet("/msx/content/series-info/{seriesId}.json", async (HttpContext context, string seriesId,
    CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var content = await factory.BuildSeriesInfoAsync(context, credential, seriesId);
    return Results.Json(content, MsxJson.Options);
});

// ---------------------------------------------------------------------------
// Diagnostico: por que o provedor nao responde a partir deste servidor?
// ---------------------------------------------------------------------------

app.MapGet("/msx/diagnostico.json", async (HttpContext context, CredentialResolver resolver,
    XtreamClient xtream) =>
{
    var credential = resolver.Resolve(context);
    var probes = await xtream.ProbeAsync(credential);

    var dns = new Dictionary<string, object?>();

    foreach (var host in xtream.CandidateBases()
        .Select(b => new Uri(b.Contains("://") ? b : "http://" + b).Host)
        .Distinct())
    {
        try
        {
            var addresses = await System.Net.Dns.GetHostAddressesAsync(host);
            dns[host] = addresses.Select(a => a.ToString()).ToArray();
        }
        catch (Exception ex)
        {
            dns[host] = $"DNS falhou: {ex.Message}";
        }
    }

    // O app sai para a internet? Se nem um site publico responde, o problema
    // e a saida do servidor (nao o provedor).
    var outbound = new List<object>();

    var outboundUrls = new List<string> { "http://example.com", "https://www.google.com" };
    outboundUrls.AddRange(xtream.CandidateBases().Select(baseUrl => baseUrl.TrimEnd('/') + "/"));

    foreach (var url in outboundUrls)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

            using var response = await http.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();

            outbound.Add(new
            {
                url,
                status = (int)response.StatusCode,
                elapsedMs = started.ElapsedMilliseconds,
                sample = body.Length > 120 ? body[..120].Replace("\n", " ") : body
            });
        }
        catch (Exception ex)
        {
            outbound.Add(new
            {
                url,
                error = $"{ex.GetType().Name}: {ex.Message}",
                elapsedMs = started.ElapsedMilliseconds
            });
        }
    }

    return Results.Json(new
    {
        request = new
        {
            host = context.Request.Host.Value,
            forwardedHost = context.Request.Headers["X-Forwarded-Host"].FirstOrDefault(),
            forwardedProto = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault(),
            scheme = context.Request.Scheme,
            publicHost = CredentialResolver.PublicHost(context),
            publicScheme = CredentialResolver.PublicScheme(context),
            credentialSource = CredentialResolver.CredentialPrefix(CredentialResolver.PublicHost(context)) is not null
                ? "host"
                : (context.Request.Query["u"].FirstOrDefault() is not null ? "query" : "secrets"),
            hasCredentials = credential.HasCredentials
        },
        candidates = xtream.CandidateBases(),
        dns,
        outbound,
        probes
    });
});

// ---------------------------------------------------------------------------
// Preview no site do MSX (base64, sem servidor)
// ---------------------------------------------------------------------------

// Menu compacto em JSON (util para gerar o link base64 na mao).
app.MapGet("/msx/preview.json", async (HttpContext context, CredentialResolver resolver,
    MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var menu = await factory.BuildPreviewMenuAsync(context, credential);
    return Results.Json(menu, MsxJson.Options);
});

// Pagina de preview: abre o msx.benzac.de com o JSON embutido em base64.
app.MapGet("/msx/preview", async (HttpContext context, CredentialResolver resolver,
    MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var menu = await factory.BuildPreviewMenuAsync(context, credential);
    var json = JsonSerializer.Serialize(menu, MsxJson.Options);
    var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    var testUrl = "https://msx.benzac.de/?start=menu:json:" + Uri.EscapeDataString(base64);

    var html = $$"""
<!doctype html>
<html lang="pt-BR">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Eh! IPTV - Preview MSX</title>
<style>
  :root { color-scheme: dark; }
  body { margin:0; background:#111; color:#eee; font:14px/1.4 system-ui, sans-serif; }
  header { display:flex; gap:16px; align-items:center; padding:10px 16px; background:#1c1c1c; border-bottom:1px solid #2e2e2e; }
  h1 { font-size:15px; margin:0; font-weight:600; }
  a { color:#7cc4ff; }
  code { color:#9aa; }
  iframe { width:100%; height:calc(100vh - 46px); border:0; display:block; }
</style>
</head>
<body>
<header>
  <h1>Eh! IPTV &mdash; preview do Media Station X</h1>
  <span>payload: <code>{{json.Length:N0}} bytes JSON / {{base64.Length:N0}} base64</code></span>
  <a href="{{WebUtility.HtmlEncode(testUrl)}}" target="_blank" rel="noopener">abrir em nova aba</a>
</header>
<iframe src="{{WebUtility.HtmlEncode(testUrl)}}" allow="autoplay; fullscreen"></iframe>
</body>
</html>
""";

    return Results.Content(html, "text/html; charset=utf-8");
});

app.Run();

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

string HelpUrl(HttpContext context) =>
    $"{CredentialResolver.PublicScheme(context, publicScheme)}://{CredentialResolver.PublicHost(context)}/msx/ajuda.json";

/// <summary>
/// Le um parametro enviado pelo MSX. O app chama o servico de live data com
/// POST, mantendo a query string e levando os parametros no corpo — entao a
/// busca e feita na query, no form e no JSON, nesta ordem.
/// </summary>
static async Task<string?> ReadParameterAsync(HttpContext context, string name)
{
    var value = context.Request.Query[name].FirstOrDefault();

    if (!string.IsNullOrWhiteSpace(value))
    {
        return value;
    }

    if (context.Request.HasFormContentType)
    {
        var form = await context.Request.ReadFormAsync();
        value = form[name].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    if (context.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in root.EnumerateObject())
                    {
                        if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                            property.Value.ValueKind == JsonValueKind.String)
                        {
                            return property.Value.GetString();
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // corpo nao e JSON: segue sem o parametro
            }
        }
    }

    return null;
}

/// <summary>
/// Le "offset" e "limit" da query do content service (o Paging Plugin do MSX
/// pede a proxima pagina de cartazes assim). O limit tem teto para nao
/// estourar o tamanho da resposta.
/// </summary>
static (int Offset, int Limit) ReadPaging(HttpContext context, int defaultLimit = 16)
{
    var offset = int.TryParse(context.Request.Query["offset"], out var parsedOffset)
        ? Math.Max(0, parsedOffset)
        : 0;

    var limit = int.TryParse(context.Request.Query["limit"], out var parsedLimit)
        ? Math.Clamp(parsedLimit, 1, 100)
        : defaultLimit;

    return (offset, limit);
}

static MsxContent BuildHelpContent(HttpContext context) => new()
{
    Type = "list",
    Headline = "Como usar",
    Items =
    [
        new MsxContentItem
        {
            Title = "Usuario e senha no primeiro trecho do host",
            Text = $"http://<usuario>-<senha>.{CredentialResolver.PublicHost(context).Split('.', 2).Skip(1).FirstOrDefault() ?? "localhost"}/msx/start.json"
        },
        new MsxContentItem
        {
            Title = "Usuario e senha na query",
            Text = $"http://{CredentialResolver.PublicHost(context)}/msx/start.json?u=<usuario>&p=<senha>"
        },
        new MsxContentItem
        {
            Title = "No app MSX",
            Text = "Settings > Start Parameter > Setup > informe o dominio do servidor"
        }
    ]
};
