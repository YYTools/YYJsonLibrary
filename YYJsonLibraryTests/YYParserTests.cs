using YYJsonLibrary;

namespace YYJsonLibraryTests;

public class YYParserTests
{
    [Fact]
    public void HandlesTooManyBrackets()
    {
        string json = """
                    {{ "hello": 4 }}
                    """;
        
        var exception = Assert.Throws<FormatException>(() =>
        {
            YYParser parser = new YYParser(json);
            parser.Parse();
        });
        Assert.Contains("Expected string token at position", exception.Message);
    }

    [Fact]
    public void HandlesMissingSeparator()
    {
        string json = """
                      { "hello" 4 }
                      """;
        
        var exception = Assert.Throws<FormatException>(() =>
        {
            YYParser parser = new YYParser(json);
            parser.Parse();
        });
        Assert.Contains("Expected ':' separator after key", exception.Message);
    }

    [Fact]
    public void HandlesMalformedObject()
    {
        string json = """
                      { "value": { "hello": 4 }
                      """;
        
        var exception = Assert.Throws<FormatException>(() =>
        {
            YYParser parser = new YYParser(json);
            parser.Parse();
        });
        Assert.Contains("Unexpected EOF while parsing object", exception.Message);
    }
    
    [Fact]
    public void HandlesEmptyObject()
    {
        string json = """
                      {}
                      """;
        
        YYParser parser = new YYParser(json);
        YYValue? obj = parser.Parse();
        Assert.NotNull(obj);
        YYObject o = Assert.IsType<YYObject>(obj);
        Assert.Empty(o.Properties);
    }

    [Fact]
    public void HandlesNullObject()
    {
        string json = "null";
        
        YYParser parser = new(json);
        YYValue? obj = parser.Parse();
        
        Assert.NotNull(obj);
        Assert.IsType<YYNull>(obj);
    }

    [Fact]
    public void HandlesEscapeSequences()
    {
        string json = """
                      { "value": "\"\\\/\b\f\n\r\t"}
                      """;
        YYParser parser = new(json);
        YYValue? obj = parser.Parse();
        
        Assert.NotNull(obj);
        string data = obj["value"].AsString();

        Assert.Equal("\"\\/\b\f\n\r\t", data);
    }

    [Fact]
    public void HandlesUnicodeEscapeSequences()
    {
        string json = """
                      { "ascii": "\u0041\u0042\u0043", "symbol": "\u2603" }
                      """;
        YYParser parser = new YYParser(json);
        YYValue? obj = parser.Parse();

        Assert.NotNull(obj);
        Assert.Equal("ABC", obj["ascii"].AsString());
        Assert.Equal("☃", obj["symbol"].AsString());
    }

    [Fact]
    public void ThrowsOnInvalidUnicodeEscape()
    {
        string jsonShort = """{ "val": "\u12" }""";
        Assert.Throws<FormatException>(() => new YYParser(jsonShort).Parse());

        string jsonNonHex = """{ "val": "\uZZZZ" }""";
        Assert.Throws<FormatException>(() => new YYParser(jsonNonHex).Parse());
    }

    [Fact]
    public void ThrowsOnInvalidEscapeChar()
    {
        string json = """{ "val": "\q" }""";
        var ex = Assert.Throws<FormatException>(() => new YYParser(json).Parse());
        Assert.Contains("Invalid escape sequence", ex.Message);
    }

    [Fact]
    public void HandlesArrays()
    {
        string json = """[10, "test", true, false, null, [1, 2]]""";
        YYParser parser = new YYParser(json);
        YYValue? val = parser.Parse();

        Assert.NotNull(val);
        var array = Assert.IsType<YYArray>(val);
        Assert.Equal(6, array.Items.Count);

        Assert.Equal(10, array[0].AsNumber());
        Assert.Equal("test", array[1].AsString());
        Assert.True(array[2].AsBool());
        Assert.False(array[3].AsBool());
        Assert.IsType<YYNull>(array[4]);

        var nestedArray = Assert.IsType<YYArray>(array[5]);
        Assert.Equal(2, nestedArray.Items.Count);
        Assert.Equal(1, nestedArray[0].AsNumber());
        Assert.Equal(2, nestedArray[1].AsNumber());
    }

    [Fact]
    public void ThrowsOnUnclosedArray()
    {
        string json = "[1, 2, 3";
        var ex = Assert.Throws<FormatException>(() => new YYParser(json).Parse());
        Assert.Contains("Unexpected EOF while parsing array", ex.Message);
    }

    [Fact]
    public void HandlesNumbersAndFloats()
    {
        string json = """{"int": 42, "float": 42.5, "exp": 1e3, "negative": -17}""";
        YYParser parser = new YYParser(json);
        YYValue? val = parser.Parse();

        Assert.NotNull(val);
        var intNum = Assert.IsType<YYNumber>(val["int"]);
        Assert.False(intNum.IsFloatingPoint);
        Assert.Equal(42, intNum.AsInt());

        var floatNum = Assert.IsType<YYNumber>(val["float"]);
        Assert.True(floatNum.IsFloatingPoint);
        Assert.Equal(42.5, floatNum.AsNumber());

        var expNum = Assert.IsType<YYNumber>(val["exp"]);
        Assert.True(expNum.IsFloatingPoint);
        Assert.Equal(1000, expNum.AsNumber());

        var negNum = Assert.IsType<YYNumber>(val["negative"]);
        Assert.False(negNum.IsFloatingPoint);
        Assert.Equal(-17, negNum.AsNumber());
    }

    [Fact]
    public void HandlesEmptyOrWhitespaceInput()
    {
        Assert.Null(new YYParser("").Parse());
        Assert.Null(new YYParser("    \t\r\n").Parse());
    }

    [Fact]
    public void ValueAccessorExceptions()
    {
        string json = """{"key": "value"}""";
        YYValue val = new YYParser(json).Parse()!;

        Assert.Throws<KeyNotFoundException>(() => val["non_existent"]);
        Assert.Throws<InvalidOperationException>(() => val[0]);
        Assert.Throws<InvalidOperationException>(() => val.AsNumber());
        Assert.Throws<InvalidOperationException>(() => val.AsString());
        Assert.Throws<InvalidOperationException>(() => val.AsBool());

        YYValue num = val["key"];
        Assert.Throws<InvalidOperationException>(() => num.AsNumber());
        Assert.Throws<InvalidOperationException>(() => num.AsBool());
        Assert.Throws<InvalidOperationException>(() => num["key"]);
    }

    [Fact]
    public void PoolsPropertyKeyStrings()
    {
        string json = """[{"duplicateKey": 1}, {"duplicateKey": 2}]""";
        YYValue val = new YYParser(json).Parse()!;
        var array = Assert.IsType<YYArray>(val);

        var obj1 = Assert.IsType<YYObject>(array[0]);
        var obj2 = Assert.IsType<YYObject>(array[1]);

        string key1 = obj1.Properties.Keys.First();
        string key2 = obj2.Properties.Keys.First();

        Assert.Equal("duplicateKey", key1);
        Assert.Same(key1, key2); // reference identity verification
    }
}
