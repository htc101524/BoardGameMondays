namespace BoardGameMondays.Core;

/// <summary>
/// Configuration for asset storage providers.
/// Supports multiple backends (Local filesystem, Azure Blob, S3, GCS, etc.)
/// NEW in v2: Designed for extensibility—new providers can be added without modifying this class.
/// Add new provider config classes alongside AzureBlobStorageOptions; update DI registration in Program.cs only.
/// </summary>
public sealed class StorageOptions
{
    /// <summary>
    /// Provider name: "Local", "AzureBlob"/"Azure"/"Blob", or custom provider class name.
    /// NEW: Custom providers can register with any identifier; DI will match by provider class name.
    /// </summary>
    public string Provider { get; set; } = "Local";

    public LocalStorageOptions Local { get; set; } = new();

    public AzureBlobStorageOptions AzureBlob { get; set; } = new();

    /// <summary>
    /// NEW: Enable audit logging of all image operations (upload, delete, migrate).
    /// Useful for compliance, troubleshooting, and migration verification.
    /// </summary>
    public bool EnableAuditLogging { get; set; } = true;

    /// <summary>
    /// NEW: Directory or table name for audit logs. Depends on provider.
    /// For Local storage: subdirectory like "audit-logs"
    /// For Azure: could be Blob container or Table Storage
    /// For S3: bucket prefix, etc.
    /// </summary>
    public string? AuditLogPath { get; set; }

    public sealed class LocalStorageOptions
    {
        /// <summary>
        /// Optional override for where to store assets when Provider=Local.
        /// In production on Azure App Service, if unset, the app will prefer the persistent %HOME% directory.
        /// </summary>
        public string? RootPath { get; set; }
    }

    public sealed class AzureBlobStorageOptions
    {
        /// <summary>
        /// Connection string to Azure Storage account.
        /// Example: "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
        /// </summary>
        public string? ConnectionString { get; set; }

        /// <summary>
        /// Single container for all assets. Paths like: avatars/{id}.jpg, games/{guid}.webp, blog/{guid}.png
        /// Can be changed per environment (e.g., dev: "bgm-assets-dev", prod: "bgm-assets")
        /// </summary>
        public string ContainerName { get; set; } = "bgm-assets";

        /// <summary>
        /// Optional: if you front storage with CDN/custom domain.
        /// Example: https://cdn.example.com or https://bgm-assets.z22.blob.core.windows.net
        /// If set, returned URLs will use this base. If empty, uses default storage account URL.
        /// </summary>
        public string? BaseUrl { get; set; }

        /// <summary>
        /// If true, app will attempt to create the container at startup/usage time.
        /// Set to false if you manage containers separately or lack creation permissions.
        /// </summary>
        public bool CreateContainerIfMissing { get; set; } = true;

        /// <summary>
        /// If true and container is created, tries to set public access (PublicAccessType.Blob).
        /// If you want private blobs + SAS tokens, keep this false and implement SAS generation separately.
        /// </summary>
        public bool PublicAccessIfCreated { get; set; } = true;
    }
}

/// <summary>
/// TEMPLATE for adding new storage providers (S3, GCS, etc.)
/// To support a new provider:
/// 1. Create a config class here (e.g., S3StorageOptions)
/// 2. Create an implementation of IAssetStorage (e.g., S3AssetStorage)
/// 3. Register in Program.cs: 
///    .When(p => p.Provider.Contains("S3")).Use<S3AssetStorage>()
/// 4. Update MIGRATIONS_GUIDE.md with the new provider's configuration
/// EXAMPLE (DO NOT UNCOMMENT unless implementing S3):
/// 
/// public sealed class S3StorageOptions
/// {
///     public string? AccessKey { get; set; }
///     public string? SecretKey { get; set; }
///     public string BucketName { get; set; } = "bgm-assets";
///     public string Region { get; set; } = "us-east-1";
///     public string? CloudFrontUrl { get; set; }
/// }
/// </summary>

