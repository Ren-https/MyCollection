using System.Globalization;
using System.Text;
using MyCollection.Models;

static class CollectionCsvExporter
{
    public static byte[] Export(IEnumerable<CollectionGame> games)
    {
        var csv = new StringBuilder();
        AppendRow(csv, "IGDB ID", "Name", "Platforms", "Status", "Notes", "Added At (UTC)");

        foreach (var game in games)
        {
            AppendRow(csv,
                game.Id.ToString(CultureInfo.InvariantCulture),
                game.Name,
                string.Join("; ", game.Platforms.Select(platform => platform.Name)),
                game.Status,
                game.Notes ?? "",
                game.AddedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var preamble = encoding.GetPreamble();
        var content = encoding.GetBytes(csv.ToString());
        var result = new byte[preamble.Length + content.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(content, 0, result, preamble.Length, content.Length);
        return result;
    }

    private static void AppendRow(StringBuilder csv, params string[] values)
    {
        csv.AppendJoin(',', values.Select(Escape));
        csv.Append("\r\n");
    }

    private static string Escape(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0) return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
