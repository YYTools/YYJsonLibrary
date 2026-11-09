using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Linq.Expressions;

namespace YYJsonLibrary;

public static class YYMapper
{
    private readonly record struct YYPropertyMetadata(
        PropertyInfo Property,
        Type EffectiveType,
        bool CanRead,
        bool CanWrite,
        bool IsNullableString,
        string SerializedName,
        bool IsIgnored,
        bool IgnoreWhenWritingNull,
        Func<object, object?>? Getter,
        Action<object, object?>? Setter
    );
    
    // cache everything ever
    private static readonly ConcurrentDictionary<(Type, YYOptions), YYPropertyMetadata[]> _propertyCache = new();
    // cache the type name
    private static readonly ConcurrentDictionary<Type, string> _gmTypeNameCache = new();
    // this is to unmap lists effectively
    private static readonly ConcurrentDictionary<Type, Func<YYArray, YYOptions, object>> _listDeserializerCache = new();
    // this is to replace the Activator.CreateInstance usages in Unmap
    private static readonly ConcurrentDictionary<Type, Func<object>> _activatorCache = new();
    
    private static YYPropertyMetadata[] GetTypeMetadata(Type type, YYOptions options)
    {
        return _propertyCache.GetOrAdd((type, options), static key =>
        {
            var (t, opt) = key;
            NullabilityInfoContext nullabilityContext = new();
            PropertyInfo[] rawProps = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            YYPropertyMetadata[] metadata = new YYPropertyMetadata[rawProps.Length];
            
            bool isGMResource = opt is { DoYYFormatting: true, GMResourceInterface: not null } &&
                                opt.GMResourceInterface.IsAssignableFrom(t);
            
            for (int i = 0; i < rawProps.Length; i++)
            {
                PropertyInfo prop = rawProps[i];
                
                Type effectiveType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                
                bool isNullableStr = false;

                if (prop.PropertyType == typeof(string) && prop.Name != "%Name")
                {
                    try
                    {
                        var info = nullabilityContext.Create(prop);
                        isNullableStr = info.WriteState == NullabilityState.Nullable;
                    }
                    catch
                    {
                        isNullableStr = true;
                    }
                }
                
                // resolve and cache the property name
                string serializedName = prop.Name;
                if (isGMResource && prop.Name == opt.GMVersionPropertyName)
                {
                    serializedName = $"${GetGameMakerTypeName(t)}";
                }
                else if (isGMResource && prop.Name == opt.GMNamePropertyName)
                {
                    serializedName = "%Name";
                }
                else if (opt.PropertyNameResolver is not null)
                {
                    string? customName = opt.PropertyNameResolver(prop);
                    if (!string.IsNullOrWhiteSpace(customName))
                    {
                        serializedName = customName;
                    }
                }
                
                // cache these as well
                bool isIgnored = opt.ShouldIgnoreProperty?.Invoke(prop) ?? false;
                bool ignoreWhenNull = opt.IgnoreWhenWritingNullResolver?.Invoke(prop) ?? false;
                
                // cache the getters and setters
                
                Func<object, object?>? getter = null;
                if (prop.CanRead)
                {
                    var getMethod = prop.GetGetMethod(nonPublic: true);
                    if (getMethod is not null)
                    {
                        getter = (target) => getMethod.Invoke(target, null); 
                    }
                }
                
                Action<object, object?>? setter = null;
                if (prop.CanWrite)
                {
                    var setMethod = prop.GetSetMethod(nonPublic: true);
                    if (setMethod is not null)
                    {
                        setter = (target, val) => setMethod.Invoke(target, new[] { val });
                    }
                }
                
                metadata[i] = new YYPropertyMetadata(
                    prop,
                    effectiveType,
                    prop.CanRead,
                    prop.CanWrite,
                    isNullableStr,
                    serializedName,
                    isIgnored,
                    ignoreWhenNull,
                    getter,
                    setter
                );
            }
            
            // sort everything alphabetically
            Array.Sort(metadata, static (a, b) =>
            {
                // $ tag first
                bool aDollar = a.SerializedName.StartsWith('$');
                bool bDollar = b.SerializedName.StartsWith('$');
                if (aDollar != bDollar)
                {
                    return aDollar ? -1 : 1;
                }
                
                // % tag second
                bool aPercent = a.SerializedName.StartsWith('%');
                bool bPercent = b.SerializedName.StartsWith('%');
                if (aPercent != bPercent)
                {
                    return aPercent ? -1 : 1;
                }
                
                // alphabetical
                return string.Compare(a.SerializedName, b.SerializedName, StringComparison.OrdinalIgnoreCase);
            });
            
            return metadata;
        });
    }
    
    /// <summary>
    /// Unmaps a list effectively
    /// </summary>
    private static object UnmapListGeneric<TElement>(YYArray arr, YYOptions options)
    {
        List<TElement> list = new(arr.Items.Count);
        for (int i = 0; i < arr.Items.Count; i++)
        {
            list.Add(Unmap<TElement>(arr.Items[i], options)!);
        }
        return list;
    }
    
    /// <summary>
    /// Replaces Activator.CreateInstance by caching anonymous functions which create the correct type
    /// </summary>
    private static object CreateInstanceFast(Type type)
    {
        // get or compile the factory
        Func<object> factory = _activatorCache.GetOrAdd(type, static t =>
        {
            /*
                I dont really understand this
                but apparently it creates a function equivalent to
                `() => new Type()` which is faster!
            */
            var newExpression = Expression.New(t);
            var lambda = Expression.Lambda<Func<object>>(newExpression);
            return lambda.Compile();
        });
        // execute it and return it
        return factory();
    }
    
    public static YYValue MapValue(object? obj, YYOptions options)
    {
        if (obj is null)
        {
            return new YYNull();
        }

        Type type = obj.GetType();
        
        // basic types
        switch (obj)
        {
            case string str:
                return new YYString(str);
            case bool b:
                return new YYBool(b);
            case float f:
                double cleaned = double.Parse(f.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
                return new YYNumber(cleaned, true);
            case double d:
                return new YYNumber(d, true);
            case decimal dec:
                return new YYNumber(Convert.ToDouble(dec), true);
            case sbyte or byte or short or ushort or int or uint:
                return new YYNumber(Convert.ToDouble(obj), false);
            case long or ulong:
                return new YYNumber(Convert.ToDouble(obj), false, obj.ToString());
            case Enum e:
                return new YYNumber(Convert.ToDouble(e), false, e.ToString("d")); // "d" makes it so it doesnt do scientific notation
            // safeguards for reflection
            case Type t: 
                return new YYString(t.Name); 
            case MemberInfo: 
                return new YYNull();
        }
        
        // dictionary
        if (obj is IDictionary dict)
        {
            YYObject yyDictObj = new() { IsDictionary = true };
            foreach (DictionaryEntry entry in dict)
            {
                yyDictObj.Properties[entry.Key.ToString()!] = MapValue(entry.Value, options);
            }
            return yyDictObj;
        }
        
        // enumerable
        if (obj is IEnumerable enumerable)
        {
            YYArray yyArray = new();
            foreach (object? item in enumerable)
            {
                yyArray.Items.Add(MapValue(item, options));
            }
            return yyArray;
        }
        
        // custom objects
        YYObject yyObject = new();
        YYPropertyMetadata[] properties = GetTypeMetadata(type, options);
        
        bool isGMResource = options.DoYYFormatting &&
                            options.GMResourceInterface is not null &&
                            options.GMResourceInterface.IsAssignableFrom(type);
        
        // if it inherits the chosen resource base type
        if (isGMResource)
        {
            YYPropertyMetadata? versionMeta = null;
            YYPropertyMetadata? nameMeta = null;

            foreach (var meta in properties)
            {
                if (!meta.CanRead)
                {
                    continue;
                }

                if (meta.Property.Name == options.GMVersionPropertyName)
                {
                    versionMeta = meta;
                }
                else if (meta.Property.Name == options.GMNamePropertyName)
                {
                    nameMeta = meta;
                }
            }

            if (versionMeta is { Property: PropertyInfo versionProp } &&
                (options.ShouldIgnoreProperty is null || !options.ShouldIgnoreProperty(versionProp)))
            {
                object? versionVal = versionProp.GetValue(obj);
                if ((versionVal is null || (versionVal is string strVal && string.IsNullOrWhiteSpace(strVal))) &&
                    options.GMVersionFallbackResolver is not null)
                {
                    versionVal = options.GMVersionFallbackResolver(type);
                }

                yyObject.Properties[$"${GetGameMakerTypeName(type)}"] = MapValue(versionVal, options);
            }

            if (nameMeta is { Property: PropertyInfo nameProp } &&
                (options.ShouldIgnoreProperty is null || !options.ShouldIgnoreProperty(nameProp)))
            {
                object? nameVal = nameProp.GetValue(obj);
                if ((nameVal is null || (nameVal is string strVal && string.IsNullOrWhiteSpace(strVal))) &&
                    options.GMNameFallbackResolver is not null)
                {
                    nameVal = options.GMNameFallbackResolver(obj);
                }

                bool ignoreNull = nameVal is null && options.IgnoreWhenWritingNullResolver is not null &&
                                  options.IgnoreWhenWritingNullResolver(nameProp);
                if (!ignoreNull)
                {
                    YYValue nameValue = MapValue(nameVal, options);
                    yyObject.Properties["%Name"] = nameValue is YYNull ? new YYString(string.Empty) : nameValue;
                }
            }
        }

        foreach (YYPropertyMetadata meta in properties)
        {
            if (!meta.CanRead || meta.IsIgnored)
            {
                continue;
            }
            
            PropertyInfo prop = meta.Property;
            
            // skip the resource base
            if (isGMResource && (prop.Name == options.GMVersionPropertyName || prop.Name == options.GMNamePropertyName))
            {
                continue;
            }
            
            object? rawValue = meta.Getter!(obj);
            
            // ignore when null
            if (rawValue is null && meta.IgnoreWhenWritingNull)
            {
                continue;
            }
            
            if (rawValue is null && prop.PropertyType == typeof(string) && meta.IsNullableString)
            {
                yyObject.Properties[meta.SerializedName] = new YYString(string.Empty);
            }
            else
            {
                yyObject.Properties[meta.SerializedName] = MapValue(rawValue, options);
            }
        }
        
        return yyObject;
    }
    
    public static T? Unmap<T>(YYValue node, YYOptions options)
    {
        return (T?)Unmap(node, typeof(T), options);
    }

    public static object? Unmap(YYValue node, Type targetType, YYOptions options)
    {
        Type effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        
        // null
        if (node is YYNull)
        {
            return null;
        }

        // string
        if (node is YYString yyStr && effectiveType == typeof(string))
        {
            return yyStr.Value;
        }

        // bool
        if (node is YYBool yyBool && effectiveType == typeof(bool))
        {
            return yyBool.Value;
        }

        // number
        if (node is YYNumber yyNum)
        {
            if (effectiveType == typeof(int))
            {
                return (int)yyNum.Value;
            }

            if (effectiveType == typeof(uint))
            {
                return (uint)yyNum.Value;
            }

            if (effectiveType == typeof(float))
            {
                return (float)yyNum.Value;
            }

            if (effectiveType == typeof(double))
            {
                return yyNum.Value;
            }

            if (effectiveType == typeof(bool))
            {
                return yyNum.Value != 0;
            }
            
            if (effectiveType.IsEnum)
            {
                return Enum.ToObject(effectiveType, BitConverter.DoubleToInt64Bits(yyNum.Value));
            }

            return Convert.ChangeType(yyNum.Value, effectiveType, System.Globalization.CultureInfo.InvariantCulture);
        }

        // array/list/hashset
        if (node is YYArray arr && typeof(IEnumerable).IsAssignableFrom(effectiveType) && effectiveType != typeof(string))
        {
            if (effectiveType.IsArray)
            {
                Type elementType = effectiveType.GetElementType()!;
                Array array = Array.CreateInstance(elementType, arr.Items.Count);

                for (int i = 0; i < arr.Items.Count; i++)
                {
                    array.SetValue(Unmap(arr.Items[i], elementType, options), i);
                }

                return array;
            }

            // lists and other collections
            if (effectiveType.IsGenericType)
            {
                // HERE
                Type genericDefinition = effectiveType.GetGenericTypeDefinition();
                Type elementType = effectiveType.GetGenericArguments()[0];
                
                // if it is a list or not we can just treat it like one
                if (genericDefinition == typeof(List<>) || 
                    genericDefinition == typeof(Collection<>) || 
                    genericDefinition == typeof(IEnumerable<>) || 
                    genericDefinition == typeof(ICollection<>) || 
                    genericDefinition == typeof(IList<>))
                {
                    // find from cache or make
                    var unmapper = _listDeserializerCache.GetOrAdd(elementType, static elemType =>
                    {
                        // get UnmapListGeneric and get that generic
                        var method = typeof(YYMapper)
                            .GetMethod(nameof(UnmapListGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
                            .MakeGenericMethod(elemType);
                        // create a delegate with that method
                        return (Func<YYArray, YYOptions, object>)Delegate.CreateDelegate(
                            typeof(Func<YYArray, YYOptions, object>), method);
                    });
                    
                    return unmapper(arr, options);
                }
                
                if (genericDefinition == typeof(HashSet<>))
                {
                    object hashSet = CreateInstanceFast(effectiveType)!;
                    MethodInfo addMethod = effectiveType.GetMethod("Add")!;

                    foreach (YYValue itemNode in arr.Items)
                    {
                        addMethod.Invoke(hashSet, new[] { Unmap(itemNode, elementType, options) });
                    }

                    return hashSet;
                }
            }
        }
        
        // dictionary
        if (node is YYObject dictObj && effectiveType.IsGenericType && effectiveType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            Type keyType = effectiveType.GetGenericArguments()[0]; // TKey
            Type valueType = effectiveType.GetGenericArguments()[1]; // TValue
            
            IDictionary dict = (IDictionary)CreateInstanceFast(effectiveType)!;

            foreach (var kvp in dictObj.Properties)
            {
                // convert the key
                object convertedKey = keyType.IsEnum 
                    ? Enum.Parse(keyType, kvp.Key, ignoreCase: true) 
                    : Convert.ChangeType(kvp.Key, keyType, CultureInfo.InvariantCulture);

                // unmap and fix up
                dict.Add(convertedKey, Unmap(kvp.Value, valueType, options));
            }

            return dict;
        }
        
        // object/class
        if (node is YYObject obj)
        {
            Type actualType = effectiveType;

            // if we have the resolver
            if (options.PolymorphicTypeResolver is not null)
            {
                Type? resolved = options.PolymorphicTypeResolver(effectiveType, obj);
                if (resolved is not null)
                {
                    actualType = resolved;
                }
            }
            
            object instance = CreateInstanceFast(actualType)!;
            
            YYPropertyMetadata[] properties = GetTypeMetadata(actualType, options);

            // go through the properties
            foreach (YYPropertyMetadata meta in properties)
            {
                if (!meta.CanWrite || meta.IsIgnored)
                {
                    continue;
                }
                
                if (obj.Properties.TryGetValue(meta.SerializedName, out YYValue? childNode))
                {
                    object? deserializedValue = Unmap(childNode, meta.EffectiveType, options);
                    meta.Setter!(instance, deserializedValue);
                }
            }
            
            return instance;
        }
        
        throw new InvalidCastException($"cant unmap node {node.GetType().Name} to type {targetType.Name}");
    }

    private static string GetGameMakerTypeName(Type type)
    {
        // w anons
        return _gmTypeNameCache.GetOrAdd(type, static t =>
        {
            if (!t.IsGenericType)
            {
                return t.Name;
            }

            string baseName = t.Name.Substring(0, t.Name.IndexOf('`'));
            Type[] genericArgs = t.GetGenericArguments();
            string genericString = string.Join(",", genericArgs.Select(GetGameMakerTypeName));
            return $"{baseName}<{genericString}>";
        });
    }
}