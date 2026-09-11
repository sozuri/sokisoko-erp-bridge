namespace SokiSoko.SapBridge.Sync;

internal static class StringExt
{
    public static string TrimPrefix(this string s, string prefix) =>
        s.StartsWith(prefix, StringComparison.Ordinal) ? s[prefix.Length..] : s;
}
