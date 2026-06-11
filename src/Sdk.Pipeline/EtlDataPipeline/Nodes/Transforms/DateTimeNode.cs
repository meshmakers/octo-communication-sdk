using System.Globalization;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Represents a date/time operation that can be performed on data.
/// </summary>
public enum DateTimeOperationDto
{
    /// <summary>
    /// Returns the current UTC date and time.
    /// </summary>
    Now = 0,

    /// <summary>
    /// Truncates a date/time value to midnight (start of day).
    /// </summary>
    StartOfDay = 1,

    /// <summary>
    /// Adds a specified number of days to a date/time value.
    /// </summary>
    AddDays = 2,

    /// <summary>
    /// Adds a specified number of hours to a date/time value.
    /// </summary>
    AddHours = 3,

    /// <summary>
    /// Adds a specified number of minutes to a date/time value.
    /// </summary>
    AddMinutes = 4,

    /// <summary>
    /// Adds a specified number of seconds to a date/time value.
    /// </summary>
    AddSeconds = 5,

    /// <summary>
    /// Computes the whole number of days between two dates (date-only comparison).
    /// </summary>
    DaysBetween = 6,

    /// <summary>
    /// Formats a date/time value using a .NET format string.
    /// </summary>
    Format = 7,

    /// <summary>
    /// Combines the date part from one value with the time part from another.
    /// </summary>
    CombineDateTime = 8,

    /// <summary>
    /// Extracts the date part (truncates to midnight). Alias for StartOfDay.
    /// </summary>
    ExtractDate = 9,

    /// <summary>
    /// Extracts the time part as a string in "HH:mm:ss" format.
    /// </summary>
    ExtractTime = 10,
}

/// <summary>
/// Represents a configuration for a DateTime node that performs date/time operations on data.
/// </summary>
[NodeName("DateTime", 1)]
public record DateTimeNodeConfiguration : SourceTargetPathNodeConfiguration
{
    /// <summary>
    /// Specifies the date/time operation to be performed on the data.
    /// </summary>
    [PropertyGroup("Options", 0)]
    public required DateTimeOperationDto Operation { get; init; }

    /// <summary>
    /// A static value to be used in the operation. For Add* operations this is a number,
    /// for Format it is the .NET format string.
    /// </summary>
    [PropertyGroup("Options", 1)]
    public object? Value { get; init; }

    /// <summary>
    /// The path to a dynamic value in the data context. Takes precedence over Value when set.
    /// </summary>
    [PropertyGroup("Paths", 2, "jsonpath")]
    public string? ValuePath { get; init; }
}

/// <summary>
/// DateTime node that performs date/time operations on pipeline data.
/// Supports arithmetic (Add*), comparison (DaysBetween), formatting, and extraction operations.
/// </summary>
/// <param name="next">The next node in the pipeline to execute after this operation.</param>
[NodeConfiguration(typeof(DateTimeNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
public class DateTimeNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<DateTimeNodeConfiguration>();
        var rootKind = dataContext.GetKind("$");
        if (rootKind == DataKind.Undefined || rootKind == DataKind.Null)
        {
            throw PipelineExecutionException.InputValueNull(nodeContext);
        }

        object? result = c.Operation switch
        {
            DateTimeOperationDto.Now => DateTime.UtcNow,
            DateTimeOperationDto.StartOfDay => GetSourceDateTime(dataContext, c, nodeContext).Date,
            DateTimeOperationDto.AddDays => GetSourceDateTime(dataContext, c, nodeContext)
                .AddDays(GetNumericValue(dataContext, c, nodeContext)),
            DateTimeOperationDto.AddHours => GetSourceDateTime(dataContext, c, nodeContext)
                .AddHours(GetNumericValue(dataContext, c, nodeContext)),
            DateTimeOperationDto.AddMinutes => GetSourceDateTime(dataContext, c, nodeContext)
                .AddMinutes(GetNumericValue(dataContext, c, nodeContext)),
            DateTimeOperationDto.AddSeconds => GetSourceDateTime(dataContext, c, nodeContext)
                .AddSeconds(GetNumericValue(dataContext, c, nodeContext)),
            DateTimeOperationDto.DaysBetween => ComputeDaysBetween(dataContext, c, nodeContext),
            DateTimeOperationDto.Format => GetSourceDateTime(dataContext, c, nodeContext)
                .ToString(GetStringValue(c, nodeContext), CultureInfo.InvariantCulture),
            DateTimeOperationDto.CombineDateTime => CombineDateAndTime(dataContext, c, nodeContext),
            DateTimeOperationDto.ExtractDate => GetSourceDateTime(dataContext, c, nodeContext).Date,
            DateTimeOperationDto.ExtractTime => GetSourceDateTime(dataContext, c, nodeContext)
                .ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            _ => throw new NotSupportedException($"Operation {c.Operation} is not supported")
        };

        dataContext.Set(c.TargetPath, result, c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);

        await next(dataContext, nodeContext).ConfigureAwait(false);
    }

    private static DateTime GetSourceDateTime(IDataContext dataContext, DateTimeNodeConfiguration config,
        INodeContext nodeContext)
    {
        var value = dataContext.Get<DateTime?>(config.Path);
        if (value == null)
        {
            throw PipelineExecutionException.ValueNotSet(nodeContext, config.Path);
        }

        return value.Value;
    }

    private static double GetNumericValue(IDataContext dataContext, DateTimeNodeConfiguration config,
        INodeContext nodeContext)
    {
        if (!string.IsNullOrWhiteSpace(config.ValuePath))
        {
            if (!dataContext.Exists(config.ValuePath!) ||
                dataContext.GetKind(config.ValuePath!) == DataKind.Null)
            {
                throw PipelineExecutionException.ValueNotSet(nodeContext, config.ValuePath);
            }

            // Use typed Get<double>() so STJ deserializes JSON numbers/strings directly,
            // avoiding the boxed-JsonElement / Convert.ToDouble incompatibility.
            return dataContext.Get<double>(config.ValuePath!);
        }

        if (config.Value == null)
        {
            throw PipelineExecutionException.ValueNotSet(nodeContext, null);
        }

        return Convert.ToDouble(config.Value, CultureInfo.InvariantCulture);
    }

    private static string GetStringValue(DateTimeNodeConfiguration config, INodeContext nodeContext)
    {
        var value = config.Value?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw PipelineExecutionException.ValueNotSet(nodeContext, null);
        }

        return value!;
    }

    private static int ComputeDaysBetween(IDataContext dataContext, DateTimeNodeConfiguration config,
        INodeContext nodeContext)
    {
        var sourceDate = GetSourceDateTime(dataContext, config, nodeContext);

        DateTime valueDate;
        if (!string.IsNullOrWhiteSpace(config.ValuePath))
        {
            var pathValue = dataContext.Get<DateTime?>(config.ValuePath!);
            if (pathValue == null)
            {
                throw PipelineExecutionException.ValueNotSet(nodeContext, config.ValuePath);
            }

            valueDate = pathValue.Value;
        }
        else
        {
            throw PipelineExecutionException.ValueNotSet(nodeContext, null);
        }

        return (int)(valueDate.Date - sourceDate.Date).TotalDays;
    }

    private static DateTime CombineDateAndTime(IDataContext dataContext, DateTimeNodeConfiguration config,
        INodeContext nodeContext)
    {
        var dateSource = GetSourceDateTime(dataContext, config, nodeContext);

        DateTime timeSource;
        if (!string.IsNullOrWhiteSpace(config.ValuePath))
        {
            var pathValue = dataContext.Get<DateTime?>(config.ValuePath!);
            if (pathValue == null)
            {
                throw PipelineExecutionException.ValueNotSet(nodeContext, config.ValuePath);
            }

            timeSource = pathValue.Value;
        }
        else
        {
            throw PipelineExecutionException.ValueNotSet(nodeContext, null);
        }

        return new DateTime(dateSource.Year, dateSource.Month, dateSource.Day,
            timeSource.Hour, timeSource.Minute, timeSource.Second,
            timeSource.Millisecond, DateTimeKind.Utc);
    }
}
