using System.Text.RegularExpressions;

namespace TorVps.Core.Config;

/// <summary>
/// Minimal INI updater for torrc_manager.cfg: sets one key in one section while preserving everything
/// else byte-for-byte — comments, blank lines, key order and the file's newline style. Matching is
/// case-insensitive (same as <see cref="ConfigParser.FirstIniValue"/>).
/// </summary>
public static class IniWriter
{
    /// <summary>Reads <paramref name="path"/> (missing file = empty), applies <see cref="SetValue(string,string,string,string)"/>
    /// and writes the result back.</summary>
    public static void SetValueInFile(string path, string section, string key, string value)
    {
        var text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        File.WriteAllText(path, SetValue(text, section, key, value));
    }

    /// <summary>
    /// Returns <paramref name="text"/> with <c>key = value</c> set under <c>[section]</c>:
    /// an existing key is updated in place, a missing key is appended at the end of the section,
    /// a missing section is appended at the end of the file.
    /// </summary>
    public static string SetValue(string text, string section, string key, string value)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) || text.Length == 0 ? "\r\n" : "\n";
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        // Split('\n') leaves one empty tail entry when the text ends with a newline — drop it so
        // "append at end" doesn't create a gap, and re-add the trailing newline when joining.
        var hadTrailingNewline = lines.Count > 0 && lines[^1].Length == 0;
        if (hadTrailingNewline)
            lines.RemoveAt(lines.Count - 1);

        var sectionHeader = $"[{section}]";
        var keyPattern = new Regex($@"^\s*{Regex.Escape(key)}\s*=", RegexOptions.IgnoreCase);

        var sectionStart = lines.FindIndex(l => string.Equals(l.Trim(), sectionHeader, StringComparison.OrdinalIgnoreCase));
        if (sectionStart < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0)
                lines.Add(string.Empty);
            lines.Add(sectionHeader);
            lines.Add($"{key} = {value}");
        }
        else
        {
            var sectionEnd = lines.Count; // exclusive: index of the next section header or EOF
            for (var i = sectionStart + 1; i < lines.Count; i++)
            {
                if (lines[i].TrimStart().StartsWith('['))
                {
                    sectionEnd = i;
                    break;
                }
            }

            var keyLine = -1;
            for (var i = sectionStart + 1; i < sectionEnd; i++)
            {
                if (keyPattern.IsMatch(lines[i]))
                {
                    keyLine = i;
                    break;
                }
            }

            if (keyLine >= 0)
            {
                lines[keyLine] = $"{key} = {value}";
            }
            else
            {
                // Append after the last non-blank line of the section so the blank separator
                // before the next section header stays where it was.
                var insertAt = sectionEnd;
                while (insertAt > sectionStart + 1 && lines[insertAt - 1].Trim().Length == 0)
                    insertAt--;
                lines.Insert(insertAt, $"{key} = {value}");
            }
        }

        return string.Join(newline, lines) + (hadTrailingNewline || text.Length == 0 ? newline : string.Empty);
    }
}
