using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Reflection;

namespace YYJsonLibrary;

public static class YYMapper
{
    private static ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache = new();
    private static readonly NullabilityInfoContext _nullabilityContext = new();

    private static bool IsNullableString(PropertyInfo prop)
    {
        if (prop.PropertyType != typeof(string))
        {
            return false;
        }

        // treat %Name as special (force empty string instead of null)
        if (prop.Name == "%Name")
        {
            return false;
        }

        try
        {
            var info = _nullabilityContext.Create(prop);
            return info.WriteState == NullabilityState.Nullable;
        }
        catch
        {
            return true;
        }
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
            case sbyte or byte or short or ushort or int or uint or long or ulong: 
                return new YYNumber(Convert.ToDouble(obj), false);
            case Enum e: 
                return new YYNumber(Convert.ToDouble(e), false);
            
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
            foreach (DictionaryEntry entry in dict) // cant do KeyValuePair here
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
        
        // get readable properties
        PropertyInfo[] properties = _propertyCache.GetOrAdd(type, t => 
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead)
                .ToArray()
        );
        
        bool isGMResource = options.DoYYFormatting &&
                            options.GMResourceInterface is not null &&
                            options.GMResourceInterface.IsAssignableFrom(type);
        
        // if it inherits the chosen resource base type
        if (isGMResource)
        {
            // try to find the specific properties for versioning and name
            PropertyInfo? versionProp = properties.FirstOrDefault(p => p.Name == options.GMVersionPropertyName);
            PropertyInfo? nameProp = properties.FirstOrDefault(p => p.Name == options.GMNamePropertyName);
            
            
            if (versionProp is not null && (options.ShouldIgnoreProperty is null || !options.ShouldIgnoreProperty(versionProp))) 
            {
                object? versionVal = versionProp.GetValue(obj);
                if ((versionVal is null || (versionVal is string strVal && string.IsNullOrWhiteSpace(strVal))) && options.GMVersionFallbackResolver is not null)
                {
                    versionVal = options.GMVersionFallbackResolver(type);
                }
                
                yyObject.Properties[$"${GetGameMakerTypeName(type)}"] = MapValue(versionVal, options);
            }
            
            if (nameProp is not null && (options.ShouldIgnoreProperty is null || !options.ShouldIgnoreProperty(nameProp))) 
            {
                object? nameVal = nameProp.GetValue(obj);
                if ((nameVal is null || (nameVal is string strVal && string.IsNullOrWhiteSpace(strVal))) && options.GMNameFallbackResolver is not null)
                {
                    nameVal = options.GMNameFallbackResolver(obj);
                }

                // check if name is null and we should ignore it
                bool ignoreNull = nameVal is null && options.IgnoreWhenWritingNullResolver is not null && options.IgnoreWhenWritingNullResolver(nameProp);

                if (!ignoreNull)
                {
                    YYValue nameValue = MapValue(nameVal, options);
                    yyObject.Properties["%Name"] = (nameValue.GetType() == typeof(YYNull)) ? new YYString(string.Empty) : nameValue;
                }
            }
        }
        
        foreach (PropertyInfo prop in properties)
        {
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
            if (rawValue is null && prop.PropertyType == typeof(string) && !IsNullableString(prop))
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
        // null
        if (node is YYNull)
        {
            return null;
        }

        // string
        if (node is YYString yyStr && targetType == typeof(string))
        {
            return yyStr.Value;
        }

        // bool
        if (node is YYBool yyBool && targetType == typeof(bool))
        {
            return yyBool.Value;
        }

        // number
        if (node is YYNumber yyNum)
        {
            if (targetType.IsEnum)
            {
                return Enum.ToObject(targetType, Convert.ToInt64(yyNum.Value));
            }

            return Convert.ChangeType(yyNum.Value, targetType, System.Globalization.CultureInfo.InvariantCulture);
        }

        // array/list
        if (node is YYArray arr && typeof(IEnumerable).IsAssignableFrom(targetType) && targetType != typeof(string))
        {
            // true arrays
            if (targetType.IsArray)
            {
                Type elementType = targetType.GetElementType()!;
                Array array = Array.CreateInstance(elementType, arr.Items.Count);

                for (int i = 0; i < arr.Items.Count; i++)
                {
                    array.SetValue(Unmap(arr.Items[i], elementType, options), i);
                }

                return array;
            }

            // lists and other collections
            if (targetType.IsGenericType)
            {
                Type genericDefinition = targetType.GetGenericTypeDefinition();
                Type elementType = targetType.GetGenericArguments()[0];
                
                // if it is a list or we can just treat it like one
                if (genericDefinition == typeof(List<>) || genericDefinition == typeof(Collection<>) || genericDefinition == typeof(IEnumerable<>) || genericDefinition == typeof(ICollection<>) || genericDefinition == typeof(IList<>))
                {
                    Type listType = typeof(List<>).MakeGenericType(elementType);
                    IList list = (IList)Activator.CreateInstance(listType)!;

                    foreach (YYValue itemNode in arr.Items)
                    {
                        list.Add(Unmap(itemNode, elementType, options));
                    }

                    return list;
                }
                
                // hashset
                if (genericDefinition == typeof(HashSet<>))
                {
                    object hashSet = Activator.CreateInstance(targetType)!;
                    MethodInfo addMethod = targetType.GetMethod("Add")!;

                    foreach (YYValue itemNode in arr.Items)
                    {
                        addMethod.Invoke(hashSet, new[] { Unmap(itemNode, elementType, options) });
                    }

                    return hashSet;
                }
            }
        }
        
        // dictionary
        if (node is YYObject dictObj && targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            Type keyType = targetType.GetGenericArguments()[0]; // TKey
            Type valueType = targetType.GetGenericArguments()[1]; // TValue
            IDictionary dict = (IDictionary)Activator.CreateInstance(targetType)!;

            foreach (var kvp in dictObj.Properties)
            {
                // convert the key
                object convertedKey;
                if (keyType.IsEnum)
                {
                    convertedKey = Enum.Parse(keyType, kvp.Key, ignoreCase: true);
                }
                else
                {
                    convertedKey = Convert.ChangeType(kvp.Key, keyType, System.Globalization.CultureInfo.InvariantCulture);
                }

                // unmap and fix up
                dict.Add(convertedKey, Unmap(kvp.Value, valueType, options));
            }

            return dict;
        }
        
        // object/class
        if (node is YYObject obj)
        {
            Type actualType = targetType;

            // if we have the resolver
            if (options.PolymorphicTypeResolver is not null)
            {
                Type? resolved = options.PolymorphicTypeResolver(targetType, obj);
                if (resolved is not null)
                {
                    actualType = resolved;
                }
            }
            
            object instance = Activator.CreateInstance(actualType)!;
            
            PropertyInfo[] properties = _propertyCache.GetOrAdd(actualType, t => 
                t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanWrite)
                    .ToArray()
            );
            
            bool isGMResource = options.DoYYFormatting && 
                                options.GMResourceInterface is not null && 
                                options.GMResourceInterface.IsAssignableFrom(actualType);

            // go through the properties
            foreach (PropertyInfo prop in properties)
            {
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
        // if its not a generic type were fine
        if (!type.IsGenericType)
        {
            return type.Name;
        }
        
        string baseName = type.Name.Substring(0, type.Name.IndexOf('`'));
        
        // the generics
        Type[] genericArgs = type.GetGenericArguments();
        
        // clean them
        string genericString = string.Join(",", genericArgs.Select(t => GetGameMakerTypeName(t)));

        return $"{baseName}<{genericString}>";
    }
}