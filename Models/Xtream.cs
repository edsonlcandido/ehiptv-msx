using System.Text.Json.Serialization;

namespace ehiptv_msx.Models;

/// <summary>Categoria do Xtream Codes (live, vod ou series).</summary>
public sealed class XtreamCategory
{
    [JsonPropertyName("category_id")]
    public string CategoryId { get; set; } = string.Empty;

    [JsonPropertyName("category_name")]
    public string CategoryName { get; set; } = string.Empty;

    [JsonPropertyName("parent_id")]
    public string ParentId { get; set; } = "0";
}

/// <summary>Canal de TV ao vivo.</summary>
public sealed class XtreamLiveStream
{
    [JsonPropertyName("num")]
    public int Num { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("stream_type")]
    public string StreamType { get; set; } = string.Empty;

    [JsonPropertyName("stream_id")]
    public string StreamId { get; set; } = string.Empty;

    [JsonPropertyName("stream_icon")]
    public string? StreamIcon { get; set; }

    [JsonPropertyName("epg_channel_id")]
    public string? EpgChannelId { get; set; }

    [JsonPropertyName("category_id")]
    public string CategoryId { get; set; } = string.Empty;

    [JsonPropertyName("custom_sid")]
    public string? CustomSid { get; set; }

    [JsonPropertyName("direct_source")]
    public string? DirectSource { get; set; }
}

/// <summary>Filme (VOD).</summary>
public sealed class XtreamVodStream
{
    [JsonPropertyName("num")]
    public int Num { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("stream_type")]
    public string StreamType { get; set; } = string.Empty;

    [JsonPropertyName("stream_id")]
    public string StreamId { get; set; } = string.Empty;

    [JsonPropertyName("stream_icon")]
    public string? StreamIcon { get; set; }

    [JsonPropertyName("rating")]
    public string? Rating { get; set; }

    [JsonPropertyName("category_id")]
    public string CategoryId { get; set; } = string.Empty;

    [JsonPropertyName("container_extension")]
    public string? ContainerExtension { get; set; }

    [JsonPropertyName("custom_sid")]
    public string? CustomSid { get; set; }

    [JsonPropertyName("direct_source")]
    public string? DirectSource { get; set; }
}

/// <summary>Serie.</summary>
public sealed class XtreamSeries
{
    [JsonPropertyName("num")]
    public int Num { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("stream_type")]
    public string StreamType { get; set; } = string.Empty;

    [JsonPropertyName("series_id")]
    public string SeriesId { get; set; } = string.Empty;

    [JsonPropertyName("cover")]
    public string? Cover { get; set; }

    [JsonPropertyName("plot")]
    public string? Plot { get; set; }

    [JsonPropertyName("cast")]
    public string? Cast { get; set; }

    [JsonPropertyName("director")]
    public string? Director { get; set; }

    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("rating")]
    public string? Rating { get; set; }

    [JsonPropertyName("category_id")]
    public string CategoryId { get; set; } = string.Empty;

    [JsonPropertyName("youtube_trailer")]
    public string? YoutubeTrailer { get; set; }
}

/// <summary>Detalhe de uma serie: temporadas e episodios.</summary>
public sealed class XtreamSeriesInfo
{
    [JsonPropertyName("seasons")]
    public List<XtreamSeason>? Seasons { get; set; }

    [JsonPropertyName("episodes")]
    public Dictionary<string, List<XtreamEpisode>>? Episodes { get; set; }
}

public sealed class XtreamSeason
{
    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("episode_count")]
    public int EpisodeCount { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class XtreamEpisode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("episode_num")]
    public string? EpisodeNum { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("season")]
    public int Season { get; set; }

    [JsonPropertyName("container_extension")]
    public string? ContainerExtension { get; set; }

    [JsonPropertyName("custom_sid")]
    public string? CustomSid { get; set; }

    [JsonPropertyName("direct_source")]
    public string? DirectSource { get; set; }

    [JsonPropertyName("info")]
    public XtreamEpisodeInfo? Info { get; set; }
}

public sealed class XtreamEpisodeInfo
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("plot")]
    public string? Plot { get; set; }

    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    [JsonPropertyName("duration_secs")]
    public int? DurationSecs { get; set; }

    [JsonPropertyName("movie_image")]
    public string? MovieImage { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }
}
