using System.Text.Json;
using System.Text.Json.Serialization;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;

namespace Sdk.Common.Tests;

public class DebugTests
{
    [Fact]
    public void NodePath_SerializeDeserialize_OK()
    {
        NodePath nodePath = new("demo/test");
        string jsonString = JsonSerializer.Serialize(nodePath);

        var deserialized = JsonSerializer.Deserialize<NodePath>(jsonString);

        Assert.Equivalent(nodePath, deserialized);
    }

    [Fact]
    public void DebugPointDto_SerializeDeserialize_OK()
    {
        DebugPointDto debugPointDto = new DebugPointDto("demo/test", new NodePath("demo/test"), null, 0)
        {
            Input = "input-test"
        };
        string jsonString = JsonSerializer.Serialize(debugPointDto);

        var deserialized = JsonSerializer.Deserialize<DebugPointDto>(jsonString);

        Assert.Equivalent(debugPointDto, deserialized);
    }

    [Fact]
    public void DebugPointDto_WithMessages_SerializeDeserialize_OK()
    {
        DebugPointDto debugPointDto = new DebugPointDto("demo/test", new NodePath("demo/test"), null, 0)
        {
            Input = "input-test"
        };
        debugPointDto.Messages = new List<DebugMessage>
        {
            new(LoggerSeverity.Debug, "testId", "testPath", "testmessage", new DateTime(2023, 1, 1, 1, 1, 1))
        };
        string jsonString = JsonSerializer.Serialize(debugPointDto);

        var deserialized = JsonSerializer.Deserialize<DebugPointDto>(jsonString);

        Assert.Equivalent(debugPointDto, deserialized);
    }
}