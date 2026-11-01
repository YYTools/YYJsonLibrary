using System.Text;

namespace YYJsonLibrary;

public ref struct YYTokenizer
{
    public YYTokenizer(string json) => _span = json.AsSpan();
    public YYTokenizer(ReadOnlySpan<char> json) => _span = json;
    
    private ReadOnlySpan<char> _span;
    private int _index;
    
    public bool NextToken(out YYTokenData token)
    {
        SkipWhitespace();

        if (_index >= _span.Length)
        {
            token = default;
            return false;
        }

        char c = _span[_index];

        int position = _index;
            
        switch (c)
        {
            case '{': token = new (YYToken.StartObject, position, 1); Step(); return true;
            case '}': token = new(YYToken.EndObject, position, 1); Step(); return true;
            case '[': token = new(YYToken.StartArray, position, 1); Step(); return true;
            case ']': token = new(YYToken.EndArray, position, 1); Step(); return true;
            case ':': token = new(YYToken.Separator, position, 1); Step(); return true;
            case ',': token = new(YYToken.Comma, position, 1); Step(); return true;
            case '"': token = new(YYToken.String, position + 1, ReadString()); return true;
            case '-' or >= '0' and <= '9': token = new(YYToken.Number, position, ReadNumber()); return true;
            case 't' or 'f': token = new(YYToken.Boolean, position, ReadLiteral(c == 't' ? "true" : "false")); return true;
            case 'n': token = new(YYToken.Null, position, ReadLiteral("null")); return true;
            default: throw new FormatException($"Unexpected character '{c}' at position '{position}'");
        }

    }
    
    public List<YYTokenData> Tokenize()
    {
        List<YYTokenData> tokens = new(EstimateTokenCount());

        while (NextToken(out YYTokenData token))
        {
            tokens.Add(token);
        }
        
        return tokens;
    }
    
    private int ReadLiteral(string expected)
    {
        if (_span.Slice(_index).StartsWith(expected))
        {
            _index += expected.Length;
            
            char? nextChar = Peek();
            if (nextChar is char c)
            {
                // check if its a malformed string like "truefalse" or "null123"
                if (char.IsLetterOrDigit(c))
                {
                    throw new FormatException($"Invalid literal near position {_index}.");
                }
            }
            
            return expected.Length;
        }
        
        throw new FormatException($"Expected literal '{expected}' at position {_index}.");
    }
    
    private int ReadString()
    {
        // skip opening quote
        Step();
        
        int startIndex = _index;
        
        while (_index < _span.Length)
        {
            char c = _span[_index];
            
            if (c == '\\')
            {
                Step();
                
                // safety
                if (_index >= _span.Length)
                {
                    throw new FormatException("Unexpected EOF while reading escape sequence in string.");
                }
                
                Step();
                continue;
            }
            
            if (c == '"')
            {
                int length = _index - startIndex;

                // skip closing quote
                Step();

                return length;
            }

            Step();
        }

        throw new FormatException("Unexpected EOF while reading string.");
    }

    private int ReadNumber()
    {
        int startIndex = _index;

        while (_index < _span.Length)
        {
            char c = _span[_index];
            
            if (c is (>= '0' and <= '9') or '-' or '+' or '.' or 'e' or 'E')
            {
                Step();
                continue;
            }
            
            // we are out of number
            break;
        }
        int length = _index - startIndex;

        return length;
    }
    
    private int EstimateTokenCount()
    {
        // a really dumb estimation
        return _span.Length / 10;
    }
    
    private void Step() => _index++;
    
    private void SkipWhitespace()
    {
        while (_index < _span.Length && char.IsWhiteSpace(_span[_index]))
        {
            _index++;
        }
    }
    
    private char? Peek(int offset = 0)
    {
        int index = _index + offset;

        if (index >= _span.Length)
        {
            return null; // we are out of range
        }
        
        return _span[index];
    }
}