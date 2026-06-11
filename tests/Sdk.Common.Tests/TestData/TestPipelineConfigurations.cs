using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Sdk.Common.Tests.TestData.Dto;

namespace Sdk.Common.Tests.TestData;

internal static class TestPipelineConfigurations
{
    public static NodeDefinitionRoot TestDataMultipleNodes => new()
    {
        Transformations = new List<NodeConfiguration>
        {
            new TestOutputNodeConfiguration
            {
                TargetPath = "$.TestOutput0",
                TargetValue = 100
            },
            new TestOutputNodeConfiguration
            {
                TargetPath = "$.TestOutput1",
                TargetValue = 101
            },
            new TestOutputNodeConfiguration
            {
                TargetPath = "$.TestOutput2",
                TargetValue = 102
            },
            new TestOutputNodeConfiguration
            {
                TargetPath = "$.TestOutput3",
                TargetValue = 103
            }
        }
    };
    
    public static NodeDefinitionRoot TestDataSingleNode => new()
    {
        Transformations = new List<NodeConfiguration>
        {
            new TestOutputNodeConfiguration
            {
                TargetPath = "$.TestOutput",
                TargetValue = 100
            }
        }
    };
    
    public static NodeDefinitionRoot Test2 => new()
    {
        Transformations = new List<NodeConfiguration>
        {
            new ExceptionNodeConfiguration()
        }
    };
    
    public static NodeDefinitionRoot Test1 => Test1WithOrder(Generator.GenerateOrder());

    public static NodeDefinitionRoot Test1WithOrder(Order order) => new()
    {
        Transformations = new List<NodeConfiguration>
        {
            new TestDataExtractNodeConfiguration
            {
                Description = "Test data extract node",
                Data = order
            },
            new SelectByPathNodeConfiguration
            {
                Description = "Transform object node",
                SelectPath = new List<PathPropertyConfigurationNode>
                {
                    new()
                    {
                        Path = "$.InvoiceNumber",
                        TargetPath = "$.InvoiceNumber",
                        Transformations = new List<NodeConfiguration>
                        {
                            new LinearScalerNodeConfiguration
                            {
                                ScaleInputMin = 0,
                                ScaleInputMax = 100,
                                ScaleOutputMin = 0,
                                ScaleOutputMax = 1000
                            },
                            new ConvertDataTypeNodeConfiguration
                            {
                                ValueType = AttributeValueTypesDto.Double
                            }
                            
                        }
                    },
                    new()
                    {
                        Path = "$.Items",
                        TargetPath = "$.OrderItems",
                        Transformations = new List<NodeConfiguration>
                        {
                            new SelectByPathNodeConfiguration
                            {
                                SelectPath = new List<PathPropertyConfigurationNode>
                                {
                                    new()
                                    {
                                        Path = "$.TransactionId",
                                        TargetPath = "$.TransactionId",
                                        Transformations = new List<NodeConfiguration>
                                        {
                                            new ConvertDataTypeNodeConfiguration
                                            {
                                                ValueType = AttributeValueTypesDto.String
                                            }
                                        }
                                    },
                                    new()
                                    {
                                        Path = "$.Quantity",
                                        TargetPath = "$.Quantity",
                                        Transformations = new List<NodeConfiguration>
                                        {
                                            new ConvertDataTypeNodeConfiguration
                                            {
                                                ValueType = AttributeValueTypesDto.Int64
                                            }
                                        }
                                    },
                                }
                            }
                        }
                    }
                }
            },
            new ProjectNodeConfiguration
            {
                Fields = new List<FieldConfiguration>
                {
                    new(){ Path = "$.Items"},
                    new(){ Path = "$.Customer"},
                    new(){ Path = "$.InvoiceAddress"},
                    new(){ Path = "$.ShippingAddress"}
                }
            }
        }
    };
}