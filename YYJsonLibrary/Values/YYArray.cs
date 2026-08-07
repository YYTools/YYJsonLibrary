namespace YYJsonLibrary;

public class YYArray : YYValue
{
    public List<YYValue> Items = new();

    public override void Write(YYWriter writer, int depth, bool inArray)
    {
        if (Items.Count == 0)
        {
            writer.Append("[]");
            return;
        }
        
        bool isNumeric = Items.All(item => item is YYNumber);
        int innerDepth = depth + 1;
        
        writer.Append('[');
        
        if (isNumeric)
        {
            bool isLargeArray = Items.Count >= 25;
            int targetLength = 100;
            int currentLength = targetLength;
            
            for (int i = 0; i < Items.Count; i++)
            {
                if (isLargeArray && currentLength >= targetLength)
                {
                    currentLength = 0;
                    writer.AppendIndent(innerDepth);
                }
                
                YYNumber numNode = (YYNumber)Items[i];
                // repeating code but idc
                string numStr = numNode.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (numNode.IsFloatingPoint && !numStr.Contains('.'))
                {
                    numStr += ".0";
                }
                
                writer.Append(numStr);
                writer.Append(',');
                
                currentLength += numStr.Length + 1;
            }

            if (isLargeArray)
            {
                writer.AppendIndent(depth);
            }
        }
        else
        {
            foreach (YYValue item in Items)
            {
                writer.AppendIndent(innerDepth);
                item.Write(writer, innerDepth, true);
                writer.Append(',');
            }
            writer.AppendIndent(depth);
        }
        
        writer.Append(']');
    }
    
    public override YYValue this[int index] => Items[index];
}