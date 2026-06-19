using System.Runtime.InteropServices;
using System.Text;

namespace YYJsonLibrary;

public ref struct YYParser
{
    public YYParser(string json, List<YYTokenData> tokens)
    {
        this = new YYParser(json, CollectionsMarshal.AsSpan(tokens));
    }
    public YYParser(string json, ReadOnlySpan<YYTokenData> tokens)
    {
        this = new YYParser(json.AsSpan(), tokens);
    }
    public YYParser(ReadOnlySpan<char> json, List<YYTokenData> tokens)
    {
        this = new YYParser(json, CollectionsMarshal.AsSpan(tokens));
    }
    public YYParser(ReadOnlySpan<char> json, ReadOnlySpan<YYTokenData> tokens)
    {
        _tokens = tokens;
        _json = json;
        _index = 0;
        _stringPool = new();
    }

    private ReadOnlySpan<char> _json;
    private ReadOnlySpan<YYTokenData> _tokens;
    private int _index;
    private HashSet<string> _stringPool;
    
    public YYValue? Parse()
    {
        if (_tokens.Length == 0)
        {
            return null;
        }
        
        return ParseValue();
    }

    private YYValue ParseValue()
    {
        YYTokenData token = _tokens[_index];
        
        switch (token.Token)
        {
            case YYToken.StartObject: 
                return ParseObject();
            case YYToken.StartArray: 
                return ParseArray();
            case YYToken.String: 
                Step();
                return ParseString(token);
            case YYToken.Boolean:
                Step();
                return ParseBool(token);
            case YYToken.Number: 
                Step();
                return ParseNumber(token);
            case YYToken.Null:
                Step();
                return new YYNull();
            default: throw new FormatException($"Invalid token at position {token.Position}");
        }
    }
    
    private YYObject ParseObject()
    {
        YYObject obj = new();
        Step(); // consume the bracket
        
        while (_index < _tokens.Length)
        {
            YYTokenData token = _tokens[_index];
            
            if (token.Token == YYToken.EndObject)
            {
                Step();
                return obj;
            }
            
            if (token.Token != YYToken.String)
            {
                throw new FormatException("Expected string token at position " + token.Position);
            }
            
            string key = GetPooledString(token.Position, token.Length);    
            Step();
            
            token = _tokens[_index];
            if (token.Token != YYToken.Seperator)
            {
                throw new FormatException("Expected seperator at position " + token.Position);
            }

            Step();
            
            obj.Properties.Add(key, ParseValue());
            
            // skip commas
            if (_index < _tokens.Length && _tokens[_index].Token == YYToken.Comma)
            {
                Step();
            }
        }

        throw new FormatException("Unexpected EOF while parsing object");
    }
    
    private YYArray ParseArray()
    {
        YYArray array = new();
        Step(); // consume bracket
        
        while (_index < _tokens.Length)
        {
            YYTokenData token = _tokens[_index];

            if (token.Token == YYToken.EndArray)
            {
                Step();
                return array;
            }
            array.Items.Add(ParseValue());
            
            // skip commas
            if (_index < _tokens.Length && _tokens[_index].Token == YYToken.Comma)
            {
                Step();
            }
        }
        throw new FormatException("Unexpected EOF while parsing array");
    }

    private YYValue ParseString(YYTokenData token)
    {
        string str = GetString(token.Position, token.Length);
        
        return new YYString(str);
    }

    private YYValue ParseBool(YYTokenData token)
    {
        ReadOnlySpan<char> span = _json.Slice(token.Position, token.Length);

        bool value = span.SequenceEqual("true");
        return new YYBool(value);
    }

    private YYValue ParseNumber(YYTokenData token)
    {
        ReadOnlySpan<char> span = _json.Slice(token.Position, token.Length);
        
        if (double.TryParse(span, out double value))
        {
            bool isFloat = span.Contains('.') || span.Contains('e') || span.Contains('E');
            return new YYNumber(value, isFloat);
        }
        
        throw new FormatException("Invalid number at position " + token.Position);
    }
    
    private void Step()
    {
        _index++;
    }

    private string GetString(int start, int length)
    {
        ReadOnlySpan<char> span = _json.Slice(start, length);
        
        if (span.Contains('\\'))
        {
            return ProcessEscapeChars(span);
        }
        
        return span.ToString();
    }

    private string GetPooledString(int start, int length)
    {
        ReadOnlySpan<char> span = _json.Slice(start, length);
        
        if (span.Contains('\\'))
        {
            return ProcessEscapeChars(span);
        }
        
#if NET9_0_OR_GREATER
        var lookup = _stringPool.GetAlternateLookup<ReadOnlySpan<char>>();

        if (lookup.TryGetValue(span, out string? value))
        {
            return value;
        }
        
        // if we dont have it, then add it
        string str = span.ToString();
        _stringPool.Add(str);
        return str;
#else
        string str = span.ToString();
        if (_stringPool.TryGetValue(str, out string? value))
        {
            return value;
        }
        _stringPool.Add(str);
        return str;
#endif
    }
    
    private string ProcessEscapeChars(ReadOnlySpan<char> rawValue)
    {
        StringBuilder sb = new(rawValue.Length);

        for (int i = 0; i < rawValue.Length; i++)
        {
            char c = rawValue[i];

            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }
            
            // next character
            i++;
            
            if (i >= rawValue.Length)
            {
                throw new FormatException("Invalid escape sequence at end of string.");
            }
            
            char escapedChar = rawValue[i];

            switch (escapedChar)
            {
                case '\\': sb.Append('\\'); break;
                case '"': sb.Append('"'); break;
                case '/': sb.Append('/'); break;
                case 'b': sb.Append('\b'); break; 
                case 'f': sb.Append('\f'); break; 
                case 'n': sb.Append('\n'); break; 
                case 'r': sb.Append('\r'); break; 
                case 't': sb.Append('\t'); break;
                case 'u': throw new NotImplementedException("I really dont want to implement this rn");
                default: throw new FormatException("Invalid escape sequence at end of string.");
            }
        }
        return sb.ToString();
    }
    
}