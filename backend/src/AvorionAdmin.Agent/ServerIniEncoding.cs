using System.Globalization;
using System.Text;

namespace AvorionAdmin.Agent;

internal sealed record DecodedServerIni(string Text, Encoding Encoding, bool HasPreamble);

internal static class ServerIniEncoding
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly byte[] Utf8Preamble = Encoding.UTF8.GetPreamble();

    static ServerIniEncoding() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static DecodedServerIni Decode(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(Utf8Preamble))
            return new DecodedServerIni(
                StrictUtf8.GetString(bytes, Utf8Preamble.Length, bytes.Length - Utf8Preamble.Length),
                StrictUtf8,
                true);

        try
        {
            return new DecodedServerIni(StrictUtf8.GetString(bytes), StrictUtf8, false);
        }
        catch (DecoderFallbackException) when (OperatingSystem.IsWindows())
        {
            // Avorion rewrites server.ini using the host Windows ANSI code page when values
            // contain non-ASCII text. Decode that real format strictly and preserve it on write.
            var ansiCodePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
            var ansi = Encoding.GetEncoding(
                ansiCodePage,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
            return new DecodedServerIni(ansi.GetString(bytes), ansi, false);
        }
    }

    public static byte[] Encode(DecodedServerIni source, string text)
    {
        var content = source.Encoding.GetBytes(text);
        if (!source.HasPreamble) return content;
        var preamble = source.Encoding.GetPreamble();
        var result = new byte[preamble.Length + content.Length];
        preamble.CopyTo(result, 0);
        content.CopyTo(result, preamble.Length);
        return result;
    }
}
