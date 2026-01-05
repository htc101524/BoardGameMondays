namespace BoardGameMondays.Core;

public static class ImageFileSniffer
{
    public static async Task<string?> DetectExtensionAsync(Stream stream, CancellationToken ct = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        var header = new byte[12];
        var read = 0;
        while (read < header.Length)
        {
            var r = await stream.ReadAsync(header.AsMemory(read, header.Length - read), ct);
            if (r == 0)
            {
                break;
            }

            read += r;
        }

        if (read < 3)
        {
            return null;
        }

        // JPEG: FF D8 FF
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ".jpg";
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (read >= 8
            && header[0] == 0x89
            && header[1] == 0x50
            && header[2] == 0x4E
            && header[3] == 0x47
            && header[4] == 0x0D
            && header[5] == 0x0A
            && header[6] == 0x1A
            && header[7] == 0x0A)
        {
            return ".png";
        }

        // GIF: "GIF87a" or "GIF89a"
        if (read >= 6
            && header[0] == (byte)'G'
            && header[1] == (byte)'I'
            && header[2] == (byte)'F'
            && header[3] == (byte)'8'
            && (header[4] == (byte)'7' || header[4] == (byte)'9')
            && header[5] == (byte)'a')
        {
            return ".gif";
        }

        // WEBP: "RIFF" .... "WEBP"
        if (read >= 12
            && header[0] == (byte)'R'
            && header[1] == (byte)'I'
            && header[2] == (byte)'F'
            && header[3] == (byte)'F'
            && header[8] == (byte)'W'
            && header[9] == (byte)'E'
            && header[10] == (byte)'B'
            && header[11] == (byte)'P')
        {
            return ".webp";
        }

        return null;
    }
}
