using System.Text;

namespace Henkan.Cli;

/// <summary>
/// Reads the first bytes of a file and says what it actually is.
/// </summary>
/// <remarks>
/// A conversion that writes a file is not the same as a conversion that works.
/// A tool asked for a PNG can happily write an error page, a zero-page PDF or a
/// copy of its input under the new name, and the only cheap way to catch that is
/// to look at what came out rather than that something came out.
/// </remarks>
internal static class FormatSniffer
{
    /// <summary>
    /// The verdict: null when the extension is one with no reliable signature and
    /// nothing can be claimed either way.
    /// </summary>
    public static bool? Matches(string path, string expectedExtension)
    {
        string expected = expectedExtension.TrimStart('.').ToLowerInvariant();
        byte[] head = ReadHead(path, 64);

        if (head.Length == 0)
        {
            return false;
        }

        return expected switch
        {
            "png" => Starts(head, [0x89, 0x50, 0x4E, 0x47]),
            "apng" => Starts(head, [0x89, 0x50, 0x4E, 0x47]),
            "jpg" or "jpeg" => Starts(head, [0xFF, 0xD8, 0xFF]),
            "gif" => Ascii(head, "GIF8"),
            "bmp" => Ascii(head, "BM"),
            "tif" or "tiff" => Ascii(head, "II") || Ascii(head, "MM"),
            "webp" => Ascii(head, "RIFF") && Ascii(head[8..], "WEBP"),
            "avif" or "heic" or "heif" => Ascii(head[4..], "ftyp"),
            "ico" => Starts(head, [0x00, 0x00, 0x01, 0x00]),
            "psd" => Ascii(head, "8BPS"),
            "jxl" => Starts(head, [0xFF, 0x0A]) || Starts(head, [0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20]),
            "jp2" => Starts(head, [0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50]) || Starts(head, [0xFF, 0x4F, 0xFF, 0x51]),
            "qoi" => Ascii(head, "qoif"),
            "dds" => Ascii(head, "DDS "),
            "ppm" => Ascii(head, "P6") || Ascii(head, "P3"),
            "exr" => Starts(head, [0x76, 0x2F, 0x31, 0x01]),

            "zip" => Ascii(head, "PK"),
            "7z" => Starts(head, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]),
            "gz" or "tgz" => Starts(head, [0x1F, 0x8B]),
            "bz2" or "tbz" => Ascii(head, "BZh"),
            "xz" or "txz" => Starts(head, [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00]),

            // Tar has no header of its own; the magic sits inside the first entry.
            "tar" => head.Length >= 6 && Tar(path),

            "pdf" => Ascii(head, "%PDF"),
            "ps" => Ascii(head, "%!PS") || Ascii(head, "%!"),
            "eps" => Ascii(head, "%!PS") || Starts(head, [0xC5, 0xD0, 0xD3, 0xC6]),
            "xps" => Ascii(head, "PK"),
            "rtf" => Ascii(head, "{\\rtf"),

            // Every modern Office and OpenDocument file is a zip, so the header
            // alone cannot tell a PPTX from an ODP. Looking inside can, and that
            // difference is exactly the kind of mislabelled output worth catching.
            "odt" => OpenDocument(path, "text"),
            "ods" => OpenDocument(path, "spreadsheet"),
            "odp" => OpenDocument(path, "presentation"),
            "epub" => Zip(path, entry => entry == "mimetype"),
            "docx" => Zip(path, entry => entry.StartsWith("word/", StringComparison.Ordinal)),
            "xlsx" => Zip(path, entry => entry.StartsWith("xl/", StringComparison.Ordinal)),
            "pptx" => Zip(path, entry => entry.StartsWith("ppt/", StringComparison.Ordinal)),

            // The legacy Office formats are all OLE compound files.
            "doc" or "xls" or "ppt" => Starts(head, [0xD0, 0xCF, 0x11, 0xE0]),

            "mp3" => Ascii(head, "ID3") || (head[0] == 0xFF && (head[1] & 0xE0) == 0xE0),
            "flac" => Ascii(head, "fLaC"),
            "ogg" or "oga" or "opus" or "ogv" => Ascii(head, "OggS"),
            "wav" or "avi" => Ascii(head, "RIFF"),
            "aiff" => Ascii(head, "FORM"),
            "ac3" => Starts(head, [0x0B, 0x77]),
            "wma" or "wmv" or "asf" => Starts(head, [0x30, 0x26, 0xB2, 0x75]),
            "mp4" or "m4a" or "m4v" or "mov" => Ascii(head[4..], "ftyp") || Ascii(head[4..], "moov") || Ascii(head[4..], "mdat"),
            "mkv" or "mka" or "webm" => Starts(head, [0x1A, 0x45, 0xDF, 0xA3]),
            "ts" => head[0] == 0x47,
            "mpg" or "mpeg" => Starts(head, [0x00, 0x00, 0x01]),

            // Plain text and a few container-less raster formats carry nothing to
            // check, so they are reported as unknown rather than guessed at.
            _ => null,
        };
    }

    /// <summary>What the file looks like, for a failure message.</summary>
    public static string Describe(string path)
    {
        byte[] head = ReadHead(path, 16);

        if (head.Length == 0)
        {
            return "empty";
        }

        if (Ascii(head, "%PDF"))
        {
            return "a PDF";
        }

        if (Ascii(head, "PK"))
        {
            return "a zip container";
        }

        if (Starts(head, [0xD0, 0xCF, 0x11, 0xE0]))
        {
            return "an OLE compound file";
        }

        if (Starts(head, [0x89, 0x50, 0x4E, 0x47]))
        {
            return "a PNG";
        }

        if (head.All(b => b is 0x09 or 0x0A or 0x0D or (>= 0x20 and < 0x7F)))
        {
            return "plain text";
        }

        return "bytes " + string.Join(" ", head.Take(6).Select(b => b.ToString("x2")));
    }

    private static bool Tar(string path)
    {
        byte[] head = ReadHead(path, 265);
        return head.Length >= 262 && Encoding.ASCII.GetString(head, 257, 5) == "ustar";
    }

    /// <summary>True when the zip declares the expected OpenDocument mime type.</summary>
    private static bool OpenDocument(string path, string kind)
    {
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(path);
            System.IO.Compression.ZipArchiveEntry? entry = archive.GetEntry("mimetype");

            if (entry is null)
            {
                return false;
            }

            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd().Trim() == $"application/vnd.oasis.opendocument.{kind}";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return false;
        }
    }

    private static bool Zip(string path, Func<string, bool> hasEntry)
    {
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(path);
            return archive.Entries.Any(e => hasEntry(e.FullName));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return false;
        }
    }

    private static byte[] ReadHead(string path, int count)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            byte[] buffer = new byte[count];
            int read = stream.ReadAtLeast(buffer, count, throwOnEndOfStream: false);
            return buffer[..read];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static bool Starts(byte[] head, byte[] signature) =>
        head.Length >= signature.Length && head.Take(signature.Length).SequenceEqual(signature);

    private static bool Ascii(byte[] head, string signature) =>
        Starts(head, Encoding.ASCII.GetBytes(signature));
}
