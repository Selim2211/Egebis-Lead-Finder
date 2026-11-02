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
        foreach (var e in Extensions)
            if (value.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
