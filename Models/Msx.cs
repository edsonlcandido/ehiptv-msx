using System.Text.Json;
using System.Text.Json.Serialization;

namespace ehiptv_msx.Models;

/// <summary>Raiz do conteudo MSX (endpoint "/").</summary>
public sealed class MsxContent
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("parameter")]
    public string Parameter { get; set; } = string.Empty;
}

/// <summary>Documento de menu MSX (endpoint "/msx/menu.json").</summary>
public sealed class MsxMenu
{
    [JsonPropertyName("headline")]
    public string Headline { get; set; } = string.Empty;

    [JsonPropertyName("menu")]
    public List<MsxMenuItem> Menu { get; set; } = [];
}

/// <summary>Um item do menu (filmes, audios, imagens...).</summary>
public sealed class MsxMenuItem
{
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public MsxData? Data { get; set; }
}

/// <summary>Conteudo de um item do menu.</summary>
public sealed class MsxData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "pages";

    [JsonPropertyName("template")]
    public MsxTemplate? Template { get; set; }

    [JsonPropertyName("items")]
    public List<MsxContentItem> Items { get; set; } = [];
}

/// <summary>Template de exibicao usado pelo MSX.</summary>
public sealed class MsxTemplate
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "default";

    [JsonPropertyName("layout")]
    public string? Layout { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("imageFiller")]
    public string? ImageFiller { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }
}

/// <summary>Item de conteudo (video, audio, imagem...).</summary>
public sealed class MsxContentItem
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }
}

public static class MsxJson
{
    /// <summary>Opcoes de serializacao do MSX: camelCase off (usa JsonPropertyName) e ignora nulls.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}
