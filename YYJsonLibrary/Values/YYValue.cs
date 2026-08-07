using System.Text;

namespace YYJsonLibrary;

public abstract class YYValue
{
    public abstract void Write(YYWriter writer, int depth, bool inArray);
    
    public virtual YYValue this[string key] => 
        throw new InvalidOperationException($"Cannot access key '{key}' in type {GetType().Name}");
    
    public virtual YYValue this[int index] => 
        throw new InvalidOperationException($"Cannot access index '{index}' in type {GetType().Name}");
    
    public virtual string AsString() => 
        throw new InvalidOperationException($"Cannot convert {GetType().Name} to string");
        
    public virtual double AsNumber() => 
        throw new InvalidOperationException($"Cannot convert {GetType().Name} to number");
        
    public virtual bool AsBool() => 
        throw new InvalidOperationException($"Cannot convert {GetType().Name} to bool");
}