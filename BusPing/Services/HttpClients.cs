using System.Net;
using System.Net.Http.Headers;

namespace BusPing.Services;

public static class HttpClients
{
    /// <summary>
    /// Creates an HttpClient tuned for the transport feeds: gzip/deflate (the live
    /// feed is ~120 KB uncompressed), a short timeout and an identifying agent.
    /// </summary>
    public static HttpClient Create(TimeSpan? timeout = null)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };

        var client = new HttpClient(handler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(20),
        };

        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("BusPing", "1.0"));
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("(student project)"));

        return client;
    }
}
