using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace YYJsonLibrary;

public ref struct YYParser
{
    private ReadOnlySpan<char> _json;
    private YYTokenizer _tokenizer;
    private YYTokenData _current;
    private bool _hasToken;
    private HashSet<string> _stringPool;
    
    public YYParser(string json) : this(json.AsSpan()) { }

    public YYParser(ReadOnlySpan<char> json)
    {
        _json = json;
        _tokenizer = new YYTokenizer(json);
        _stringPool = new HashSet<string>();
        _hasToken = _tokenizer.NextToken(out _current);
    }
    
    public YYValue? Parse()
    {
        if (!_hasToken)
        {
            return null;
        }
        
        return ParseValue();
    }

    private YYValue ParseValue()
    {
        if (!_hasToken)
        {
            throw new FormatException("Unexpected EOF while expecting a value.");
        }
        
        YYTokenData token = _current;
        
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
        
        while (_hasToken)
        {
            if (_current.Token == YYToken.EndObject)
            {
                Step();
                return obj;
            }
            
            if (_current.Token != YYToken.String)
            {
                throw new FormatException("Expected string token at position " + _current.Position);
            }
            
            string key = GetPooledString(_current.Position, _current.Length);    
            Step();

            if (!_hasToken || _current.Token != YYToken.Separator)
            {
                throw new FormatException($"Expected ':' separator after key '{key}'");
            }
            
            Step();
            
            obj.Properties[key] = ParseValue();
            
            // skip commas
            if (_hasToken && _current.Token == YYToken.Comma)
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
        
        while (_hasToken)
        {
            if (_current.Token == YYToken.EndArray)
            {
                Step();
                return array;
            }
            array.Items.Add(ParseValue());
            
            // skip commas
            if (_hasToken && _current.Token == YYToken.Comma)
            {
                Step();
            }
        }
        throw new FormatException("Unexpected EOF while parsing array");
    }

    private YYValue ParseString(YYTokenData token)
    {
        return new YYString(GetString(token.Position, token.Length));
    }

    private YYValue ParseBool(YYTokenData token)
    {
        ReadOnlySpan<char> span = _json.Slice(token.Position, token.Length);
        
        return new YYBool(span.SequenceEqual("true"));
    }

    private YYValue ParseNumber(YYTokenData token)
    {
        ReadOnlySpan<char> span = _json.Slice(token.Position, token.Length);
        
        if (double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            bool isFloat = span.Contains('.') || span.Contains('e') || span.Contains('E');
            return new YYNumber(value, isFloat);
        }
        
        throw new FormatException("Invalid number at position " + token.Position);
    }
    
    private void Step()
    {
        _hasToken = _tokenizer.NextToken(out _current);
    }

    private string GetString(int start, int length)
    {
        ReadOnlySpan<char> span = _json.Slice(start, length);
        
        return (span.Contains('\\')) ? ProcessEscapeChars(span) : span.ToString();
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
                case 'u': 
                    if (i + 4 >= rawValue.Length || !ushort.TryParse(rawValue.Slice(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort hex))
                    {
                        throw new FormatException("Invalid unicode escape sequence.");
                    }
                    sb.Append((char)hex);
                    i += 4;
                    break;
                default: throw new FormatException("Invalid escape sequence at end of string.");
            }
        }
        return sb.ToString();
    }
    
}