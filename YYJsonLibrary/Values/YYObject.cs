using System.Text;

namespace YYJsonLibrary;

public class YYObject : YYValue
{
    public Dictionary<string, YYValue> Properties = new();
    public bool IsDictionary { get; set; } = false;
    
    public override void Write(YYWriter writer, int depth, bool inArray)
    {
        if (Properties.Count == 0)
        {
            writer.Append("{}");
            return;
        }
        
        writer.Append('{');
        int innerDepth = depth + 1;
        
        if (IsDictionary)
        {
            foreach (var (key, value) in Properties)
            {
                writer.AppendIndent(innerDepth);
                writer.AppendInQuotes(key);
                writer.Append(':');
                value.Write(writer, innerDepth, true);
                writer.Append(',');
            }
            writer.AppendIndent(depth);
        }
        else
        {
            // iterate through the combined list
            foreach (var (key, val) in Properties)
            {
                if (!inArray)
                {
                    writer.AppendIndent(innerDepth);
                }
                
                writer.AppendInQuotes(key);
                writer.Append(':');
                
                val.Write(writer, innerDepth, inArray);
                
                writer.Append(',');
            }
            
            if (!inArray)
            {
                writer.AppendIndent(depth);
            }
        }
        
        writer.Append('}');
    }
    
    public override YYValue this[string key]
    {
        get
        {
            if (Properties.TryGetValue(key, out YYValue? value))
            {
                return value;
            }
                
            throw new KeyNotFoundException($"Property '{key}' does not exist on this object.");
        }
    }
}