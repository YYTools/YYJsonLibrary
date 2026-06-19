using System.Text;

namespace YYJsonLibrary;

public class YYNull : YYValue
{
    public override void Write(YYWriter writer, int depth, bool inArray) => writer.Append("null");
    
    
}