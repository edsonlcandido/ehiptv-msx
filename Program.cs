using ehiptv_msx.Models;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Raiz: aponta para o menu usando o host real da requisicao.
app.MapGet("/", (HttpContext context) => Results.Json(new MsxContent
{
    Name = "My Example Content",
    Version = "1.0.0",
    Parameter = $"menu:{context.Request.Scheme}://{context.Request.Host}/msx/menu.json"
}, MsxJson.Options));

app.MapGet("/msx/menu.json", () => Results.Json(BuildMenu(), MsxJson.Options));
app.MapGet("/msx/start.json", () => Results.Json(BuildMenu(), MsxJson.Options));

app.Run();

static MsxMenu BuildMenu() => new()
{
    Headline = "My Example Content",
    Menu =
    [
        new MsxMenuItem
        {
            Icon = "movie",
            Label = "My Example Movies",
            Data = new MsxData
            {
                Type = "pages",
                Template = new MsxTemplate
                {
                    Type = "separate",
                    Layout = "0,0,2,4",
                    Icon = "msx-white-soft:movie",
                    Color = "msx-glass"
                },
                Items =
                [
                    new MsxContentItem { Title = "Video 1", Action = "video:http://msx.benzac.de/media/video1.mp4" },
                    new MsxContentItem { Title = "Video 2", Action = "video:http://msx.benzac.de/media/video2.mp4" },
                    new MsxContentItem { Title = "Video 3", Action = "video:http://msx.benzac.de/media/video3.mp4" }
                ]
            }
        },
        new MsxMenuItem
        {
            Icon = "music-note",
            Label = "My Example Audios",
            Data = new MsxData
            {
                Type = "pages",
                Template = new MsxTemplate
                {
                    Type = "separate",
                    Layout = "0,0,2,3",
                    Icon = "msx-white-soft:music-note",
                    Color = "msx-glass"
                },
                Items =
                [
                    new MsxContentItem { Title = "Audio 1", Action = "audio:http://msx.benzac.de/media/audio1.mp3" },
                    new MsxContentItem { Title = "Audio 2", Action = "audio:http://msx.benzac.de/media/audio2.mp3" },
                    new MsxContentItem { Title = "Audio 3", Action = "audio:http://msx.benzac.de/media/audio3.mp3" }
                ]
            }
        },
        new MsxMenuItem
        {
            Icon = "image",
            Label = "My Example Images",
            Data = new MsxData
            {
                Type = "pages",
                Template = new MsxTemplate
                {
                    Type = "default",
                    Layout = "0,0,3,2",
                    Color = "msx-glass",
                    ImageFiller = "cover",
                    Action = "image:context"
                },
                Items =
                [
                    new MsxContentItem { Image = "http://msx.benzac.de/img/bg1.jpg" },
                    new MsxContentItem { Image = "http://msx.benzac.de/img/bg2.jpg" },
                    new MsxContentItem { Image = "http://msx.benzac.de/img/bg3.jpg" }
                ]
            }
        }
    ]
};
