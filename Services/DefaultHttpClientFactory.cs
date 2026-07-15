namespace NetworkHealthMonitor.Services;

public sealed class DefaultHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        return new HttpClient();
    }
}
