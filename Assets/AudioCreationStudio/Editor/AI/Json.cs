using System.Globalization;
using System.Text;

namespace AudioCreationStudio.EditorUI
{
    /// <summary>Tiny JSON writer helpers (escaping only); request bodies are built by hand for exact control.</summary>
    public static class Json
    {
        public static string Str(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        public static string Num(double v) => double.IsNaN(v) || double.IsInfinity(v) ? "0" : v.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
