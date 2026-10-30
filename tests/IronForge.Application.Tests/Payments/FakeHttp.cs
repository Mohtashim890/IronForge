using System.Net;
using System.Text;

namespace IronForge.Application.Tests.Payments;

/// <summary>Answers provider calls from a list of canned responses and records the requests.</summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    public FakeHttp Respond(HttpStatusCode status, string body)
    {
        _responses.Enqueue((status, body));
        return this;
    }

    public HttpClient Client() => new(this);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));

        var (status, responseBody) = _responses.Count > 0 ? _responses.Dequeue() : (HttpStatusCode.InternalServerError, "{}");

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        };
    }
}
