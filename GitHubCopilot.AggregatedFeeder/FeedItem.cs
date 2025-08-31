namespace TestProjectNet8;

public class FeedItem : System.IEquatable<FeedItem>
{
    public decimal Price { get; }
    public decimal Size { get; private set; }

    public FeedItem(decimal price, decimal size)
    {
        Price = price;
        Size = size;
    }

    public void AddSize(decimal size) => Size += size;

    public bool Equals(FeedItem? other) =>
        other is not null && Price == other.Price && Size == other.Size;

    public override bool Equals(object? obj) => Equals(obj as FeedItem);

    public override int GetHashCode() => System.HashCode.Combine(Price, Size);

    public override string ToString() => $"{Price}:{Size}";
}