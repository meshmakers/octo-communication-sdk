namespace Sdk.Plugs.Sample.Dto;

public class OrderItem
{
    public int OrderItemId { get; set; }
    public string TransactionId { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal TotalPrice { get; set; }
}