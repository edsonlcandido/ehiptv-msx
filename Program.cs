using System.Net;
using System.Text;
using System.Text.Json;
using ehiptv_msx.Models;
using ehiptv_msx.Services;

var builder = WebApplication.CreateBuilder(args);

// Query keys que nao sao repassadas para o player_api.php do provedor.
string[] reservedQueryKeys = ["username", "password", "u", "p", "action"];

builder.Services.AddMemoryCache();

builder.Services.AddHttpClient("xtream", client =>
{
    client.Timeout = TimeSpan.FromSeconds(90);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; ehiptv-msx/1.0)");
});

builder.Services.Configure<XtreamOptions>(builder.Configuration.GetSection("Xtream"));
builder.Services.AddSingleton<CredentialResolver>();
builder.Services.AddSingleton<XtreamClient>();
builder.Services.AddSingleton<XtreamApiService>();
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
              .WithMethods("GET", "OPTIONS");
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

// Canais: hub de categorias.
app.MapGet("/msx/content/live.json", async (HttpContext context, CredentialResolver resolver,
    MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var content = await factory.BuildLiveHubAsync(context, credential);
    return Results.Json(content, MsxJson.Options);
});

// Canais: lista de canais da categoria.
app.MapGet("/msx/content/live/{categoryId}.json", async (HttpContext context, string categoryId,
    CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var content = await factory.BuildLiveCategoryAsync(context, credential, categoryId);
    return Results.Json(content, MsxJson.Options);
});

// Filmes: hub (categoria padrao) e categoria especifica.
app.MapGet("/msx/content/vod.json", async (HttpContext context, CredentialResolver resolver,
    MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var content = await factory.BuildVodHubAsync(context, credential);
    return Results.Json(content, MsxJson.Options);
});

app.MapGet("/msx/content/vod/{categoryId}.json", async (HttpContext context, string categoryId,
    CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var content = await factory.BuildVodCategoryAsync(context, credential, categoryId);
    return Results.Json(content, MsxJson.Options);
});

// Series: hub, categoria e episodios.
app.MapGet("/msx/content/series.json", async (HttpContext context, CredentialResolver resolver,
    MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var content = await factory.BuildSeriesHubAsync(context, credential);
    return Results.Json(content, MsxJson.Options);
});

app.MapGet("/msx/content/series/{categoryId}.json", async (HttpContext context, string categoryId,
    CredentialResolver resolver, MsxContentFactory factory) =>
{
    var credential = resolver.Resolve(context);
    var content = await factory.BuildSeriesCategoryAsync(context, credential, categoryId);
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

    foreach (var url in new[] { "http://example.com", "https://www.google.com", "http://<provedor>/" })
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

static string HelpUrl(HttpContext context) =>
    $"{CredentialResolver.PublicScheme(context)}://{CredentialResolver.PublicHost(context)}/msx/ajuda.json";

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
