namespace BoardGameMondays.Core;

public sealed class StorageOptions
{
    public string Provider { get; set; } = "Local";

    public AzureBlobStorageOptions AzureBlob { get; set; } = new();

    public sealed class AzureBlobStorageOptions
    {
        public string? ConnectionString { get; set; }

        // Single container for all assets. Paths like: avatars/{id}.jpg, games/{guid}.webp
        public string ContainerName { get; set; } = "bgm-assets";

        // Optional: if you front storage with CDN/custom domain.
        // Example: https://cdn.example.com
        public string? BaseUrl { get; set; }

        // If true, app will attempt to create the container at startup usage time.
        public bool CreateContainerIfMissing { get; set; } = true;

        // If true and container is created, tries to set public access.
        // If you want private blobs + SAS, keep this false and implement SAS later.
        public bool PublicAccessIfCreated { get; set; } = true;
    }
}
