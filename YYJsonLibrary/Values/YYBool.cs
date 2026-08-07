using System.Text;

namespace YYJsonLibrary;

public class YYBool : YYValue
{
    public bool Value { get; set; }
    public YYBool(bool value) => Value = value;

    public override void Write(YYWriter writer, int depth, bool inArray)
    {
        writer.Append(Value ? "true" : "false");
    }
    
    public override bool AsBool() => Value;
}