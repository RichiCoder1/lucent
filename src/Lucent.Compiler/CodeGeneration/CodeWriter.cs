using System.Text;

namespace Lucent.Compiler.CodeGeneration;

internal sealed class CodeWriter
{
    private readonly StringBuilder _builder = new();
    private int _indent;

    public void Indent() => _indent++;

    public void Unindent() => _indent--;

    public void Line(string text = "")
    {
        if (text.Length > 0)
        {
            _builder.Append(' ', _indent * 4);
            _builder.Append(text);
        }

        _builder.Append('\n');
    }

    public override string ToString() => _builder.ToString();
}
