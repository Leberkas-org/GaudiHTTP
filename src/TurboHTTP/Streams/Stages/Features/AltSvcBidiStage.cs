using System.Net;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Protocol.AltSvc;

namespace TurboHTTP.Streams.Stages.Features;

/// <summary>
/// Bidirectional stage that discovers HTTP/3 availability via Alt-Svc headers (RFC 7838).
/// <para><b>Request direction:</b> checks the <see cref="AltSvcCache"/> for a valid HTTP/3
/// entry and upgrades the request version to 3.0 if found.</para>
/// <para><b>Response direction:</b> parses Alt-Svc headers from HTTP/1.1 and HTTP/2 responses
/// and stores them in the cache for future requests.</para>
/// </summary>
internal sealed class AltSvcBidiStage
    : GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    private readonly AltSvcCache _cache;

    private readonly Inlet<HttpRequestMessage> _inRequest = new("AltSvc.In.Request");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("AltSvc.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("AltSvc.In.Response");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("AltSvc.Out.Response");

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape { get; }

    public AltSvcBidiStage(AltSvcCache cache)
    {
        _cache = cache;
        Shape = new BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        public Logic(AltSvcBidiStage stage) : base(stage.Shape)
        {
            // Request direction: upgrade version if HTTP/3 is cached for the target host.
            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inRequest);

                    if (request.RequestUri is not null
                        && request.Version.Major < 3
                        && stage._cache.TryGetHttp3(request.RequestUri.Host, out var entry))
                    {
                        // Upgrade to HTTP/3. Use the advertised port if different from origin.
                        request.Version = HttpVersion.Version30;

                        if (entry.Port != request.RequestUri.Port)
                        {
                            var builder = new UriBuilder(request.RequestUri) { Port = entry.Port };

                            // Use advertised host if specified, otherwise keep origin host.
                            if (!string.IsNullOrEmpty(entry.Host))
                            {
                                builder.Host = entry.Host;
                            }

                            request.RequestUri = builder.Uri;
                        }
                        else if (!string.IsNullOrEmpty(entry.Host))
                        {
                            var builder = new UriBuilder(request.RequestUri) { Host = entry.Host };
                            request.RequestUri = builder.Uri;
                        }
                    }

                    Push(stage._outRequest, request);
                },
                onUpstreamFinish: () => Complete(stage._outRequest),
                onUpstreamFailure: ex =>
                {
                    Log.Warning("AltSvcBidiStage: Request upstream failure absorbed: {0}", ex.Message);
                    Complete(stage._outRequest);
                });

            SetHandler(stage._outRequest,
                onPull: () => Pull(stage._inRequest),
                onDownstreamFinish: _ => Cancel(stage._inRequest));

            // Response direction: parse Alt-Svc headers and update cache.
            SetHandler(stage._inResponse,
                onPush: () =>
                {
                    var response = Grab(stage._inResponse);

                    if (response.Headers.TryGetValues("Alt-Svc", out var altSvcValues))
                    {
                        var host = response.RequestMessage?.RequestUri?.Host;
                        if (!string.IsNullOrEmpty(host))
                        {
                            foreach (var value in altSvcValues)
                            {
                                var entries = AltSvcParser.Parse(value, out var isClear);
                                if (isClear)
                                {
                                    stage._cache.Clear(host);
                                }
                                else if (entries.Count > 0)
                                {
                                    stage._cache.Store(host, entries);
                                }
                            }
                        }
                    }

                    Push(stage._outResponse, response);
                },
                onUpstreamFinish: () => Complete(stage._outResponse),
                onUpstreamFailure: ex =>
                {
                    Log.Warning("AltSvcBidiStage: Response upstream failure absorbed: {0}", ex.Message);
                    Complete(stage._outResponse);
                });

            SetHandler(stage._outResponse,
                onPull: () => Pull(stage._inResponse),
                onDownstreamFinish: _ => Cancel(stage._inResponse));
        }
    }
}
