using EFT;
namespace AishiKeys
{
    internal static class AishiMusicText
    {
        internal static string Get(string name, string fallback)
        {
            string key = "aishi_music_" + name;
            string text = key.Localized();
            return string.IsNullOrEmpty(text) || text == key ? fallback : text;
        }
    }
}

