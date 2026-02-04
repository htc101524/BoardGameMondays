using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BoardGameMondays.Core;

public interface IAssetStorage
{
    Task<string> SaveAvatarAsync(Guid memberId, Stream content, string extension, CancellationToken ct = default);

    Task<string> SaveGameImageAsync(Stream content, string extension, CancellationToken ct = default);

    Task<string> SaveBlogImageAsync(Stream content, string extension, CancellationToken ct = default);
}
