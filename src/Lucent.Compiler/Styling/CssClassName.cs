namespace Lucent.Compiler.Styling;

/// <summary>The deliberately small class-name escape shared by CSS and Class literals.</summary>
internal static class CssClassName
{
    internal static bool TryRead(string text, ref int index, int end, out string name, out int start, out int length)
    {
        start = index;
        name = string.Empty;
        if (index >= end || !IsStart(text[index])) { length = 0; return false; }
        var value = new System.Text.StringBuilder();
        while (index < end)
        {
            var current = text[index];
            if (IsPart(current)) { value.Append(current); index++; continue; }
            if (current == '\\' && index + 1 < end && text[index + 1] == ':')
            {
                value.Append(':'); index += 2; continue;
            }
            break;
        }
        name = value.ToString();
        length = index - start;
        return name.Length > 0;
    }

    internal static bool IsStart(char value) => char.IsLetter(value) || value == '_';
    internal static bool IsPart(char value) => IsStart(value) || char.IsDigit(value) || value == '-';
}
