using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ehiptv_msx.Models;

namespace ehiptv_msx.Services;

/// <summary>
/// Endpoint "/player_api.php" no padrao IPTV Xtream Codes.
///
/// Repassa a resposta do provedor (mesmas acoes, mesmos nomes de campo) e
/// completa o "direct_source" de cada item com a URL de playback no padrao
/// Xtream Codes, montada a partir da base do "server_info".
///
/// Assim o app tambem serve como um servidor Xtream Codes padrao: qualquer
/// player de IPTV (TiviMate, IPTV Smarters, Perfect Player...) pode apontar
/// para o proprio dominio com o mesmo usuario e senha.
/// </summary>
public sealed class XtreamApiService
{
    private readonly XtreamClient _xtream;
    private readonly ILogger<XtreamApiService> _logger;

    public XtreamApiService(XtreamClient xtream, ILogger<XtreamApiService> logger)
    {
        _xtream = xtream;
        _logger = logger;
    }

    /// <summary>
    /// Executa a acao pedida no player_api.php e devolve o JSON no padrao.
    /// Devolve null quando o provedor nao responde.
    /// </summary>
    public async Task<string?> HandleAsync(XtreamCredential credential, string? action, string? extra)
    {
        var raw = await _xtream.GetRawAsync(credential, action, extra);

        if (raw is null)
        {
            return null;
        }

        JsonNode? node;

        try
        {
            node = JsonNode.Parse(raw);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "player_api.php devolveu JSON invalido.");
            return raw;
        }

        if (node is null)
        {
            return raw;
        }

        var playbackBase = await _xtream.ResolvePlaybackBaseAsync(credential);

        switch (Normalize(action))
        {
            case "":
            case "get_account_info":
                SanitizeUserInfo(node);
                break;

            case "get_live_streams":
                FillList(node, item => credential.BuildFrom(playbackBase, "live", StreamId(item), "ts"));
                break;

            case "get_vod_streams":
                FillList(node, item => credential.BuildFrom(playbackBase, "movie", StreamId(item), ContainerExtension(item)));
                break;

            case "get_vod_info":
                FillMovieData(node, item => credential.BuildFrom(playbackBase, "movie", StreamId(item), ContainerExtension(item)));
                break;

            case "get_series_info":
                FillEpisodes(node, credential, playbackBase);
                break;
        }

        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string Normalize(string? action) => (action ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Le um campo de texto do JSON. O provedor mistura string e numero
    /// (ex.: "stream_id": 379589), entao os dois sao aceitos.
    /// </summary>
    private static string? Text(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<string>(out var text))
        {
            return text;
        }

        if (value.TryGetValue<long>(out var number))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static string StreamId(JsonObject item) => Text(item["stream_id"]) ?? string.Empty;

    private static string ContainerExtension(JsonObject item)
    {
        var extension = Text(item["container_extension"]);
        return string.IsNullOrWhiteSpace(extension) ? "mp4" : extension;
    }

    /// <summary>
    /// A senha nao volta na resposta: o player ja sabe qual senha enviou e
    /// ecoar de volta so cria um risco de log.
    /// </summary>
    private static void SanitizeUserInfo(JsonNode node)
    {
        if (node is JsonObject root && root["user_info"] is JsonObject user)
        {
            user["password"] = null;
        }
    }

    /// <summary>Preenche o "direct_source" de cada item de uma lista.</summary>
    private static void FillList(JsonNode node, Func<JsonObject, string> factory)
    {
        if (node is not JsonArray array)
        {
            return;
        }

        foreach (var entry in array)
        {
            if (entry is not JsonObject item)
            {
                continue;
            }

            var existing = Text(item["direct_source"]);

            if (!string.IsNullOrWhiteSpace(existing))
            {
                continue;
            }

            item["direct_source"] = factory(item);
        }
    }

    /// <summary>Preenche o "direct_source" do "movie_data" (get_vod_info).</summary>
    private static void FillMovieData(JsonNode node, Func<JsonObject, string> factory)
    {
        if (node is not JsonObject root || root["movie_data"] is not JsonObject movie)
        {
            return;
        }

        var existing = Text(movie["direct_source"]);

        if (string.IsNullOrWhiteSpace(existing))
        {
            movie["direct_source"] = factory(movie);
        }
    }

    /// <summary>Preenche o "direct_source" de cada episodio (get_series_info).</summary>
    private static void FillEpisodes(JsonNode node, XtreamCredential credential, string playbackBase)
    {
        if (node is not JsonObject root || root["episodes"] is not JsonObject seasons)
        {
            return;
        }

        foreach (var season in seasons)
        {
            if (season.Value is not JsonArray episodes)
            {
                continue;
            }

            foreach (var entry in episodes)
            {
                if (entry is not JsonObject episode)
                {
                    continue;
                }

                var existing = Text(episode["direct_source"]);

                if (!string.IsNullOrWhiteSpace(existing))
                {
                    continue;
                }

                var id = Text(episode["id"]);

                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var extension = Text(episode["container_extension"]);
                episode["direct_source"] = credential.BuildFrom(
                    playbackBase, "series", id!, string.IsNullOrWhiteSpace(extension) ? "mp4" : extension);
            }
        }
    }
}
