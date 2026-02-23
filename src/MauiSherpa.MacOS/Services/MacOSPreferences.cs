using Foundation;

namespace MauiSherpa.Services;

/// <summary>
/// macOS implementation of IPreferences backed by NSUserDefaults.
/// </summary>
public class MacOSPreferences : IPreferences
{
    private readonly NSUserDefaults _defaults = NSUserDefaults.StandardUserDefaults;

    public bool ContainsKey(string key, string? sharedName = null) =>
        _defaults.ValueForKey(new NSString(PrefKey(key, sharedName))) != null;

    public void Remove(string key, string? sharedName = null) =>
        _defaults.RemoveObject(PrefKey(key, sharedName));

    public void Clear(string? sharedName = null)
    {
        var domain = sharedName ?? _defaults.ToDictionary().Keys.FirstOrDefault()?.ToString();
        if (domain != null)
            _defaults.RemovePersistentDomain(domain);
    }

    public T Get<T>(string key, T defaultValue, string? sharedName = null)
    {
        var prefKey = PrefKey(key, sharedName);

        if (typeof(T) == typeof(string))
        {
            var val = _defaults.StringForKey(prefKey);
            return val != null ? (T)(object)val : defaultValue;
        }
        if (typeof(T) == typeof(int))
        {
            if (!ContainsKey(key, sharedName)) return defaultValue;
            return (T)(object)(int)_defaults.IntForKey(prefKey);
        }
        if (typeof(T) == typeof(long))
        {
            if (!ContainsKey(key, sharedName)) return defaultValue;
            // NSUserDefaults doesn't have LongForKey - use StringForKey
            var str = _defaults.StringForKey(prefKey);
            return str != null && long.TryParse(str, out var l) ? (T)(object)l : defaultValue;
        }
        if (typeof(T) == typeof(double))
        {
            if (!ContainsKey(key, sharedName)) return defaultValue;
            return (T)(object)_defaults.DoubleForKey(prefKey);
        }
        if (typeof(T) == typeof(float))
        {
            if (!ContainsKey(key, sharedName)) return defaultValue;
            return (T)(object)_defaults.FloatForKey(prefKey);
        }
        if (typeof(T) == typeof(bool))
        {
            if (!ContainsKey(key, sharedName)) return defaultValue;
            return (T)(object)_defaults.BoolForKey(prefKey);
        }
        if (typeof(T) == typeof(DateTime))
        {
            var str = _defaults.StringForKey(prefKey);
            return str != null && DateTime.TryParse(str, out var dt) ? (T)(object)dt : defaultValue;
        }

        return defaultValue;
    }

    public void Set<T>(string key, T value, string? sharedName = null)
    {
        var prefKey = PrefKey(key, sharedName);

        switch (value)
        {
            case string s: _defaults.SetString(s, prefKey); break;
            case int i: _defaults.SetInt(i, prefKey); break;
            case long l: _defaults.SetString(l.ToString(), prefKey); break;
            case double d: _defaults.SetDouble(d, prefKey); break;
            case float f: _defaults.SetFloat(f, prefKey); break;
            case bool b: _defaults.SetBool(b, prefKey); break;
            case DateTime dt: _defaults.SetString(dt.ToString("O"), prefKey); break;
        }

        _defaults.Synchronize();
    }

    private static string PrefKey(string key, string? sharedName) =>
        sharedName != null ? $"{sharedName}.{key}" : key;
}
