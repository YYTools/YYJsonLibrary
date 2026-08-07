using System.Text;

namespace YYJsonLibrary;

public ref struct YYTokenizer
{
    public YYTokenizer(string json) => _span = json.AsSpan();
    public YYTokenizer(ReadOnlySpan<char> json) => _span = json;
    
    private ReadOnlySpan<char> _span;
    private int _index;
    
    public List<YYTokenData> Tokenize()
    {
        List<YYTokenData> tokens = new(EstimateTokenCount());

        while (_index < _span.Length)
        {
            SkipWhitespace();
            
            if (_index >= _span.Length) break;

            char c = _span[_index];

            int position = _index;
            
            switch (c)
            {
                case '{': tokens.Add(new(YYToken.StartObject, position, 1)); Step(); break;
                case '}': tokens.Add(new(YYToken.EndObject, position, 1)); Step(); break;
                case '[': tokens.Add(new(YYToken.StartArray, position, 1)); Step(); break;
                case ']': tokens.Add(new(YYToken.EndArray, position, 1)); Step(); break;
                case ':': tokens.Add(new(YYToken.Seperator, position, 1)); Step(); break;
                case ',': tokens.Add(new(YYToken.Comma, position, 1)); Step(); break;
                case '"': tokens.Add(new(YYToken.String, position + 1, ReadString())); break;
                case '-' or >= '0' and <= '9': tokens.Add(new(YYToken.Number, position, ReadNumber())); break;
                case 't' or 'f': tokens.Add(new(YYToken.Boolean, position, ReadLiteral(c == 't' ? "true" : "false"))); break;
                case 'n': tokens.Add(new(YYToken.Null, position, ReadLiteral("null"))); break;
                default: throw new FormatException($"Unexpected character '{c}' at position '{position}'");
            }
            
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
        Step();
        
        int startIndex = _index;
        
        while (_index < _span.Length)
        {
            char c = _span[_index];
            
            if (c == '\\')
            {
                Step();
                Step();
                continue;
            }
            
            if (c == '"')
            {
                int length = _index - startIndex;

                // skip closing quotes
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
            
            bool isNumber = c switch
            {
                >= '0' and <= '9' or '-' or '+' or '.' or 'e' or 'E' => true,
                _ => false
            };
            
            if (isNumber)
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

    private char? GetCurrentChar()
    {
        return Peek();
    }
    
    private void SkipWhitespace()
    {
        while (GetCurrentChar() is char c && char.IsWhiteSpace(c))
        {
            Step();
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