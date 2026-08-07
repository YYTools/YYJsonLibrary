using System.Text;

namespace YYJsonLibrary;

public class YYString(string value) : YYValue
{
    public string Value { get; set; } = value;

    public override void Write(YYWriter writer, int depth, bool inArray)
    {
        writer.Append('"');
        foreach (char c in Value)
        {
            switch (c)
            {
                case '\\': writer.Append(@"\\"); break;
                case '"': writer.Append(@"\"""); break;
                case '\b': writer.Append(@"\b"); break;
                case '\f': writer.Append(@"\f"); break;
                case '\n': writer.Append(@"\n"); break;
                case '\r': writer.Append(@"\r"); break;
                case '\t': writer.Append(@"\t"); break;
                default:
                    if (c < 32)
                    {
                        writer.Append($"\\u{(int)c:x4}");
                    }
                    else
                    {
                        writer.Append(c.ToString());
                    }
                    break;
            }
        }
        writer.Append('"');
    }
    
    public override string AsString() => Value;
}