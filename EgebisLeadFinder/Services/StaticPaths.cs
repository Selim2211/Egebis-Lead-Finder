namespace EgebisLeadFinder.Services;

/// <summary>wwwroot'tan sunulan dosya istekleri (oturum kontrolu ve istek logu bunlarda atlanir).</summary>
public static class StaticPaths
{
    private static readonly string[] Prefixes = { "/css", "/js", "/lib", "/img", "/_content", "/favicon" };
    private static readonly string[] Extensions = { ".css", ".js", ".png", ".jpg", ".jpeg", ".svg", ".ico", ".woff", ".woff2", ".map", ".webp", ".gif" };

    public static bool IsStatic(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value)) return false;
        foreach (var p in Prefixes)
            if (path.StartsWithSegments(p)) return true;
        // Uzanti kurali yalnizca kok dizindeki dosyalar icindedir (/favicon.ico gibi). "/Users/Index/x.js" gibi bir yolun
        // sonuna uzanti eklemek oturum denetimini atlatamasin.
        if (value.IndexOf('/', 1) >= 0) return false;
        foreach (var e in Extensions)
            if (value.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
