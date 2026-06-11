using System.Text.Json.Nodes;
using Bogus;

namespace Sdk.Common.Tests.TestData.Dto;

internal static class Generator
{
    static Generator()
    {
        Randomizer.Seed = new Random(8675309);
    }

    internal static SimpleData GenerateSimpleData()
    {
        var faker = new Faker<SimpleData>()
            .RuleFor(s => s.Value, f => f.Random.Int(1, 100));
        return faker.Generate();
    }
    
    internal static IEnumerable<SimpleData> GenerateSimpleDataList()
    {
        var faker = new Faker<SimpleData>()
            .RuleFor(s => s.Value, f => f.Random.Int(1, 100));
        return faker.Generate(10);
    }
    
    internal static Order GenerateOrder()
    {
        var customerFaker = new Faker<Customer>()
            .RuleFor(c => c.Id, f => f.Random.Int(1, 100))
            .RuleFor(c => c.Name, f => f.Person.FullName)
            .RuleFor(c => c.EMail, f => f.Person.Email)
            .RuleFor(c => c.Tel1, f => f.Person.Phone)
            .RuleFor(c => c.Tel2, f => f.Person.Phone)
            .RuleFor(c => c.VatId, f => f.Random.AlphaNumeric(10));
        var productFaker = new Faker<Product>()
            .RuleFor(p => p.Id, f => f.Random.AlphaNumeric(15))
            .RuleFor(p => p.Title, f => f.Commerce.ProductName())
            .RuleFor(p => p.Weight, f => f.Random.Int(1, 100))
            .RuleFor(p => p.Sku, f => f.Random.AlphaNumeric(10));
        var orderItemFaker = new Faker<OrderItem>()
            .RuleFor(oi => oi.OrderItemId, f => f.Random.Int(1, 100))
            .RuleFor(oi => oi.TransactionId, f => f.Random.AlphaNumeric(15))
            .RuleFor(oi => oi.Product, f => productFaker.Generate())
            .RuleFor(oi => oi.Quantity, f => f.Random.Int(1, 100))
            .RuleFor(oi => oi.TotalPrice, f => f.Random.Decimal(1, 100));
        var addressFaker = new Faker<Address>()
            .RuleFor(a => a.Street, f => f.Address.StreetAddress())
            .RuleFor(a => a.City, f => f.Address.City())
            .RuleFor(a => a.Zip, f => f.Address.ZipCode())
            .RuleFor(a => a.Country, f => f.Address.Country());
        var orderFaker = new Faker<Order>()
            .RuleFor(o => o.InvoiceNumber, f => f.Random.Int(1, 1000))
            .RuleFor(o => o.InvoiceDate, f => f.Date.Past())
            .RuleFor(o => o.InvoiceAddress, f => addressFaker.Generate())
            .RuleFor(o => o.ShippingAddress, f => addressFaker.Generate())
            .RuleFor(o => o.Customer, f => customerFaker.Generate())
            .RuleFor(o => o.Items, f => f.Make(3, () => orderItemFaker.Generate()).ToArray());
        
        var order = orderFaker.Generate();
        return order;
    }
    
    internal static TransferOrderList GenerateOrders()
    {
        List<Order> orders = new();
        for (int i = 0; i < 10; i++)
        {
            orders.Add(GenerateOrder());
        }
 
        return new TransferOrderList { Orders = orders.ToArray() };
    }


    internal static JsonObject GenerateColumnDataNode()
    {
        const string json = """
        {
            "data": {
                "timestamp": [
                    "2024-10-27T15:07:18.545+01:00",
                    "2024-10-27T15:07:28.545+01:00",
                    "2024-10-27T15:07:38.557+01:00",
                    "2024-10-27T15:07:48.545+01:00",
                    "2024-10-27T15:07:58.545+01:00",
                    "2024-10-27T15:08:28.457+01:00"
                ],
                "batteryPower": [0, 0, 0, 0, 0, 0],
                "productionPower": [2605, 2576, 2568, 2556, 2534, 2539],
                "additionalProductionPower": [46.0, 46.0, 45.0, 48.0, 47.0, 47.0],
                "batteryStateOfCharge": [100, 100, 100, 100, 100, 100],
                "consumption": [1770, 1760, 1787, 1787, 2016, 2000],
                "net": [-881, -862, -826, -817, -565, -586]
            }
        }
        """;
        return (JsonNode.Parse(json) as JsonObject)!;
    }
}