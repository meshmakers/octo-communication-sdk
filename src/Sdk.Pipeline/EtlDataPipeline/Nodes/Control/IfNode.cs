using System.Text.Json.Nodes;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;

/// <summary>
/// Defines the comparison operator for an if node.
/// </summary>
public enum CompareOperator
{
    /// <summary>
    /// Equal operator.
    /// </summary>
    Equal = Equals,

    /// <summary>
    /// Equal operator.
    /// </summary>
    Equals = 0,

    /// <summary>
    /// Not equal operator.
    /// </summary>
    NotEqual = NotEquals,

    /// <summary>
    /// Equal operator.
    /// </summary>
    NotEquals = 1,

    /// <summary>
    /// Contains operator if the value is a string.
    /// </summary>
    Contain = 2,

    /// <summary>
    /// Contains operator if the value is a string.
    /// </summary>
    Contains = Contain,

    /// <summary>
    /// Less than operator.
    /// </summary>
    LessThan = 3,

    /// <summary>
    /// Less or equal than operator.
    /// </summary>
    LessEqualsThan = 4,

    /// <summary>
    /// Greater than operator.
    /// </summary>
    GreaterThan = 5,

    /// <summary>
    /// Greater or equal than operator.
    /// </summary>
    GreaterEqualsThan = 6,

    /// <summary>
    /// Starts with operator if the value is a string.
    /// </summary>
    StartsWith = 7,

    /// <summary>
    /// Ends with operator if the value is a string.
    /// </summary>
    EndsWith = 8,

    /// <summary>
    /// Regex match operator if the value is a string.
    /// </summary>
    RegexMatch = 9
}

/// <summary>
/// Configuration for an if node.
/// </summary>
[NodeName("If", 1)]
public record IfNodeConfiguration : PathNodeConfiguration, IChildNodeConfiguration
{
    /// <inheritdoc />
    public required ICollection<NodeConfiguration>? Transformations { get; set; }

    /// <summary>
    /// Defines the comparison operator.
    /// </summary>
    [PropertyGroup("Options", 0)]
    public CompareOperator Operator { get; set; } = CompareOperator.Equal;

    /// <summary>
    /// The path to the value to compare. Either this or <see cref="Value"/> must be set.
    /// </summary>
    [PropertyGroup("Paths", 2, "jsonpath")]
    public string? ValuePath { get; set; }

    /// <summary>
    /// The value to compare. Either this or <see cref="ValuePath"/> must be set.
    /// </summary>
    [PropertyGroup("Options", 1)]
    public object? Value { get; set; }

    /// <summary>
    /// Defines the value type
    /// </summary>
    [PropertyGroup("Options", 2)]
    public AttributeValueTypesDto ValueType { get; set; }
}

/// <summary>
/// A node that executes a set of transformations if a condition is met.
/// </summary>
[NodeConfiguration(typeof(IfNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
public class IfNode(NodeDelegate next) : ChildNodeBase
{
    /// <inheritdoc />
    public override async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var nodeConfiguration = nodeContext.GetNodeConfiguration<IfNodeConfiguration>();

        // We support equal with null values!
        var comparisonValue = GetComparisonValue(nodeContext, dataContext, nodeConfiguration);
        var value = GetValueFromDataContext(nodeContext, dataContext, nodeConfiguration.Path, nodeConfiguration.ValueType);

        switch (nodeConfiguration.Operator)
        {
            case CompareOperator.Equal:
                if ((value?.Equals(comparisonValue) ?? false) || value == null && comparisonValue == null)
                {
                    await IterateElement(dataContext, nodeContext, nodeConfiguration);
                }

                break;
            case CompareOperator.NotEqual:
                if (!((value?.Equals(comparisonValue) ?? false) || value == null && comparisonValue == null))
                {
                    await IterateElement(dataContext, nodeContext, nodeConfiguration);
                }

                break;
            case CompareOperator.Contains:
                if (comparisonValue != null && value != null &&
                    (value.ToString()?.ToLower().Contains(comparisonValue.ToString()?.ToLower() ?? "") ?? false))
                {
                    await IterateElement(dataContext, nodeContext, nodeConfiguration);
                }

                break;

            case CompareOperator.GreaterThan:
                if (value is IComparable comparableValue1 && comparableValue1.CompareTo(comparisonValue) > 0)
                {
                    await IterateElement(dataContext, nodeContext, nodeConfiguration);
                }

                break;

            case CompareOperator.GreaterEqualsThan:
                if (value is IComparable comparableValue2 && comparableValue2.CompareTo(comparisonValue) >= 0)
                {
                    await IterateElement(dataContext, nodeContext, nodeConfiguration);
                }

                break;

            case CompareOperator.LessThan:
                if (value is IComparable comparableValue3 && comparableValue3.CompareTo(comparisonValue) < 0)
                {
                    await IterateElement(dataContext, nodeContext, nodeConfiguration);
                }

                break;

            case CompareOperator.LessEqualsThan:
                if (value is IComparable comparableValue4 && comparableValue4.CompareTo(comparisonValue) <= 0)
                {
                    await IterateElement(dataContext, nodeContext, nodeConfiguration);
                }

                break;

            case CompareOperator.StartsWith:
                if (comparisonValue != null && value != null &&
                    (value.ToString()?.ToLower().StartsWith(comparisonValue.ToString()?.ToLower() ?? "") ?? false))
                {
                    await IterateElement(dataContext, nodeContext, nodeConfiguration);
                }

                break;

            case CompareOperator.EndsWith:
                if (comparisonValue != null && value != null &&
                    (value.ToString()?.ToLower().EndsWith(comparisonValue.ToString()?.ToLower() ?? "") ?? false))
                {
                    await IterateElement(dataContext, nodeContext, nodeConfiguration);
                }

                break;

            case CompareOperator.RegexMatch:
                if (comparisonValue != null && value != null &&
                    System.Text.RegularExpressions.Regex.IsMatch(value.ToString() ?? "", comparisonValue.ToString() ?? ""))
                {
                    await IterateElement(dataContext, nodeContext, nodeConfiguration);
                }

                break;

            default:
                nodeContext.Error($"Operator '{nodeConfiguration.Operator}' is not supported.");
                throw PipelineConfigurationException.InvalidOperation($"Operator '{nodeConfiguration.Operator}' is not supported.");
        }


        await next(dataContext, nodeContext);
    }

    private static async Task IterateElement(IDataContext dataContext, INodeContext nodeContext, IfNodeConfiguration nodeConfiguration)
    {
        var last = new NodeDelegate((ds, _) =>
        {
            // Mirror the inner data context's root state back to the outer context.
            var node = ds.Get<JsonNode>("$");
            dataContext.Set("$", node);
            return Task.CompletedTask;
        });

        nodeContext.Debug("Condition met. Processing child transformations.");
        await ProcessChildTransformationsAsSequenceAsync(dataContext, nodeContext, last, nodeConfiguration);
        nodeContext.Debug("Child transformations done.");
    }

    private static object? GetComparisonValue(INodeContext nodeContext, IDataContext dataContext, IfNodeConfiguration nodeConfiguration)
    {
        if (nodeConfiguration.Value != null)
        {
            return nodeConfiguration.ValueType switch
            {
                AttributeValueTypesDto.Boolean => Convert.ToBoolean(nodeConfiguration.Value),
                AttributeValueTypesDto.Int => Convert.ToInt32(nodeConfiguration.Value),
                AttributeValueTypesDto.Int64 => Convert.ToInt64(nodeConfiguration.Value),
                AttributeValueTypesDto.Double => Convert.ToDouble(nodeConfiguration.Value),
                AttributeValueTypesDto.String => (string)nodeConfiguration.Value,
                AttributeValueTypesDto.DateTime => Convert.ToDateTime(nodeConfiguration.Value),
                AttributeValueTypesDto.Enum => Convert.ToInt32(nodeConfiguration.Value),
                _ => throw PipelineExecutionException.DefinedValueTypeNotSupported(nodeContext.NodePath, nodeConfiguration.ValueType, nodeConfiguration.Value)
            };
        }

        if (nodeConfiguration.ValuePath != null)
        {
           return GetValueFromDataContext(nodeContext, dataContext, nodeConfiguration.ValuePath, nodeConfiguration.ValueType);
        }

        // if the value is null, it CAN be a valid case, we just want to make sure that some value is defined
        return null;
    }

    private static object? GetValueFromDataContext(INodeContext nodeContext, IDataContext dataContext, string path,
        AttributeValueTypesDto valueType)
    {
        object? value = valueType switch
        {
            AttributeValueTypesDto.Boolean => dataContext.Get<bool?>(path),
            AttributeValueTypesDto.Int => dataContext.Get<int?>(path),
            AttributeValueTypesDto.Int64 => dataContext.Get<long?>(path),
            AttributeValueTypesDto.Double => dataContext.Get<double?>(path),
            AttributeValueTypesDto.String => dataContext.Get<string>(path),
            AttributeValueTypesDto.DateTime => dataContext.Get<DateTime?>(path),
            AttributeValueTypesDto.Enum => dataContext.Get<int?>(path),
            _ => throw PipelineExecutionException.ValueTypeNotSupported(nodeContext.NodePath, valueType, path)
        };
        return value;
    }
}
