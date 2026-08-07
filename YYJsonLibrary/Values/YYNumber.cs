using System.Text;

namespace YYJsonLibrary;

public class YYNumber : YYValue
{
    public double Value { get; set; }
    public bool IsFloatingPoint { get; set; }
    public YYNumber(double value, bool isFloatingPoint) => (Value, IsFloatingPoint) = (value, isFloatingPoint);

    public override void Write(YYWriter writer, int depth, bool inArray)
    {
        string numStr;
        
        // floats that dont contain an actual decimal
        if (IsFloatingPoint)
        {
            numStr = Value.ToString("0.0################", System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            numStr = Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        
        writer.Append(numStr);
    }
    public override double AsNumber() => Value;
    public int AsInt() => (int)Value; // miight as well
}