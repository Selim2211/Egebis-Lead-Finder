using System.Diagnostics;

namespace EgebisLeadFinder.Services.Diagnostics;

/// <summary>Son isteklerin sunucu isleme sureleri (Sistem Teshisi ekrani icin, bellekte, son 500 istek).</summary>
public class RequestTimings
{
    public record Entry(DateTime At, string Method, string Path, int Status, double Ms);

    private const int Capacity = 500;
    private readonly Entry[] _items = new Entry[Capacity];
    private int _next;
    private int _count;
    private readonly object _lock = new();

    public void Add(Entry e)
    {
        lock (_lock)
        {
            _items[_next] = e;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
        }
    }

    public List<Entry> Snapshot()
    {
        lock (_lock)
        {
            var list = new List<Entry>(_count);
            for (var i = 0; i < _count; i++) list.Add(_items[(_next - _count + i + Capacity) % Capacity]);
            return list;
        }
    }
}

/// <summary>
/// Her sayfa isteginin sunucuda ne kadar surdugunu olcer: "Server-Timing" basligina yazar (tarayici
/// bunu okuyup ag suresini ayirabilir) ve RequestTimings'e kaydeder. Statik dosyalar atlanir.
/// </summary>
public class RequestTimingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RequestTimings _timings;

    public RequestTimingMiddleware(RequestDelegate next, RequestTimings timings)
    {
        _next = next;
        _timings = timings;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (StaticPaths.IsStatic(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["Server-Timing"] = $"app;dur={sw.Elapsed.TotalMilliseconds:0.0}";
            return Task.CompletedTask;
        });
        try
        {
            await _next(context);
        }
        finally
        {
            _timings.Add(new RequestTimings.Entry(DateTime.UtcNow, context.Request.Method,
                context.Request.Path.Value ?? "/", context.Response.StatusCode, sw.Elapsed.TotalMilliseconds));
        }
    }
}
