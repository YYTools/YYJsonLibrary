using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;

namespace YYJsonLibrary;

public static class YYMapper
{
    private readonly record struct YYPropertyMetadata(
        PropertyInfo Property,
        bool CanRead,
        bool CanWrite,
        bool IsNullableString
    );
    
    private static ConcurrentDictionary<Type, YYPropertyMetadata[]> _propertyCache = new();
    private static readonly ConcurrentDictionary<Type, string> _gmTypeNameCache = new();
    
    private static YYPropertyMetadata[] GetTypeMetadata(Type type)
    {
        return _propertyCache.GetOrAdd(type, static t =>
        {
            NullabilityInfoContext nullabilityContext = new();
            PropertyInfo[] rawProps = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            YYPropertyMetadata[] metadata = new YYPropertyMetadata[rawProps.Length];

            for (int i = 0; i < rawProps.Length; i++)
            {
                PropertyInfo prop = rawProps[i];
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

                metadata[i] = new YYPropertyMetadata(
                    prop,
                    prop.CanRead,
                    prop.CanWrite,
                    isNullableStr
                );
            }

            return metadata;
        });
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
        YYPropertyMetadata[] properties = GetTypeMetadata(type);
        
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
            if (!meta.CanRead)
            {
                continue;
            }
            
            PropertyInfo prop = meta.Property;
            
            // skip the resource base
            if (isGMResource && (prop.Name == options.GMVersionPropertyName || prop.Name == options.GMNamePropertyName))
            {
                continue;
            }
            
            // the ignore attribute
            if (options.ShouldIgnoreProperty is not null && options.ShouldIgnoreProperty(prop))
            {
                continue;
            }
            
            object? rawValue = prop.GetValue(obj);
            
            if (rawValue is null && options.IgnoreWhenWritingNullResolver is not null && options.IgnoreWhenWritingNullResolver(prop))
            {
                continue;
            }
            
            string finalKeyName = prop.Name;
            
            // run the property name resolver
            if (options.PropertyNameResolver is not null)
            {
                string? customName = options.PropertyNameResolver(prop);
                
                if (!string.IsNullOrWhiteSpace(customName))
                {
                    finalKeyName = customName;
                }
            }

            // recursively map
            if (rawValue is null && prop.PropertyType == typeof(string) && meta.IsNullableString)
            {
                yyObject.Properties[finalKeyName] = new YYString(string.Empty);
            }
            else
            {
                yyObject.Properties[finalKeyName] = MapValue(rawValue, options);
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
            if (effectiveType.IsEnum)
            {
                return Enum.ToObject(effectiveType, Convert.ToInt64(yyNum.Value));
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
                Type genericDefinition = effectiveType.GetGenericTypeDefinition();
                Type elementType = effectiveType.GetGenericArguments()[0];
                
                // if it is a list or not we can just treat it like one
                if (genericDefinition == typeof(List<>) || 
                    genericDefinition == typeof(Collection<>) || 
                    genericDefinition == typeof(IEnumerable<>) || 
                    genericDefinition == typeof(ICollection<>) || 
                    genericDefinition == typeof(IList<>))
                {
                    Type listType = typeof(List<>).MakeGenericType(elementType);
                    IList list = (IList)Activator.CreateInstance(listType)!;
                    foreach (YYValue itemNode in arr.Items)
                    {
                        list.Add(Unmap(itemNode, elementType, options));
                    }
                    return list;
                }
                
                if (genericDefinition == typeof(HashSet<>))
                {
                    object hashSet = Activator.CreateInstance(effectiveType)!;
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
            
            IDictionary dict = (IDictionary)Activator.CreateInstance(effectiveType)!;

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
            
            object instance = Activator.CreateInstance(actualType)!;
            
            YYPropertyMetadata[] properties = GetTypeMetadata(actualType);
            
            bool isGMResource = options.DoYYFormatting && 
                                options.GMResourceInterface is not null && 
                                options.GMResourceInterface.IsAssignableFrom(actualType);

            // go through the properties
            foreach (YYPropertyMetadata meta in properties)
            {
                if (!meta.CanWrite)
                {
                    continue;
                }

                PropertyInfo prop = meta.Property;
                
                // the ignore attribute
                if (options.ShouldIgnoreProperty is not null && options.ShouldIgnoreProperty(prop))
                {
                    continue;
                }

                string jsonKeyName = prop.Name;
                
                if (isGMResource && prop.Name == options.GMVersionPropertyName)
                {
                    jsonKeyName = $"${GetGameMakerTypeName(actualType)}";
                }
                else if (isGMResource && prop.Name == options.GMNamePropertyName)
                {
                    jsonKeyName = "%Name";    
                }
                else if (options.PropertyNameResolver is not null)
                {
                    // then use the resolver to find the custom name
                    string? customName = options.PropertyNameResolver(prop);
                    if (!string.IsNullOrWhiteSpace(customName))
                    {
                        jsonKeyName = customName;
                    }
                }
                
                if (obj.Properties.TryGetValue(jsonKeyName, out YYValue? childNode))
                {
                    object? deserializedValue = Unmap(childNode, prop.PropertyType, options);
                    prop.SetValue(instance, deserializedValue);
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