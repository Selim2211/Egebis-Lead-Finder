using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.FileProviders;

namespace EgebisLeadFinder.Localization;

/// <summary>
/// Istegin dilini cookie'den belirler (varsayilan Turkce). Ingilizce secildiginde uretilen HTML/JSON ve /js dosyalari
/// cevrilir. Turkce'de hicbir ek is yapmaz.
/// </summary>
public class LocalizationMiddleware
{
    private static readonly CultureInfo EnglishUi = new("en");
    private static readonly CultureInfo EnglishFormat = new("en-GB");
    private static readonly CultureInfo TurkishUi = new("tr-TR");
    private static readonly ConcurrentDictionary<string, (DateTimeOffset Stamp, string Body)> JsCache = new();

    private readonly RequestDelegate _next;
    private readonly IFileProvider _webRoot;

    public LocalizationMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        _next = next;
        _webRoot = env.WebRootFileProvider;
    }

    public static string ResolveLanguage(HttpRequest request) =>
        Loc.NormalizeCode(request.Cookies.TryGetValue(Loc.CookieName, out var value) ? value : null);

    public async Task InvokeAsync(HttpContext context)
    {
        var english = ResolveLanguage(context.Request) == Loc.English;
        CultureInfo.CurrentUICulture = english ? EnglishUi : TurkishUi;
        if (english) CultureInfo.CurrentCulture = EnglishFormat; // tarih / sayi bicimi
        if (!english)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path;
        if (path.Value is { } p && p.StartsWith("/js/", StringComparison.OrdinalIgnoreCase)
            && p.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            && HttpMethods.IsGet(context.Request.Method))
        {
            if (await TryServeScriptAsync(context, p)) return;
        }

        if (IsAsset(path)) { await _next(context); return; }

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context);
        }
        finally
        {
            context.Response.Body = original;
        }

        var type = context.Response.ContentType ?? string.Empty;
        var isHtml = type.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
        var isJson = type.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);
        if ((isHtml || isJson) && buffer.Length > 0 && !context.Response.Headers.ContainsKey("Content-Encoding"))
        {
            var text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            text = isHtml ? MarkupTranslator.TranslateHtml(MarkupTranslator.TagLangSuffix(text)) : MarkupTranslator.TranslateJson(text);
            var bytes = Encoding.UTF8.GetBytes(text);
            context.Response.ContentLength = bytes.Length;
            await original.WriteAsync(bytes);
            return;
        }

        buffer.Position = 0;
        if (buffer.Length > 0) await buffer.CopyToAsync(original);
    }

    private static bool IsAsset(PathString path) =>
        path.StartsWithSegments("/css") || path.StartsWithSegments("/lib") || path.StartsWithSegments("/img")
        || path.StartsWithSegments("/health") || path.StartsWithSegments("/js");

    private async Task<bool> TryServeScriptAsync(HttpContext context, string path)
    {
        var file = _webRoot.GetFileInfo(path);
        if (!file.Exists) return false;

        var cached = JsCache.GetValueOrDefault(path);
        if (cached.Body is null || cached.Stamp != file.LastModified)
        {
            string source;
            await using (var stream = file.CreateReadStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                source = await reader.ReadToEndAsync(context.RequestAborted);
            cached = (file.LastModified, MarkupTranslator.TranslateJs(source));
            JsCache[path] = cached;
        }

        context.Response.ContentType = "text/javascript; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache";
        await context.Response.WriteAsync(cached.Body, context.RequestAborted);
        return true;
    }
}
