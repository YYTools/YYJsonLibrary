using System.Text;

namespace YYJsonLibrary;

public class YYWriter
{
    private StringBuilder _sb = new();
    public int depth;
    
    public YYOptions Options { get; set; }
    public YYWriter(YYOptions? options = null)
    {
        Options = options ??= new YYOptions();
        depth = 0;
    }

    public string Write(YYValue root)
    {
        _sb.Clear();
        root.Write(this, depth, false);
        return _sb.ToString();
    }
    
    public void Append(string value) => _sb.Append(value);
    public void Append(char value) => _sb.Append(value);
    public void AppendLine(string value = "") => _sb.AppendLine(value);
    public void AppendInQuotes(string value = "") => _sb.Append($"\"{value}\"");
    public void AppendIndent(int indentDepth)
    {
        _sb.Append(Environment.NewLine);
        _sb.Append(Options.IndentChar, indentDepth * Options.IndentCount);
    }
}