using System.Reflection;
using YYJsonLibrary;

namespace YYJsonLibraryTests;

public abstract class TestTypeBase
{
    public string Name { get; set; } = string.Empty;
}

public class TestTypeA : TestTypeBase
{
    public int Volume { get; set; }
}

public class TestTypeB : TestTypeBase
{
    public int Count { get; set; } = 9;
}

public abstract class TestContainerBase
{
    public string Tag { get; set; } = string.Empty;
}

public class TestContainer<T> : TestContainerBase where T : new()
{
    public T? Item { get; set; }
}

public class SimplePerson
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public bool IsEmployed { get; set; }
}

public class YYDeserializerTests
{
    public YYDeserializerTests()
    {
        // register the test assembly
        YYDeserializer.RegisterAssembly(typeof(YYDeserializerTests).Assembly);
    }

    [Fact]
    public void Deserialize_SimpleModel_FromString()
    {
        string json = """
                      {
                        "Name": "Nichole",
                        "Age": 20,
                        "IsEmployed": true
                      }
                      """;

        SimplePerson? person = YYDeserializer.Deserialize<SimplePerson>(json);

        Assert.NotNull(person);
        Assert.Equal("Nichole", person.Name);
        Assert.Equal(20, person.Age);
        Assert.True(person.IsEmployed);
    }

    [Fact]
    public void Deserialize_SimpleModel_FromSpan()
    {
        string json = """
                      {
                        "Name": "Bob",
                        "Age": 25,
                        "IsEmployed": false
                      }
                      """;

        SimplePerson? person = YYDeserializer.Deserialize<SimplePerson>(json.AsSpan());

        Assert.NotNull(person);
        Assert.Equal("Bob", person.Name);
        Assert.Equal(25, person.Age);
        Assert.False(person.IsEmployed);
    }

    [Fact]
    public void Deserialize_UsingTypeParameter()
    {
        string json = """{ "Name": "Charlie", "Age": 40 }""";
        object? result = YYDeserializer.Deserialize(json, typeof(SimplePerson));

        Assert.NotNull(result);
        var person = Assert.IsType<SimplePerson>(result);
        Assert.Equal("Charlie", person.Name);
        Assert.Equal(40, person.Age);
    }

    [Fact]
    public void Deserialize_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(YYDeserializer.Deserialize<SimplePerson>(""));
        Assert.Null(YYDeserializer.Deserialize<SimplePerson>("null"));
    }

    [Fact]
    public void Deserialize_PolymorphicTypes()
    {
        string json = """
                {
                    "$TestTypeA": "v1.0",
                    "Name": "ABCD",
                    "Volume": 85
                    }
                """;

        TestTypeBase? typeBase = (TestTypeBase?)YYDeserializer.Deserialize(json, typeof(TestTypeBase));
        Assert.NotNull(typeBase);
        var typeA = Assert.IsType<TestTypeA>(typeBase);
        Assert.Equal("ABCD", typeA.Name);
        Assert.Equal(85, typeA.Volume);

        json = """
                {
                    "$TestTypeB": "v1.0",
                    "Name": "WASD",
                    "Count": 7
                }
                """;

        typeBase = (TestTypeBase?)YYDeserializer.Deserialize(json, typeof(TestTypeBase));
        Assert.NotNull(typeBase);
        var typeB = Assert.IsType<TestTypeB>(typeBase);
        Assert.Equal("WASD", typeB.Name);
        Assert.Equal(7, typeB.Count);
    }

    [Fact]
    public void Deserialize_PolymorphicGenericType()
    {
        string containerJson = """
                               {
                                 "$TestContainer<TestTypeA>": "v1",
                                 "Tag": "idk",
                                 "Item": {
                                   "Name": "big gun",
                                   "Volume": 90
                                 }
                               }
                               """;

        TestContainerBase? containerBase = (TestContainerBase?)YYDeserializer.Deserialize(containerJson, typeof(TestContainerBase));
        Assert.NotNull(containerBase);
        var container = Assert.IsType<TestContainer<TestTypeA>>(containerBase);
        Assert.Equal("idk", container.Tag);
        Assert.NotNull(container.Item);
        Assert.Equal("big gun", container.Item.Name);
        Assert.Equal(90, container.Item.Volume);
    }
}
