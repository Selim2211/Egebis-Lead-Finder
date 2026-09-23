using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace EgebisLeadFinder.Data;

/// <summary>
/// [MaxLength] olan string alanlari sinira gore kirpar. Web sitesi/AI'dan gelen tek
/// bir uzun deger (ör. 250 karakterlik bir sektor metni) toplu kayitta butun
/// firmalari dusurmesin diye kayittan once cagrilir.
/// </summary>
public static class StringLengthGuard
{
    private static readonly ConcurrentDictionary<Type, (PropertyInfo Property, int Max)[]> Cache = new();

    public static void Apply(object entity)
    {
        foreach (var (property, max) in Cache.GetOrAdd(entity.GetType(), Discover))
        {
            if (property.GetValue(entity) is string value && value.Length > max)
                property.SetValue(entity, value[..max].TrimEnd());
        }
    }

    private static (PropertyInfo, int)[] Discover(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) && p.CanWrite)
            .Select(p => (Property: p, Attr: p.GetCustomAttribute<MaxLengthAttribute>()))
            .Where(x => x.Attr is { Length: > 0 })
            .Select(x => (x.Property, x.Attr!.Length))
            .ToArray();
}
