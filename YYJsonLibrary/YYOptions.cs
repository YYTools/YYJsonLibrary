using System.Reflection;

namespace YYJsonLibrary;

public class YYOptions
{
    public bool IndentWithTabs { get; set; } = false;
    public char IndentChar { get; set; } = ' ';
    public int IndentCount { get; set; } = 2;
    public bool DoYYFormatting = true; // TODO: optional YY formatting?
    
    public Type? GMResourceInterface { get; set; } 
    public string GMVersionPropertyName { get; set; } = "ResourceVersion";
    public string GMNamePropertyName { get; set; } = "ResourceName";
    
    public Func<Type, object?>? GMVersionFallbackResolver { get; set; }
    public Func<object, object?>? GMNameFallbackResolver { get; set; }
    
    public Func<PropertyInfo, string?>? PropertyNameResolver { get; set; }
    public Func<Type, YYObject, Type?>? PolymorphicTypeResolver { get; set; }
    
    public Func<PropertyInfo, bool>? ShouldIgnoreProperty { get; set; }
    public Func<PropertyInfo, bool>? IgnoreWhenWritingNullResolver { get; set; }
}