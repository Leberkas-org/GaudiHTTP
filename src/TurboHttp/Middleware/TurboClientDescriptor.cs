using System;
using System.Collections.Generic;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;

namespace TurboHttp.Middleware;

internal sealed class TurboClientDescriptor
{
    public RedirectPolicy? RedirectPolicy { get; set; }
    public RetryPolicy? RetryPolicy { get; set; }
    public bool EnableCookies { get; set; }
    public CookieJar? CustomCookieJar { get; set; }
    public CachePolicy? CachePolicy { get; set; }
    public List<Type> MiddlewareTypes { get; } = [];
}
