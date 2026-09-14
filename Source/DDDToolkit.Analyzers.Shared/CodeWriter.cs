using System;
using System.Text;

namespace DDDToolkit.Analyzers.Common;

/// <summary>Minimal indented writer for generated C#.</summary>
internal sealed class CodeWriter
{
    private readonly StringBuilder _builder = new();
    private int _indent;

    public CodeWriter Line()
    {
        _builder.Append('\n');
        return this;
    }

    public CodeWriter Line(string text)
    {
        if (text.Length == 0)
        {
            return Line();
        }

        _builder.Append(' ', _indent * 4).Append(text).Append('\n');
        return this;
    }

    /// <summary>Writes each line of a multi-line string at the current indentation.</summary>
    public CodeWriter Lines(string text)
    {
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            Line(line.TrimEnd());
        }

        return this;
    }

    /// <summary>Writes a line at column zero (for preprocessor directives).</summary>
    public CodeWriter Directive(string text)
    {
        _builder.Append(text).Append('\n');
        return this;
    }

    public CodeWriter Open(string header)
    {
        Line(header);
        Line("{");
        _indent++;
        return this;
    }

    public CodeWriter Close(string trailer = "")
    {
        _indent--;
        Line("}" + trailer);
        return this;
    }

    /// <summary>Opens a block and returns a token that closes it when disposed.</summary>
    public IDisposable Block(string header)
    {
        Open(header);
        return new Closer(this);
    }

    public override string ToString() => _builder.ToString();

    private sealed class Closer(CodeWriter writer) : IDisposable
    {
        public void Dispose() => writer.Close();
    }
}
