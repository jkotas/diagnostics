namespace Microsoft.Diagnostics.DebugServices
{
    public interface IField
    {
        string Name { get; }
        uint Offset { get; }
    }
}