using YYJsonLibrary;

namespace YYJsonLibraryTests;

public class YYTokenizerTests
{
    [Fact]
    public void HandlesFormatted()
    {
        string json = """
                      {
                        "name": "Test",
                        "id": 12345,
                        "trueBool": true,
                        "falseBool": false,
                        "nullValue": null,
                        "nums": [98.5, -42, 3.14e-2],
                        "metadata": {
                          "path": "C:\\tests\\data.json",
                          "quote": "\"Success\"\n\t"
                        }
                      }
                      """;

        var tokenizer = new YYTokenizer(json);
        List<YYTokenData> tokens = tokenizer.Tokenize();
        
        Assert.Equal(9, tokens.Count(t => t.Token == YYToken.Separator));
        
        Assert.Equal(1, tokens.Count(t => t.Token == YYToken.StartArray));
        
        Assert.Equal(1, tokens.Count(t => t.Token == YYToken.EndArray));
        
        Assert.Equal(2, tokens.Count(t => t.Token == YYToken.StartObject));
        
        Assert.Equal(2, tokens.Count(t => t.Token == YYToken.EndObject));
    }

    [Fact]
    public void HandlesCompact()
    {
        string json = """
                      {"name":"Test","id":12345,"trueBool":true,"falseBool":false,"nullValue":null,"nums":[98.5,-42,3.14e-2],"metadata":{"path":"C:\\tests\\data.json","quote":"\"Success\"\n\t"}}
                      """;

        var tokenizer = new YYTokenizer(json);
        List<YYTokenData> tokens = tokenizer.Tokenize();
        
        Assert.Equal(9, tokens.Count(t => t.Token == YYToken.Separator));
        
        Assert.Equal(1, tokens.Count(t => t.Token == YYToken.StartArray));
        
        Assert.Equal(1, tokens.Count(t => t.Token == YYToken.EndArray));
        
        Assert.Equal(2, tokens.Count(t => t.Token == YYToken.StartObject));
        
        Assert.Equal(2, tokens.Count(t => t.Token == YYToken.EndObject));
    }

    [Fact]
    public void ThrowsOnInvalid()
    {
        string json = """
                      {HELLOIMINVAID: 6, "t":4}
                      """;
        var exception = Assert.Throws<FormatException>(() =>
        {
            var tokenizer = new YYTokenizer(json);
            tokenizer.Tokenize();
        });
        Assert.Contains("Unexpected character", exception.Message);
    }
    
    [Fact]
    public void ThrowsOnInvalidLiteral()
    {
        string json = """
                      {"t":tru,}
                      """;
        var exception = Assert.Throws<FormatException>(() =>
        {
            var tokenizer = new YYTokenizer(json);
            tokenizer.Tokenize();
        });
        Assert.Contains("Expected literal", exception.Message);
    }
    
    [Fact]
    public void ThrowsOnMalformedLiteral()
    {
        string json = """
                      {"t":truefalse,}
                      """;
        var exception = Assert.Throws<FormatException>(() =>
        {
            var tokenizer = new YYTokenizer(json);
            tokenizer.Tokenize();
        });
        Assert.Contains("Invalid literal", exception.Message);
    }

    [Fact]
    public void ThrowsOnEOFWhileReadingString()
    {
        string json = """
                      {"t":"hello}
                      """;
        var exception = Assert.Throws<FormatException>(() =>
        {
            var tokenizer = new YYTokenizer(json);
            tokenizer.Tokenize();
        });
        Assert.Contains("Unexpected EOF while reading string.", exception.Message);
    }

    [Fact]
    public void ThrowsOnEOFWhileReadingEscapeSequence()
    {
        string json = "{\"t\":\"hello\\";
        var exception = Assert.Throws<FormatException>(() =>
        {
            var tokenizer = new YYTokenizer(json);
            tokenizer.Tokenize();
        });
        Assert.Contains("Unexpected EOF while reading escape sequence in string.", exception.Message);
    }

    [Fact]
    public void HandlesEmptyAndWhitespace()
    {
        var tokenizerEmpty = new YYTokenizer("");
        Assert.False(tokenizerEmpty.NextToken(out _));
        Assert.Empty(tokenizerEmpty.Tokenize());

        var tokenizerWhitespace = new YYTokenizer("  \t\r\n  ");
        Assert.False(tokenizerWhitespace.NextToken(out _));
        Assert.Empty(tokenizerWhitespace.Tokenize());
    }

    [Fact]
    public void HandlesScientificNumbers()
    {
        string json = "[1e5, -3.14e+2, 1.5E-3, 0]";
        var tokenizer = new YYTokenizer(json);
        List<YYTokenData> tokens = tokenizer.Tokenize();

        Assert.Equal(YYToken.StartArray, tokens[0].Token);
        Assert.Equal(YYToken.Number, tokens[1].Token);
        Assert.Equal(YYToken.Comma, tokens[2].Token);
        Assert.Equal(YYToken.Number, tokens[3].Token);
        Assert.Equal(YYToken.Comma, tokens[4].Token);
        Assert.Equal(YYToken.Number, tokens[5].Token);
        Assert.Equal(YYToken.Comma, tokens[6].Token);
        Assert.Equal(YYToken.Number, tokens[7].Token);
        Assert.Equal(YYToken.EndArray, tokens[8].Token);
    }

    [Fact]
    public void HandlesEscapedQuotesAndBackslashes()
    {
        string json = """["\"escaped quote\"", "backslash\\\\here"]""";
        var tokenizer = new YYTokenizer(json);
        List<YYTokenData> tokens = tokenizer.Tokenize();

        Assert.Equal(YYToken.StartArray, tokens[0].Token);
        Assert.Equal(YYToken.String, tokens[1].Token);
        Assert.Equal(YYToken.Comma, tokens[2].Token);
        Assert.Equal(YYToken.String, tokens[3].Token);
        Assert.Equal(YYToken.EndArray, tokens[4].Token);
    }
}
