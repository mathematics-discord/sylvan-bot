namespace RatBot.Features.Bridge;

public sealed record BridgeAttachment
{
    public BridgeAttachment(string id, string filename, string url, string proxyUrl, int size,
        string? contentType, int? width, int? height, string? description, bool ephemeral)
    {
        this.Id = id;
        this.Filename = filename;
        this.Url = url;
        this.ProxyUrl = proxyUrl;
        this.Size = size;
        this.ContentType = contentType;
        this.Width = width;
        this.Height = height;
        this.Description = description;
        this.Ephemeral = ephemeral;
    }

    public string Id { get; init; }
    public string Filename { get; init; }
    public string Url { get; init; }
    public string ProxyUrl { get; init; }
    public int Size { get; init; }
    public string? ContentType { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Description { get; init; }
    public bool Ephemeral { get; init; }

    public void Deconstruct(
        out string id,
        out string filename,
        out string url,
        out string proxyUrl,
        out int size,
        out string? contentType,
        out int? width,
        out int? height,
        out string? description,
        out bool ephemeral)
    {
        id = this.Id;
        filename = this.Filename;
        url = this.Url;
        proxyUrl = this.ProxyUrl;
        size = this.Size;
        contentType = this.ContentType;
        width = this.Width;
        height = this.Height;
        description = this.Description;
        ephemeral = this.Ephemeral;
    }
}
