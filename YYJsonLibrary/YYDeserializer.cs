using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;

namespace YYJsonLibrary;

public static class YYDeserializer
{
    private static readonly ConcurrentDictionary<string, Type?> _typeRegistry = new();
    // because some classes exist on different assemblies
    private static readonly ConcurrentDictionary<Assembly, byte> _registeredAssemblies = new();
    
    static YYDeserializer()
    {
        // this just registers the assembly we are on
        RegisterAssembly(Assembly.GetExecutingAssembly());
    }
    
    // need to run this for whatever assembly we are serializing, annoying.
    public static void RegisterAssembly(Assembly assembly)
    {
        _registeredAssemblies.TryAdd(assembly, 0); // 0 is just a dummy value
    }

    public static object? Deserialize(ReadOnlySpan<char> json, Type type, YYOptions? options = null)
    {
        options ??= new YYOptions();
        
        // if we dont have a polymorphic resolver then we use our default one
        options.PolymorphicTypeResolver ??= (baseType, obj) => 
        {
            string? classTag = obj.Properties.Keys.FirstOrDefault(k => k.StartsWith('$'));
            if (classTag is null) return null;
            
            string className = classTag.Substring(1);
            Type? resolvedType = ResolveType(className);
            
            if (resolvedType is not null && baseType.IsAssignableFrom(resolvedType))
            {
                return resolvedType;
            }
            return null;
        };

        YYTokenizer tokenizer = new(json);
        List<YYTokenData> tokens = tokenizer.Tokenize();
        
        YYParser parser = new(json, tokens);
        YYValue? rootNode = parser.Parse();

        if (rootNode is null or YYNull)
        {
            return null;
        }
        
        return YYMapper.Unmap(rootNode, type, options);
    }

    public static object? Deserialize(string json, Type type, YYOptions? options = null)
    {
        return Deserialize(json.AsSpan(), type, options);
    }
    
    public static T? Deserialize<T>(ReadOnlySpan<char> json, YYOptions? options = null) where T : new()
    {
        return (T?)Deserialize(json, typeof(T), options);
    }
    
    public static T? Deserialize<T>(string json, YYOptions? options = null) where T : new()
    {
        return (T?)Deserialize(json, typeof(T), options);
    }
    
    private static Type? ResolveType(string className)
    {
        // check if we already have it cached, if not then we get it
        return _typeRegistry.GetOrAdd(className, name => 
        {
            // loop through all of the registered assemblies
            foreach (Assembly assembly in _registeredAssemblies.Keys)
            {
                Type? foundType = assembly.GetTypes().FirstOrDefault(t => t.Name == name);
            
                if (foundType is not null)
                {
                    return foundType;
                }
            }

            return null; // then its either not found or not registered
        });
    }
}