using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;

namespace YYJsonLibrary;

public static class YYDeserializer
{
    private static readonly ConcurrentDictionary<string, Type?> _typeRegistry = new();
    // because some classes exist on different assemblies
    private static readonly ConcurrentDictionary<Assembly, Dictionary<string, Type>> _registeredAssemblies = new();
    
    static YYDeserializer()
    {
        // this just registers the assembly we are on
        RegisterAssembly(Assembly.GetExecutingAssembly());
    }
    
    // need to run this for whatever assembly we are serializing, annoying.
    public static void RegisterAssembly(Assembly assembly)
    {
        _registeredAssemblies.GetOrAdd(assembly, static asm =>
        {
            Dictionary<string, Type> map = new(StringComparer.Ordinal);
            Type?[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types;
            }

            foreach (Type? t in types)
            {
                if (t is not null)
                {
                    map.TryAdd(t.Name, t);
                }
            }
            return map;
        });
    }
    
    private static Type? DefaultPolymorphicResolver(Type baseType, YYObject obj)
    {
        string? classTag = obj.Properties.Keys.FirstOrDefault(static k => k.StartsWith('$'));
        if (classTag is null)
        {
            return null;
        }
        
        Type? resolvedType = ResolveType(classTag.Substring(1));
        return resolvedType is not null && baseType.IsAssignableFrom(resolvedType) ? resolvedType : null;
    }
    
    public static object? Deserialize(ReadOnlySpan<char> json, Type type, YYOptions? options = null)
    {
        options ??= new YYOptions();
        
        // if we dont have a polymorphic resolver then we use our default one
        options.PolymorphicTypeResolver ??= DefaultPolymorphicResolver;
        
        YYParser parser = new(json);
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
        return _typeRegistry.GetOrAdd(className, static name =>
        {
            // check if its a generic
            int openBracketIndex = name.IndexOf('<');
            if (openBracketIndex != -1 && name.EndsWith('>'))
            {
                string baseName = name.Substring(0, openBracketIndex).Trim();
                string argsString = name.Substring(openBracketIndex + 1, name.Length - openBracketIndex - 2);

                List<string> argNames = SplitGenericArgs(argsString);
                Type[] resolvedArgs = new Type[argNames.Count];

                for (int i = 0; i < argNames.Count; i++)
                {
                    Type? argType = ResolveType(argNames[i]);

                    if (argType is null)
                    {
                        return null; // couldnt resolve
                    }

                    resolvedArgs[i] = argType;
                }

                // in reflection generic definitions are named like "KeyframeStore`1" because theyre stupid
                string genericDefinitionName = $"{baseName}`{resolvedArgs.Length}";
                Type? openGenericType = FindRawType(genericDefinitionName);
                if (openGenericType is { IsGenericTypeDefinition: true })
                {
                    return openGenericType.MakeGenericType(resolvedArgs);
                }

                return null;
            }

            return FindRawType(name);
        });
    }
    
    private static Type? FindRawType(string name)
    {
        foreach (Dictionary<string, Type> typeMap in _registeredAssemblies.Values)
        {
            if (typeMap.TryGetValue(name, out Type? foundType))
            {
                return foundType;
            }
        }
        return null;
    }
    
    /// <summary>
    /// Splits comma separated generic arguments (e.g. "ClassName<T, T2>, ClassName2")
    /// </summary>
    private static List<string> SplitGenericArgs(string argsString)
    {
        List<string> args = new();
        int depth = 0;
        int startIndex = 0;
        for (int i = 0; i < argsString.Length; i++)
        {
            char c = argsString[i];
            
            if (c == '<')
            {
                depth++;
            }
            else if (c == '>')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                args.Add(argsString.Substring(startIndex, i - startIndex).Trim());
                startIndex = i + 1;
            }
        }
        
        if (startIndex < argsString.Length)
        {
            args.Add(argsString.Substring(startIndex).Trim());
        }
        
        return args;
    }
}