using System.Globalization;
using System.Text;

namespace YYJsonLibrary;

public class YYNumber : YYValue
{
    public double Value { get; set; }
    public bool IsFloatingPoint { get; set; }
    public string? RawText { get; set; }
    
    public YYNumber(double value, bool isFloatingPoint, string? rawText = null)
    {
        Value = value;
        IsFloatingPoint = isFloatingPoint;
        RawText = rawText;
    }

    public override void Write(YYWriter writer, int depth, bool inArray)
    {
        if (RawText is not null)
        {
            writer.Append(RawText);
            return;
        }
        
        string numStr = IsFloatingPoint
            ? Value.ToString("0.0################", CultureInfo.InvariantCulture)
            : Value.ToString("0", CultureInfo.InvariantCulture);
        
        writer.Append(numStr);
    }
    public override double AsNumber() => Value;
    public int AsInt() => (int)Value; // miight as well
}